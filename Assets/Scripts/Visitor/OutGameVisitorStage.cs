using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using F = MasterHouse.HouseUIRuntime;

namespace MasterHouse
{
    /// <summary>
    /// Hub 场景里的访客 NPC 舞台层（纯表现，§16.4）：
    /// ①业务访客：每帧轮询 VisitorManager 的在场实例列表生成/回收演员（实例动态增删，§9），
    ///   演员状态随实例业务状态同步（表现不回写业务），点击转发 instanceId 给 HubPage 触发对话；
    ///   闲逛台词经 DialogueManager.BubbleRequested 事件推给对应演员的句子气泡（内容选取在对话系统侧）。
    /// ②串门邻居（ambient）：随机轮换进场，在门口排队等玩家决定去留（名册在 VisitorTuningConfig）。
    /// ③把场景归一化坐标换算成锚点（跟随观景模式的 uvRect 平移缩放），并按深度排序前后遮挡。
    /// 只在起居室出现。
    /// </summary>
    internal sealed class OutGameVisitorStage : MonoBehaviour
    {
        // 起居室的入口大门与门前地面（场景归一化坐标，左下为原点）
        private static readonly Vector2 DoorPoint = new Vector2(.115f, .32f);
        // 前台站位区（访客接待前待在这里，§9）：按同样的手摆方式补的门厅前台坐标，多房间随家具二轮再说（§16.7）
        private static readonly Vector2[] FrontDeskPoints =
        {
            new Vector2(.20f, .26f),
            new Vector2(.155f, .21f),
            new Vector2(.245f, .215f),
            new Vector2(.115f, .16f),
        };
        // 邻居在门口的排队点（同时最多 MaxAmbient 只，队伍前移时依次补位）
        private static readonly Vector2[] QueuePoints =
        {
            new Vector2(.185f, .225f),
            new Vector2(.13f, .175f),
            new Vector2(.235f, .19f),
        };
        // 手摆的可行走落点：避开沙发、茶几、书架与背景人物
        private static readonly Vector2[] WanderPoints =
        {
            new Vector2(.30f, .22f),
            new Vector2(.25f, .12f),
            new Vector2(.42f, .10f),
            new Vector2(.58f, .07f),
            new Vector2(.66f, .05f),
            new Vector2(.78f, .15f),
            new Vector2(.36f, .27f),
        };
        private const int MaxAmbient = 3;

        /// <summary>访客业务状态（只读轮询，§2.1；表现结果不回写业务，§16.4）。</summary>
        private static VisitorManager Visitor => GameManager.Instance.VisitorManager;

        /// <summary>氛围邻居名册（调参配置，§4.5）。</summary>
        private static VisitorTuningConfig Tuning => GameManager.Instance.VisitorTuning;

        private RawImage sceneArt;
        private RectTransform layerRoot;
        private Action<int> onGuestClicked;
        private bool initialSpawnDone;
        private int frontDeskSlot;
        private readonly List<OutGameVisitorActor> actors = new List<OutGameVisitorActor>();
        /// <summary>业务演员：instanceId → 演员。</summary>
        private readonly Dictionary<int, OutGameVisitorActor> businessActors = new Dictionary<int, OutGameVisitorActor>();
        private readonly List<int> departKeys = new List<int>();
        // 邻居按进场顺序单独记录（actors 每帧按深度重排，不能用它当队伍顺序）
        private readonly List<OutGameVisitorActor> ambientOrder = new List<OutGameVisitorActor>();
        private readonly HashSet<int> activeAmbient = new HashSet<int>();
        private readonly List<float> respawnTimers = new List<float>();

        /// <summary>在场景根下创建访客层。业务访客按 VisitorManager 的在场实例生成：建层时已在场 → 按状态直接落位；
        /// 此后新实例由 Update 轮询捕捉，从大门走进前台。</summary>
        public static OutGameVisitorStage Build(RectTransform sceneRoot, RawImage art, Action<int> onGuestClicked)
        {
            var existing = sceneRoot.Find("VisitorStage");
            if (existing != null) Destroy(existing.gameObject);
            var root = F.Stretch(sceneRoot, "VisitorStage");
            root.gameObject.AddComponent<RectMask2D>(); // 观景模式缩放时裁掉跑出画面的演员
            var stage = root.gameObject.AddComponent<OutGameVisitorStage>();
            stage.sceneArt = art;
            stage.layerRoot = root;
            stage.onGuestClicked = onGuestClicked;
            // 建层时已在场的实例：直接落位淡入（错峰）
            var spawned = 0;
            foreach (var instance in Visitor.Data.Instances)
            {
                stage.SpawnBusiness(instance, walkIn: false, delay: .3f + spawned * .6f + UnityEngine.Random.Range(0f, .5f));
                spawned++;
            }
            stage.initialSpawnDone = true;
            // 邻居首发阵容：随机挑几只错峰进场
            var roster = Tuning != null ? Tuning.ambientVisitors : null;
            if (roster != null)
            {
                var order = new List<int>();
                for (var i = 0; i < roster.Count; i++) order.Insert(UnityEngine.Random.Range(0, order.Count + 1), i);
                for (var k = 0; k < Mathf.Min(MaxAmbient, order.Count); k++)
                    stage.SpawnAmbient(order[k], 5f + k * 3.5f + UnityEngine.Random.Range(0f, 2f));
            }
            // 闲逛台词直接订对话系统的气泡通道：内容选取（种族对话池 → 加权抽取 → recent 去重）
            // 全在 DialogueManager 里，舞台只负责把成文的句子送到对应演员头顶
            if (GameManager.Instance != null && GameManager.Instance.DialogueManager != null)
                GameManager.Instance.DialogueManager.BubbleRequested += stage.OnBubbleRequested;
            return stage;
        }

        private void OnDestroy()
        {
            if (GameManager.Instance != null && GameManager.Instance.DialogueManager != null)
                GameManager.Instance.DialogueManager.BubbleRequested -= OnBubbleRequested;
        }

        /// <summary>闲逛台词冒泡（§8 满意后闲逛触发点）：推给对应演员的句子气泡展示。</summary>
        private void OnBubbleRequested(VisitorInstance instance, string line)
        {
            if (instance == null || string.IsNullOrEmpty(line)) return;
            if (!businessActors.TryGetValue(instance.InstanceId, out var actor) || actor == null) return;
            // 气泡停留时长按 tick 配置（§4.5），表现层换算成秒（表现层豁免，§16.4）
            var ticksPerSecond = GameConfig.Instance != null ? Mathf.Max(1, GameConfig.Instance.TicksPerSecond) : 10;
            var holdTicks = Tuning != null ? Tuning.bubbleHoldTicks : 40;
            actor.ShowLine(line, holdTicks / (float)ticksPerSecond);
        }

        /// <summary>生成一位业务访客演员。walkIn=true 从大门走到前台；false 按业务状态直接落位淡入（建层回填）。</summary>
        private void SpawnBusiness(VisitorInstance instance, bool walkIn, float delay = 0f)
        {
            var race = instance.Race;
            var frontPoint = FrontDeskPoints[frontDeskSlot % FrontDeskPoints.Length];
            frontDeskSlot++;
            var instanceId = instance.InstanceId;
            var actor = OutGameVisitorActor.Create(layerRoot, "i" + instanceId, instance.DisplayName,
                race != null ? race.sheetPath : string.Empty,
                isAmbient: false, spawnDelay: walkIn ? UnityEngine.Random.Range(0f, .6f) : delay,
                DoorPoint, frontPoint, WanderPoints,
                () => onGuestClicked?.Invoke(instanceId), null,
                spawnInside: !walkIn);
            if (actor == null) return;
            actor.SyncBusinessState(instance.State);
            actors.Add(actor);
            businessActors[instanceId] = actor;
        }

        private void SpawnAmbient(int rosterIndex, float delay)
        {
            var neighbor = Tuning.ambientVisitors[rosterIndex];
            var actor = OutGameVisitorActor.Create(layerRoot, "neighbor_" + neighbor.id,
                neighbor.displayName, neighbor.sheetPath,
                isAmbient: true, spawnDelay: delay,
                DoorPoint, QueuePoints[0], WanderPoints,
                null, () => OnAmbientGone(rosterIndex));
            if (actor == null) return;
            activeAmbient.Add(rosterIndex);
            actors.Add(actor);
            ambientOrder.Add(actor);
        }

        /// <summary>一只邻居离场 → 冷却一阵后换一只不在场的进来（刷新循环）。</summary>
        private void OnAmbientGone(int rosterIndex)
        {
            activeAmbient.Remove(rosterIndex);
            respawnTimers.Add(UnityEngine.Random.Range(8f, 16f));
        }

        private void Update()
        {
            // ①业务实例 → 演员 同步（只读轮询，§2.1）：新实例进场、状态推进、离场回收
            var instances = Visitor.Data.Instances;
            foreach (var instance in instances)
            {
                if (businessActors.TryGetValue(instance.InstanceId, out var actor) && actor != null)
                {
                    actor.SyncBusinessState(instance.State);
                }
                else
                {
                    SpawnBusiness(instance, walkIn: initialSpawnDone);
                }
            }
            departKeys.Clear();
            foreach (var pair in businessActors)
            {
                if (pair.Value == null) { departKeys.Add(pair.Key); continue; }
                if (Visitor.Find(pair.Key) == null)
                {
                    pair.Value.BeginDepart(); // 实例已离场（拒绝/超时/闲逛结束/日结清场）→ 走向门口消失
                    departKeys.Add(pair.Key);
                }
            }
            foreach (var key in departKeys) businessActors.Remove(key);

            // ②邻居刷新循环
            for (var i = respawnTimers.Count - 1; i >= 0; i--)
            {
                respawnTimers[i] -= Time.unscaledDeltaTime;
                if (respawnTimers[i] > 0f) continue;
                respawnTimers.RemoveAt(i);
                if (Tuning == null) continue;
                var candidates = new List<int>();
                for (var r = 0; r < Tuning.ambientVisitors.Count; r++)
                    if (!activeAmbient.Contains(r)) candidates.Add(r);
                if (candidates.Count > 0)
                    SpawnAmbient(candidates[UnityEngine.Random.Range(0, candidates.Count)], 0f);
            }
            // ③门口队位动态分配：还在排队的邻居按进场顺序占 QueuePoints，前面走了后面补位
            ambientOrder.RemoveAll(actor => actor == null);
            var slot = 0;
            foreach (var actor in ambientOrder)
            {
                if (!actor.IsQueuingAtDoor) continue;
                actor.SetWaitPoint(QueuePoints[Mathf.Min(slot, QueuePoints.Length - 1)]);
                slot++;
            }
        }

        private void LateUpdate()
        {
            if (sceneArt == null) return;
            var uv = sceneArt.uvRect;
            actors.RemoveAll(actor => actor == null);
            // 按深度排前后：y 大（远）在前面的兄弟位，y 小（近）在后，天然形成近处遮挡远处
            actors.Sort((a, b) => b.ScenePosition.y.CompareTo(a.ScenePosition.y));
            for (var i = 0; i < actors.Count; i++)
            {
                var actor = actors[i];
                var rect = (RectTransform)actor.transform;
                rect.SetSiblingIndex(i);
                var anchor = new Vector2(
                    (actor.ScenePosition.x - uv.x) / Mathf.Max(uv.width, .0001f),
                    (actor.ScenePosition.y - uv.y) / Mathf.Max(uv.height, .0001f));
                rect.anchorMin = rect.anchorMax = anchor;
                rect.anchoredPosition = Vector2.zero;
            }
        }
    }
}

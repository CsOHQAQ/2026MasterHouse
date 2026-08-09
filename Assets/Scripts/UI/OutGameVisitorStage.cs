using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using F = MasterHouse.OutGameUIFactory;

namespace MasterHouse
{
    /// <summary>
    /// Hub 场景里的访客 NPC 舞台层：
    /// ①业务访客：按 served 状态刷新，点击转发给 OutGameUI 触发对话；
    /// ②串门邻居（ambient）：随机轮换进场，在门口排队等玩家决定去留，走掉后隔一阵换一只新的进来；
    /// ③把场景归一化坐标换算成锚点（跟随观景模式的 uvRect 平移缩放，与家具热点同一套换算），并按深度排序前后遮挡。
    /// 只在起居室出现；业务结算仍由 OutGameUI 持有，这里只做表现转发。
    /// </summary>
    internal sealed class OutGameVisitorStage : MonoBehaviour
    {
        // 起居室的入口大门与门前地面（场景归一化坐标，左下为原点）
        private static readonly Vector2 DoorPoint = new Vector2(.115f, .32f);
        private static readonly Vector2 EntrancePoint = new Vector2(.16f, .235f);
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

        /// <summary>过渡桥接：冻结的旧表现层读新 HouseClock 模块（§16.4）；HourF 判定在 3.4 访客业务重写时改整数比较。</summary>
        private static HouseClockData Clock => GameManager.Instance.HouseClockManager.Data;

        /// <summary>过渡桥接：旧表现层读访客内容表（§16.6）；3.4 访客业务重写时随本类接新数据。</summary>
        private static VisitorTable Visitors => GameManager.Instance.VisitorTable;

        private RawImage sceneArt;
        private RectTransform layerRoot;
        private bool[] served;
        private Action<int> onGuestClicked;
        private Action<int> onGuestArrived;
        private readonly bool[] guestSpawned = new bool[4];
        private readonly List<OutGameVisitorActor> actors = new List<OutGameVisitorActor>();
        private readonly OutGameVisitorActor[] byGuest = new OutGameVisitorActor[4];
        // 邻居按进场顺序单独记录（actors 每帧按深度重排，不能用它当队伍顺序）
        private readonly List<OutGameVisitorActor> ambientOrder = new List<OutGameVisitorActor>();
        private readonly HashSet<int> activeAmbient = new HashSet<int>();
        private readonly List<float> respawnTimers = new List<float>();

        /// <summary>
        /// 在场景根下创建访客层。业务访客按游戏时钟与到访状态生成：已到访（存档记录）或已过拜访时间 → 常驻屋内；
        /// 未到拜访时间 → 由 Update 盯着时钟，到点从大门走进来。served 为 true 的业务访客不再出现。
        /// </summary>
        public static OutGameVisitorStage Build(RectTransform sceneRoot, RawImage art, bool[] served, bool[] arrived,
            Action<int> onGuestClicked, Action<int> onGuestArrived)
        {
            var existing = sceneRoot.Find("VisitorStage");
            if (existing != null) Destroy(existing.gameObject);
            var root = F.Stretch(sceneRoot, "VisitorStage");
            root.gameObject.AddComponent<RectMask2D>(); // 观景模式缩放时裁掉跑出画面的演员
            var stage = root.gameObject.AddComponent<OutGameVisitorStage>();
            stage.sceneArt = art;
            stage.layerRoot = root;
            stage.served = served;
            stage.onGuestClicked = onGuestClicked;
            stage.onGuestArrived = onGuestArrived;
            var spawned = 0;
            for (var i = 0; i < Visitors.visitors.Count; i++)
            {
                if (served != null && i < served.Length && served[i])
                {
                    stage.guestSpawned[i] = true; // 已完成/已拒绝：本周不再出现
                    continue;
                }
                var guest = Visitors.visitors[i];
                var wasArrived = arrived != null && i < arrived.Length && arrived[i];
                var timeReached = Clock.HourF >= guest.visitHour;
                if (!wasArrived && !timeReached) continue; // 还没到拜访时间，Update 里等时钟
                // 刚踩点到访（半游戏小时内）→ 从大门走进来；否则视为早已在屋内（读档/切房间回来）→ 直接出现在屋里
                var walkIn = !wasArrived && Clock.HourF - guest.visitHour < .5f;
                stage.SpawnGuest(i, walkIn,
                    walkIn ? 1.2f + spawned * 2.8f + UnityEngine.Random.Range(0f, 1.5f)
                           : .3f + spawned * .6f + UnityEngine.Random.Range(0f, .5f));
                spawned++;
            }
            // 邻居首发阵容：随机挑几只错峰进场
            var roster = Visitors.ambientVisitors;
            var order = new List<int>();
            for (var i = 0; i < roster.Count; i++) order.Insert(UnityEngine.Random.Range(0, order.Count + 1), i);
            for (var k = 0; k < Mathf.Min(MaxAmbient, order.Count); k++)
                stage.SpawnAmbient(order[k], 5f + k * 3.5f + UnityEngine.Random.Range(0f, 2f));
            return stage;
        }

        /// <summary>生成一位业务访客。walkIn=true 从大门走到入口再进屋；false 直接淡入屋内游走点（常驻回填）。</summary>
        private void SpawnGuest(int index, bool walkIn, float delay)
        {
            guestSpawned[index] = true;
            var guest = Visitors.visitors[index];
            var actor = OutGameVisitorActor.Create(layerRoot, guest.id, guest.displayName, guest.sheetPath,
                isAmbient: false, spawnDelay: delay,
                DoorPoint, EntrancePoint, WanderPoints,
                () => onGuestClicked?.Invoke(index), null,
                spawnInside: !walkIn);
            if (actor == null) return;
            actors.Add(actor);
            byGuest[index] = actor;
            onGuestArrived?.Invoke(index);
        }

        private void SpawnAmbient(int rosterIndex, float delay)
        {
            var neighbor = Visitors.ambientVisitors[rosterIndex];
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

        /// <summary>服务成功 → 演员庆祝并进入限时停留；到点自行走向门口消失。</summary>
        public void NotifyServed(int guestIndex)
        {
            var actor = ActorOf(guestIndex);
            if (actor != null) actor.NotifyServed();
        }

        /// <summary>拒绝接待 → 演员直接返回门口离开。</summary>
        public void NotifyRefused(int guestIndex)
        {
            var actor = ActorOf(guestIndex);
            if (actor != null) actor.NotifyRefused();
        }

        private OutGameVisitorActor ActorOf(int guestIndex) =>
            guestIndex >= 0 && guestIndex < byGuest.Length ? byGuest[guestIndex] : null;

        private void Update()
        {
            // 业务访客到点进场（按加速的游戏时钟；跨过拜访小时即从大门走进来）
            for (var i = 0; i < guestSpawned.Length && i < Visitors.visitors.Count; i++)
            {
                if (guestSpawned[i]) continue;
                if (served != null && i < served.Length && served[i]) { guestSpawned[i] = true; continue; }
                if (Clock.HourF >= Visitors.visitors[i].visitHour)
                    SpawnGuest(i, walkIn: true, delay: UnityEngine.Random.Range(0f, 1f));
            }
            // 邻居刷新循环
            for (var i = respawnTimers.Count - 1; i >= 0; i--)
            {
                respawnTimers[i] -= Time.unscaledDeltaTime;
                if (respawnTimers[i] > 0f) continue;
                respawnTimers.RemoveAt(i);
                var candidates = new List<int>();
                for (var r = 0; r < Visitors.ambientVisitors.Count; r++)
                    if (!activeAmbient.Contains(r)) candidates.Add(r);
                if (candidates.Count > 0)
                    SpawnAmbient(candidates[UnityEngine.Random.Range(0, candidates.Count)], 0f);
            }
            // 门口队位动态分配：还在排队的邻居按进场顺序占 QueuePoints，前面走了后面补位
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

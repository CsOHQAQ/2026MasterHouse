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
    /// ②串门邻居（ambient）：随机轮换进场，在门口排队等玩家决定去留（名册在 VisitorTuningConfig）；只待在起居室。
    /// ③四宫格世界（2026-08-13）：舞台层挂在世界根下、覆盖全部 4 个房间；演员持 (房间, 房内归一化坐标)，
    ///   经 HubWorldGrid 换算成世界锚点，平移缩放由世界根的 transform 承担（不再做 uvRect 数学）。
    /// ④~~拖拽换房~~ **已整体退役（2026-08-22 一轮测试改进 #8）**：分房改为接待后的强制分配模式
    ///   （点击空房，见 HubPage），闲逛换房能力随拖拽一并取消。
    /// 大门与前台在起居室（房间 0）。
    /// </summary>
    internal sealed class OutGameVisitorStage : MonoBehaviour
    {
        /// <summary>入口区兜底（房间表缺配时）：旧版起居室大门附近。</summary>
        // 兜底入口区（2026-08-18 压低）：原来的 .15~.33 在活动区上沿之外，
        // 缺配的房间里等待中的访客会悬在半空
        private static readonly Rect DefaultEntryArea = Rect.MinMaxRect(.08f, .03f, .18f, .13f);
        // 等待/排队点在入口区内的相对位置（分数坐标）：业务访客与邻居都在门口等，
        // 请进来（接待/请进屋）才走进屋内；错开站位避免叠在一起
        private static readonly Vector2[] EntrySlots =
        {
            new Vector2(.35f, .45f),
            new Vector2(.65f, .25f),
            new Vector2(.20f, .15f),
            new Vector2(.80f, .55f),
        };
        /// <summary>活动区兜底（房间表缺配时）：贴着地面的一条窄带。</summary>
        private static readonly Rect DefaultWalkArea = Rect.MinMaxRect(.04f, .02f, .96f, .15f);
        /// <summary>接待室的活动/入口区（2026-08-16 主楼场景）：房间表只配业务四间，接待室先走代码常量。
        /// 2026-08-18 按美术红框压低：原来上沿到 .22，访客会站到左侧柜台上去。</summary>
        private static Rect ReceptionWalkArea = Rect.MinMaxRect(.06f, .025f, .94f, .15f);

        /// <summary>
        /// 用 HubSceneWorld Prefab 里的「接待室可走带」矩形覆盖代码兜底值（HubSceneBinder 建层时调）。
        /// 接待室区域矩形的下沿并不等于地板线，光靠代码常量贴不准——拖 Prefab 里那个矩形才是正解。
        /// 传 null = Prefab 没配，沿用兜底带。
        /// </summary>
        internal static void ConfigureReceptionWalkArea(Rect? area)
        {
            if (!area.HasValue || area.Value.width <= .01f) return;
            var walk = area.Value;
            // 矩形被拖成一条线时给它补一点纵深：否则近沿=远沿，所有人会被算到同一个点上
            if (walk.height < MinWalkDepth) walk.height = MinWalkDepth;
            ReceptionWalkArea = walk;
            // 入口区跟着可走带走（2026-08-18 反馈「范围调好了人还在上面」）：
            // 门口排队用的是入口区，它要是还留在代码常量上，调可走带对排队的人一点用没有。
            // 取可走带靠门那一侧的前半段：横向左 38%，纵向压在偏前的位置，队伍就贴着地。
            ReceptionEntryArea = Rect.MinMaxRect(
                walk.xMin, walk.yMin,
                Mathf.Lerp(walk.xMin, walk.xMax, .38f), Mathf.Lerp(walk.yMin, walk.yMax, .75f));
        }
        private static Rect ReceptionEntryArea = Rect.MinMaxRect(.06f, .03f, .4f, .13f);
        /// <summary>活动区没有单独配透视收缩比时的默认远端宽度比。</summary>
        private const float DefaultFarWidthScale = .8f;

        /// <summary>
        /// 可走区的「椭圆度」：2 = 正椭圆，越大越接近圆角矩形。
        /// 红框是又宽又浅的一条带（约 0.9 × 0.12），内接正椭圆会把中段压到只剩七成宽、
        /// 前后沿收成一个点，房间左右两头就走不到了。取 3 只削掉四角，
        /// 既有椭圆的圆润轮廓与前后收口，又保住整条带的可用宽度。
        /// </summary>
        private const float WalkOvalPower = 3f;

        /// <summary>可走带的最小纵深（区域内分数）：矩形被拖成一条线时兜住，免得所有人塌到同一个点。</summary>
        private const float MinWalkDepth = .03f;
        private const int MaxAmbient = 3;
        /// <summary>
        /// 演员的统一世界缩放：全场访客共用 VisitorTuningConfig 的基准大小，
        /// 再叠加脚底深度的轻微透视缩放。缺配置时按 0.6 兜底。
        /// </summary>
        private static float ActorWorldScale =>
            Tuning != null ? Mathf.Clamp(Tuning.actorWorldScale, .2f, 1f) : .68f;
        /// <summary>假透视深度缩小（2026-08-16 反馈）：脚底 y 每升高 1（房内归一化）缩小的比例与下限——
        /// 地面带内轻微收小，被拖出活动区贴墙时继续缩，不会「贴在墙上还原大」。</summary>
        private const float ActorDepthShrink = 1.1f;
        /// <summary>深度缩小的下限（2026-08-17 调高）：走到房间最里侧也不小于这个比例，
        /// 免得访客缩成一个看不清的小点。乘上 ActorWorldScale 即最小显示尺寸。</summary>
        private const float ActorMinDepthScale = .68f;
        /// <summary>氛围邻居（串门临时访客）总开关：2026-08-14 屏蔽——名册与逻辑保留，改 true 即恢复。</summary>
        private const bool AmbientEnabled = false;

        /// <summary>访客业务状态（只读轮询，§2.1；表现结果不回写业务，§16.4）。</summary>
        private static VisitorManager Visitor => GameManager.Instance.VisitorManager;

        /// <summary>氛围邻居名册（调参配置，§4.5）。</summary>
        private static VisitorTuningConfig Tuning => GameManager.Instance.VisitorTuning;

        /// <summary>四宫格世界根（指针坐标换算用；舞台层是它的子物体，平移缩放天然跟随）。</summary>
        private RectTransform worldRoot;
        private RectTransform layerRoot;
        private Action<int> onGuestClicked;
        private bool initialSpawnDone;
        private int frontDeskSlot;

        /// <summary>取某业务访客当前的世界归一化站位（相机「聚焦访客」用；不在场返回 false）。</summary>
        public bool TryGetActorWorld(int instanceId, out Vector2 world01)
        {
            world01 = default;
            if (!businessActors.TryGetValue(instanceId, out var actor) || actor == null) return false;
            world01 = HubWorldGrid.RoomToWorld(actor.RoomIndex, actor.ScenePosition);
            return true;
        }

        private readonly List<OutGameVisitorActor> actors = new List<OutGameVisitorActor>();
        /// <summary>业务演员：instanceId → 演员。</summary>
        private readonly Dictionary<int, OutGameVisitorActor> businessActors = new Dictionary<int, OutGameVisitorActor>();
        private readonly List<int> departKeys = new List<int>();
        // 邻居按进场顺序单独记录（actors 每帧按深度重排，不能用它当队伍顺序）
        private readonly List<OutGameVisitorActor> ambientOrder = new List<OutGameVisitorActor>();
        private readonly HashSet<int> activeAmbient = new HashSet<int>();
        private readonly List<float> respawnTimers = new List<float>();

        /// <summary>在四宫格世界根下创建访客层（覆盖全部房间）。业务访客按 VisitorManager 的在场实例生成：
        /// 建层时已在场 → 按 (状态, 房间) 直接落位；此后新实例由 Update 轮询捕捉，从起居室大门走进前台。</summary>
        public static OutGameVisitorStage Build(RectTransform worldRoot, Action<int> onGuestClicked)
        {
            var existing = worldRoot.Find("VisitorStage");
            if (existing != null) Destroy(existing.gameObject);
            var root = F.Stretch(worldRoot, "VisitorStage");
            var stage = root.gameObject.AddComponent<OutGameVisitorStage>();
            stage.worldRoot = worldRoot;
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
            var roster = AmbientEnabled && Tuning != null ? Tuning.ambientVisitors : null;
            if (roster != null)
            {
                var order = new List<int>();
                for (var i = 0; i < roster.Count; i++) order.Insert(UnityEngine.Random.Range(0, order.Count + 1), i);
                for (var k = 0; k < Mathf.Min(MaxAmbient, order.Count); k++)
                    stage.SpawnAmbient(order[k], 5f + k * 3.5f + UnityEngine.Random.Range(0f, 2f));
            }
            // 闲逛冒泡直接订对话系统的气泡通道：什么时候冒、说哪句由 DialogueManager 定（§8），
            // 舞台只负责把台词推给对应演员（#5 文字恢复显示，2026-08-22）
            if (GameManager.Instance != null && GameManager.Instance.DialogueManager != null)
                GameManager.Instance.DialogueManager.BubbleRequested += stage.OnBubbleRequested;
            stage.RebuildFurnitureProxies(); // 家具深度代理（2026-08-16 访客与家具分层）
            return stage;
        }

        private void OnDestroy()
        {
            if (GameManager.Instance != null && GameManager.Instance.DialogueManager != null)
                GameManager.Instance.DialogueManager.BubbleRequested -= OnBubbleRequested;
        }

        /// <summary>闲逛冒泡（§8 满意后闲逛触发点）：把选好的那句台词推给对应演员的文字气泡（#5 恢复显示）。</summary>
        private void OnBubbleRequested(VisitorInstance instance, string line)
        {
            if (instance == null || string.IsNullOrEmpty(line)) return;
            if (!businessActors.TryGetValue(instance.InstanceId, out var actor) || actor == null) return;
            // 气泡停留时长按 tick 配置（§4.5），表现层换算成秒（表现层豁免，§16.4）
            var ticksPerSecond = GameConfig.Instance != null ? Mathf.Max(1, GameConfig.Instance.TicksPerSecond) : 10;
            var holdTicks = Tuning != null ? Tuning.bubbleHoldTicks : 40;
            actor.ShowBubble(line, holdTicks / (float)ticksPerSecond);
        }

        /// <summary>生成一位业务访客演员：出现在起居室入口区并**在门口等待接待**（请进来了才进屋，接待成功
        /// 由业务状态推进触发 EnterWandering 走进屋内）。false = 按业务状态直接落位淡入（建层回填，
        /// 所在房间由 Update 的实例同步按 instance.RoomIndex 校正）。</summary>
        private void SpawnBusiness(VisitorInstance instance, bool walkIn, float delay = 0f)
        {
            var race = instance.Race;
            // 访客进场/排队都在底层接待室（2026-08-16 主楼场景）
            var frontPoint = EntrySlotPoint(HubWorldGrid.Reception, frontDeskSlot);
            frontDeskSlot++;
            var instanceId = instance.InstanceId;
            var actor = OutGameVisitorActor.Create(layerRoot, "i" + instanceId, instance.DisplayName,
                race != null ? race.sheetPath : string.Empty,
                isAmbient: false, spawnDelay: walkIn ? UnityEngine.Random.Range(0f, .6f) : delay,
                RandomEntryPoint(HubWorldGrid.Reception), frontPoint, RandomWalkPoint, EntryArea,
                () => onGuestClicked?.Invoke(instanceId), null,
                spawnInside: !walkIn, startRoom: HubWorldGrid.Reception,
                noTalkReason: () => TalkStateOf(instanceId));
            if (actor == null) return;
            actor.SyncBusinessState(instance.State);
            actors.Add(actor);
            businessActors[instanceId] = actor;
        }

        /// <summary>
        /// 「现在点他有没有对话」——演员每帧问这一句，用来决定头顶标记亮不亮、名牌怎么写。
        /// 判据只有 VisitorManager.NoTalkReason 一处（与 Hub 的 Toast、访客卡同源）。
        ///
        /// 实例已离场（演员还在走向门口）时 Find 返回 null，NoTalkReason 照口径给非 None，标记自然收起。
        /// 退出播放模式期间 GameManager 可能先没，兜一手免得演员的 Update 报空引用。
        /// </summary>
        private static VisitorManager.ENoTalkReason TalkStateOf(int instanceId)
        {
            var visitor = GameManager.Instance != null ? GameManager.Instance.VisitorManager : null;
            return visitor == null
                ? VisitorManager.ENoTalkReason.Wandering
                : visitor.NoTalkReason(visitor.Find(instanceId));
        }

        // ── 拖拽换房已整体退役（#8）：分房走 HubPage 的强制分配模式，几何工具（可走椭圆）保留 ──

        /// <summary>
        /// 访客可走体积（2026-08-18 按美术红框重做）：一块**贴地的透视椭圆**。
        ///
        /// 范围取房间表的「访客区」列（策划按房间美术的红框标定，§16.6 内容进资产）——
        /// 之前这里读的是家具**地面网格**，那块区域比红框高一大截，访客会走到柜台、窗台上去。
        /// 形状从矩形/梯形改成椭圆：前后两端收口、中段最宽，既贴合地毯/地面的实际形状，
        /// 又让访客自然分布在纵深上——有人站在家具前面、有人在后面，前景后景的层次就出来了。
        /// 透视仍然保留：远端半宽按地面网格的远端宽度比向中心收。
        /// </summary>
        private struct WalkVolume
        {
            public float centerX, nearHalf, farScale, yNear, yFar;
        }

        private static WalkVolume WalkVolumeOf(int roomIndex)
        {
            var table = GameManager.Instance != null ? GameManager.Instance.FurnitureRoomTable : null;
            if (roomIndex >= 0 && roomIndex < HubWorldGrid.RoomCount &&
                table != null && roomIndex < table.rooms.Count && table.rooms[roomIndex] != null)
            {
                var room = table.rooms[roomIndex];
                var area = room.visitorWalkArea;
                if (area.width > .01f)
                {
                    if (area.height < MinWalkDepth) area.height = MinWalkDepth;
                    // 透视收缩比仍取地面网格的（同一块地面，远端该收多少是一致的）
                    var farScale = DefaultFarWidthScale;
                    foreach (var grid in room.grids)
                    {
                        if (grid == null || grid.surface != FurnitureSurfaceType.Floor) continue;
                        farScale = Mathf.Clamp(grid.farWidthScale, .2f, 1f);
                        break;
                    }
                    return new WalkVolume
                    {
                        centerX = area.center.x,
                        nearHalf = area.width * .5f,
                        farScale = farScale,
                        yNear = area.yMin,
                        yFar = area.yMax,
                    };
                }
            }
            var fallback = roomIndex == HubWorldGrid.Reception ? ReceptionWalkArea : DefaultWalkArea;
            return new WalkVolume
            {
                centerX = fallback.center.x,
                nearHalf = fallback.width * .5f,
                farScale = DefaultFarWidthScale,
                yNear = fallback.yMin,
                yFar = fallback.yMax,
            };
        }

        /// <summary>给定脚底 y 的假透视深度缩放。</summary>
        private static float DepthScaleAt(float y) =>
            Mathf.Clamp(1f - y * ActorDepthShrink, ActorMinDepthScale, 1f);

        /// <summary>给定深度 t（0 = 近沿、1 = 远沿）处的半宽：近沿最宽、按远端宽度比向中心收。</summary>
        private static float HalfWidthAt(in WalkVolume volume, float t) =>
            volume.nearHalf * Mathf.Lerp(1f, volume.farScale, Mathf.Clamp01(t));

        /// <summary>
        /// 活动区 → 单位圆：横向按该深度的半宽归一、纵向把 [yNear, yFar] 映到 [-1, 1]。
        /// 于是「椭圆内」就等价于「单位圆内」，钳制与随机取点都在圆上做，简单且没有死角。
        /// </summary>
        private static Vector2 ToWalkDisc(in WalkVolume volume, Vector2 point)
        {
            var t = Mathf.InverseLerp(volume.yNear, volume.yFar, point.y);
            var half = HalfWidthAt(volume, t);
            return new Vector2(half > 1e-5f ? (point.x - volume.centerX) / half : 0f, t * 2f - 1f);
        }

        private static Vector2 FromWalkDisc(in WalkVolume volume, Vector2 disc)
        {
            var t = Mathf.Clamp01((disc.y + 1f) * .5f);
            return new Vector2(volume.centerX + disc.x * HalfWidthAt(volume, t),
                Mathf.Lerp(volume.yNear, volume.yFar, t));
        }

        /// <summary>单位方形坐标下的「到边界的比例」：&lt;=1 在区内，&gt;1 在区外。</summary>
        private static float OvalNorm(Vector2 disc) =>
            Mathf.Pow(Mathf.Pow(Mathf.Abs(disc.x), WalkOvalPower) +
                      Mathf.Pow(Mathf.Abs(disc.y), WalkOvalPower), 1f / WalkOvalPower);

        /// <summary>把点钳进可走椭圆：越界的沿着中心方向收回边界上。</summary>
        internal static Vector2 ClampWalk(int roomIndex, Vector2 point)
        {
            var volume = WalkVolumeOf(roomIndex);
            var disc = ToWalkDisc(volume, point);
            var norm = OvalNorm(disc);
            if (norm > 1f) disc /= norm;
            return FromWalkDisc(volume, disc);
        }

        /// <summary>
        /// 可走椭圆内随机取一个落点。方形内取样后丢弃落在椭圆外的（命中率约八成），
        /// 这样落点在整块区域里是均匀的——不会像极坐标那样往中心堆。
        /// </summary>
        internal static Vector2 RandomWalkPoint(int roomIndex)
        {
            var volume = WalkVolumeOf(roomIndex);
            var disc = Vector2.zero;
            for (var attempt = 0; attempt < 8; attempt++)
            {
                disc = new Vector2(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-1f, 1f));
                var norm = OvalNorm(disc);
                if (norm <= 1f) break;
                if (attempt == 7) disc /= norm; // 兜底：连着没命中就收到边界上，不留死循环
            }
            return FromWalkDisc(volume, disc);
        }

        /// <summary>房间的访客入口区（归一化矩形，房间表可配、按房间美术门位标定；缺配回落默认门位）。</summary>
        internal static Rect EntryArea(int roomIndex)
        {
            if (roomIndex == HubWorldGrid.Reception) return ReceptionEntryArea;
            var table = GameManager.Instance != null ? GameManager.Instance.FurnitureRoomTable : null;
            if (table != null && roomIndex >= 0 && roomIndex < table.rooms.Count && table.rooms[roomIndex] != null)
            {
                var area = table.rooms[roomIndex].visitorEntryArea;
                if (area.width > .01f && area.height > .01f) return area;
            }
            return DefaultEntryArea;
        }

        /// <summary>入口区内随机取一个出现/离场点。</summary>
        private static Vector2 RandomEntryPoint(int roomIndex)
        {
            var area = EntryArea(roomIndex);
            return new Vector2(
                UnityEngine.Random.Range(area.xMin, area.xMax),
                UnityEngine.Random.Range(area.yMin, area.yMax));
        }

        /// <summary>入口区内的第 slot 个等待站位（分数坐标 → 归一化坐标，错开不叠人）。</summary>
        private static Vector2 EntrySlotPoint(int roomIndex, int slot)
        {
            var area = EntryArea(roomIndex);
            var f = EntrySlots[slot % EntrySlots.Length];
            return new Vector2(area.xMin + f.x * area.width, area.yMin + f.y * area.height);
        }

        private void SpawnAmbient(int rosterIndex, float delay)
        {
            var neighbor = Tuning.ambientVisitors[rosterIndex];
            var actor = OutGameVisitorActor.Create(layerRoot, "neighbor_" + neighbor.id,
                neighbor.displayName, neighbor.sheetPath,
                isAmbient: true, spawnDelay: delay,
                RandomEntryPoint(HubWorldGrid.Reception), EntrySlotPoint(HubWorldGrid.Reception, 0), RandomWalkPoint, EntryArea,
                null, () => OnAmbientGone(rosterIndex), startRoom: HubWorldGrid.Reception);
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
                    // 房间同步：业务真相在 instance.RoomIndex（分配模式点房落位后这里把演员送进那间房；
                    // 舞台重建回填时把演员落到上次所在的房间）。
                    // 前台排队/等分房两态还没有真正的房间，表现上住在底层接待室（2026-08-16 主楼场景）
                    var homeRoom = instance.State == EVisitorState.FrontDesk || instance.State == EVisitorState.AwaitingRoom
                        ? HubWorldGrid.Reception
                        : instance.RoomIndex;
                    if (actor.RoomIndex != homeRoom)
                        actor.TeleportToRoom(homeRoom, RandomWalkPoint(homeRoom));
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
            // ③门口队位动态分配：还在排队的邻居按进场顺序占入口区站位，前面走了后面补位
            ambientOrder.RemoveAll(actor => actor == null);
            var slot = 0;
            foreach (var actor in ambientOrder)
            {
                if (!actor.IsQueuingAtDoor) continue;
                actor.SetWaitPoint(EntrySlotPoint(HubWorldGrid.Reception, slot));
                slot++;
            }
        }

        // ── 家具深度代理（2026-08-16 访客与家具分层）──
        // 家具烘焙在房间贴图里没有独立层级，这里给每件已摆家具生成一张与烘焙像素重合的前景代理，
        // 与访客一起按「脚底世界 y」排兄弟序：访客站到家具后面就会被正确遮挡。

        /// <summary>家具代理及其深度键（脚底世界 y；并列时按烘焙序稳定排序）。</summary>
        private readonly List<(RectTransform rect, RawImage image, float depthY, int order)> furnitureProxies =
            new List<(RectTransform, RawImage, float, int)>();
        private readonly List<(Transform transform, float depthY, int order)> depthSortCache =
            new List<(Transform, float, int)>();

        /// <summary>重建家具深度代理（建层与摆放变化后由场景层调用）。</summary>
        public void RebuildFurnitureProxies()
        {
            foreach (var proxy in furnitureProxies)
                if (proxy.rect != null) Destroy(proxy.rect.gameObject);
            furnitureProxies.Clear();
            for (var room = 0; room < HubWorldGrid.RoomCount; room++)
            {
                foreach (var info in FurnitureSceneComposer.GetPlacedFurniture(room))
                {
                    if (info.Entry == null || info.Entry.sprite == null || info.Entry.sprite.texture == null) continue;
                    var min = HubWorldGrid.RoomToWorld(room, info.ViewportRect.min);
                    var max = HubWorldGrid.RoomToWorld(room, info.ViewportRect.max);
                    var rect = F.Rect(layerRoot, $"Furniture_{room}_{info.Entry.id}", min, max, Vector2.zero, Vector2.zero);
                    // 与烘焙同语义（2026-08-17 修复大小偏差）：烘焙把精灵的**紧致可见区**（textureRect）
                    // 拉伸填满矩形，而 Image 画的是含透明留白的完整精灵框——这里用 RawImage + 紧致 uv 对齐
                    var sprite = info.Entry.sprite;
                    var texture = sprite.texture;
                    var tight = sprite.textureRect;
                    var image = rect.gameObject.AddComponent<RawImage>();
                    image.texture = texture;
                    image.uvRect = new Rect(tight.x / texture.width, tight.y / texture.height,
                        tight.width / texture.width, tight.height / texture.height);
                    image.raycastTarget = false; // 点击仍归家具热点/演员
                    if (info.Flipped) rect.localScale = new Vector3(-1f, 1f, 1f);
                    var depthViewportY = float.IsNaN(info.DepthViewportY)
                        ? info.ViewportRect.yMin
                        : info.DepthViewportY;
                    var depthY = HubWorldGrid.RoomToWorld(room,
                        new Vector2(info.ViewportRect.center.x, depthViewportY)).y;
                    furnitureProxies.Add((rect, image, depthY, info.Order));
                }
            }
        }

        private void LateUpdate()
        {
            actors.RemoveAll(actor => actor == null);

            // 统一深度排序：家具代理 + 访客按脚底世界 y 从远到近排兄弟序（y 大在前、被 y 小的遮挡）；
            // 家具代理随昼夜调色与烘焙底图保持一致
            depthSortCache.Clear();
            var tint = HouseDayLight.Now().tint;
            foreach (var proxy in furnitureProxies)
            {
                if (proxy.rect == null) continue;
                proxy.image.color = tint;
                depthSortCache.Add((proxy.rect, proxy.depthY, proxy.order));
            }
            foreach (var actor in actors)
            {
                var rect = (RectTransform)actor.transform;
                // 平移缩放由世界根的 transform 承担，这里只需把 (房间, 房内坐标) 换算成世界锚点
                var anchor = HubWorldGrid.RoomToWorld(actor.RoomIndex, actor.ScenePosition);
                rect.anchorMin = rect.anchorMax = anchor;
                rect.anchoredPosition = Vector2.zero;
                // 演员统一世界缩放 × 假透视深度（2026-08-16）：基准不随房间变化，
                // 地面带内脚底越靠里越小；**离开地面（超过地面带远沿）后定格**不再继续缩
                var cappedY = Mathf.Min(actor.ScenePosition.y, WalkVolumeOf(actor.RoomIndex).yFar);
                rect.localScale = Vector3.one * (ActorWorldScale * DepthScaleAt(cappedY));
                depthSortCache.Add((rect, anchor.y, int.MaxValue));
            }
            depthSortCache.Sort((a, b) =>
            {
                var byDepth = b.depthY.CompareTo(a.depthY);
                return byDepth != 0 ? byDepth : a.order.CompareTo(b.order);
            });
            for (var i = 0; i < depthSortCache.Count; i++)
                depthSortCache[i].transform.SetSiblingIndex(i);
        }
    }
}

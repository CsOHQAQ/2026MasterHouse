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
    /// ④拖拽换房：按住业务访客拖到别的房间松手 → 经页面回调走 VisitorManager.MoveVisitorToRoom；
    ///   业务层拒绝时下一帧的实例同步会把演员弹回原房间。
    /// 大门与前台在起居室（房间 0）。
    /// </summary>
    internal sealed class OutGameVisitorStage : MonoBehaviour
    {
        /// <summary>入口区兜底（房间表缺配时）：旧版起居室大门附近。</summary>
        private static readonly Rect DefaultEntryArea = Rect.MinMaxRect(.08f, .15f, .18f, .33f);
        // 等待/排队点在入口区内的相对位置（分数坐标）：业务访客与邻居都在门口等，
        // 请进来（接待/请进屋）才走进屋内；错开站位避免叠在一起
        private static readonly Vector2[] EntrySlots =
        {
            new Vector2(.35f, .45f),
            new Vector2(.65f, .25f),
            new Vector2(.20f, .15f),
            new Vector2(.80f, .55f),
        };
        /// <summary>活动区兜底（房间表缺配时）：与旧的手摆游走带大致等价。</summary>
        private static readonly Rect DefaultWalkArea = Rect.MinMaxRect(.04f, .03f, .96f, .35f);
        /// <summary>接待室的活动/入口区（2026-08-16 主楼场景）：房间表只配业务四间，接待室先走代码常量；
        /// 入口区取左侧大门一带，游走带铺满底层地面。</summary>
        private static readonly Rect ReceptionWalkArea = Rect.MinMaxRect(.06f, .03f, .94f, .22f);
        private static readonly Rect ReceptionEntryArea = Rect.MinMaxRect(.06f, .04f, .4f, .2f);
        private const int MaxAmbient = 3;
        /// <summary>
        /// 演员的统一世界缩放（2026-08-17 按截图调大；2026-08-17 按参考视频再调）：
        /// 参考视频里人身高 ≈ 小屋高的 1/3 ≈ 单层楼高的 0.7，故把访客调到约 0.7 个房间高。
        /// 全场访客同一基准大小；调整体大小改这里。
        /// </summary>
        private const float ActorWorldScale = .68f;
        /// <summary>假透视深度缩小（2026-08-16 反馈）：脚底 y 每升高 1（房内归一化）缩小的比例与下限——
        /// 地面带内轻微收小，被拖出活动区贴墙时继续缩，不会「贴在墙上还原大」。</summary>
        private const float ActorDepthShrink = 1.1f;
        private const float ActorMinDepthScale = .35f;
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
        /// <summary>
        /// 拖拽松手回调（instanceId, 目标房间）→ 业务是否接受这个落点。
        /// 页面翻译成 VisitorManager.MoveVisitorToRoom；返回 false 时演员弹回拖拽起手位置。
        /// </summary>
        private Func<int, int, bool> onGuestDropped;
        private bool initialSpawnDone;
        private int frontDeskSlot;
        /// <summary>正被玩家拖拽的演员（RTS 边缘推屏用：相机层每帧据此重投影，保证访客钉在指针下）。</summary>
        private OutGameVisitorActor draggingActor;
        /// <summary>抓取偏移（世界归一化坐标）：起手时演员落脚点相对指针的差，拖拽全程保持。</summary>
        private Vector2 dragGrabOffset;

        /// <summary>是否有访客正在被拖拽（HubSceneBinder 边缘推屏的开关）。</summary>
        public bool HasActiveDrag => draggingActor != null && draggingActor.Dragging;

        /// <summary>取某业务访客当前的世界归一化站位（相机「聚焦访客」用；不在场返回 false）。</summary>
        public bool TryGetActorWorld(int instanceId, out Vector2 world01)
        {
            world01 = default;
            if (!businessActors.TryGetValue(instanceId, out var actor) || actor == null) return false;
            world01 = HubWorldGrid.RoomToWorld(actor.RoomIndex, actor.ScenePosition);
            return true;
        }

        /// <summary>按当前鼠标位置重投影被拖拽的访客（相机平移/缩放后由相机层每帧调用；无拖拽时空转）。</summary>
        public void RefreshDragProjection()
        {
            if (!HasActiveDrag) return;
            ProjectDrag(draggingActor, Input.mousePosition);
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
        public static OutGameVisitorStage Build(RectTransform worldRoot, Action<int> onGuestClicked,
            Func<int, int, bool> onGuestDropped)
        {
            var existing = worldRoot.Find("VisitorStage");
            if (existing != null) Destroy(existing.gameObject);
            var root = F.Stretch(worldRoot, "VisitorStage");
            var stage = root.gameObject.AddComponent<OutGameVisitorStage>();
            stage.worldRoot = worldRoot;
            stage.layerRoot = root;
            stage.onGuestClicked = onGuestClicked;
            stage.onGuestDropped = onGuestDropped;
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
            // 闲逛台词直接订对话系统的气泡通道：内容选取（种族对话池 → 加权抽取 → recent 去重）
            // 全在 DialogueManager 里，舞台只负责把成文的句子送到对应演员头顶
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
                spawnInside: !walkIn, startRoom: HubWorldGrid.Reception);
            if (actor == null) return;
            actor.SyncBusinessState(instance.State);
            AttachDrag(actor, instanceId);
            actors.Add(actor);
            businessActors[instanceId] = actor;
        }

        // ── 拖拽换房（§16.4：拖动只改表现坐标，松手经页面回调走业务方法）──

        /// <summary>给业务访客演员挂拖拽事件（演员建层时已有 EventTrigger，直接续加条目）。</summary>
        private void AttachDrag(OutGameVisitorActor actor, int instanceId)
        {
            var trigger = actor.gameObject.GetComponent<UnityEngine.EventSystems.EventTrigger>();
            if (trigger == null) trigger = actor.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            AddTrigger(trigger, UnityEngine.EventSystems.EventTriggerType.BeginDrag,
                data =>
                {
                    if (actor == null) return;
                    actor.BeginPlayerDrag();
                    if (!actor.Dragging) return;
                    draggingActor = actor;
                    // 抓取偏移（2026-08-14 跟手修复）：记住「演员落脚点 − 指针」的世界差，
                    // 拖拽全程按这个差跟随——否则起手瞬间演员脚底会吸到指针上，视觉上就是一跳
                    dragGrabOffset = Vector2.zero;
                    if (data is UnityEngine.EventSystems.PointerEventData pointer
                        && TryScreenToWorld(pointer.position, out var grabWorld))
                        dragGrabOffset = HubWorldGrid.RoomToWorld(actor.RoomIndex, actor.ScenePosition) - grabWorld;
                });
            AddTrigger(trigger, UnityEngine.EventSystems.EventTriggerType.Drag, data =>
            {
                if (actor != null && data is UnityEngine.EventSystems.PointerEventData pointer)
                    DragActor(actor, pointer);
            });
            AddTrigger(trigger, UnityEngine.EventSystems.EventTriggerType.EndDrag,
                _ => { if (actor != null) DropActor(actor, instanceId); });
        }

        private static void AddTrigger(UnityEngine.EventSystems.EventTrigger trigger,
            UnityEngine.EventSystems.EventTriggerType type,
            UnityEngine.Events.UnityAction<UnityEngine.EventSystems.BaseEventData> callback)
        {
            var entry = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(callback);
            trigger.triggers.Add(entry);
        }

        /// <summary>
        /// 访客可走体积（2026-08-16 用户定案）：与家具**地面网格**同一块梯形透视区——
        /// 近沿全宽、越远越向中心收（读网格的远端宽度比），随机落点/松手钳制共用。
        /// 接待室没有地面网格，用代码常量矩形按轻微透视处理。
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
                foreach (var grid in room.grids)
                {
                    if (grid == null || grid.surface != FurnitureSurfaceType.Floor) continue;
                    var width = grid.cols * grid.cellWidth;
                    return new WalkVolume
                    {
                        centerX = (grid.x + width * .5f) / room.sceneWidth,
                        nearHalf = Mathf.Max(.05f, width * .5f / room.sceneWidth - .01f),
                        farScale = Mathf.Clamp(grid.farWidthScale, .2f, 1f),
                        yNear = Mathf.Max(0f, 1f - (grid.y + grid.rows * grid.cellHeight) / room.sceneHeight) + .015f,
                        yFar = 1f - grid.y / room.sceneHeight - .015f,
                    };
                }
            }
            var area = roomIndex == HubWorldGrid.Reception ? ReceptionWalkArea : DefaultWalkArea;
            return new WalkVolume
            {
                centerX = area.center.x,
                nearHalf = area.width * .5f,
                farScale = .8f,
                yNear = area.yMin,
                yFar = area.yMax,
            };
        }

        /// <summary>给定脚底 y 的假透视深度缩放。</summary>
        private static float DepthScaleAt(float y) =>
            Mathf.Clamp(1f - y * ActorDepthShrink, ActorMinDepthScale, 1f);

        /// <summary>给定深度 y 处的半宽（梯形：近沿 → 远沿按远端宽度比向中心收）。</summary>
        private static float HalfWidthAt(in WalkVolume volume, float y)
        {
            var t = Mathf.InverseLerp(volume.yNear, volume.yFar, y);
            return volume.nearHalf * Mathf.Lerp(1f, volume.farScale, t);
        }

        /// <summary>把点钳进可走梯形（拖拽落位用）。</summary>
        internal static Vector2 ClampWalk(int roomIndex, Vector2 point)
        {
            var volume = WalkVolumeOf(roomIndex);
            point.y = Mathf.Clamp(point.y, volume.yNear, volume.yFar);
            var half = HalfWidthAt(volume, point.y);
            point.x = Mathf.Clamp(point.x, volume.centerX - half, volume.centerX + half);
            return point;
        }

        /// <summary>可走梯形内随机取一个落点（先取深度、按该深度的宽度取横向）。</summary>
        internal static Vector2 RandomWalkPoint(int roomIndex)
        {
            var volume = WalkVolumeOf(roomIndex);
            var y = UnityEngine.Random.Range(volume.yNear, volume.yFar);
            var half = HalfWidthAt(volume, y);
            return new Vector2(volume.centerX + UnityEngine.Random.Range(-half, half), y);
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

        /// <summary>拖拽跟随（uGUI Drag 事件路径）。</summary>
        private void DragActor(OutGameVisitorActor actor, UnityEngine.EventSystems.PointerEventData pointer)
        {
            ProjectDrag(actor, pointer.position);
        }

        /// <summary>屏幕坐标 → 世界归一化坐标（壳 Canvas 为 Overlay 模式，无相机参与换算）。</summary>
        private bool TryScreenToWorld(Vector2 screenPosition, out Vector2 world)
        {
            world = default;
            if (worldRoot == null) return false;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    worldRoot, screenPosition, null, out var local)) return false;
            var rect = worldRoot.rect;
            world = new Vector2(
                Mathf.Clamp01((local.x - rect.xMin) / Mathf.Max(rect.width, 1f)),
                Mathf.Clamp01((local.y - rect.yMin) / Mathf.Max(rect.height, 1f)));
            return true;
        }

        /// <summary>拖拽跟随：指针世界坐标 + 抓取偏移 → (房间, 房内坐标)。
        /// 拖拽事件与 RTS 边缘推屏的每帧重投影共用。
        /// 拖拽中**不**钳活动区（演员是被拎在手里的，自由跟手；硬钳的话跨房瞬间会在两个红框区之间瞬移），
        /// 活动区约束推迟到松手落位（见 DropActor）。</summary>
        private void ProjectDrag(OutGameVisitorActor actor, Vector2 screenPosition)
        {
            if (actor == null || !actor.Dragging) return;
            if (!TryScreenToWorld(screenPosition, out var world)) return;
            world.x = Mathf.Clamp01(world.x + dragGrabOffset.x);
            world.y = Mathf.Clamp01(world.y + dragGrabOffset.y);
            // 指针在墙体/天空等无效区时沿用当前房间坐标系（拖拽本就自由跟手，落位时才裁决）
            var room = HubWorldGrid.RoomAt(world);
            if (room == HubWorldGrid.None) room = actor.RoomIndex;
            actor.UpdatePlayerDrag(room, HubWorldGrid.WorldToLocal(room, world));
        }

        /// <summary>
        /// 松手：表现先落位，业务经页面回调裁决（§5.2 按状态分派）。
        /// 业务拒绝时立刻弹回起手位置——跨房间的情况实例同步下一帧也会纠正，
        /// 但同房间内被拒（前台访客在起居室里被拖动）只能靠这条路。
        /// </summary>
        private void DropActor(OutGameVisitorActor actor, int instanceId)
        {
            if (draggingActor == actor) draggingActor = null; // 边缘推屏停表
            if (!actor.Dragging) return;
            var room = actor.RoomIndex;
            // 丢在接待室或无效区 = 不换房，弹回起手位置（接待室不是业务房间，分不了房）
            if (room < 0 || room >= HubWorldGrid.RoomCount)
            {
                actor.CancelPlayerDrag();
                return;
            }
            // 落位钳回可走梯形（拖拽中自由跟手，约束在这里补上）
            actor.UpdatePlayerDrag(room, ClampWalk(room, actor.ScenePosition));
            var accepted = onGuestDropped != null && onGuestDropped(instanceId, room);
            if (accepted) actor.EndPlayerDrag();
            else actor.CancelPlayerDrag();
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
                    // 房间同步：业务真相在 instance.RoomIndex（拖拽被业务层拒绝时这里把演员弹回原房间；
                    // 舞台重建回填时把演员落到上次所在的房间）。
                    // 前台排队/等分房两态还没有真正的房间，表现上住在底层接待室（2026-08-16 主楼场景）
                    var homeRoom = instance.State == EVisitorState.FrontDesk || instance.State == EVisitorState.AwaitingRoom
                        ? HubWorldGrid.Reception
                        : instance.RoomIndex;
                    if (!actor.Dragging && actor.RoomIndex != homeRoom)
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
                    var depthY = HubWorldGrid.RoomToWorld(room, new Vector2(info.ViewportRect.center.x, info.ViewportRect.yMin)).y;
                    furnitureProxies.Add((rect, image, depthY, info.Order));
                }
            }
        }

        private void LateUpdate()
        {
            actors.RemoveAll(actor => actor == null);

            // 统一深度排序：家具代理 + 访客按脚底世界 y 从远到近排兄弟序（y 大在前、被 y 小的遮挡）；
            // 拖拽中的访客压最上层；家具代理随昼夜调色与烘焙底图保持一致
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
                var depthY = actor.Dragging ? float.MinValue : anchor.y; // 拖拽中永远压最上
                depthSortCache.Add((rect, depthY, int.MaxValue));
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

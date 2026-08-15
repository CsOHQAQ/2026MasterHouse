using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// Hub 场景层绑定（2026-08-13 四宫格重做）：
    /// 四个房间平铺成 2×2 连续世界（HubWorldGrid），世界根挂在裁剪容器下，
    /// 相机 = 世界根的 anchoredPosition（平移）+ localScale（缩放）；滚轮以鼠标为中心缩放、
    /// 按住左键拖拽平移，缩到 0.5 倍时四个房间尽收眼底。「当前房间」由视口中心落在哪个象限决定，
    /// 变化时回调 HubPage 刷新导航高亮与说明卡。
    /// 房间背景取家具烘焙图；热点按房间建在世界坐标上（跟随相机，无需逐帧换算）；
    /// 访客舞台覆盖整个世界（拖拽访客换房见 OutGameVisitorStage）。
    /// 背景/热点是动态表现件，允许运行时生成（§16.2）。
    /// </summary>
    public sealed class HubSceneBinder
    {
        private const float MinZoom = .5f;   // 恰好看全 2×2 四个房间
        private const float MaxZoom = 3.5f;
        /// <summary>拖访客的 RTS 边缘推屏：指针距场景边缘阈值（视口像素）与推屏速度（视口像素/秒）。</summary>
        private const float EdgeScrollMargin = 56f;
        private const float EdgeScrollSpeed = 1100f;
        /// <summary>点击/拖拽的分界：松手时位移小于它才算一次点击（9px 见方）。平移、双击、开家具详情三处共用。</summary>
        private const float ClickThresholdSq = 81f;
        /// <summary>双击间隔（与家具模式的双击收纳同一个手感，见 FurnitureRoomController.DoubleClickSeconds）。</summary>
        private const float DoubleClickSeconds = .35f;

        private HubPage page;
        private RectTransform sceneRoot;
        private RectTransform worldRoot;
        private readonly RawImage[] roomArts = new RawImage[HubWorldGrid.RoomCount];
        private Image sceneWash;
        private Image ambientLight;
        private OutGameHubSceneOverlayView overlay;
        private OutGameVisitorStage stage;
        private RectTransform hotspotRoot;

        /// <summary>相机状态：世界根左下角相对视口左下角的偏移（视口坐标）与缩放。</summary>
        private Vector2 camPan;
        private float camZoom = 1f;
        private bool panning;
        private Vector2 lastPointerLocal;
        /// <summary>本次按下的起点与有效性（区分「点一下聚焦房间」和「按住拖拽平移」）。</summary>
        private Vector2 pressPointerLocal;
        private bool pressValid;
        private Tween focusTween;
        /// <summary>上一次空地点击的时刻与位置（双击判定用；家具上的点击不参与，见 §4.2）。</summary>
        private float lastGroundClickTime;
        private Vector2 lastGroundClickPointer;
        private static readonly List<RaycastResult> raycastCache = new List<RaycastResult>();

        private static CodexTable Codex => GameManager.Instance.CodexTable;

        public void Build(OutGameHubView view, HubPage owner)
        {
            page = owner;
            sceneRoot = view.sceneRoot;
            overlay = view.sceneOverlay;

            // 裁剪容器：世界总比视口大，缩放平移时裁掉溢出画面，避免盖住四周 UI
            var clip = HouseUIRuntime.Stretch(sceneRoot, "WorldClip");
            clip.gameObject.AddComponent<RectMask2D>();

            // 世界根：2× 视口大小（每个房间一个视口大），pivot 左下，位置/缩放即相机
            worldRoot = HouseUIRuntime.Rect(clip, "World", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            worldRoot.pivot = Vector2.zero;

            for (var room = 0; room < HubWorldGrid.RoomCount; room++)
            {
                var origin = HubWorldGrid.CellOrigin(room);
                var art = HouseUIRuntime.Rect(worldRoot, "RoomArt" + room,
                    origin, origin + new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
                roomArts[room] = art.gameObject.AddComponent<RawImage>();
                roomArts[room].raycastTarget = false; // 场景图不拦截指针：拖拽平移与热点/演员都依赖穿透
            }
            ApplySceneArt();

            // 洗色层盖在房间图之上、热点与演员之下（与旧版层序一致）；随世界一起缩放（纯色无所谓拉伸）
            sceneWash = HouseUIRuntime.StretchPanel(worldRoot, "SceneWash", new Color(.015f, .02f, .04f, .22f));
            sceneWash.raycastTarget = false;

            hotspotRoot = HouseUIRuntime.Stretch(worldRoot, "FurnitureHotspots");
            BuildHotspots();
            BuildVisitorStage();

            // 环境光层（2026-08-14 昼夜光照）：盖在房间图/热点/访客之上、场景框架 UI 之下，
            // 按局内时钟在色带上插值——清晨暖金→正午无色（原图即烈日基准）→黄昏橙红→入夜深蓝。
            // 纯表现件不拦截点击；随世界缩放平移，天然只影响场景不影响四周 UI。
            ambientLight = HouseUIRuntime.StretchPanel(worldRoot, "AmbientLight", Color.clear);
            ambientLight.raycastTarget = false;
            UpdateDayLight();

            BindOverlay();

            // 初始相机：满屏当前房间
            SnapToRoom(page.RoomIndex);
        }

        // ══════════ 昼夜光照 ══════════

        /// <summary>每帧按局内时钟推环境光（HubPage.OnUpdate 调；叠加层开着也走，面板后面的天色照常流动）。
        /// 色带定义在 HouseDayLight（与标题页封面共用）。</summary>
        public void UpdateDayLight()
        {
            if (ambientLight == null) return;
            var (tint, veil) = HouseDayLight.Now();
            for (var room = 0; room < roomArts.Length; room++)
                if (roomArts[room] != null) roomArts[room].color = tint;
            ambientLight.color = veil;
        }

        // ══════════ 相机 ══════════

        /// <summary>
        /// 每帧相机输入（HubPage.HandleInput 调，叠加层开着时页面输入被壳拦下、天然不抢滚轮）：
        /// 滚轮以鼠标为中心缩放；指针不在 UI 控件上时按住左键拖拽平移。
        /// </summary>
        public void HandleCamera()
        {
            if (worldRoot == null || sceneRoot == null) return;
            var viewport = sceneRoot.rect.size;
            if (viewport.x < 1f || viewport.y < 1f) return;
            SyncWorldSize(viewport);

            var pointerLocal = PointerInViewport(out var insideScene);

            var scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > .01f && insideScene)
            {
                KillFocusTween();
                var nextZoom = Mathf.Clamp(camZoom * (1f + scroll * .12f), MinZoom, MaxZoom);
                if (!Mathf.Approximately(nextZoom, camZoom))
                {
                    // 以鼠标为锚缩放：光标下的世界点在缩放前后保持不动
                    var worldAtPointer = (pointerLocal - camPan) / camZoom;
                    camZoom = nextZoom;
                    camPan = pointerLocal - worldAtPointer * camZoom;
                }
            }

            if (Input.GetMouseButtonDown(0) && insideScene && !IsPointerOverBlockingUI())
            {
                KillFocusTween();
                panning = true;
                pressValid = true;
                pressPointerLocal = pointerLocal;
                lastPointerLocal = pointerLocal;
            }
            if (Input.GetMouseButtonUp(0))
            {
                panning = false;
                // 位移够小才算一次点击；否则这一下是拖拽，只平移、什么都不触发（§4.1）
                if (pressValid && (pointerLocal - pressPointerLocal).sqrMagnitude < ClickThresholdSq)
                    HandleSceneClick(pointerLocal, viewport);
                pressValid = false;
            }
            if (panning)
            {
                camPan += pointerLocal - lastPointerLocal;
                lastPointerLocal = pointerLocal;
            }

            // RTS 边缘推屏（2026-08-14）：拖着访客顶到场景边缘时相机朝该方向平移；
            // uGUI 的 Drag 事件只在指针移动时触发，这里每帧重投影把访客钉在指针下
            if (stage != null && stage.HasActiveDrag)
            {
                var edge = Vector2.zero;
                if (pointerLocal.x < EdgeScrollMargin) edge.x = -1f;
                else if (pointerLocal.x > viewport.x - EdgeScrollMargin) edge.x = 1f;
                if (pointerLocal.y < EdgeScrollMargin) edge.y = -1f;
                else if (pointerLocal.y > viewport.y - EdgeScrollMargin) edge.y = 1f;
                if (edge != Vector2.zero)
                {
                    KillFocusTween();
                    camPan -= edge * (EdgeScrollSpeed * Time.unscaledDeltaTime);
                }
                ClampCamera(viewport);
                ApplyCamera();
                DetectCurrentRoom(viewport);
                stage.RefreshDragProjection();
                return; // 拖拽期间不再处理普通平移（按下起点已被演员射线挡掉，这里双保险）
            }

            ClampCamera(viewport);
            ApplyCamera();
            DetectCurrentRoom(viewport);
        }

        /// <summary>
        /// 一次点击落定（位移已确认够小）。两条分支互斥，无延迟、无待定计时器（§4.2）：
        ///   命中家具热点 → 打开那一件的详情。**家具上只认单击**
        ///   落在空地　　 → 走双击判定：总览态双击推该房满屏，房间态双击缩回总览
        ///
        /// 家具上不参与双击是为了避开一个死结：双击的第一击若落在家具上，单击已经把详情面板压栈了，
        /// 第二击就落到面板上，双击必然失效。规则对玩家也直观——家具是物件，地板是场景。
        /// </summary>
        private void HandleSceneClick(Vector2 pointerLocal, Vector2 viewport)
        {
            var hotspot = HotspotUnderPointer();
            if (hotspot != null)
            {
                page.OpenFurnitureDetail(hotspot.RoomIndex, hotspot.FurnitureId);
                lastGroundClickTime = 0f; // 家具上的点击不算进双击序列
                return;
            }
            var now = Time.unscaledTime;
            var isDouble = now - lastGroundClickTime < DoubleClickSeconds &&
                           (pointerLocal - lastGroundClickPointer).sqrMagnitude < ClickThresholdSq;
            if (!isDouble)
            {
                lastGroundClickTime = now;
                lastGroundClickPointer = pointerLocal;
                return;
            }
            lastGroundClickTime = 0f; // 消费掉，免得三连击又触发一次
            SfxManager.Play(ESfx.PageTransition); // 音效需求 #5：视野切换即转场
            if (camZoom < 1f)
            {
                // 总览态：推到双击处所在的房间满屏
                var worldPoint = (pointerLocal - camPan) / camZoom;
                var world01 = new Vector2(
                    Mathf.Clamp01(worldPoint.x / (viewport.x * 2f)),
                    Mathf.Clamp01(worldPoint.y / (viewport.y * 2f)));
                FocusRoom(HubWorldGrid.RoomAt(world01));
            }
            else
            {
                ZoomToOverview();
            }
        }

        /// <summary>
        /// 指针正下方最上层的家具热点；没有（或最上层是别的 UI）时返回 null。
        ///
        /// 取**最上层**而不是"命中列表里有没有"：热点上面若压着访客演员或四周卡片，
        /// 那一下点击属于它们，不该被热点抢走。
        /// </summary>
        private static HubFurnitureHotspot HotspotUnderPointer()
        {
            if (EventSystem.current == null) return null;
            var data = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
            raycastCache.Clear();
            EventSystem.current.RaycastAll(data, raycastCache);
            if (raycastCache.Count == 0) return null;
            return raycastCache[0].gameObject.GetComponentInParent<HubFurnitureHotspot>();
        }

        /// <summary>缩回总览（zoom 0.5 = 四个房间尽收眼底）。此时世界尺寸恰等于视口，平移会被 ClampCamera 钳成 0。</summary>
        private void ZoomToOverview()
        {
            if (worldRoot == null || sceneRoot == null) return;
            KillFocusTween();
            var fromZoom = camZoom;
            var fromPan = camPan;
            focusTween = DOTween.To(() => 0f, t =>
            {
                if (sceneRoot == null || worldRoot == null) { KillFocusTween(); return; }
                camZoom = Mathf.Lerp(fromZoom, MinZoom, t);
                camPan = Vector2.Lerp(fromPan, Vector2.zero, t);
                ClampCamera(sceneRoot.rect.size);
                ApplyCamera();
                // 刻意**不**调 DetectCurrentRoom：总览态没有当前房间，沿用缩回前的那间（§4.4）
            }, 1f, .55f).SetEase(Ease.InOutCubic).SetUpdate(true);
        }

        /// <summary>房间导航/方向键切换：相机平滑推到目标房间（1 倍缩放满屏该房间）。</summary>
        public void FocusRoom(int roomIndex)
        {
            if (worldRoot == null || sceneRoot == null) return;
            var viewport = sceneRoot.rect.size;
            if (viewport.x < 1f) { SnapToRoom(roomIndex); return; }
            SyncWorldSize(viewport);
            var targetZoom = Mathf.Max(1f, camZoom);
            var targetPan = PanCenteredOn(roomIndex, targetZoom, viewport);
            KillFocusTween();
            var fromPan = camPan;
            var fromZoom = camZoom;
            focusTween = DOTween.To(() => 0f, t =>
            {
                // 页面可能在推镜途中被销毁（闭包补间不随对象自动回收，此处自杀兜底）
                if (sceneRoot == null || worldRoot == null)
                {
                    KillFocusTween();
                    return;
                }
                camZoom = Mathf.Lerp(fromZoom, targetZoom, t);
                camPan = Vector2.Lerp(fromPan, targetPan, t);
                var size = sceneRoot.rect.size;
                ClampCamera(size);
                ApplyCamera();
                DetectCurrentRoom(size);
            }, 1f, .55f).SetEase(Ease.InOutCubic).SetUpdate(true);
        }

        /// <summary>无动画直达（建层初始化/布局未就绪时的回退）。</summary>
        private void SnapToRoom(int roomIndex)
        {
            var viewport = sceneRoot.rect.size;
            if (viewport.x < 1f) viewport = new Vector2(1920f, 1080f); // 首帧布局未算完，用设计分辨率近似
            SyncWorldSize(viewport);
            camZoom = 1f;
            camPan = PanCenteredOn(roomIndex, camZoom, viewport);
            ClampCamera(viewport);
            ApplyCamera();
        }

        /// <summary>让某房间中心对准视口中心时的平移量。</summary>
        private Vector2 PanCenteredOn(int roomIndex, float zoom, Vector2 viewport)
        {
            var worldCenter01 = HubWorldGrid.CellOrigin(roomIndex) + new Vector2(.25f, .25f);
            var worldPoint = Vector2.Scale(worldCenter01, viewport * 2f); // 世界尺寸 = 2× 视口
            return viewport * .5f - worldPoint * zoom;
        }

        /// <summary>世界根尺寸恒为 2× 视口（分辨率变化时跟随）。</summary>
        private void SyncWorldSize(Vector2 viewport)
        {
            var target = viewport * 2f;
            if ((worldRoot.sizeDelta - target).sqrMagnitude > .5f) worldRoot.sizeDelta = target;
        }

        private void ApplyCamera()
        {
            worldRoot.localScale = new Vector3(camZoom, camZoom, 1f);
            worldRoot.anchoredPosition = camPan;
        }

        /// <summary>把世界钳在视口内：任何缩放下画面边缘都不露底。</summary>
        private void ClampCamera(Vector2 viewport)
        {
            camZoom = Mathf.Clamp(camZoom, MinZoom, MaxZoom);
            var worldSize = viewport * 2f * camZoom;
            camPan.x = Mathf.Clamp(camPan.x, viewport.x - worldSize.x, 0f);
            camPan.y = Mathf.Clamp(camPan.y, viewport.y - worldSize.y, 0f);
        }

        /// <summary>
        /// 视口中心落在哪个象限即当前房间；变化时回调页面刷新导航与说明卡。
        ///
        /// **总览态直接短路**（§4.4）：zoom 0.5 时视口中心恰好落在世界正中心 (0.5, 0.5)，
        /// 而 HubWorldGrid.RoomAt 的边界判定是 `>=`，于是恒返回房间 1（卧室）——
        /// 导航高亮、说明卡、以及点「家具摆放」进哪间房全会跟着跳。总览态本就没有「当前房间」
        /// 这个概念，沿用缩回前的那间是最小意外；双击选房时 zoom 推回 1，这里自然恢复工作。
        /// </summary>
        private void DetectCurrentRoom(Vector2 viewport)
        {
            if (camZoom < 1f) return;
            var worldPoint = (viewport * .5f - camPan) / camZoom;
            var world01 = new Vector2(
                Mathf.Clamp01(worldPoint.x / (viewport.x * 2f)),
                Mathf.Clamp01(worldPoint.y / (viewport.y * 2f)));
            var room = HubWorldGrid.RoomAt(world01);
            if (room != page.RoomIndex)
            {
                page.NotifyCameraRoomChanged(room);
                BindOverlay();
            }
        }

        /// <summary>鼠标在场景视口里的位置（视口左下为原点）。</summary>
        private Vector2 PointerInViewport(out bool inside)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                sceneRoot, Input.mousePosition, null, out var local);
            var rect = sceneRoot.rect;
            inside = rect.Contains(local);
            return new Vector2(local.x - rect.xMin, local.y - rect.yMin);
        }

        /// <summary>
        /// 指针是否压在会吃点击的 UI 上（演员/四周卡片）：是则不启动拖拽平移。场景图本身不吃射线。
        ///
        /// **家具热点刻意不算「挡住」**（§4.1）：它铺满了房间里相当一部分面积，一刀切的话
        /// 在家具上按下就完全没法平移相机，连缩小后的双击选房也一起失效。
        /// 它该不该消费这一下点击，改由松手时的位移阈值裁决（见 HandleSceneClick）。
        /// </summary>
        private static bool IsPointerOverBlockingUI()
        {
            if (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()) return false;
            return HotspotUnderPointer() == null;
        }

        private void KillFocusTween()
        {
            if (focusTween != null && focusTween.IsActive()) focusTween.Kill();
            focusTween = null;
        }

        /// <summary>页面退出时的清理（HubPage.OnExit 调）：杀掉仍在跑的推镜补间，防目标销毁后空跑。</summary>
        public void Dispose() => KillFocusTween();

        // ══════════ 内容 ══════════

        /// <summary>房间背景 = 家具布局合成图（背景+当前摆放；缺失时立即烘焙——一进游戏默认家具就可见）。</summary>
        public void ApplySceneArt()
        {
            for (var room = 0; room < HubWorldGrid.RoomCount; room++)
            {
                if (roomArts[room] == null) continue;
                var baked = FurnitureSceneComposer.EnsureBaked(room);
                if (baked != null) roomArts[room].texture = baked;
            }
        }

        /// <summary>观景模式切换的场景侧表现：洗色层显隐（相机不复位，视角保持玩家现状）。</summary>
        public void SetImmersiveVisual(bool on)
        {
            panning = false;
            if (sceneWash != null)
            {
                var washGroup = HouseUIUtil.Group(sceneWash.gameObject);
                washGroup.DOKill();
                washGroup.DOFade(on ? 0f : 1f, .25f).SetUpdate(true);
            }
        }

        /// <summary>家具摆放退出后：重烘焙当前房间背景、重建热点、刷新说明卡的装饰分。</summary>
        public void RefreshAfterFurniture()
        {
            FurnitureSceneComposer.RequestBake(page.RoomIndex, _ =>
            {
                ApplySceneArt();
                BuildHotspots();
                // 说明卡平时只在建层与换房时刷新，而刚摆完家具正是装饰分变化的那一刻
                BindOverlay();
            });
        }

        /// <summary>四个房间的已摆放家具热点：悬停弹提示卡，点击暂接设备面板（3.5c）。
        /// 热点建在世界坐标上，跟随相机无需逐帧换算。</summary>
        private void BuildHotspots()
        {
            if (hotspotRoot == null) return;
            for (var i = hotspotRoot.childCount - 1; i >= 0; i--)
                Object.Destroy(hotspotRoot.GetChild(i).gameObject);
            for (var room = 0; room < HubWorldGrid.RoomCount; room++)
            {
                foreach (var info in FurnitureSceneComposer.GetPlacedFurniture(room))
                {
                    var viewport = info.ViewportRect; // 房内归一化矩形
                    var min = HubWorldGrid.RoomToWorld(room, viewport.min);
                    var max = HubWorldGrid.RoomToWorld(room, viewport.max);
                    var hotspot = HouseUIRuntime.Rect(hotspotRoot, $"Hotspot_{room}_{info.Entry.id}",
                        min, max, Vector2.zero, Vector2.zero);
                    var image = hotspot.gameObject.AddComponent<Image>();
                    image.sprite = HouseUIRuntime.WhiteSprite;
                    image.color = Color.clear;
                    // **不挂 Button**（§4.1）：Button 没有拖拽阈值，拖完在同一热点松手照样触发 onClick。
                    // 热点只吃射线（供悬停提示卡）并携带「哪个房间的哪件家具」，点击裁决在相机层
                    var marker = hotspot.gameObject.AddComponent<HubFurnitureHotspot>();
                    marker.RoomIndex = room;
                    marker.FurnitureId = info.Entry.id;

                    var card = HouseUIRuntime.Panel(hotspot, "Card", new Vector2(.5f, 1),
                        new Vector2(0, 46), new Vector2(250, 76), new Color(.32f, .06f, .18f, .92f));
                    HouseUIRuntime.StretchLabel(card.transform, "Text",
                        $"＋  {info.Entry.displayName}\n<size=13>查看家具</size>", 19, HouseUIUtil.White,
                        TextAnchor.MiddleCenter, FontStyle.Bold);
                    var cardGroup = HouseUIUtil.Group(card.gameObject, 0f);
                    cardGroup.blocksRaycasts = false;
                    cardGroup.interactable = false;

                    var trigger = hotspot.gameObject.AddComponent<EventTrigger>();
                    var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                    enter.callback.AddListener(_ => { cardGroup.DOKill(); cardGroup.DOFade(1f, .16f).SetUpdate(true); });
                    trigger.triggers.Add(enter);
                    var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                    exit.callback.AddListener(_ => { cardGroup.DOKill(); cardGroup.DOFade(0f, .16f).SetUpdate(true); });
                    trigger.triggers.Add(exit);
                }
            }
        }

        /// <summary>整体重建舞台（GM 重置后访客重新进场）。庆祝/离场等状态表现由舞台层轮询实例状态自驱（§9）。</summary>
        public void RebuildStage() => BuildVisitorStage();

        /// <summary>重建访客 NPC 层（覆盖整个四宫格世界）。舞台只读 VisitorManager 状态，表现不回写业务（§16.4）；
        /// 拖拽换房经页面回调走业务方法。</summary>
        private void BuildVisitorStage()
        {
            if (worldRoot == null) return;
            stage = OutGameVisitorStage.Build(worldRoot, page.OnVisitorClicked, page.OnVisitorDropped);
        }

        /// <summary>场景说明卡与设备热点按钮（Prefab 字段可能因手动编辑缺失，逐项判空）。相机换房时刷新。</summary>
        public void BindOverlay()
        {
            if (overlay == null) return;
            var room = Codex.rooms[page.RoomIndex];
            // 房间装饰分（家具库存说明 §6.3）：它决定这间房的客人完成服务后多给多少小费，
            // 玩家得能在不进家具模式的情况下看到它
            if (overlay.captionHeader != null)
                overlay.captionHeader.text =
                    $"CURRENT ROOM / 04 · 装饰分 {FurniturePlacementQuery.DecorationScoreOf(page.RoomIndex)}";
            if (overlay.roomName != null) overlay.roomName.text = room.displayName;
            if (overlay.roomNote != null) overlay.roomNote.text = room.note;
            // 原型残留的假热点已删除（2026-08-15）：它的标题按房间下标写死「手冲咖啡台 / 旧书检索机 /
            // 黑胶唱机」，与房里实际摆了什么毫无关系。真热点按实际摆放逐件生成，见 BuildHotspots
            if (overlay.hotspotButton != null) overlay.hotspotButton.gameObject.SetActive(false);
        }
    }
}

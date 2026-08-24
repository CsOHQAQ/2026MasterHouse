using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using F = MasterHouse.HouseUIRuntime; // §16.7 毒点②已断：不再依赖退役中的 OutGameUIFactory

namespace MasterHouse
{
    /// <summary>
    /// 家具摆放模式：配置表驱动的房间就地编辑。
    /// 摆放（网格吸附 + 绿/红占位预览）、收纳（拖回收纳栏/双击，桌面家具级联）、解锁（HOUSE CREDIT），
    /// 地面家具按底边深度行排层级；背景为透视相机下的 3D 感平面，带远景模糊与拖拽失焦景深。
    /// </summary>
    public sealed class FurnitureRoomController : MonoBehaviour
    {
        private const float PixelsPerUnit = 100f;
        private const float CameraFov = 25f;
        private const float SnapMarginPx = 50f;
        private const float DoubleClickSeconds = .35f;

        // 家具落地投影：美术软阴影贴图 + 相对家具显示宽的外扩系数（略宽一圈，让底边外能看见影缘）
        internal const string ShadowSpritePath = "OutGameUI/furniture-shadow";
        internal const float ShadowWidthScale = 1.08f;

        // 渲染次序（同一 sortingLayer 内）
        private const int OrderBackground = 0;
        private const int OrderNightBackground = 1;
        private const int OrderDepthBlur = 2;
        private const int OrderFocusBlur = 4;
        private const int OrderWallGrid = 10;
        private const int OrderWallItemBase = 20;
        private const int OrderFloorGrid = 60;
        /// <summary>可叠放家具（地毯类）的渲染带：压在所有立式地面家具（OrderFloorItemBase 起）之下。</summary>
        private const int OrderFloorStackableBase = 70;
        private const int OrderFloorItemBase = 100;
        /// <summary>昼夜罩色层：压在全部场景内容之上（家具层序 100+n 远够不到 500）。</summary>
        private const int OrderDayVeil = 500;
        private const float ZDayVeil = .05f;
        private const int OrderGhost = 400;

        /// <summary>
        /// 家具场景独占的层（Unity 内置的空闲层 3）：House UI 画布 2026-08-20 改成
        /// ScreenSpaceCamera 之后，UI 相机与家具相机得各画各的，靠层分开。
        /// </summary>
        internal const int FurnitureSceneLayer = 3;

        // 分层 Z 偏移（只用于相机视差，绘制次序由 sortingOrder 决定）
        private const float ZBackground = .15f;
        private const float ZNightBackground = .145f;
        private const float ZDepthBlur = .14f;
        private const float ZFocusBlur = .13f;
        private const float ZWall = .07f;
        private const float ZFloorPerRow = -.022f;
        private const float ZGhost = -.6f;

        /// <summary>过渡桥接：家具模式读写新 Economy 模块（§16.3）；GameManager 由 OutGameBootstrap 保证存在。</summary>
        private static EconomyManager Economy => GameManager.Instance.EconomyManager;

        /// <summary>会话内保留的摆放布局（进出模式不丢，重开进程重置）。货币/声望/所有权由 Economy 模块统一管理。</summary>
        private sealed class SessionState
        {
            public readonly List<FurniturePlacementConfig> Placements = new List<FurniturePlacementConfig>();
        }

        private sealed class DragState
        {
            public FurnitureEntry Entry;
            public FurnitureRuntimeItem Item;      // null = 从收纳栏拖出
            public bool Flipped;                   // F 键左右镜像
            public FurnitureRuntimeGrid CandidateGrid;
            public int CandidateCol;
            public int CandidateRow;
            public bool CandidateOk;
            public bool OverInventory;
            public GameObject Ghost;
            public SpriteRenderer GhostRenderer;
        }

        private static FurnitureRoomController active;

        /// <summary>会话布局按房间 id 分桶（家具模式随 Hub 当前房间动态加载）；未编辑过的房间沿用其默认摆放。</summary>
        private static readonly Dictionary<string, SessionState> sessions = new Dictionary<string, SessionState>();

        private FurnitureTable furnitureTable;
        private FurnitureRoomEntry room;
        /// <summary>当前编辑的房间下标（已解析，越界已回落 0）。业务侧查询（装饰分/需求）都按下标走。</summary>
        private int roomIndex;
        private Action onClosed;
        private bool closing;

        private Transform stageRoot;
        private Vector3 stageOrigin;
        private FurnitureCameraRig rig;
        private FurnitureRoomHud hud;
        private SpriteRenderer focusBlurRenderer;
        private SpriteRenderer backgroundRenderer;
        private SpriteRenderer nightBackgroundRenderer;
        private SpriteRenderer depthBlurRenderer;
        private SpriteRenderer dayVeilRenderer;
        private bool usesPlacementBackground;

        private readonly Dictionary<string, FurnitureRuntimeGrid> grids = new Dictionary<string, FurnitureRuntimeGrid>();
        private readonly Dictionary<string, FurnitureRuntimeItem> items = new Dictionary<string, FurnitureRuntimeItem>();
        private int nextItemId = 1;
        private bool gridToggleOn;
        private DragState drag;
        private string lastClickItemId;
        private float lastClickTime;

        /// <summary>打开家具模式并加载指定房间（随 Hub 当前房间动态加载；下标越界回落 0）。配置表缺失时返回 false。
        /// onStoreRequested：玩家点「购买家具」→ 本模式先正常关闭（含 onClosed 收尾），随后由页面打开商店。</summary>
        public static bool Open(int roomIndex, Action onClosed, Action onStoreRequested = null)
        {
            if (active != null) return true;
            // 家具表并入 Def 体系（§16.7）：统一由 GameManager 加载
            var furniture = GameManager.Instance.FurnitureTable;
            var rooms = GameManager.Instance.FurnitureRoomTable;
            if (furniture == null || rooms == null || rooms.rooms.Count == 0)
            {
                Debug.LogWarning("[Furniture] 配置表缺失，请先执行菜单 MasterHouse → 家具系统 → 创建配置表。");
                return false;
            }
            // 越界回落 0——**存解析后的下标**再传下去：存原始入参会让 HUD 显示别的房间的装饰分
            var resolvedIndex = roomIndex >= 0 && roomIndex < rooms.rooms.Count ? roomIndex : 0;
            var roomEntry = rooms.rooms[resolvedIndex];
            if (roomEntry == null)
            {
                Debug.LogWarning($"[Furniture] 房间配置为空（下标 {roomIndex}），无法进入家具模式。");
                return false;
            }
            // 家具场景独占一层：UI 相机只画 UI 层，这层归家具相机（2026-08-20 画布转 Camera 模式）
            var go = new GameObject("FurnitureRoomMode") { layer = FurnitureSceneLayer };
            active = go.AddComponent<FurnitureRoomController>();
            active.Init(furniture, roomEntry, resolvedIndex, onClosed, onStoreRequested);
            return true;
        }

        private void Init(FurnitureTable furniture, FurnitureRoomEntry roomEntry, int resolvedRoomIndex,
            Action closedCallback, Action onStoreRequested = null)
        {
            furnitureTable = furniture;
            room = roomEntry;
            roomIndex = resolvedRoomIndex;
            onClosed = closedCallback;
            // 把舞台放到远离节点玩法棋盘的位置，避免主相机内容互相穿帮。
            stageOrigin = new Vector3(500f, 0f, 0f);

            stageRoot = new GameObject("FurnitureStage") { layer = FurnitureSceneLayer }.transform;
            stageRoot.position = stageOrigin;

            BuildBackground();
            BuildCamera();
            BuildGrids();

            hud = new FurnitureRoomHud();
            hud.Build(furnitureTable, GetSlotState, RemainingOf, Economy.PriceOf, Economy.UnlockReputationOf,
                Economy.SellbackValueOf);
            hud.ExitClicked += Close;
            // 购买家具：先正常关闭（存布局、恢复壳 Canvas），再由页面开商店
            hud.StoreClicked += () => { Close(); onStoreRequested?.Invoke(); };
            hud.GridToggleClicked += ToggleGrids;
            hud.SlotPressed += OnSlotPressed;
            hud.SellPressed += OnSellPressed;
            hud.PurchaseConfirmed += OnPurchaseConfirmed;
            hud.SellConfirmed += OnSellConfirmed;
            Economy.Changed += OnEconomyChanged;

            RestoreState();
            RecomputeDecorationScore();
            OnEconomyChanged();
            hud.RefreshInventory();
            hud.SetGridToggle(false);
            RefreshAllGridColors();

            // 进入时闪现全部网格，提示可编辑区域。
            foreach (var grid in grids.Values) grid.SetVisible(true);
            DOVirtual.DelayedCall(1.6f, ApplyGridVisibility).SetTarget(this);
            // 右键出售是个没有视觉入口的隐藏手势，进场提示一次（家具库存说明 §10 待确认 #3）
            hud.ShowToast("右键收纳栏里的家具可以半价出售");
        }

        #region 舞台搭建

        private void BuildBackground()
        {
            var placementBackground = PlacementBackground();
            usesPlacementBackground = placementBackground != room.background;
            backgroundRenderer = CreateLayer("Background", placementBackground, OrderBackground, ZBackground, 1f,
                fitWidth: usesPlacementBackground);
            var nightBackground = PlacementNightBackground();
            if (nightBackground != null)
            {
                nightBackgroundRenderer = CreateLayer("NightBackground", nightBackground,
                    OrderNightBackground, ZNightBackground, 0f, fitWidth: usesPlacementBackground);
            }
            else Debug.LogWarning($"[Furniture] 夜间房间图缺失：room {roomIndex + 1}");
            if (room.depthBlurOverlay != null)
                depthBlurRenderer = CreateLayer("DepthBlur", room.depthBlurOverlay, OrderDepthBlur, ZDepthBlur, 1f);
            if (room.focusBlurOverlay != null)
                focusBlurRenderer = CreateLayer("FocusBlur", room.focusBlurOverlay, OrderFocusBlur, ZFocusBlur, 0f);
            // 昼夜光照（2026-08-16）：与 Hub/标题页同一条 HouseDayLight 色带——
            // 背景与家具乘调色；深夜罩色单独一层盖在全部场景内容之上（HUD 是独立 Canvas 不受影响）
            dayVeilRenderer = CreateLayer("DayVeil", HouseUIRuntime.WhiteSprite, OrderDayVeil, ZDayVeil, 0f);
            ApplyDayLight();
        }

        /// <summary>
        /// 放置模式背景由 FurnitureHudPage Prefab 持有，和主宅总览房间表分离。
        /// Prefab 没配或索引越界时回退到房间表背景，保证旧资源仍可正常进入布置模式。
        /// </summary>
        private Sprite PlacementBackground()
        {
            var prefab = Resources.Load<GameObject>(OutGamePrefabResourcePaths.FurnitureHud);
            var view = prefab != null ? prefab.GetComponent<OutGameFurnitureHudView>() : null;
            var backgrounds = view != null ? view.roomBackgrounds : null;
            return backgrounds != null && roomIndex >= 0 && roomIndex < backgrounds.Length &&
                   backgrounds[roomIndex] != null
                ? backgrounds[roomIndex]
                : room.background;
        }

        private Sprite PlacementNightBackground()
        {
            var prefab = Resources.Load<GameObject>(OutGamePrefabResourcePaths.FurnitureHud);
            var view = prefab != null ? prefab.GetComponent<OutGameFurnitureHudView>() : null;
            var backgrounds = view != null ? view.roomNightBackgrounds : null;
            if (backgrounds != null && roomIndex >= 0 && roomIndex < backgrounds.Length &&
                backgrounds[roomIndex] != null)
                return backgrounds[roomIndex];
            return usesPlacementBackground
                ? null
                : Resources.Load<Sprite>($"OutGameUI/RoomNight/room-night-{roomIndex + 1:00}");
        }

        /// <summary>网格当前是按哪个夜色权重建的（夜色推移超过一档就重建）。</summary>
        private float gridNightAlpha = -1f;
        private const float GridRebuildStep = .02f;

        /// <summary>按当前夜色权重重建网格并重摆已有家具（格子的行列语义不变，只是画到了新位置）。</summary>
        private void RebuildGridsForNight()
        {
            foreach (var grid in grids.Values) grid?.Destroy();
            grids.Clear();
            BuildGrids();
            foreach (var item in items.Values) LayoutItem(item);
        }

        /// <summary>每帧按局内时钟推昼夜光照（乘法调色保对比度；只改 rgb、保留各层自己的透明度动画）。</summary>
        private void ApplyDayLight()
        {
            var (tint, veil) = HouseDayLight.Now();
            var nightAlpha = HouseDayLight.NightRoomAlphaNow();
            // Placement night art now shares the daytime room's exact crop and
            // dimensions, so changing light must not rebuild or move the grids.
            var geometryNightAlpha = usesPlacementBackground ? 0f :
                (nightBackgroundRenderer != null ? nightAlpha : 0f);
            // 夜色推移到一定程度就按新几何重建网格并重摆家具（每帧重建太浪费，0.02 一档肉眼看不出跳）
            if (Mathf.Abs(geometryNightAlpha - gridNightAlpha) > GridRebuildStep) RebuildGridsForNight();
            // 放置模式的完整夜图接管时同步退掉白天底图，避免白天房间框从夜图上下方露出，
            // 也避免两套房间画面叠加后被误认为一层前景遮罩。
            var hasNightBackground = nightBackgroundRenderer != null && nightBackgroundRenderer.sprite != null;
            if (backgroundRenderer != null)
            {
                var dayAlpha = hasNightBackground ? 1f - nightAlpha : 1f;
                backgroundRenderer.color = new Color(tint.r, tint.g, tint.b, dayAlpha);
            }
            if (hasNightBackground)
                nightBackgroundRenderer.color = new Color(1f, 1f, 1f, nightAlpha);
            TintPreserveAlpha(depthBlurRenderer, tint);
            TintPreserveAlpha(focusBlurRenderer, tint);
            foreach (var item in items.Values)
                TintPreserveAlpha(item.Renderer, tint);
            // 夜图本身已经完成蓝调与灯光，浮现时同步退掉旧的纯色夜罩，避免双重压暗。
            if (dayVeilRenderer != null) dayVeilRenderer.color = Color.Lerp(veil, Color.clear, nightAlpha);
        }

        private static void TintPreserveAlpha(SpriteRenderer renderer, Color tint)
        {
            if (renderer == null) return;
            renderer.color = new Color(tint.r, tint.g, tint.b, renderer.color.a);
        }

        private SpriteRenderer CreateLayer(string name, Sprite sprite, int order, float z, float alpha,
            bool fitWidth = false)
        {
            var go = new GameObject(name) { layer = FurnitureSceneLayer };
            go.transform.SetParent(stageRoot, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = order;
            renderer.color = new Color(1f, 1f, 1f, alpha);
            if (sprite == null)
            {
                Debug.LogWarning($"[Furniture] 房间背景层 {name} 精灵缺失。");
                return renderer;
            }
            renderer.sprite = sprite;
            go.transform.position = PxToWorld(room.sceneWidth * .5f, room.sceneHeight * .5f, z);
            go.transform.localScale = fitWidth
                ? SpriteScaleByWidth(sprite, room.sceneWidth)
                : SpriteScale(sprite, room.sceneWidth, room.sceneHeight);
            return renderer;
        }

        private void BuildCamera()
        {
            var pivot = PxToWorld(room.sceneWidth * .5f, room.sceneHeight * .5f, 0f);
            var halfExtents = new Vector2(room.sceneWidth, room.sceneHeight) / PixelsPerUnit * .5f;
            rig = new GameObject("FurnitureCameraRig").AddComponent<FurnitureCameraRig>();
            rig.Init(pivot, halfExtents, CameraFov); // 初始完整显示背景；滚轮缩放 + 右键/中键平移在吊架内
            // 背景 = Hub 的定格画面（2026-08-20：摆放时背景别变黑/变天空）。
            // 调用方（HubPage）在进场前已把 UI 同步藏干净、拍完立刻整块关画布——
            // 所以这里同步拍即可，拍到的一定是干净的场景
            var houseUi = HouseUIManager.Instance;
            if (houseUi != null && houseUi.Canvas != null)
                rig.CaptureBackdrop(houseUi.Canvas.worldCamera);
        }

        private void BuildGrids()
        {
            gridNightAlpha = usesPlacementBackground ? 0f : FurnitureNightLayout.NightAlphaNow();
            foreach (var config in room.grids)
            {
                if (config == null || string.IsNullOrEmpty(config.id)) continue;
                var order = config.surface == FurnitureSurfaceType.Floor ? OrderFloorGrid : OrderWallGrid;
                var z = config.surface == FurnitureSurfaceType.Floor ? 0f : ZWall - .01f;
                // 房间表中的网格已经按四张完整放置背景逐张标定；旧背景回退时才做昼夜几何校正。
                var effectiveConfig = usesPlacementBackground
                    ? config
                    : FurnitureNightLayout.Adjust(room, config, gridNightAlpha);
                var grid = new FurnitureRuntimeGrid(
                    effectiveConfig, PxToWorld, z);
                grid.BuildVisual(stageRoot, F.WhiteSprite, order);
                grids[grid.Id] = grid;
            }
            foreach (var blocked in room.blockedCells)
                if (blocked != null && grids.TryGetValue(blocked.gridId, out var grid))
                    grid.MarkSceneBlocked(blocked.col, blocked.row);
        }

        #endregion

        #region 坐标换算

        /// <summary>
        /// 把精灵缩放到指定的场景像素尺寸。必须用 bounds（世界单位）而非 rect（导入后像素）：
        /// 原图超过导入 Max Size 被降采样时，rect 变小但 bounds 不变，用 rect 会把大图放大（如 5120px 房间背景放大 2.5 倍）。
        /// 房间图层会按指定宽高铺满；家具另走 FurnitureScale，按配置的实际宽高独立校正两轴。
        /// </summary>
        private static Vector3 SpriteScale(Sprite sprite, float scenePxWidth, float scenePxHeight)
        {
            // 按**实际图形区**（网格顶点包络）填框，不按整张画布（2026-08-20 修「新家具缩窄」）：
            // 新素材普遍放在 1024 大画布里、横向留白很多，按画布算会把留白也算进宽度。
            // FullRect 网格的顶点包络就是整картина布，老素材行为不变。
            var size = TightSize(sprite);
            return new Vector3(
                scenePxWidth / PixelsPerUnit / Mathf.Max(1e-4f, size.x),
                scenePxHeight / PixelsPerUnit / Mathf.Max(1e-4f, size.y), 1f);
        }

        /// <summary>按场景宽度等比缩放完整背景；相机初始视野会按屏幕比例补足上下区域。</summary>
        private static Vector3 SpriteScaleByWidth(Sprite sprite, float scenePxWidth)
        {
            var size = TightSize(sprite);
            var scale = scenePxWidth / PixelsPerUnit / Mathf.Max(1e-4f, size.x);
            return new Vector3(scale, scale, 1f);
        }

        /// <summary>sprite 图形区的世界尺寸（顶点包络；导入降采样不影响，顶点保持设计尺寸）。</summary>
        private static Vector2 TightSize(Sprite sprite)
        {
            var vertices = sprite.vertices;
            if (vertices == null || vertices.Length == 0) return sprite.bounds.size;
            Vector2 min = vertices[0], max = vertices[0];
            foreach (var v in vertices)
            {
                min = Vector2.Min(min, v);
                max = Vector2.Max(max, v);
            }
            return max - min;
        }

        /// <summary>
        /// 家具按表里的实际宽高分别缩放两轴。素材画面比例不等于家具现实比例时，
        /// 策划可直接用显示宽高做校正；源尺寸仍取 Tight Sprite，透明画布留白不参与计算。
        /// </summary>
        private static Vector3 FurnitureScale(FurnitureEntry entry)
        {
            var source = FurnitureDisplaySizing.TightSize(entry.sprite);
            var display = FurnitureDisplaySizing.Resolve(entry);
            return new Vector3(
                display.x / PixelsPerUnit / Mathf.Max(1e-4f, source.x),
                display.y / PixelsPerUnit / Mathf.Max(1e-4f, source.y),
                1f);
        }

        /// <summary>场景像素（左上原点、Y 向下）→ 世界坐标。</summary>
        private Vector3 PxToWorld(float px, float py, float z)
        {
            return stageOrigin + new Vector3(px / PixelsPerUnit, (room.sceneHeight - py) / PixelsPerUnit, z);
        }

        /// <summary>鼠标 → 舞台平面（z=stageOrigin.z）上的场景像素坐标。</summary>
        private Vector2 MouseScenePx()
        {
            var ray = rig.Camera.ScreenPointToRay(Input.mousePosition);
            if (Mathf.Abs(ray.direction.z) < 1e-5f) return Vector2.zero;
            var point = ray.GetPoint((stageOrigin.z - ray.origin.z) / ray.direction.z);
            return new Vector2((point.x - stageOrigin.x) * PixelsPerUnit,
                room.sceneHeight - (point.y - stageOrigin.y) * PixelsPerUnit);
        }

        #endregion

        #region 家具实例

        private static string TableGridId(string itemId) => "tbl_" + itemId;

        // 家具族表直接保存细分后的最终占格，不再在运行时暗乘 2；这样策划表里看到的数值
        // 就是拖拽预览与碰撞实际使用的格数。桌面格是一条单行承载带，纵向固定占 1 行。
        private static int FootprintCols(FurnitureRuntimeGrid grid, FurnitureEntry entry) =>
            entry.cols;

        private static int FootprintRows(FurnitureRuntimeGrid grid, FurnitureEntry entry) =>
            grid.Surface == FurnitureSurfaceType.Table ? 1 : entry.rows;

        private List<FurnitureRuntimeItem> ChildrenOf(FurnitureRuntimeItem host)
        {
            var result = new List<FurnitureRuntimeItem>();
            var gridId = TableGridId(host.Id);
            foreach (var item in items.Values)
                if (item.GridId == gridId) result.Add(item);
            return result;
        }

        private void AnchorOf(FurnitureRuntimeItem item, out float leftPx, out float bottomPy, out int order, out float z)
        {
            var grid = grids[item.GridId];
            var entry = item.Entry;
            var display = FurnitureDisplaySizing.Resolve(entry);
            var footCols = FootprintCols(grid, entry);
            var footRows = FootprintRows(grid, entry);
            leftPx = grid.X + item.Col * grid.CellWidth + (footCols * grid.CellWidth - display.x) * .5f;
            if (grid.Surface == FurnitureSurfaceType.Floor)
            {
                var bottomRow = item.Row + footRows;
                // 2.5D 假透视：家具中心随其底边行向网格中心收拢（与格子视觉同一映射）
                leftPx = grid.MapX(leftPx + display.x * .5f, bottomRow) - display.x * .5f;
                bottomPy = grid.Y + bottomRow * grid.CellHeight;
                // 可叠放（地毯）平铺地面：始终压在立式家具之下（带内仍按深度行排前后）
                order = entry.stackable ? OrderFloorStackableBase + bottomRow : OrderFloorItemBase + bottomRow * 10;
                z = ZFloorPerRow * bottomRow;
                return;
            }
            if (grid.Surface == FurnitureSurfaceType.Wall)
            {
                bottomPy = grid.Y + (item.Row + footRows) * grid.CellHeight;
                order = OrderWallItemBase + item.Row + footRows;
                z = ZWall;
                return;
            }
            // 桌面家具：跟随宿主层级
            var host = items[grids[item.GridId].HostItemId];
            AnchorOf(host, out _, out _, out var hostOrder, out var hostZ);
            bottomPy = grid.Y + grid.CellHeight;
            order = hostOrder + 3;
            z = hostZ - .02f;
        }

        private void LayoutItem(FurnitureRuntimeItem item)
        {
            AnchorOf(item, out var left, out var bottom, out var order, out var z);
            var display = FurnitureDisplaySizing.Resolve(item.Entry);
            item.Root.transform.position = PxToWorld(
                left + display.x * .5f, bottom - display.y * .5f, z);
            item.Renderer.sortingOrder = order;
            if (item.Shadow != null) item.Shadow.sortingOrder = order - 1; // 投影压在自己脚下、盖住地毯
        }

        /// <summary>
        /// 家具落地投影：直接用美术给的软阴影贴图（<see cref="ShadowSpritePath"/>，自带浓淡与渐隐 alpha）。
        /// **只由家具宽度定尺寸**——宽 = 家具显示宽 × <see cref="ShadowWidthScale"/>，
        /// 高按贴图自身宽高比等比跟随（不拉伸变形），中心压在家具底边线上。
        /// 挂在家具根下，父级 <see cref="FurnitureScale"/> 已把家具拉到显示尺寸，这里把目标世界尺寸反算回局部值。
        /// **口径必须与 FurnitureScale 一致取 Tight 包络**：家具素材普遍是 1024 大画布加透明留白，
        /// 用 <c>sprite.bounds</c>（整张画布）反算会让影子跟着留白一起被撑宽、并沉到脚底以下的空白里。
        /// </summary>
        private static SpriteRenderer CreateShadow(FurnitureRuntimeItem item)
        {
            var sprite = Resources.Load<Sprite>(ShadowSpritePath);
            if (sprite == null || item.Entry.sprite == null) return null;
            var go = new GameObject("Shadow") { layer = FurnitureSceneLayer };
            go.transform.SetParent(item.Root.transform, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            var tight = FurnitureDisplaySizing.TightBounds(item.Entry.sprite); // 家具真实图形区（轴心为原点）
            var shadowBounds = sprite.bounds.size;
            var display = FurnitureDisplaySizing.Resolve(item.Entry);
            var aspect = shadowBounds.y / Mathf.Max(.0001f, shadowBounds.x); // 贴图原始扁度
            var localX = ShadowWidthScale * tight.size.x / Mathf.Max(.0001f, shadowBounds.x);
            var localY = ShadowWidthScale * aspect * display.x / Mathf.Max(1f, display.y)
                         * tight.size.y / Mathf.Max(.0001f, shadowBounds.y);
            go.transform.localScale = new Vector3(localX, localY, 1f);
            // 影子中心压在家具**真实脚底**（图形区底边）、并对齐图形区中轴，而不是画布底边/中心
            go.transform.localPosition = new Vector3(tight.center.x, tight.min.y, 0f);
            return renderer;
        }

        private void SyncTableGrid(FurnitureRuntimeItem host)
        {
            var config = host.Entry.tableSurface;
            if (config == null || !config.enabled) return;
            if (!grids.TryGetValue(TableGridId(host.Id), out var grid)) return;
            AnchorOf(host, out var left, out var bottom, out var order, out _);
            var frameScale = FurnitureDisplaySizing.FrameScale(host.Entry);
            grid.SetOrigin(left + config.offsetX * frameScale.x,
                bottom - config.surfaceHeight * frameScale.y - grid.CellHeight);
            grid.SetSortingOrder(order + 2);
            foreach (var child in ChildrenOf(host)) LayoutItem(child);
        }

        private FurnitureRuntimeItem PlaceItem(FurnitureEntry entry, string gridId, int col, int row, bool silent,
            bool flipped = false)
        {
            var grid = grids[gridId];
            var footCols = FootprintCols(grid, entry);
            var footRows = FootprintRows(grid, entry);
            var item = new FurnitureRuntimeItem
            {
                Id = "it" + nextItemId++,
                Entry = entry,
                GridId = gridId,
                Col = col,
                Row = row,
                Flipped = flipped,
            };
            item.Root = new GameObject("Furniture_" + entry.id) { layer = FurnitureSceneLayer };
            item.Root.transform.SetParent(stageRoot, false);
            item.Renderer = item.Root.AddComponent<SpriteRenderer>();
            item.Renderer.sprite = entry.sprite;
            item.Renderer.flipX = flipped;
            if (entry.sprite != null)
                item.Root.transform.localScale = FurnitureScale(entry);
            // 光影：落地/桌面家具脚下垫柔和椭圆投影（壁挂与地毯类不投）
            if (!entry.stackable && grid.Surface != FurnitureSurfaceType.Wall)
                item.Shadow = CreateShadow(item);
            items[item.Id] = item;
            placedCountsDirty = true; // 余量缓存跟着 items 走，标脏就贴在改集合的这一行旁边
            grid.SetOccupied(col, row, footCols, footRows, item.Id, true, entry.stackable);

            if (entry.tableSurface != null && entry.tableSurface.enabled)
            {
                // 桌面格与家具配置显示框使用同一坐标口径；FrameScale 保留为统一换算入口。
                var frameScale = FurnitureDisplaySizing.FrameScale(entry);
                var config = new FurnitureGridConfig
                {
                    id = TableGridId(item.Id),
                    surface = FurnitureSurfaceType.Table,
                    cols = entry.tableSurface.cols,
                    rows = 1,
                    cellWidth = entry.tableSurface.cellWidth * frameScale.x,
                    cellHeight = entry.tableSurface.cellHeight * frameScale.y,
                };
                var tableGrid = new FurnitureRuntimeGrid(config, PxToWorld, ZFloorPerRow * (row + footRows) - .01f);
                tableGrid.HostItemId = item.Id;
                tableGrid.BuildVisual(stageRoot, F.WhiteSprite, OrderFloorItemBase);
                grids[tableGrid.Id] = tableGrid;
            }

            LayoutItem(item);
            SyncTableGrid(item);
            if (!silent && item.Root != null)
                item.Root.transform.DOPunchScale(Vector3.one * .045f, .28f, 6, .7f).SetTarget(this);
            RefreshAllGridColors();
            ApplyGridVisibility();
            RecomputeDecorationScore();
            return item;
        }

        private void StoreItem(FurnitureRuntimeItem item, bool silent)
        {
            var names = new List<string> { item.Entry.displayName };
            if (item.Entry.tableSurface != null && item.Entry.tableSurface.enabled)
            {
                foreach (var child in ChildrenOf(item))
                {
                    names.Add(child.Entry.displayName);
                    StoreItem(child, true);
                }
                if (grids.TryGetValue(TableGridId(item.Id), out var tableGrid))
                {
                    tableGrid.Destroy();
                    grids.Remove(tableGrid.Id);
                }
            }
            if (grids.TryGetValue(item.GridId, out var grid))
                grid.SetOccupied(item.Col, item.Row, FootprintCols(grid, item.Entry), FootprintRows(grid, item.Entry),
                    item.Id, false, item.Entry.stackable);
            Destroy(item.Root);
            items.Remove(item.Id);
            placedCountsDirty = true; // 同 PlaceItem：items 一变余量就得重算
            RefreshAllGridColors();
            ApplyGridVisibility();
            RecomputeDecorationScore();
            if (!silent)
            {
                hud.RefreshInventory();
                hud.ShowToast("已收纳：" + string.Join("、", names));
            }
        }

        private void MoveItem(FurnitureRuntimeItem item, string gridId, int col, int row)
        {
            if (grids.TryGetValue(item.GridId, out var from))
                from.SetOccupied(item.Col, item.Row, FootprintCols(from, item.Entry), FootprintRows(from, item.Entry),
                    item.Id, false, item.Entry.stackable);
            item.GridId = gridId;
            item.Col = col;
            item.Row = row;
            var target = grids[gridId];
            target.SetOccupied(col, row, FootprintCols(target, item.Entry), FootprintRows(target, item.Entry),
                item.Id, true, item.Entry.stackable);
            LayoutItem(item);
            SyncTableGrid(item);
            item.Root.transform.DOPunchScale(Vector3.one * .045f, .28f, 6, .7f).SetTarget(this);
            RefreshAllGridColors();
        }

        // ── 可摆余量（家具库存说明 §5.1/§5.6）──
        //
        // 取代了原来的 IsPlaced(id)：那个只遍历**当前打开房间**的 items，于是四宫格改造之后
        // 「每种家具只能拥有一件」形同虚设——同一张沙发可以在四个房间各摆一份。

        /// <summary>跨房间已摆放数（id → 件数）。脏标记驱动，见 RebuildPlacedCounts 的成本说明。</summary>
        private readonly Dictionary<string, int> placedCounts = new Dictionary<string, int>();
        private bool placedCountsDirty = true;

        /// <summary>
        /// 重建跨房间已摆放数。
        ///
        /// **必须缓存**：RefreshInventory 一次会调 EntriesOf 约 7 遍（页签可用性 + 三个页签的 hasAny +
        /// 分页数 + 当前页），每遍遍历整张 121 行家具表并对每条调 stateGetter，加上 12 个槽位 ——
        /// 单次刷新 800+ 次余量查询。重建本身是 O(全房间摆放件数)，只在摆放/收纳/进场时发生。
        ///
        /// 两条口径：
        ///   ① 当前房间读**实时 items**而不是 sessions——MoveItem 不落会话，读 sessions 会漏掉刚挪过的家具
        ///   ② 其余房间读会话布局，未编辑过的回落该房默认摆放（与 FurniturePlacementQuery 同一套回落）；
        ///      **直接读 sessions 私有字段，绕开 CaptureSessionPlacements**——后者对 active 房间会触发
        ///      一次 SaveState()，热路径上不能碰。控制器本来就是 sessions 的所有者，不算越权（§11.4 约束的是业务层）
        /// </summary>
        private void RebuildPlacedCounts()
        {
            placedCounts.Clear();
            foreach (var item in items.Values) Bump(item.Entry.id); // 要点①
            var rooms = GameManager.Instance != null ? GameManager.Instance.FurnitureRoomTable : null;
            if (rooms != null)
                foreach (var roomEntry in rooms.rooms) // 房间表是 List，遍历顺序稳定（§11.2）
                {
                    if (roomEntry == null || roomEntry.id == room.id) continue; // 当前房间已由 items 统计
                    var placements = sessions.TryGetValue(roomEntry.id, out var state)
                        ? state.Placements
                        : roomEntry.initialPlacements; // 要点②
                    foreach (var placement in placements)
                        if (placement != null && !string.IsNullOrEmpty(placement.furnitureId)) Bump(placement.furnitureId);
                }
            placedCountsDirty = false;

            void Bump(string id) => placedCounts[id] = (placedCounts.TryGetValue(id, out var n) ? n : 0) + 1;
        }

        private int PlacedCountOf(string furnitureId)
        {
            if (placedCountsDirty) RebuildPlacedCounts();
            return placedCounts.TryGetValue(furnitureId, out var count) ? count : 0;
        }

        /// <summary>可摆余量 = 拥有数 − 全部房间已摆放数。拥有数实时读 Economy（买卖不必让缓存失效）。</summary>
        private int RemainingOf(string furnitureId) =>
            Mathf.Max(0, Economy.OwnedCountOf(furnitureId) - PlacedCountOf(furnitureId));

        #endregion

        #region 输入与拖拽

        private void Update()
        {
            if (closing) return;
            ApplyDayLight(); // 昼夜光照随时钟流动（摆放中时钟若在走，天色照常变化）

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (hud.PopupOpen) hud.CloseUnlockPopup();
                else if (drag != null) EndDrag(false);
                else Close();
                return;
            }

            if (drag != null)
            {
                if (Input.GetKeyDown(KeyCode.F) && !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift)) // 拖拽中 F 翻转幽灵
                {
                    drag.Flipped = !drag.Flipped;
                    if (drag.GhostRenderer != null) drag.GhostRenderer.flipX = drag.Flipped;
                }
                UpdateDrag();
                if (Input.GetMouseButtonUp(0)) EndDrag(true);
                return;
            }

            if (Input.GetKeyDown(KeyCode.F) && !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift) && !hud.PopupOpen)
            {
                // 非拖拽态：F 翻转鼠标下的已摆放家具，立即落会话
                var hover = HitItem(MouseScenePx());
                if (hover != null)
                {
                    hover.Flipped = !hover.Flipped;
                    if (hover.Renderer != null) hover.Renderer.flipX = hover.Flipped;
                    SaveState();
                }
            }

            if (Input.GetMouseButtonDown(0) && !hud.PopupOpen)
            {
                // 指针落在 HUD 控件（收纳栏、按钮）上时交给 uGUI，槽位经 SlotPressed 起手。
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
                TryPickItem();
            }
        }

        private FurnitureRuntimeItem HitItem(Vector2 scenePx)
        {
            FurnitureRuntimeItem hit = null;
            var hitOrder = int.MinValue;
            foreach (var item in items.Values)
            {
                AnchorOf(item, out var left, out var bottom, out var order, out _);
                var display = FurnitureDisplaySizing.Resolve(item.Entry);
                if (scenePx.x >= left && scenePx.x <= left + display.x &&
                    scenePx.y >= bottom - display.y && scenePx.y <= bottom &&
                    order > hitOrder)
                {
                    hit = item;
                    hitOrder = order;
                }
            }
            return hit;
        }

        private void TryPickItem()
        {
            var hit = HitItem(MouseScenePx());
            if (hit == null) return;
            if (hit.Id == lastClickItemId && Time.unscaledTime - lastClickTime < DoubleClickSeconds)
            {
                lastClickItemId = null;
                StoreItem(hit, false);
                return;
            }
            lastClickItemId = hit.Id;
            lastClickTime = Time.unscaledTime;
            BeginDrag(hit.Entry, hit);
        }

        private void OnSlotPressed(string furnitureId)
        {
            if (drag != null || closing) return;
            var entry = furnitureTable.Find(furnitureId);
            if (entry == null) return;
            if (hud.PopupOpen) return;
            // 余量 0 有两种来路（未拥有 / 全摆出去了），都当作「想再要一件」→ 弹购买窗
            //（家具库存说明 §9 待确认 #3 的默认实现）。声望不够则只提示门槛。
            if (RemainingOf(furnitureId) <= 0)
            {
                if (!Economy.IsFurnitureRevealed(entry))
                {
                    hud.ShowToast($"「？」声望达到 {Economy.UnlockReputationOf(entry)} 后解禁（当前 {Economy.Data.Reputation}）");
                    return;
                }
                if (Economy.PriceOf(entry) <= 0)
                {
                    hud.ShowToast($"「{entry.displayName}」商店买不到 · 先从别的房间收纳一件");
                    return;
                }
                hud.ShowPurchasePopup(entry, Economy.Data.Currency);
                return;
            }
            BeginDrag(entry, null);
        }

        private void BeginDrag(FurnitureEntry entry, FurnitureRuntimeItem item)
        {
            // 音效需求 #2：拾起（收纳栏拖出与场上拿起两条路径都汇到这里）；家具表可配专属音，空则全局默认
            SfxManager.PlayOverride(entry.pickupSound, ESfx.FurniturePickup);
            drag = new DragState { Entry = entry, Item = item, Flipped = item != null && item.Flipped };
            hud.SetDragDimming(true); // 布置中顶部 UI 淡出让位（收纳栏保留作拖回落点）
            drag.Ghost = new GameObject("DragGhost") { layer = FurnitureSceneLayer };
            drag.Ghost.transform.SetParent(stageRoot, false);
            drag.GhostRenderer = drag.Ghost.AddComponent<SpriteRenderer>();
            drag.GhostRenderer.sprite = entry.sprite;
            drag.GhostRenderer.flipX = drag.Flipped;
            drag.GhostRenderer.sortingOrder = OrderGhost;
            drag.GhostRenderer.color = new Color(1f, 1f, 1f, .85f);
            if (entry.sprite != null)
                drag.Ghost.transform.localScale = FurnitureScale(entry);

            if (item != null)
            {
                // 拖动带桌面的家具时，桌上家具跟着幽灵走
                if (entry.tableSurface != null && entry.tableSurface.enabled)
                {
                    AnchorOf(item, out var hostLeft, out var hostBottom, out _, out _);
                    var hostDisplay = FurnitureDisplaySizing.Resolve(entry);
                    foreach (var child in ChildrenOf(item))
                    {
                        AnchorOf(child, out var childLeft, out var childBottom, out _, out _);
                        var childDisplay = FurnitureDisplaySizing.Resolve(child.Entry);
                        var childGhost = new GameObject("GhostChild_" + child.Entry.id) { layer = FurnitureSceneLayer };
                        childGhost.transform.SetParent(drag.Ghost.transform, false);
                        var renderer = childGhost.AddComponent<SpriteRenderer>();
                        renderer.sprite = child.Entry.sprite;
                        renderer.sortingOrder = OrderGhost + 1;
                        renderer.color = new Color(1f, 1f, 1f, .85f);
                        // 子物体位置相对宿主锚点（像素差换算为本地偏移；父物体已缩放，先除回）
                        var hostScale = drag.Ghost.transform.localScale;
                        var offsetX = (childLeft + childDisplay.x * .5f) - (hostLeft + hostDisplay.x * .5f);
                        var offsetY = (hostBottom - hostDisplay.y * .5f) - (childBottom - childDisplay.y * .5f);
                        childGhost.transform.localPosition = new Vector3(
                            offsetX / PixelsPerUnit / hostScale.x, offsetY / PixelsPerUnit / hostScale.y, 0f);
                        if (child.Entry.sprite != null)
                        {
                            var childScale = FurnitureScale(child.Entry);
                            childGhost.transform.localScale = new Vector3(
                                childScale.x / hostScale.x, childScale.y / hostScale.y, 1f);
                        }
                        child.Root.SetActive(false);
                    }
                    if (grids.TryGetValue(TableGridId(item.Id), out var tableGrid)) tableGrid.SetVisible(false);
                }
                var sourceGrid = grids[item.GridId];
                sourceGrid.SetOccupied(item.Col, item.Row, FootprintCols(sourceGrid, entry),
                    FootprintRows(sourceGrid, entry), item.Id, false, entry.stackable);
                item.Root.SetActive(false);
            }

            ShowGridsForEntry(entry);
            RefreshAllGridColors();
            if (focusBlurRenderer != null)
            {
                focusBlurRenderer.DOKill();
                focusBlurRenderer.DOFade(.85f, .3f);
            }
            UpdateDrag();
        }

        private void UpdateDrag()
        {
            var entry = drag.Entry;
            var display = FurnitureDisplaySizing.Resolve(entry);
            var px = MouseScenePx();
            var wantLeft = px.x - display.x * .5f;
            var wantBottom = px.y + display.y * .25f;

            // 拖回收纳优先：指针悬在收纳栏面板上即判定收回（面板亮粉提示）。
            // 收纳栏正后方的地板行想摆放：先「隐藏界面」或滚轮放大，让收纳栏离开指针下方。
            drag.OverInventory = drag.Item != null && hud.IsPointerOverInventory(Input.mousePosition);
            hud.SetInventoryDropHint(drag.OverInventory);

            RefreshAllGridColors();
            drag.CandidateGrid = null;
            if (drag.OverInventory)
            {
                drag.Ghost.transform.position = PxToWorld(
                    wantLeft + display.x * .5f, wantBottom - display.y * .5f, ZGhost);
                drag.GhostRenderer.color = new Color(1f, 1f, 1f, .85f);
                return;
            }
            FurnitureRuntimeGrid best = null;
            var bestDist = float.MaxValue;
            var bestIsTable = false;
            foreach (var grid in grids.Values)
            {
                if (!entry.Supports(grid.Surface)) continue; // 表面类型可多选（如纸箱：地面/桌面）
                if (drag.Item != null && grid.HostItemId == drag.Item.Id) continue;
                if (!grid.Contains(px, SnapMarginPx)) continue;
                var dist = grid.DistanceSq(px);
                // 桌面格与地面格空间重叠：指针悬在桌面格内时桌面格优先，
                // 否则多选表面的小件永远被大范围地面格抢走、落到桌子身后的地板行（层级被桌子压住）
                var isTable = grid.Surface == FurnitureSurfaceType.Table;
                var better = best == null || (isTable && !bestIsTable) ||
                             (isTable == bestIsTable && dist < bestDist);
                if (better)
                {
                    best = grid;
                    bestDist = dist;
                    bestIsTable = isTable;
                }
            }
            if (best != null)
            {
                var footCols = FootprintCols(best, entry);
                var footRows = FootprintRows(best, entry);
                var footWidth = footCols * best.CellWidth;
                var footHeight = footRows * best.CellHeight;
                var row = Mathf.Clamp(Mathf.RoundToInt(
                    (wantBottom - footHeight - best.Y) / best.CellHeight), 0, best.Rows - footRows);
                // 假透视反算：指针中心先还原到均匀网格坐标（按该落点的底边行），再算列
                var desiredCenter = best.InvMapX(wantLeft + display.x * .5f, row + footRows);
                var col = Mathf.Clamp(Mathf.RoundToInt(
                    (desiredCenter - footWidth * .5f - best.X) / best.CellWidth), 0, best.Cols - footCols);
                drag.CandidateGrid = best;
                drag.CandidateCol = col;
                drag.CandidateRow = row;
                drag.CandidateOk = best.FootprintFree(col, row, footCols, footRows, drag.Item?.Id, entry.stackable);
                best.PaintPreview(col, row, footCols, footRows, drag.CandidateOk);
            }

            float ghostLeft, ghostBottom;
            if (drag.CandidateGrid != null)
            {
                var grid = drag.CandidateGrid;
                var footCols = FootprintCols(grid, entry);
                var footRows = FootprintRows(grid, entry);
                ghostLeft = grid.X + drag.CandidateCol * grid.CellWidth +
                            (footCols * grid.CellWidth - display.x) * .5f;
                if (grid.Surface == FurnitureSurfaceType.Floor) // 假透视：幽灵与最终落位同一映射
                    ghostLeft = grid.MapX(ghostLeft + display.x * .5f, drag.CandidateRow + footRows)
                                - display.x * .5f;
                ghostBottom = grid.Surface == FurnitureSurfaceType.Table
                    ? grid.Y + grid.CellHeight
                    : grid.Y + (drag.CandidateRow + footRows) * grid.CellHeight;
            }
            else
            {
                ghostLeft = wantLeft;
                ghostBottom = wantBottom;
            }
            drag.Ghost.transform.position = PxToWorld(
                ghostLeft + display.x * .5f, ghostBottom - display.y * .5f, ZGhost);
            var invalid = !drag.OverInventory && !(drag.CandidateGrid != null && drag.CandidateOk);
            drag.GhostRenderer.color = invalid ? new Color(1f, .58f, .52f, .85f) : new Color(1f, 1f, 1f, .85f);
        }

        private void EndDrag(bool commit)
        {
            var state = drag;
            drag = null;
            hud.SetDragDimming(false);
            hud.SetInventoryDropHint(false);
            if (focusBlurRenderer != null)
            {
                focusBlurRenderer.DOKill();
                focusBlurRenderer.DOFade(0f, .3f);
            }

            void RestoreDragged()
            {
                if (state.Item == null) return;
                state.Item.Root.SetActive(true);
                var grid = grids[state.Item.GridId];
                grid.SetOccupied(state.Item.Col, state.Item.Row, FootprintCols(grid, state.Entry),
                    FootprintRows(grid, state.Entry), state.Item.Id, true, state.Entry.stackable);
                if (state.Entry.tableSurface != null && state.Entry.tableSurface.enabled)
                {
                    if (grids.TryGetValue(TableGridId(state.Item.Id), out var tableGrid)) tableGrid.SetVisible(true);
                    foreach (var child in ChildrenOf(state.Item)) child.Root.SetActive(true);
                }
            }

            if (commit && state.OverInventory && state.Item != null)
            {
                SfxManager.PlayOverride(state.Entry.putdownSound, ESfx.FurniturePlace); // 音效需求 #2：放置（收回收纳栏也算落定）
                RestoreDragged();
                Destroy(state.Ghost);
                StoreItem(state.Item, false);
            }
            else if (commit && state.CandidateGrid != null && state.CandidateOk)
            {
                SfxManager.PlayOverride(state.Entry.putdownSound, ESfx.FurniturePlace); // 音效需求 #2：放置（落地）
                Destroy(state.Ghost);
                if (state.Item != null)
                {
                    state.Item.Root.SetActive(true);
                    state.Item.Flipped = state.Flipped;
                    if (state.Item.Renderer != null) state.Item.Renderer.flipX = state.Flipped;
                    if (state.Entry.tableSurface != null && state.Entry.tableSurface.enabled)
                    {
                        if (grids.TryGetValue(TableGridId(state.Item.Id), out var tableGrid)) tableGrid.SetVisible(true);
                        foreach (var child in ChildrenOf(state.Item)) child.Root.SetActive(true);
                    }
                    MoveItem(state.Item, state.CandidateGrid.Id, state.CandidateCol, state.CandidateRow);
                }
                else
                {
                    PlaceItem(state.Entry, state.CandidateGrid.Id, state.CandidateCol, state.CandidateRow, false, state.Flipped);
                    hud.RefreshInventory();
                }
            }
            else
            {
                if (commit && state.Item == null)
                {
                    var hasSurface = false;
                    foreach (var grid in grids.Values)
                        if (state.Entry.Supports(grid.Surface)) { hasSurface = true; break; }
                    if (!hasSurface && state.Entry.Supports(FurnitureSurfaceType.Table))
                        hud.ShowToast("需要先摆放带桌面格的家具（如茶几）");
                    else if (state.CandidateGrid != null && !state.CandidateOk)
                        hud.ShowToast("该位置无法摆放");
                }
                RestoreDragged();
                Destroy(state.Ghost);
            }
            RefreshAllGridColors();
            ApplyGridVisibility();
        }

        #endregion

        #region 网格显隐

        /// <summary>拖拽中显示该家具支持的全部表面网格（表面类型可多选）。</summary>
        private void ShowGridsForEntry(FurnitureEntry entry)
        {
            foreach (var grid in grids.Values) grid.SetVisible(entry.Supports(grid.Surface));
        }

        private void ApplyGridVisibility()
        {
            if (drag != null) return;
            foreach (var grid in grids.Values) grid.SetVisible(gridToggleOn);
        }

        private void ToggleGrids()
        {
            gridToggleOn = !gridToggleOn;
            hud.SetGridToggle(gridToggleOn);
            ApplyGridVisibility();
        }

        private void RefreshAllGridColors()
        {
            foreach (var grid in grids.Values) grid.RefreshCellColors();
        }

        #endregion

        #region 流通数值与收纳栏

        /// <summary>
        /// 槽位状态。<see cref="FurnitureSlotState.Placed"/> 的语义已由「已经摆出去了」改成
        /// **「余量为 0」**（全摆出去了 / 只买了这些），见家具库存说明 §5.6。
        /// </summary>
        private FurnitureSlotState GetSlotState(string furnitureId)
        {
            var entry = furnitureTable.Find(furnitureId);
            if (!Economy.IsFurnitureOwned(furnitureId))
                return Economy.IsFurnitureRevealed(entry) ? FurnitureSlotState.Locked : FurnitureSlotState.Unknown;
            return RemainingOf(furnitureId) > 0 ? FurnitureSlotState.Available : FurnitureSlotState.Placed;
        }

        private void OnPurchaseConfirmed(string furnitureId)
        {
            var entry = furnitureTable.Find(furnitureId);
            if (entry == null) return;
            var result = Economy.TryPurchaseFurniture(entry);
            if (result == FurniturePurchaseResult.Success)
            {
                SfxManager.Play(ESfx.Reward); // 音效需求 #7：商城购买成功（家具模式内的购买入口）
                hud.RefreshInventory();
                hud.ShowToast($"已购入「{entry.displayName}」 · ◈ -{Economy.PriceOf(entry)}");
            }
            else if (result == FurniturePurchaseResult.NotEnoughCurrency)
            {
                hud.ShowToast("货币不足，先去完成客人服务吧");
            }
            else if (result == FurniturePurchaseResult.NotForSale)
            {
                hud.ShowToast($"「{entry.displayName}」商店不出售");
            }
        }

        /// <summary>
        /// 右键槽位：半价出售（家具库存说明 §5.5）。**卖的是余量**——已经摆在房间里的要先收纳回来，
        /// 这样「余量 = 拥有数 − 已摆数」永远不会算成负数，也避开了「卖掉正摆着的家具 → 同步删实例
        /// → 桌面家具级联掉下来」那条链。
        /// </summary>
        private void OnSellPressed(string furnitureId)
        {
            if (drag != null || closing || hud.PopupOpen) return;
            var entry = furnitureTable.Find(furnitureId);
            if (entry == null) return;
            if (Economy.PriceOf(entry) <= 0)
            {
                hud.ShowToast($"「{entry.displayName}」不能出售（商店表里没配价格）");
                return;
            }
            var remaining = RemainingOf(furnitureId);
            if (remaining <= 0)
            {
                hud.ShowToast($"「{entry.displayName}」没有可出售的余量 · 先从房间里收纳一件回来");
                return;
            }
            hud.ShowSellPopup(entry, Economy.SellbackValueOf(entry), remaining);
        }

        private void OnSellConfirmed(string furnitureId)
        {
            var entry = furnitureTable.Find(furnitureId);
            if (entry == null) return;
            // 弹窗开着期间余量可能已变（对话事件/GM 都能动库存），这里再核一次
            if (RemainingOf(furnitureId) <= 0)
            {
                hud.ShowToast($"「{entry.displayName}」已经没有余量可卖了");
                return;
            }
            var refund = Economy.SellFurniture(entry);
            if (refund <= 0)
            {
                hud.ShowToast($"「{entry.displayName}」出售失败");
                return;
            }
            SfxManager.Play(ESfx.Reward);
            hud.RefreshInventory();
            hud.ShowToast($"已出售「{entry.displayName}」 · ◈ +{refund}");
        }

        /// <summary>装饰分来源之一：全部房间已摆放装饰品的得分总和（当前房间先落会话，再逐房间求和）。</summary>
        private void RecomputeDecorationScore()
        {
            SaveState();
            SyncDecorationFromSession();
            // **本房装饰分不挂 Economy.Changed**：SetFurnitureDecorationScore 在值相等时 early-return 不广播，
            // 今天「全局增量 == 本房增量」碰巧成立（一个会话只编辑一个房间），但那是隐式不变式，
            // 多一个装饰分来源就会静默失效。这里是三个变更点（Init/PlaceItem/StoreItem）的汇聚处，主动推最稳
            RefreshHudEconomy();
        }

        private void OnEconomyChanged() => RefreshHudEconomy();

        /// <summary>把三个全局值 + 本房装饰分与它换来的小费加成推给 HUD。</summary>
        private void RefreshHudEconomy()
        {
            if (hud == null) return;
            var roomDecor = FurniturePlacementQuery.DecorationScoreOf(roomIndex);
            // 预览与实际入账共用 EconomyManager 的同一个公式，两者永远不会漂开
            var tipBonus = Economy.LeaveTipPreview(roomDecor, true) - Economy.LeaveTipPreview(0, true);
            hud.SetEconomy(Economy.Data.Currency, Economy.Data.Reputation, Economy.DecorationScore, roomDecor, tipBonus);
        }

        #endregion

        #region 会话状态

        /// <summary>导出指定房间的当前布局供存档/烘焙。null = 该房间从未编辑过（沿用其默认摆放）。</summary>
        public static List<FurniturePlacementConfig> CaptureSessionPlacements(string roomId)
        {
            if (active != null && active.room != null && active.room.id == roomId) active.SaveState();
            if (string.IsNullOrEmpty(roomId) || !sessions.TryGetValue(roomId, out var state)) return null;
            return new List<FurniturePlacementConfig>(state.Placements);
        }

        /// <summary>从存档恢复指定房间的布局；placements 为 null 时回落到该房间默认摆放（存档接缝，待定 #9）。</summary>
        public static void RestoreSessionPlacements(string roomId, List<FurniturePlacementConfig> placements)
        {
            if (string.IsNullOrEmpty(roomId)) return;
            if (placements == null)
            {
                sessions.Remove(roomId);
            }
            else
            {
                var state = new SessionState();
                state.Placements.AddRange(placements);
                sessions[roomId] = state;
            }
            // 改的是**别的房间**的会话布局，正开着的那个实例的余量缓存要作废
            if (active != null) active.placedCountsDirty = true;
            SyncDecorationFromSession();
        }

        /// <summary>关闭当前打开的家具模式（GM 全量重置等外部流程使用）。</summary>
        public static void CloseActive()
        {
            if (active != null) active.Close();
        }

        /// <summary>清空全部房间布局（新游戏）。</summary>
        public static void ResetSession()
        {
            sessions.Clear();
            if (active != null) active.placedCountsDirty = true;
            SyncDecorationFromSession();
        }

        /// <summary>不打开家具模式也能把布局对应的装饰品得分回写流通服务（读档/新游戏后 Hub 立即显示正确装饰分）。
        /// 多房间：逐房间取会话布局（无会话则用该房间默认摆放）求和。</summary>
        private static void SyncDecorationFromSession()
        {
            var table = GameManager.Instance.FurnitureTable;
            var rooms = GameManager.Instance.FurnitureRoomTable;
            if (table == null || rooms == null)
            {
                Economy.SetFurnitureDecorationScore(0);
                return;
            }
            var sum = 0;
            foreach (var roomEntry in rooms.rooms)
            {
                if (roomEntry == null) continue;
                var placements = sessions.TryGetValue(roomEntry.id, out var state)
                    ? state.Placements
                    : roomEntry.initialPlacements;
                foreach (var placement in placements)
                {
                    var entry = placement == null ? null : table.Find(placement.furnitureId);
                    if (entry != null) sum += entry.decorationScore;
                }
            }
            Economy.SetFurnitureDecorationScore(sum);
        }

        private void RestoreState()
        {
            var placements = sessions.TryGetValue(room.id, out var state) ? state.Placements : room.initialPlacements;

            // 先摆基础网格上的家具，再摆桌面家具（宿主此时已生成桌面网格）
            foreach (var placement in placements)
            {
                if (placement == null || placement.IsOnHost) continue;
                var entry = furnitureTable.Find(placement.furnitureId);
                if (entry == null || !grids.TryGetValue(placement.gridId ?? string.Empty, out var grid)) continue;
                if (!entry.Supports(grid.Surface)) continue;
                if (!grid.FootprintFree(placement.col, placement.row, FootprintCols(grid, entry),
                        FootprintRows(grid, entry), null, entry.stackable)) continue;
                PlaceItem(entry, placement.gridId, placement.col, placement.row, true, placement.flipped);
            }
            foreach (var placement in placements)
            {
                if (placement == null || !placement.IsOnHost) continue;
                var entry = furnitureTable.Find(placement.furnitureId);
                if (entry == null || !entry.Supports(FurnitureSurfaceType.Table)) continue;
                // 按**落位坐标**认宿主，不按家具 id（§5.4）：同房间可以摆多件同款，
                // 按 id 找会把两张桌子上的东西全塞给第一张，挤不下的还会在下面那道 FootprintFree 静默丢失
                FurnitureRuntimeItem host = null;
                foreach (var item in items.Values)
                    if (!item.IsOnTableGrid && item.GridId == placement.hostGridId &&
                        item.Col == placement.hostCol && item.Row == placement.hostRow) { host = item; break; }
                if (host == null || !grids.TryGetValue(TableGridId(host.Id), out var grid)) continue;
                if (!grid.FootprintFree(placement.col, placement.row, FootprintCols(grid, entry),
                        FootprintRows(grid, entry), null, entry.stackable)) continue;
                PlaceItem(entry, grid.Id, placement.col, placement.row, true, placement.flipped);
            }
        }

        private void SaveState()
        {
            var state = new SessionState();
            foreach (var item in items.Values)
            {
                if (item.IsOnTableGrid) continue;
                state.Placements.Add(new FurniturePlacementConfig
                {
                    furnitureId = item.Entry.id,
                    gridId = item.GridId,
                    col = item.Col,
                    row = item.Row,
                    flipped = item.Flipped,
                });
            }
            foreach (var item in items.Values)
            {
                if (!item.IsOnTableGrid) continue;
                var host = items[grids[item.GridId].HostItemId];
                state.Placements.Add(new FurniturePlacementConfig
                {
                    furnitureId = item.Entry.id,
                    // 宿主记**落位坐标**而不是家具 id（§5.4）：同房间两张同款桌子靠 id 分不开
                    hostGridId = host.GridId,
                    hostCol = host.Col,
                    hostRow = host.Row,
                    col = item.Col,
                    row = item.Row,
                    flipped = item.Flipped,
                });
            }
            sessions[room.id] = state; // 按房间分桶保存
        }

        #endregion

        private void Close()
        {
            if (closing) return;
            closing = true;
            if (drag != null)
            {
                Destroy(drag.Ghost);
                drag = null;
            }
            SaveState();
            Economy.Changed -= OnEconomyChanged;
            DOTween.Kill(this);
            if (focusBlurRenderer != null) focusBlurRenderer.DOKill();
            hud?.Destroy();
            if (rig != null) Destroy(rig.gameObject);
            if (stageRoot != null) Destroy(stageRoot.gameObject);
            var callback = onClosed;
            onClosed = null;
            Destroy(gameObject);
            callback?.Invoke();
        }

        private void OnDestroy()
        {
            // 应用退出时各常驻对象的销毁顺序不确定，GameManager 可能先没
            if (GameManager.Instance != null) Economy.Changed -= OnEconomyChanged;
            if (active == this) active = null;
        }
    }
}

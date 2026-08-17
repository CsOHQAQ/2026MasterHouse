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
        private const float OverviewZoom = 1f; // 恰好看全整栋主楼剖面
        private const float MaxZoom = 6f;      // 比「单房推满视口宽」再深一档
        /// <summary>外景下层栈桥独立对齐：柱距比主楼宽约 5.56%，以中心横向收缩；平台顶面向下校正约 23 px。</summary>
        private const float LowerStructureScaleX = .9444f;
        private const float LowerStructureShiftX = -.0007f;
        private const float LowerStructureShiftY = .024f;
        private static readonly Vector4[] LowerStructureGradeKeys =
        {
            new Vector4(0f, .5f, .577f, .813f),
            new Vector4(60f, .473f, .623f, .765f),
            new Vector4(300f, .65f, .74f, .846f),
            new Vector4(660f, .5f, .615f, .772f),
            new Vector4(750f, .45f, .625f, .791f),
            new Vector4(810f, 1.321f, 1.068f, .973f),
            new Vector4(900f, 1.429f, 1.136f, 1.027f),
            new Vector4(1260f, 1.429f, 1.159f, 1.041f),
            new Vector4(1440f, .5f, .577f, .813f),
        };
        /// <summary>低于此缩放视为「总览态」（看整栋楼，无当前房间概念）；聚焦单房的缩放约 3.7~4.8。</summary>
        private const float FocusedZoomThreshold = 2f;
        /// <summary>外景层级（2026-08-16）：总览再缩小，主楼剖面淡出并落到外景图中房屋的位置——
        /// 复用开场推镜的对齐变换（反向），进出取景一致。此档的最小缩放。</summary>
        private static float ExteriorMinZoom => 1f / OpeningZoomFx.AlignScale;
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
        private CanvasGroup worldGroup;
        private RectTransform exteriorRect;
        private RawImage exteriorBackdrop;
        /// <summary>天空循环的交叉淡化层：与外景底图重合，显示「下一帧」并按权重淡入。</summary>
        /// <summary>两套延时序列的播放材质（当前帧 + 下一帧 + 遮罩，单次渲染内混合）。</summary>
        private Material exteriorCycle;
        private Material houseCycle;
        /// <summary>纯天空层（房子抹掉）：主楼层可见时盖住外景的房子，避免旗杆/招牌重影。</summary>
        private RawImage skyOnly;
        private Material skyOnlyCycle;
        private RectTransform lowerStructureRect;
        private RawImage lowerStructure;
        private Material lowerStructureCycle;
        /// <summary>聚焦档接管的高清静态主楼图（延时帧分辨率不够，推近了糊）。</summary>
        private RawImage houseStatic;
        private Vector2 viewportSize = new Vector2(1920f, 1080f);
        private RawImage houseBackdrop;
        private readonly RawImage[] roomArts = new RawImage[HubWorldGrid.RoomCount];
        private readonly RawImage[] nightRoomArts = new RawImage[HubWorldGrid.RoomCount];
        private Image sceneWash;
        private Image ambientLight;
        private OutGameHubSceneOverlayView overlay;
        private OutGameVisitorStage stage;
        private RectTransform hotspotRoot;
        /// <summary>家具标签层：与热点分离并置顶，标签才不会被访客或相邻家具压住。</summary>
        private RectTransform labelRoot;

        /// <summary>相机状态：世界根左下角相对视口左下角的偏移（视口坐标）与缩放。</summary>
        private Vector2 camPan;
        private float camZoom = 1f;
        /// <summary>滚轮设的目标缩放；camZoom 每帧向它指数逼近，滚起来才是连续的而不是一节一节跳。</summary>
        private float targetZoom = 1f;
        private Vector2 zoomAnchorViewport;
        private Vector2 zoomAnchorWorld;
        private bool zoomAnchored;
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

            // 外景层（2026-08-16 外景层级）：与主楼世界锁在同一坐标系随相机缩放——
            // 外景缩放 = AlignScale × 相机缩放（对齐变换），缩到最小档恰好整张外景满屏；
            // 压在世界层之下，总览及以上被不透明的主楼世界盖住
            exteriorRect = HouseUIRuntime.Rect(clip, "ExteriorBackdrop", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            exteriorRect.pivot = Vector2.zero;
            exteriorBackdrop = exteriorRect.gameObject.AddComponent<RawImage>();
            exteriorBackdrop.texture = Resources.Load<Texture2D>("OutGameUI/house-exterior");
            exteriorBackdrop.raycastTarget = false;

            // 世界层改由 Prefab 固化（2026-08-16）：主楼底图 + 房间矩形 + 接待室标记，
            // 布局以 Prefab 为唯一真相，缺失是报错不是回退（§16.2）
            var worldPrefab = Resources.Load<GameObject>(OutGamePrefabResourcePaths.HubSceneWorld);
            if (worldPrefab == null)
            {
                Debug.LogError("[HouseUI] Hub 场景世界层 Prefab 缺失，无法建场景（§16.2 不回退代码布局）：" + OutGamePrefabResourcePaths.HubSceneWorld);
                return;
            }
            var worldGo = Object.Instantiate(worldPrefab, clip, false);
            worldGo.name = "World";
            var worldView = worldGo.GetComponent<OutGameHubWorldView>();
            if (worldView == null || worldView.houseBackdrop == null || worldView.roomArts == null)
            {
                Debug.LogError("[HouseUI] Hub 场景世界层 Prefab 缺少视图组件或引用：OutGameHubWorldView");
                return;
            }
            worldRoot = (RectTransform)worldGo.transform;
            worldRoot.pivot = Vector2.zero; // 相机数学以左下角为原点（双保险，Prefab 已固化）
            worldGroup = worldGo.AddComponent<CanvasGroup>(); // 外景层级的淡出用（表现，不碰布局）
            houseBackdrop = worldView.houseBackdrop;
            for (var room = 0; room < HubWorldGrid.RoomCount && room < worldView.roomArts.Length; room++)
                roomArts[room] = worldView.roomArts[room];

            // 天空循环（2026-08-17 用户定案）：外景层直接播参考延时视频的分帧——
            // 日月升落、云层变换、星空、窗户亮灯全在帧里，不再单独实现各效果。
            // 交叉淡化层叠在外景底图之上，两帧按时间权重混合，时间再慢也是平滑推进。
            // 两层延时序列各用一个 UICycleBlend 材质：当前帧/下一帧在**同一次渲染**里混合。
            // 主楼层贴紧贴建筑的遮罩（天空留透明，下层外景的太阳/云/星空才透得上来）；
            // 遮罩自带四周渐隐，所以不再额外做 uv 羽化——羽化太宽会让建筑半透、露出外景那栋楼成重影。
            exteriorCycle = CreateCycleMaterial(exteriorBackdrop, null, Vector2.zero);
            // 去楼背景层：铺在完整外景之上，随主楼层的可见度淡入。
            var skyOnlyRect = HouseUIRuntime.Stretch(exteriorRect, "SkyOnly");
            skyOnly = skyOnlyRect.gameObject.AddComponent<RawImage>();
            skyOnly.raycastTarget = false;
            skyOnlyCycle = CreateCycleMaterial(skyOnly, null, Vector2.zero);

            // HouseCycle 原帧在底边裁断了支柱：从外景帧单独抽出下层栈桥，置于去楼背景之上、主楼之下。
            // 它与外景共用昼夜帧，但有独立纵向校正，使平台顶面与主楼底梁衔接。
            lowerStructureRect = HouseUIRuntime.Stretch(exteriorRect, "LowerStructure");
            lowerStructure = lowerStructureRect.gameObject.AddComponent<RawImage>();
            lowerStructure.raycastTarget = false;
            lowerStructureCycle = CreateCycleMaterial(lowerStructure, "OutGameUI/lower-structure-mask", Vector2.zero);
            if (lowerStructureCycle != null)
                lowerStructureCycle.SetVector("_GradeY", new Vector4(.055f, .245f, 1f, 0f));
            houseCycle = CreateCycleMaterial(houseBackdrop, "OutGameUI/house-cycle-mask", Vector2.zero);

            // 建筑清晰度分级（2026-08-17）：延时帧只有 1280 宽，推近 4~5 倍必糊。
            // 聚焦时把 5120 宽的静态主楼图淡入接管（昼夜靠色带调色），总览时它透明、由延时帧的光影当家。
            var staticRect = HouseUIRuntime.Stretch(worldRoot, "HouseStatic");
            houseStatic = staticRect.gameObject.AddComponent<RawImage>();
            houseStatic.texture = Resources.Load<Texture2D>("OutGameUI/house-main");
            houseStatic.raycastTarget = false;
            CreateCycleMaterial(houseStatic, "OutGameUI/house-cycle-mask", Vector2.zero); // 同一遮罩，只画建筑
            if (houseBackdrop != null)
                staticRect.SetSiblingIndex(houseBackdrop.transform.GetSiblingIndex() + 1);

            ApplySceneArt(); // 先喂烘焙图：下面按图的真实宽高比内嵌，需要贴图尺寸

            // Prefab 布局是真相：反读各矩形的实际归一化区域同步给 HubWorldGrid（相机聚焦/访客站位/热点全跟着走）。
            // 锚点 + 偏移一起算：在 Prefab 模式里用矩形工具直接拖（改的是 offset）也能生效。
            // 房间画面按烘焙图**真实宽高比**内嵌进手调矩形（贴底居中，缺口露主楼自己的墙面）——
            // 内容完整显示、永不拉伸（2026-08-16 修复：不再裁内容）
            var designSize = worldRoot.rect.size;
            if (designSize.x < 1f) designSize = new Vector2(1920f, 1080f);
            var regions = new Rect[HubWorldGrid.RoomCount + 1];
            var crops = new Rect[HubWorldGrid.RoomCount + 1];
            for (var room = 0; room < HubWorldGrid.RoomCount; room++)
            {
                crops[room] = Rect.MinMaxRect(0, 0, 1, 1);
                if (roomArts[room] == null) { regions[room] = HubWorldGrid.RegionOf(room); continue; }
                var authored = NormalizedRegion(roomArts[room].rectTransform, designSize);
                var display = FitTextureInRegion(authored, roomArts[room].texture, designSize);
                var rect = roomArts[room].rectTransform;
                rect.anchorMin = display.min;
                rect.anchorMax = display.max;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                regions[room] = display;
            }
            BuildNightRoomArts();
            regions[HubWorldGrid.Reception] = worldView.receptionArea != null
                ? NormalizedRegion(worldView.receptionArea, designSize)
                : HubWorldGrid.RegionOf(HubWorldGrid.Reception);
            crops[HubWorldGrid.Reception] = Rect.MinMaxRect(0, 0, 1, 1);
            HubWorldGrid.Configure(regions, crops);

            // 洗色层盖在房间图之上、热点与演员之下（与旧版层序一致）；随世界一起缩放（纯色无所谓拉伸）。
            // 2026-08-17 起主楼剖面播延时分帧、自带昼夜，洗色只保留很淡的一层压对比度用；
            // **必须铺到外景范围**——只盖主楼矩形的话，它自己的边界就是画面上那条竖直明暗线
            sceneWash = HouseUIRuntime.StretchPanel(worldRoot, "SceneWash", new Color(.015f, .02f, .04f, .08f));
            sceneWash.raycastTarget = false;
            var washRect = sceneWash.rectTransform;
            washRect.anchorMin = OpeningZoomFx.AlignOffset;
            washRect.anchorMax = OpeningZoomFx.AlignOffset + Vector2.one * OpeningZoomFx.AlignScale;
            washRect.offsetMin = washRect.offsetMax = Vector2.zero;

            hotspotRoot = HouseUIRuntime.Stretch(worldRoot, "FurnitureHotspots");
            // 家具标签单独一层、建在最后 = 画在最上（2026-08-17）：
            // 热点本身不能置顶（它会挡住访客的点击），但标签必须压过访客与相邻家具
            labelRoot = HouseUIRuntime.Stretch(worldRoot, "FurnitureLabels");
            BuildHotspots();
            BuildVisitorStage();

            // 环境光层：2026-08-17 主楼改播延时分帧后夜色已在帧里，本层保留为接缝（恒透明），
            // 需要额外染色时（如剧情特殊天气）直接给它上色即可
            ambientLight = HouseUIRuntime.StretchPanel(worldRoot, "AmbientLight", Color.clear);
            ambientLight.raycastTarget = false;
            UpdateDayLight();

            BindOverlay();

            // 初始相机：整栋主楼总览（开场推镜落点就是这幅画面，2026-08-16）
            SnapOverview();
        }

        /// <summary>矩形在世界根中的归一化区域：锚点 + 像素偏移（按设计尺寸折算）。</summary>
        private static Rect NormalizedRegion(RectTransform rect, Vector2 designSize)
        {
            var min = rect.anchorMin + new Vector2(rect.offsetMin.x / designSize.x, rect.offsetMin.y / designSize.y);
            var max = rect.anchorMax + new Vector2(rect.offsetMax.x / designSize.x, rect.offsetMax.y / designSize.y);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// <summary>
        /// 在手调矩形内按贴图真实宽高比取最大内嵌矩形（贴底、水平居中）：
        /// 世界归一化坐标不是等比的，比较时都换算到设计像素。贴图缺失时原样返回。
        /// </summary>
        private static Rect FitTextureInRegion(Rect authored, Texture texture, Vector2 designSize)
        {
            if (texture == null || authored.width <= 0f || authored.height <= 0f) return authored;
            var textureRatio = texture.width / (float)Mathf.Max(texture.height, 1);
            var regionPxWidth = authored.width * designSize.x;
            var regionPxHeight = authored.height * designSize.y;
            var regionRatio = regionPxWidth / regionPxHeight;
            if (Mathf.Approximately(textureRatio, regionRatio)) return authored;
            if (textureRatio > regionRatio)
            {
                // 图比矩形扁：占满宽，高度收缩、贴底
                var height = authored.width * designSize.x / textureRatio / designSize.y;
                return new Rect(authored.x, authored.y, authored.width, height);
            }
            // 图比矩形高：占满高，宽度收缩、水平居中
            var width = authored.height * designSize.y * textureRatio / designSize.x;
            return new Rect(authored.center.x - width * .5f, authored.y, width, authored.height);
        }

        /// <summary>
        /// 聚焦场上某访客（2026-08-16 用户定案）：镜头移动到**访客站位**并放大——
        /// 倍率取所在区域标准取景的 1.25 倍（下限 2.5），比看整间房更贴近人物。
        /// </summary>
        public void FocusVisitor(int instanceId)
        {
            if (stage == null || !stage.TryGetActorWorld(instanceId, out var world01)) return;
            var room = HubWorldGrid.RoomAt(world01);
            var targetZoom = Mathf.Clamp(
                (room != HubWorldGrid.None ? HubWorldGrid.FocusZoom(room) : 3f) * 1.25f, 2.5f, MaxZoom);
            FocusWorldPoint(world01, targetZoom);
        }

        /// <summary>相机平滑推到「世界点居中 + 指定倍率」（聚焦访客用；边界仍由 ClampCamera 兜底）。</summary>
        private void FocusWorldPoint(Vector2 world01, float endZoom)
        {
            if (worldRoot == null || sceneRoot == null) return;
            var viewport = sceneRoot.rect.size;
            if (viewport.x < 1f) viewport = new Vector2(1920f, 1080f);
            SyncWorldSize(viewport);
            var targetPan = viewport * .5f - Vector2.Scale(world01, viewport) * endZoom;
            KillFocusTween();
            var fromPan = camPan;
            var fromZoom = camZoom;
            focusTween = DOTween.To(() => 0f, t =>
            {
                if (sceneRoot == null || worldRoot == null) { KillFocusTween(); return; }
                camZoom = Mathf.Lerp(fromZoom, endZoom, t);
                camPan = Vector2.Lerp(fromPan, targetPan, t);
                SyncZoomTarget(); // 补间在开车，别让滚轮的平滑逻辑再插手
                var size = sceneRoot.rect.size;
                ClampCamera(size);
                ApplyCamera();
                DetectCurrentRoom(size);
            }, 1f, .55f).SetEase(Ease.InOutCubic).SetUpdate(true);
        }

        // ── 缩放平滑与「重影档位」跳过（2026-08-17）──

        /// <summary>
        /// 重影带：总览 ⇄ 外景之间主楼层半透明的那段缩放（主楼建筑与外景那栋楼同时可见 = 招牌旗杆成双）。
        /// 下界即主楼完全透明处，上界是总览（完全不透明）。滚轮的目标缩放不允许落在带内。
        /// </summary>
        private static float GhostBandLow => Mathf.Lerp(ExteriorMinZoom, OverviewZoom, .4f);

        /// <summary>把目标缩放推出重影带：按滚动方向送到最近的一侧边界，玩家因此停不在重影档位上。</summary>
        private static float SnapOutOfGhostBand(float zoom, float scroll)
        {
            if (zoom <= GhostBandLow || zoom >= OverviewZoom) return zoom;
            return scroll < 0f ? GhostBandLow : OverviewZoom; // 往外滚就落到外景侧，往里滚就落到总览侧
        }

        /// <summary>每帧把 camZoom 指数逼近 targetZoom，并保持光标下的世界点不动。</summary>
        private void ApplyZoomEasing()
        {
            if (Mathf.Approximately(camZoom, targetZoom)) return;
            // 穿过重影带时加速通过（那几帧的半透明叠影不该被看清）
            var inBand = camZoom > GhostBandLow && camZoom < OverviewZoom;
            var speed = inBand ? 26f : 13f;
            camZoom = Mathf.Lerp(camZoom, targetZoom, 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime));
            if (Mathf.Abs(camZoom - targetZoom) < 5e-4f) camZoom = targetZoom;
            // 拖拽期间**不做**锚点校正：否则每帧把 camPan 重设回缩放锚点，会吃掉玩家拖出的位移，
            // 在临界点（滚轮被吸附、逼近要持续十几帧）尤其明显 —— 手感就是拖不动、卡一下。
            if (zoomAnchored && !panning) camPan = zoomAnchorViewport - zoomAnchorWorld * camZoom;
        }

        /// <summary>相机补间/直达后同步目标值，免得平滑逻辑把镜头又拉回去。</summary>
        private void SyncZoomTarget()
        {
            targetZoom = camZoom;
            zoomAnchored = false;
        }

        /// <summary>相机当前是否已聚焦在某区域（缩放到聚焦档且视口中心落在该区域）。</summary>
        public bool IsFocusedOn(int roomIndex)
        {
            if (worldRoot == null || sceneRoot == null || camZoom < FocusedZoomThreshold) return false;
            var viewport = sceneRoot.rect.size;
            if (viewport.x < 1f) return false;
            var centerPoint = (viewport * .5f - camPan) / camZoom;
            var world01 = new Vector2(
                Mathf.Clamp01(centerPoint.x / viewport.x),
                Mathf.Clamp01(centerPoint.y / viewport.y));
            return HubWorldGrid.RoomAt(world01) == roomIndex;
        }

        /// <summary>无动画直达总览（建层初始化用）。</summary>
        private void SnapOverview()
        {
            var viewport = sceneRoot.rect.size;
            if (viewport.x < 1f) viewport = new Vector2(1920f, 1080f);
            SyncWorldSize(viewport);
            camZoom = OverviewZoom;
            camPan = Vector2.zero;
            SyncZoomTarget();
            ApplyCamera();
        }

        // ══════════ 昼夜光照 ══════════

        /// <summary>
        /// 把四张夜间房间图铺在对应高清房间烘焙图上方。夜图只接管背景；家具前景代理与访客舞台
        /// 后建在更高层，夜图完全浮现后家具和人物仍会保留。
        /// </summary>
        private void BuildNightRoomArts()
        {
            for (var room = 0; room < nightRoomArts.Length; room++)
            {
                if (roomArts[room] == null) continue;
                var path = $"OutGameUI/RoomNight/room-night-{room + 1:00}";
                var texture = Resources.Load<Texture2D>(path);
                if (texture == null)
                {
                    Debug.LogWarning("[HouseUI] 夜间房间图缺失：" + path);
                    continue;
                }
                var rect = HouseUIRuntime.Stretch(roomArts[room].rectTransform, "NightRoomArt");
                var image = rect.gameObject.AddComponent<RawImage>();
                image.texture = texture;
                image.color = Color.clear;
                image.raycastTarget = false;
                nightRoomArts[room] = image;
            }
        }

        /// <summary>每帧按局内时钟推环境光（HubPage.OnUpdate 调；叠加层开着也走，面板后面的天色照常流动）。
        /// 色带定义在 HouseDayLight（与标题页封面共用）。</summary>
        public void UpdateDayLight()
        {
            if (ambientLight == null) return;
            var (tint, _) = HouseDayLight.Now();
            // 外景层与主楼剖面放的都是延时分帧，天色/夜色在帧里，不再叠调色与夜罩（叠了会双重变暗）
            if (exteriorBackdrop != null) exteriorBackdrop.color = Color.white;
            if (houseBackdrop != null) houseBackdrop.color = Color.white;
            // 清晰度分级（2026-08-17）：总览时延时帧当家（有室内光影动画）；
            // 往单间推近时，高清静态主楼图与高清房间烘焙图一起淡入接管（延时帧只有 1280 宽，推近了糊）
            var lod = Mathf.InverseLerp(OverviewZoom * 1.35f, FocusedZoomThreshold, camZoom);
            if (houseStatic != null) houseStatic.color = new Color(tint.r, tint.g, tint.b, lod);
            var roomColor = Color.Lerp(Color.white, tint, .5f); // 家具/房间只上半强度，好跟延时帧衔接
            roomColor.a = lod;
            var nightAlpha = HouseDayLight.NightRoomAlphaNow() * lod;
            for (var room = 0; room < roomArts.Length; room++)
            {
                if (roomArts[room] != null) roomArts[room].color = roomColor;
                if (nightRoomArts[room] != null)
                    nightRoomArts[room].color = new Color(1f, 1f, 1f, nightAlpha);
            }
            ambientLight.color = Color.clear;
            UpdateSceneCycle();
        }

        /// <summary>建延时序列的播放材质；maskPath 空 = 整幅可见，fadeUV = 额外的四周羽化（uv 口径，零 = 不加）。</summary>
        private static Material CreateCycleMaterial(RawImage layer, string maskPath, Vector2 fadeUV)
        {
            if (layer == null) return null;
            var shader = Resources.Load<Shader>("Shaders/UICycleBlend");
            if (shader == null)
            {
                Debug.LogWarning("[HouseUI] 延时播放 shader 缺失（Resources/Shaders/UICycleBlend），退化为单帧显示");
                return null;
            }
            var material = new Material(shader);
            var mask = string.IsNullOrEmpty(maskPath) ? null : Resources.Load<Texture2D>(maskPath);
            if (mask == null && !string.IsNullOrEmpty(maskPath))
                Debug.LogWarning("[HouseUI] 延时遮罩缺失：" + maskPath + "（该层会连天空一起画）");
            material.SetTexture("_MaskTex", mask != null ? mask : Texture2D.whiteTexture);
            material.SetVector("_FadeUV", fadeUV);
            layer.material = material;
            return material;
        }

        /// <summary>
        /// 场景昼夜循环（2026-08-17 用户定案）：外景层与主楼剖面各按局内时钟播自己的延时分帧，
        /// 相邻两帧交叉淡化——日月升落、云层、星空、窗灯、室内光影都在帧里，随时间平滑推进。
        /// 「昼夜交替」关闭时定格在正午。
        /// </summary>
        private void UpdateSceneCycle()
        {
            var minute = HouseSettings.Data.dayNightEnabled
                ? GameManager.Instance.HouseClockManager.Data.MinuteOfDayF
                : 12f * 60f;
            PlayCycle(SkyCycle.Exterior, minute, exteriorBackdrop, exteriorCycle);
            PlayCycle(SkyCycle.SkyOnly, minute, skyOnly, skyOnlyCycle);
            PlayCycle(SkyCycle.Exterior, minute, lowerStructure, lowerStructureCycle);
            PlayCycle(SkyCycle.House, minute, houseBackdrop, houseCycle);
            if (lowerStructureCycle != null)
            {
                var gain = SampleLowerStructureGrade(minute);
                lowerStructureCycle.SetVector("_GradeGain", new Vector4(gain.x, gain.y, gain.z, 1f));
            }
            // 纯天空层的可见度 = 主楼层的可见度（主楼一露面，外景的房子就得被藏起来）
            var worldAlpha = worldGroup != null ? worldGroup.alpha : 1f;
            if (skyOnly != null) skyOnly.color = new Color(1f, 1f, 1f, worldAlpha);
            if (lowerStructure != null) lowerStructure.color = new Color(1f, 1f, 1f, worldAlpha);
        }

        private static void PlayCycle(SkyCycle cycle, float minute, RawImage layer, Material material)
        {
            if (layer == null) return;
            if (!cycle.Sample(minute, out var from, out var to, out var blend)) return;
            layer.texture = from; // → shader 的 _MainTex（CanvasRenderer 直接喂）
            if (material == null) return;
            material.SetTexture("_NextTex", to);
            material.SetFloat("_Blend", blend);
        }

        /// <summary>下层支柱来自外景帧，接缝处需跟随 HouseCycle 的曝光与色温；关键点间连续插值避免跳色。</summary>
        private static Vector3 SampleLowerStructureGrade(float minuteOfDay)
        {
            var t = Mathf.Repeat(minuteOfDay - 420f, 1440f);
            for (var i = 1; i < LowerStructureGradeKeys.Length; i++)
            {
                var next = LowerStructureGradeKeys[i];
                if (t > next.x) continue;
                var previous = LowerStructureGradeKeys[i - 1];
                var blend = Mathf.InverseLerp(previous.x, next.x, t);
                return Vector3.Lerp(
                    new Vector3(previous.y, previous.z, previous.w),
                    new Vector3(next.y, next.z, next.w),
                    blend);
            }
            var fallback = LowerStructureGradeKeys[LowerStructureGradeKeys.Length - 1];
            return new Vector3(fallback.y, fallback.z, fallback.w);
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

            // 滚轮只改**目标**缩放，实际缩放每帧指数逼近（2026-08-17）——直接改 camZoom 是一格一跳，太生硬。
            var scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > .01f && insideScene)
            {
                KillFocusTween();
                // 以鼠标为锚：记下光标下的世界点，插值过程中让它始终停在光标处
                zoomAnchorViewport = pointerLocal;
                zoomAnchorWorld = (pointerLocal - camPan) / camZoom;
                zoomAnchored = true;
                targetZoom = SnapOutOfGhostBand(
                    Mathf.Clamp(targetZoom * (1f + scroll * .16f), ExteriorMinZoom, MaxZoom), scroll);
            }
            ApplyZoomEasing();

            if (Input.GetMouseButtonDown(0) && insideScene && !IsPointerOverBlockingUI())
            {
                KillFocusTween();
                zoomAnchored = false; // 手一按下就把平移控制权交给拖拽，缩放不再回拉视角
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
            var worldPoint = (pointerLocal - camPan) / camZoom;
            var world01 = new Vector2(
                Mathf.Clamp01(worldPoint.x / viewport.x),
                Mathf.Clamp01(worldPoint.y / viewport.y));
            var clickedRoom = hotspot != null ? hotspot.RoomIndex : HubWorldGrid.RoomAt(world01);
            // 当前聚焦对象 = 视口中心所在区域（总览/外景态没有聚焦对象）
            var centerPoint = (viewport * .5f - camPan) / camZoom;
            var center01 = new Vector2(
                Mathf.Clamp01(centerPoint.x / viewport.x),
                Mathf.Clamp01(centerPoint.y / viewport.y));
            var focusedRoom = camZoom >= FocusedZoomThreshold ? HubWorldGrid.RoomAt(center01) : HubWorldGrid.None;

            // 任意缩放**单击**房间即聚焦（2026-08-16 用户定案）：点中的不是当前聚焦房间就推过去；
            // 接待室也可聚焦（招呼排队的客人）；墙体/天空不响应
            if (clickedRoom != HubWorldGrid.None && clickedRoom != focusedRoom)
            {
                SfxManager.Play(ESfx.PageTransition); // 音效需求 #5：视野切换即转场
                FocusRoom(clickedRoom);
                lastGroundClickTime = 0f;
                return;
            }
            // 点的是当前聚焦房间：家具热点开详情，空地走双击缩回总览
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
            SfxManager.Play(ESfx.PageTransition);
            ZoomToOverview();
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

        /// <summary>缩回总览（zoom 1 = 整栋主楼尽收眼底）。此时世界尺寸恰等于视口，平移会被 ClampCamera 钳成 0。</summary>
        private void ZoomToOverview()
        {
            if (worldRoot == null || sceneRoot == null) return;
            KillFocusTween();
            var fromZoom = camZoom;
            var fromPan = camPan;
            focusTween = DOTween.To(() => 0f, t =>
            {
                if (sceneRoot == null || worldRoot == null) { KillFocusTween(); return; }
                camZoom = Mathf.Lerp(fromZoom, OverviewZoom, t);
                camPan = Vector2.Lerp(fromPan, Vector2.zero, t);
                SyncZoomTarget();
                ClampCamera(sceneRoot.rect.size);
                ApplyCamera();
                // 刻意**不**调 DetectCurrentRoom：总览态没有当前房间，沿用缩回前的那间（§4.4）
            }, 1f, .55f).SetEase(Ease.InOutCubic).SetUpdate(true);
        }

        /// <summary>房间导航/方向键/访客聚焦共用：相机平滑推到目标区域的**标准取景**（区域宽推满视口宽）。
        /// 不再保留更深的当前缩放（2026-08-16：带着客房深缩放跳接待厅会糊成特写）。</summary>
        public void FocusRoom(int roomIndex)
        {
            if (worldRoot == null || sceneRoot == null) return;
            var viewport = sceneRoot.rect.size;
            if (viewport.x < 1f) { SnapToRoom(roomIndex); return; }
            SyncWorldSize(viewport);
            var endZoom = HubWorldGrid.FocusZoom(roomIndex);
            var targetPan = PanCenteredOn(roomIndex, endZoom, viewport);
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
                camZoom = Mathf.Lerp(fromZoom, endZoom, t);
                camPan = Vector2.Lerp(fromPan, targetPan, t);
                SyncZoomTarget();
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
            camZoom = HubWorldGrid.FocusZoom(roomIndex);
            camPan = PanCenteredOn(roomIndex, camZoom, viewport);
            SyncZoomTarget();
            ClampCamera(viewport);
            ApplyCamera();
        }

        /// <summary>让某区域中心对准视口中心时的平移量。</summary>
        private Vector2 PanCenteredOn(int roomIndex, float zoom, Vector2 viewport)
        {
            var worldCenter01 = HubWorldGrid.RegionOf(roomIndex).center;
            var worldPoint = Vector2.Scale(worldCenter01, viewport); // 世界尺寸 = 1× 视口（总览即全楼）
            return viewport * .5f - worldPoint * zoom;
        }

        /// <summary>世界根/外景层尺寸恒等于视口（图均 16:9，分辨率变化时跟随）。</summary>
        private void SyncWorldSize(Vector2 viewport)
        {
            viewportSize = viewport;
            if ((worldRoot.sizeDelta - viewport).sqrMagnitude > .5f) worldRoot.sizeDelta = viewport;
            if (exteriorRect != null && (exteriorRect.sizeDelta - viewport).sqrMagnitude > .5f)
                exteriorRect.sizeDelta = viewport;
            if (lowerStructureRect != null)
            {
                lowerStructureRect.localScale = new Vector3(LowerStructureScaleX, 1f, 1f);
                lowerStructureRect.anchoredPosition = new Vector2(
                    viewport.x * LowerStructureShiftX,
                    -viewport.y * LowerStructureShiftY);
            }
        }

        private void ApplyCamera()
        {
            worldRoot.localScale = new Vector3(camZoom, camZoom, 1f);
            worldRoot.anchoredPosition = camPan;
            if (exteriorRect != null)
            {
                // 外景点 e 对应主楼点 m = s·e + t，代入世界渲染式 → 外景缩放 s·z、位移 t·视口·z + 相机位移
                var scale = OpeningZoomFx.AlignScale * camZoom;
                exteriorRect.localScale = new Vector3(scale, scale, 1f);
                exteriorRect.anchoredPosition =
                    Vector2.Scale(OpeningZoomFx.AlignOffset, viewportSize) * camZoom + camPan;
            }
        }

        /// <summary>
        /// 把画面钳在**外景图**范围内（2026-08-17 用户定案）：外景层与主楼按对齐变换同坐标系渲染、
        /// 始终垫在主楼后面，所以任何缩放下都以外景不露底为界——主楼四周多出外景余量
        /// （左 23%/右 10%/下 17%/上 17%），放大后也能继续往下拖看到山坡海面；主楼底图四边已羽化融进外景。
        /// 最小档外景恰好满屏（边界收敛为对齐位）；总览以下主楼剖面按进度淡出（放大回来四房浮现）。
        /// </summary>
        private void ClampCamera(Vector2 viewport)
        {
            camZoom = Mathf.Clamp(camZoom, ExteriorMinZoom, MaxZoom);
            var extScale = OpeningZoomFx.AlignScale * camZoom;
            var extOffset = Vector2.Scale(OpeningZoomFx.AlignOffset, viewport) * camZoom;
            camPan.x = Mathf.Clamp(camPan.x, viewport.x * (1f - extScale) - extOffset.x, -extOffset.x);
            camPan.y = Mathf.Clamp(camPan.y, viewport.y * (1f - extScale) - extOffset.y, -extOffset.y);
            if (worldGroup == null) return;
            if (camZoom < OverviewZoom)
            {
                // 内景浮现集中在贴近总览的后段（放大到房屋接近剖面大小才显形，2026-08-16 用户定案）
                var fadeStart = Mathf.Lerp(ExteriorMinZoom, OverviewZoom, .4f);
                worldGroup.alpha = Mathf.InverseLerp(fadeStart, OverviewZoom, camZoom);
            }
            else
            {
                worldGroup.alpha = 1f;
            }
        }

        /// <summary>
        /// 视口中心落在哪个业务房间即当前房间；变化时回调页面刷新导航与说明卡。
        ///
        /// **总览态直接短路**（§4.4）：看整栋楼时没有「当前房间」概念，沿用聚焦前的那间是最小意外；
        /// 双击选房把 zoom 推到聚焦档，这里自然恢复工作。
        /// 接待室与墙体/天空不算业务房间（RoomAt 返回 Reception/None 时保持现状）。
        /// </summary>
        private void DetectCurrentRoom(Vector2 viewport)
        {
            if (camZoom < FocusedZoomThreshold) return;
            var worldPoint = (viewport * .5f - camPan) / camZoom;
            var world01 = new Vector2(
                Mathf.Clamp01(worldPoint.x / viewport.x),
                Mathf.Clamp01(worldPoint.y / viewport.y));
            var room = HubWorldGrid.RoomAt(world01);
            if (room < 0 || room >= HubWorldGrid.RoomCount) return;
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
                if (stage != null) stage.RebuildFurnitureProxies(); // 深度代理跟着新布局走
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
            if (labelRoot != null)
                for (var i = labelRoot.childCount - 1; i >= 0; i--)
                    Object.Destroy(labelRoot.GetChild(i).gameObject);
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

                    // 标签建在**独立的置顶层**、锚在家具矩形上（2026-08-17）：
                    // 紧贴家具顶沿（原来悬在 46px 外太远），且不会被访客或相邻家具压住
                    var regionWidth = HubWorldGrid.RegionOf(room).width;
                    var anchorRect = HouseUIRuntime.Rect(labelRoot != null ? labelRoot : hotspotRoot,
                        $"Label_{room}_{info.Entry.id}", min, max, Vector2.zero, Vector2.zero);
                    var card = HouseUIRuntime.Panel(anchorRect, "Card", new Vector2(.5f, 1),
                        new Vector2(0, 34f * regionWidth), new Vector2(250, 76), new Color(.32f, .06f, .18f, .92f));
                    card.transform.localScale = Vector3.one * regionWidth;
                    HouseUIRuntime.StretchLabel(card.transform, "Text",
                        $"＋  {info.Entry.displayName}\n<size=13>查看家具</size>", 19, HouseUIUtil.White,
                        TextAnchor.MiddleCenter, FontStyle.Bold);
                    var cardGroup = HouseUIUtil.Group(card.gameObject, 0f);
                    cardGroup.blocksRaycasts = false;
                    cardGroup.interactable = false;

                    var trigger = hotspot.gameObject.AddComponent<EventTrigger>();
                    var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                    // 同层内也提到最上：几件家具挨着时，当前悬停的那个标签压过邻居
                    enter.callback.AddListener(_ => anchorRect.SetAsLastSibling());
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

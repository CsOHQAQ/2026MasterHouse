#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// 「制作咖啡」小游戏的 Prefab 与资产生成器（与 CircuitMinigamePrefabGenerator 同一策略）：
    /// **默认只补缺失、绝不覆盖手调**；要恢复默认布局必须从菜单显式确认重建。
    ///
    /// 生成物放在 Assets/GameData/Minigames/ 而不是 Resources 下——
    /// 整条链路（日程表 → NeedDef → MinigameDef → Prefab / 关卡）全是强引用（§8.5）。
    ///
    /// ⚠ 本文件是 §8.5 依赖方向约束的明示例外（authoring 工具，整个类在 UNITY_EDITOR 内）：
    /// 它同时认识 Coffee 内部与宿主层的 MinigameDef / MinigameNeedDef。
    /// 约束真正管的是运行时代码：Minigame/Coffee/ 下除本文件外不得出现任何 Manager 或宿主类型引用。
    /// </summary>
    public static class CoffeeMinigamePrefabGenerator
    {
        private const string Folder = "Assets/GameData/Minigames";
        private const string LevelFolder = Folder + "/CoffeeLevels";
        private const string PrefabPath = Folder + "/CoffeeMinigame.prefab";
        private const string MinigameDefPath = Folder + "/Minigame_制作咖啡.asset";
        private const string DefaultLevelPath = LevelFolder + "/Coffee_Default.asset";
        private const string NeedDefPath = "Assets/GameData/Needs/Need_制作咖啡.asset";
        private const string GrindLoopClipPath = "Assets/Resources/SoundEffect/MiniGame/Coffee/研磨音效.mp3";
        private const string PourLoopClipPath = "Assets/Resources/SoundEffect/MiniGame/Coffee/冲泡音效.mp3";
        // 阶段通关音暂时复用全局正向提示音（2026-08-20）；将来有专属素材直接换 Prefab 上的引用
        private const string StageClearClipPath = "Assets/Resources/SoundEffect/4_ScoreGain_260812.mp3";

        // ── 2.0 设计图素材（2026-08-20 接入冲泡环节；磨豆底图待美术定稿，暂不接）──
        private const string PourArtDir = "Assets/PC ui 2.0/咖啡/";
        private const string GrindArtDir = "Assets/PC ui 2.0/咖啡研磨/";
        private const string PauseArtDir = "Assets/PC ui 2.0/局内暂停弹窗/";

        /// <summary>
        /// 全页唯一的素材缩放规则：**显示尺寸 = 素材原始像素 × ArtScale**。
        ///
        /// 底图 5120×2880 正好是 1920×1080 的 2.667 倍，其余素材（ESC 条、底卡、暂停底板与按钮）
        /// 都按同一倍率导出——从设计图量到的 ESC 显示尺寸 189×62 反推素材内容 503×165，
        /// 正好对上（2026-08-20 验证过）。所以不必逐张给尺寸。
        /// </summary>
        private const float ArtScale = 1920f / 5120f;

        // ── 设计图量出来的版式（1920×1080 口径，见 Docs/待办工作流/冲泡咖啡参考.png）──

        /// <summary>左下 ESC 条的视觉左边距；左上底卡沿用同一个，两边对齐才不显歪</summary>
        private const float EdgeMarginX = 42f;
        private const float EscMarginY = 20f;
        private const float CardMarginY = 30f;

        /// <summary>底部提示文字的中心距屏底距离</summary>
        private const float MessageBottom = 173f;

        /// <summary>
        /// 咖啡液面圆在**底图自己画面里**的位置，归一化（原点左下）：
        /// 5120×2880 的底图上实测圆心 (2593, 1373)、半径 560。
        ///
        /// 判定区因此**挂在底图节点底下、用锚点比例定位**，而不是锚屏幕中心给死尺寸——
        /// 底图被「填满裁切」放大时判定圆跟着一起放大，视觉与判定在任何屏幕比例下都咬合。
        /// （若锚屏幕中心：16:10 屏上底图会被放大约 11%，画出来的杯子就比判定圆大一圈。
        /// 这是 2026-08-20 特意选的做法，别改回去。）
        /// </summary>
        private static readonly Vector2 CupCenterUv = new Vector2(2593f / 5120f, 1f - 1373f / 2880f);
        private const float CupRadiusUvX = 560f / 5120f;
        private const float CupRadiusUvY = 560f / 2880f;

        /// <summary>
        /// 磨豆两条轨道在底图自己画面里的位置，归一化（原点左下）：5120×2880 的底图上实测
        /// 磨盘圆心 (2545, 1331)，白瓷碗上那两道细线的半径 800（内轨）与 982（外轨）。
        /// 轨道区按**外轨直径**摆、挂在底图节点下，理由同 <see cref="CupCenterUv"/>。
        /// 换算到 1920×1080：圆心在屏幕中心左 6、上 41，外轨 r=368、内轨 r=300。
        /// </summary>
        private static readonly Vector2 TrackCenterUv = new Vector2(2545f / 5120f, 1f - 1331f / 2880f);
        private const float TrackRadiusUvX = 982f / 5120f;
        private const float TrackRadiusUvY = 982f / 2880f;

        /// <summary>障碍珠的显示直径：素材内容 100px × ArtScale，照规矩来</summary>
        private const float BeadDisplaySize = 38f;
        private const float BeadContentPx = 100f;

        /// <summary>
        /// 把手（指针）的显示直径。**这是全页唯一破例不按 ArtScale 的件**：
        /// 素材内容 284px × ArtScale = 106，而两条轨道中心只隔 68（368−300）——
        /// 106 的指针待在内轨时外沿到 r=353，已经压住外轨珠子的内沿（r=349），
        /// 既看不出自己在哪条轨，切轨的位移也只有自身直径的 2/3。
        /// 缩到 68（半径 34）后离另一条轨上的珠子留 15px 空隙（2026-08-20 拍板）。
        /// </summary>
        private const float PointerDisplaySize = 68f;
        private const float PointerContentPx = 284f;

        // ── 取色 ──

        /// <summary>页面底色：水彩纸的米白。只在磨豆环节露出来（冲泡有整屏底图盖住）</summary>
        private static readonly Color Paper = new Color(0.969f, 0.953f, 0.922f, 1f);

        /// <summary>设计图上那支蓝 #5676A5：提示文字、ESC 上的字、暂停按钮的字都用它</summary>
        private static readonly Color InkBlue = new Color(0.337f, 0.463f, 0.647f, 1f);

        /// <summary>次要文字（得分那一行、调参标签）：同一支蓝调淡</summary>
        private static readonly Color InkBlueMuted = new Color(0.451f, 0.549f, 0.694f, 1f);

        /// <summary>进度条的槽色（条本身用 InkBlue）</summary>
        private static readonly Color BarTrack = new Color(0.337f, 0.463f, 0.647f, 0.22f);

        /// <summary>
        /// 暂停遮罩：与素材「遮罩.png」同浓度（整张都是 alpha 153/255 的纯黑）。
        /// 用纯色不用那张图——一张 5120×2880 的全屏纯色贴图不值这份显存与包体。
        /// </summary>
        private static readonly Color Scrim = new Color(0f, 0f, 0f, 0.6f);

        [MenuItem("MasterHouse/小游戏/创建制作咖啡资产（补齐缺失）")]
        public static void CreateIfMissing() => Generate(false);

        [MenuItem("MasterHouse/小游戏/重建制作咖啡 Prefab（覆盖手调）")]
        public static void Rebuild()
        {
            if (!EditorUtility.DisplayDialog("重建制作咖啡 Prefab",
                    "会用默认布局覆盖 " + PrefabPath + " 上的全部手调内容，且不能 Undo。\n\n" +
                    "MinigameDef / 关卡 / NeedDef 资产不受影响（只补缺失）。",
                    "重建", "取消"))
                return;
            Generate(true);
        }

        private static void Generate(bool overwritePrefab)
        {
            EnsureFolder("Assets/GameData", "Minigames");
            EnsureFolder(Folder, "CoffeeLevels");
            var created = new List<string>();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null || overwritePrefab)
            {
                prefab = BuildPrefab();
                created.Add(PrefabPath + (overwritePrefab ? "（重建）" : string.Empty));
            }
            else if (PatchPrefabIfMissing(out var patched))
            {
                created.Add(PrefabPath + "（补" + patched + "）");
            }

            var defaultLevel = AssetDatabase.LoadAssetAtPath<CoffeeLevelDef>(DefaultLevelPath);
            if (defaultLevel == null)
            {
                defaultLevel = ScriptableObject.CreateInstance<CoffeeLevelDef>();
                AssetDatabase.CreateAsset(defaultLevel, DefaultLevelPath);
                created.Add(DefaultLevelPath);
            }

            var def = AssetDatabase.LoadAssetAtPath<MinigameDef>(MinigameDefPath);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<MinigameDef>();
                def.minigameId = "coffee";
                def.displayName = "制作咖啡";
                def.prefab = prefab;
                def.levels = new List<MinigameLevelDef> { defaultLevel };
                AssetDatabase.CreateAsset(def, MinigameDefPath);
                created.Add(MinigameDefPath);
            }
            else
            {
                // 只补空引用，不动策划已经配好的
                if (def.prefab == null)
                {
                    def.prefab = prefab;
                    EditorUtility.SetDirty(def);
                    created.Add(MinigameDefPath + "（补 prefab 引用）");
                }
                if (def.levels == null || def.levels.Count == 0)
                {
                    def.levels = new List<MinigameLevelDef> { defaultLevel };
                    EditorUtility.SetDirty(def);
                    created.Add(MinigameDefPath + "（补空关卡池）");
                }
            }

            var need = AssetDatabase.LoadAssetAtPath<MinigameNeedDef>(NeedDefPath);
            if (need == null && AssetDatabase.IsValidFolder("Assets/GameData/Needs"))
            {
                need = ScriptableObject.CreateInstance<MinigameNeedDef>();
                need.needId = "coffee";
                need.description = "想喝一杯现磨的手冲咖啡，拜托你了";
                need.minigame = def;
                AssetDatabase.CreateAsset(need, NeedDefPath);
                created.Add(NeedDefPath);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(created.Count > 0
                ? "[制作咖啡] 已创建：\n" + string.Join("\n", created) +
                  "\n\n本菜单只建小游戏自己的资产。要真正跑起来还缺两样共享内容（都是策划数据，请手动配）：" +
                  "\n① 日程表某一行的「需求」列换成 Need_制作咖啡（当前 9 行都已配了其他需求，挑一行换）" +
                  "\n② Excel/对话表.xlsx 给 Need_制作咖啡 配一行 needTalk、写一个带 StartMinigame 事件的选项，" +
                  "然后跑 Tools/导表/export_config.bat"
                : "[制作咖啡] 资产已齐全，未做修改。");
        }

        // ══════════ Prefab 布局（1920×1080 参考分辨率）══════════

        private static GameObject BuildPrefab()
        {
            var root = new GameObject("CoffeeMinigamePage", typeof(RectTransform), typeof(Image),
                typeof(CoffeeMinigameView), typeof(CoffeeMinigame));
            root.layer = 5;
            var rootRect = (RectTransform)root.transform;
            Stretch(rootRect);
            var backdrop = root.GetComponent<Image>();
            backdrop.color = Paper;
            backdrop.raycastTarget = true; // 挡住底下 Hub 页的点击；全屏页没有暴露在外的遮罩可点

            var view = root.GetComponent<CoffeeMinigameView>();

            // 兄弟顺序即绘制顺序：两个环节根在最下，HUD 压在上面，暂停弹窗永远在最顶
            BuildGrind(rootRect, view);
            BuildPour(rootRect, view);
            BuildHud(rootRect, view);
            BuildFooter(rootRect, view);
            BuildEscButton(rootRect, view);
            BuildTransition(rootRect, view);
            BuildPause(rootRect, view);
            AssignAudioClips(view);

            bool ok;
            var asset = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out ok);
            if (!ok) Debug.LogError("[制作咖啡] Prefab 保存失败：" + PrefabPath);
            Object.DestroyImmediate(root);
            return asset;
        }

        /// <summary>
        /// HUD（2026-08-20 按 2.0 设计图重摆）：左上角一张空白底卡，从上到下装
        /// 「阶段名 / 上一环节得分 / 进度条」；底部一行提示文字直接压在底图上（设计图里没有底板）。
        ///
        /// 设计图上其实只画了提示文字与左下角的 ESC，底卡这一套是访谈时补回来的——
        /// 冲泡环节的进度不再由水位表达（满杯底图），没有这根条玩家就完全不知道还要搅多久。
        /// </summary>
        private static void BuildHud(RectTransform parent, CoffeeMinigameView view)
        {
            // 空白底卡素材躺在「咖啡研磨」目录里，但它是张通用空卡，两个环节共用一张
            var cardSprite = Art(GrindArtDir, "极简风格天气时间卡片-3 1");

            // 素材 624×350，四周是水彩留白，实际内容 554×309 且正好居中——
            // 所以 rect 中心 = 视觉中心，按视觉边距算位置即可，不必另做 pivot 修正
            var content = new Vector2(554f, 309f) * ArtScale;
            var card = ArtImage(parent, "HudCard", cardSprite, new Vector2(0, 1),
                new Vector2(EdgeMarginX + content.x * .5f, -(CardMarginY + content.y * .5f)));
            card.raycastTarget = false;
            view.hudCard = card;

            view.phaseLabel = Label(card.transform, "Phase", "① 磨豆子", 26, InkBlue,
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 34), new Vector2(188, 34),
                TextAnchor.MiddleCenter, FontStyle.Bold);

            view.scoreLabel = Label(card.transform, "Score", "得分 50/50", 20, InkBlueMuted,
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 3), new Vector2(188, 28),
                TextAnchor.MiddleCenter);

            var track = Rect(card.transform, "ProgressBar", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                new Vector2(0, -34), new Vector2(168, 12));
            ImageOn(track, BarTrack).raycastTarget = false;

            // 填充条：代码驱动 anchorMax.x（0~1），初始为空
            var fill = Rect(track, "Fill", new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, Vector2.zero);
            ImageOn(fill, InkBlue).raycastTarget = false;
            view.progressFill = fill;
        }

        /// <summary>
        /// 左下角的「ESC 暂停」键位条（设计图：视觉左边距 42、下边距 20，显示 189×62）。
        /// 文案烘在素材里，所以这颗按钮不挂 Text；两态走 SpriteSwap。
        /// 「咖啡」与「咖啡研磨」两个目录各有一份同尺寸的 ESC 图，这里统一用冲泡那份。
        /// </summary>
        private static void BuildEscButton(RectTransform parent, CoffeeMinigameView view)
        {
            var normal = Art(PourArtDir, "ESC-默认");
            var hover = Art(PourArtDir, "ESC-hover");
            var content = new Vector2(503f, 165f) * ArtScale; // 素材的不透明内容，用来按视觉边距定位
            var image = ArtImage(parent, "EscButton", normal, Vector2.zero,
                new Vector2(EdgeMarginX + content.x * .5f, EscMarginY + content.y * .5f));
            view.escButton = SpriteSwapButton(image, normal, hover);
        }

        /// <summary>
        /// 磨豆环节（2026-08-20 接 2.0 美术）：整屏底图 + 障碍珠模板 + 把手指针。
        /// **两条轨道不由代码画**——它们是底图上白瓷碗那两道细线，运行时只生成红珠障碍和指针。
        /// </summary>
        private static void BuildGrind(RectTransform parent, CoffeeMinigameView view)
        {
            var grindRoot = Rect(parent, "GrindRoot", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            view.grindRoot = grindRoot;

            // 与冲泡同例：名字带 background 吃昼夜调色，EnvelopeParent 填满裁切
            var background = ArtImage(grindRoot, "Background", Art(GrindArtDir, "底图"),
                new Vector2(.5f, .5f), Vector2.zero);
            background.raycastTarget = true;
            var fitter = background.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = 5120f / 2880f;
            view.grindBackground = background;

            // 轨道区：按外轨直径摆，挂在底图底下用锚点比例定位（见 TrackCenterUv），
            // 底图裁切放大时两条轨道跟着放大，始终压在画出来的细线上
            var area = Rect(background.transform, "GrindArea",
                new Vector2(TrackCenterUv.x - TrackRadiusUvX, TrackCenterUv.y - TrackRadiusUvY),
                new Vector2(TrackCenterUv.x + TrackRadiusUvX, TrackCenterUv.y + TrackRadiusUvY),
                Vector2.zero, Vector2.zero);
            view.grindArea = area;

            view.grindContentRoot = Rect(area, "GrindContent", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                Vector2.zero, Vector2.zero);

            // 障碍珠模板：运行时隐藏并克隆成一段弧（§16.2 动态列表项）。
            // 珠子多大以这个节点为准——GrindGame 既不改克隆件尺寸，排布间距也是照它的宽度算的
            var bead = ArtImageByContent(view.grindContentRoot, "ObstacleBeadTemplate",
                Art(GrindArtDir, "红色珠子"), new Vector2(.5f, .5f), Vector2.zero,
                BeadContentPx, BeadDisplaySize);
            bead.raycastTarget = false;
            view.obstacleBeadTemplate = bead;

            // 指针（把手）建在模板之后：兄弟顺序即绘制顺序，指针永远压在珠子上面。
            // 尺寸破例不按 ArtScale，理由见 PointerDisplaySize
            var pointerImage = ArtImageByContent(area, "Pointer", Art(GrindArtDir, "把手"),
                new Vector2(.5f, .5f), Vector2.zero, PointerContentPx, PointerDisplaySize);
            pointerImage.raycastTarget = false;
            view.pointer = (RectTransform)pointerImage.transform;
            view.pointerImage = pointerImage;
        }

        /// <summary>
        /// 冲泡环节（2026-08-20 按 2.0 设计图接美术）：整屏底图 + 一个看不见的判定区。
        /// 杯子、咖啡液、碟子、杯把都画在底图里，这一层不再摆任何占位图形。
        /// </summary>
        private static void BuildPour(RectTransform parent, CoffeeMinigameView view)
        {
            var pourRoot = Rect(parent, "PourRoot", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            view.pourRoot = pourRoot;

            // 名字里带 background：HouseDayLightTint 靠它认出「页面底图」，让画面随时钟慢慢变天色。
            // EnvelopeParent = 填满裁切——非 16:9 时宁可切掉四周的水彩边缘，也不留黑边（2026-08-20 拍板）
            var background = ArtImage(pourRoot, "Background", Art(PourArtDir, "image 341"),
                new Vector2(.5f, .5f), Vector2.zero);
            background.raycastTarget = true;
            var fitter = background.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = 5120f / 2880f;
            view.pourBackground = background;

            // 判定区：杯是圆的，判定取内切圆（PourGame.InsideCup）。它**不挂图**——
            // 杯子已经画在底图里了，这一层只是个看不见的方框。
            // 挂在底图底下、用锚点比例定位，这样底图被裁切放大时判定圆跟着一起放大（见 CupCenterUv）。
            // 1920×1080 下换算出来就是：圆心在屏幕中心右 12、上 25，半径 210
            var cup = Rect(background.transform, "CupArea",
                new Vector2(CupCenterUv.x - CupRadiusUvX, CupCenterUv.y - CupRadiusUvY),
                new Vector2(CupCenterUv.x + CupRadiusUvX, CupCenterUv.y + CupRadiusUvY),
                Vector2.zero, Vector2.zero);
            view.cupArea = cup;

            AddWaterImage(view);
        }

        /// <summary>
        /// 环节过场幕布（2026-08-20）：整屏纸色 + 居中的环节名，默认隐藏，换环节时放一次。
        /// 建在 ESC 之后、暂停弹窗之前——过场要连 HUD 一起盖住才干净，但暂停弹窗得压在它上面。
        /// 没用素材：纸色是纯色，环节名是文字，一张全屏贴图不值这份显存（同暂停遮罩的取舍）。
        /// </summary>
        private static void BuildTransition(RectTransform parent, CoffeeMinigameView view)
        {
            var root = Rect(parent, "TransitionRoot", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            view.transitionRoot = root;

            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            view.transitionGroup = group;

            var sheet = ImageOn(root, Paper);
            sheet.raycastTarget = true; // 幕布期间点不到底下的东西

            view.transitionLabel = Label(root, "Title", "② 冲咖啡", 64, InkBlue,
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(900, 110),
                TextAnchor.MiddleCenter, FontStyle.Bold);

            root.gameObject.SetActive(false); // Prefab 里就是关的，运行时由 CoffeeMinigame 开
        }

        /// <summary>
        /// 局内暂停弹窗（2026-08-20，素材 PC ui 2.0/局内暂停弹窗）：整屏遮罩 + 纸面板 + 两颗按钮。
        /// **页面级**——磨豆与冲泡共用这一个，不随环节切换重建。
        /// 建完即隐藏，打开由 CoffeeMinigame 负责（ESC 键经壳的 ConsumeEscape 传下来，或点左下角那颗）。
        /// 【放弃】从页面上挪进了这里：设计图上页面只剩左下角一颗 ESC。
        /// </summary>
        private static void BuildPause(RectTransform parent, CoffeeMinigameView view)
        {
            var pauseRoot = Rect(parent, "PauseRoot", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            view.pauseRoot = pauseRoot;

            var scrim = ImageOn(
                Rect(pauseRoot, "Scrim", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero), Scrim);
            scrim.raycastTarget = true; // 吃掉落在面板外的点击：暂停期间底下的页面不该还能点

            var board = ArtImage(pauseRoot, "Board", Art(PauseArtDir, "游戏菜单弹窗底板-1 1"),
                new Vector2(.5f, .5f), Vector2.zero);
            board.raycastTarget = true; // 面板内的空白同样不穿透

            // 面板内容区高 592（1664 × ArtScale 再去掉素材四周的投影留白），
            // 标题 + 两颗按钮整体大致居中：标题顶 205、放弃底 -170
            Label(board.transform, "Title", "已暂停", 44, InkBlue,
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 170), new Vector2(600, 70),
                TextAnchor.MiddleCenter, FontStyle.Bold);

            view.resumeButton = PauseButton(board.transform, "ResumeButton", "继续", new Vector2(0, 0));
            view.abortButton = PauseButton(board.transform, "AbortButton", "放弃", new Vector2(0, -130));

            pauseRoot.gameObject.SetActive(false); // Prefab 里就是关的，运行时由 CoffeeMinigame 开
        }

        /// <summary>暂停弹窗上的一颗按钮：整图两态（默认 / 悬停）走 SpriteSwap，文案另挂 Text。</summary>
        private static Button PauseButton(Transform parent, string name, string caption, Vector2 position)
        {
            var normal = Art(PauseArtDir, "默认");
            var hover = Art(PauseArtDir, "悬停");
            var image = ArtImage(parent, name, normal, new Vector2(.5f, .5f), position);
            Label(image.transform, "Caption", caption, 32, InkBlue,
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(360, 60),
                TextAnchor.MiddleCenter, FontStyle.Bold);
            return SpriteSwapButton(image, normal, hover);
        }

        /// <summary>
        /// 节点/引用粒度的「补缺失」：给已存在的 Prefab 补后加的水面节点与循环音剪辑，
        /// 不动其他手调内容。以后再加新节点，照这个模式往 patched 里追一条即可。
        /// </summary>
        private static bool PatchPrefabIfMissing(out string patched)
        {
            patched = string.Empty;
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var view = root.GetComponent<CoffeeMinigameView>();
                if (view == null) return false;

                var notes = new List<string>();
                if (view.cupArea != null && view.waterImage == null)
                {
                    AddWaterImage(view);
                    notes.Add("水面节点");
                }
                if (AssignAudioClips(view)) notes.Add("音效剪辑");

                // 2.0 版式（整屏底图 / 左上底卡 / 左下 ESC / 暂停弹窗）是整页改版，补节点补不出来，
                // 只提示、不擅自重建——重建会覆盖手调，那是要人显式点菜单的操作
                if (view.pourBackground == null || view.grindBackground == null ||
                    view.obstacleBeadTemplate == null || view.escButton == null ||
                    view.pauseRoot == null || view.transitionRoot == null)
                    Debug.LogWarning("[制作咖啡] 这份 Prefab 还是改版前的老版式" +
                                     "（缺 两个环节的底图 / 红珠模板 / ESC 暂停 / 暂停弹窗 / 过场幕布）。" +
                                     "要换成 2.0 设计图的版式，请执行菜单 " +
                                     "MasterHouse → 小游戏 → 重建制作咖啡 Prefab（覆盖手调）");

                if (notes.Count == 0) return false;
                patched = string.Join("、", notes);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// 补本小游戏的三个音效剪辑（研磨 / 冲泡两路循环音 + 阶段通关的一次性音，2026-08-20）：
        /// **只补空的**，已手动换过的不动。
        /// 剪辑不进音效表——这些是本小游戏的专属表现，配在自己的 Prefab 上更就近（换音 = 换这里的引用）。
        /// </summary>
        private static bool AssignAudioClips(CoffeeMinigameView view)
        {
            var changed = false;
            changed |= Fill(ref view.grindLoopClip, GrindLoopClipPath, "研磨循环音");
            changed |= Fill(ref view.pourLoopClip, PourLoopClipPath, "冲泡循环音");
            changed |= Fill(ref view.stageClearClip, StageClearClipPath, "阶段通关音");
            return changed;
        }

        /// <summary>空引用才填，填不到只警告——留空的后果是「该处静音」，不该拦住整条补齐流程。</summary>
        private static bool Fill(ref AudioClip slot, string path, string label)
        {
            if (slot != null) return false;
            slot = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (slot != null) return true;
            Debug.LogWarning($"[制作咖啡] 未找到{label}：{path}（留空 = 该处静音）");
            return false;
        }

        /// <summary>
        /// 杯内液面：铺满 cupArea，材质由 CoffeeMinigame 运行时创建（Prefab 不挂材质资产）。
        /// 换美术底图后它只画搅动波纹与边缘晃动——满杯的咖啡已经在底图里了（2026-08-20）。
        /// </summary>
        private static void AddWaterImage(CoffeeMinigameView view)
        {
            var water = Rect(view.cupArea, "Water", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            water.SetAsFirstSibling();
            var image = ImageOn(water, Color.white);
            image.raycastTarget = false;
            view.waterImage = image;
        }

        /// <summary>
        /// 底部（2026-08-20 按设计图）：一行提示文字，居中、无底板、直接压在底图上，
        /// 中心距屏底 173、字色 #5676A5。调参行只给测试场景看，默认隐藏。
        /// 【放弃】不在这儿了——它挪进了暂停弹窗。
        /// </summary>
        private static void BuildFooter(RectTransform parent, CoffeeMinigameView view)
        {
            view.messageLabel = Label(parent, "Message", string.Empty, 28, InkBlue,
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, MessageBottom), new Vector2(-600, 44),
                TextAnchor.MiddleCenter, FontStyle.Bold);

            // 摆在 ESC 条上方，免得两者叠在一起（ESC 占到屏底 82 的高度）
            view.tuningLabel = Label(parent, "Tuning", string.Empty, 20, InkBlueMuted,
                new Vector2(0, 0), new Vector2(0, 0), new Vector2(330, 112), new Vector2(600, 32),
                TextAnchor.MiddleLeft);
            // 调参信息只给测试场景看：默认隐藏，由 CoffeeLevelTestBootstrap 显式打开，正式局不显示
            view.tuningLabel.gameObject.SetActive(false);
        }

        // ══════════ 绘制原语（与 CircuitMinigamePrefabGenerator 同一套）══════════

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, .5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 min, Vector2 max,
            Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static Image ImageOn(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private static Text Label(Transform parent, string name, string value, int size, Color color,
            Vector2 min, Vector2 max, Vector2 position, Vector2 dimensions, TextAnchor alignment,
            FontStyle style = FontStyle.Normal)
        {
            var text = Rect(parent, name, min, max, position, dimensions).gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            text.text = value;
            return text;
        }

        // ══════════ 2.0 素材原语（2026-08-20）══════════

        /// <summary>
        /// 取一张 2.0 素材。缺图是 LogError 不是回退（§16.2）——
        /// 生成器只管报出来，Prefab 上会留一个没图的 Image，一眼能看见缺了哪块。
        /// </summary>
        private static Sprite Art(string dir, string name)
        {
            var path = dir + name + ".png";
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
                Debug.LogError("[制作咖啡] 缺素材：" + path +
                               "（若贴图刚导入，检查它的 Texture Type 是不是 Sprite (2D and UI)）");
            return sprite;
        }

        /// <summary>
        /// 摆一张整图素材：显示尺寸 = 素材原始像素 × <see cref="ArtScale"/>。
        /// 这些素材的不透明内容都在画布里居中（已逐张验过），所以 rect 中心 = 视觉中心；
        /// 要按视觉边距摆，把「内容尺寸 × ArtScale」的一半加进 position 即可，不必另做 pivot 修正。
        /// </summary>
        private static Image ArtImage(Transform parent, string name, Sprite sprite, Vector2 anchor,
            Vector2 position)
        {
            var size = sprite != null
                ? new Vector2(sprite.rect.width, sprite.rect.height) * ArtScale
                : new Vector2(160f, 60f); // 缺图已经 LogError 过，这里给个看得见的占位尺寸
            var rect = Rect(parent, name, anchor, anchor, position, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = Color.white;
            return image;
        }

        /// <summary>
        /// 按「素材里那块不透明内容要显示成多大」摆图：画布连同四周留白一起等比缩放。
        /// 给**破例不按 ArtScale** 的件用（目前只有磨豆的把手与障碍珠）。
        /// </summary>
        private static Image ArtImageByContent(Transform parent, string name, Sprite sprite, Vector2 anchor,
            Vector2 position, float contentWidthPx, float targetWidthPx)
        {
            var image = ArtImage(parent, name, sprite, anchor, position);
            if (sprite == null || contentWidthPx <= 0f) return image;
            var scale = targetWidthPx / contentWidthPx;
            ((RectTransform)image.transform).sizeDelta =
                new Vector2(sprite.rect.width, sprite.rect.height) * scale;
            return image;
        }

        /// <summary>把一张整图变成两态按钮（默认 / 悬停），与 OutGameUI 的键位条同一套做法。</summary>
        private static Button SpriteSwapButton(Image image, Sprite normal, Sprite hover)
        {
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = hover,
                pressedSprite = hover,
                selectedSprite = normal,
            };
            return button;
        }

        private static void EnsureFolder(string parent, string leaf)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + leaf))
                AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
#endif

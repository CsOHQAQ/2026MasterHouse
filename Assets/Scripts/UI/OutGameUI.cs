using System;
using System.Collections.Generic;
using System.Globalization;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using F = MasterHouse.OutGameUIFactory;

namespace MasterHouse
{
    /// <summary>
    /// web-demo 局外界面的 Unity 运行时复刻。完整静态页面由 Prefab 管理布局，
    /// 本控制器只绑定数据、事件和 DOTween；动态 House 内容保留运行时填充。
    /// </summary>
    public sealed class OutGameUI : MonoBehaviour
    {
        private enum View { Title, Opening, Hub }
        private enum SystemPanel { None, Tasks, Device, Journal, Contacts, Archive, Calendar, Inventory, Settings, Profile, Market }

        private const string SavePrefix = "MasterHouse.OutGame.Save.";
        private const float PanelWidth = 1280f;

        private Canvas canvas;
        private RectTransform canvasRect;
        private RectTransform viewRoot;
        private RectTransform sceneRoot;
        private RectTransform modalRoot;
        private RectTransform toastRoot;
        private CanvasGroup toastGroup;
        private Tween toastTween;
        private RawImage sceneArt;
        private Text clockLabel;
        private Text creditHudLabel;
        private Text economyChipLabel;
        private Button[] titleMenuButtons;
        private int titleMenuIndex;
        private OutGamePaperView activePaperView;
        private OutGameHubView activeHubView;
        private OutGameSystemPanelView activeSystemPanel;
        private static Texture2D titleHorizontalVignette;
        private static Texture2D titleVerticalVignette;
        private static Texture2D titleMenuGradient;
        private static Texture2D titleRuleGradient;
        private static Texture2D titleHoverGradient;

        private View view;
        private SystemPanel openedPanel;
        private int activeSlot = 1;
        private int roomIndex;
        private int guestIndex;
        private int selectedDevice;
        private int selectedArchive;
        private int fogRadius = 5;
        private int bgm = 64;
        private int sfx = 78;
        private string windowMode = "无边框";
        private string placedFurniture = "whale";
        private bool archiveWorld;
        private bool journalAchievements;
        private bool galleryAchievements;
        private bool autoDialogue;
        private bool showInteractionHints = true;
        private bool cameraShake = true;
        private bool dialogueOpen;
        private bool roomTransitioning;
        private bool furnitureModeOpen;
        private bool hubImmersive;
        private bool scenePanning;
        private Vector3 lastPanPosition;
        private Text immersiveLabel;
        private readonly System.Collections.Generic.List<(RectTransform rect, Rect viewport)> furnitureHotspots =
            new System.Collections.Generic.List<(RectTransform, Rect)>();
        private OutGameVisitorStage visitorStage;
        private Text hudPhaseLabel;
        private Text hudPhaseRangeLabel;
        private int hudPhaseShown = -1;
        private float autoSaveTimer;

        /// <summary>
        /// 过渡桥接：冻结的旧 UI 读写新 HouseClock 模块（§16.4，GameManager 全局 tick 驱动，由 OutGameBootstrap 保证存在）；
        /// 本属性随 OutGameUI 退役删除（3.9）。
        /// </summary>
        private static HouseClockManager Clock => GameManager.Instance.HouseClockManager;

        /// <summary>过渡桥接：冻结的旧 UI 读写新 Economy 模块（§16.3）；本属性随 OutGameUI 退役删除（3.9）。</summary>
        private static EconomyManager Economy => GameManager.Instance.EconomyManager;

        /// <summary>过渡桥接：旧 UI 读内容表（§16.6）；随 OutGameUI 退役删除（3.9）。</summary>
        private static VisitorTable Visitors => GameManager.Instance.VisitorTable;
        private static CodexTable Codex => GameManager.Instance.CodexTable;

        /// <summary>过渡桥接：旧 UI 读写访客业务（§16.3 Visitor 模块）；随 OutGameUI 退役删除（3.9）。</summary>
        private static VisitorManager Visitor => GameManager.Instance.VisitorManager;

        // 档案面板的分类缓存（内容表运行时只读，首次访问填充一次）
        private static List<CodexEntryDef> furnitureArchives;
        private static List<CodexEntryDef> worldArchives;

        private static List<CodexEntryDef> FurnitureArchives
        {
            get
            {
                if (furnitureArchives == null)
                {
                    furnitureArchives = new List<CodexEntryDef>();
                    Codex.GetArchives(ECodexArchiveCategory.NarrativeFurniture, furnitureArchives);
                }
                return furnitureArchives;
            }
        }

        private static List<CodexEntryDef> WorldArchives
        {
            get
            {
                if (worldArchives == null)
                {
                    worldArchives = new List<CodexEntryDef>();
                    Codex.GetArchives(ECodexArchiveCategory.World, worldArchives);
                }
                return worldArchives;
            }
        }

        /// <summary>
        /// 局外 UI 不依赖当前打开的场景。即使从 Untitled、备份场景或其他玩法场景进入 Play，
        /// 也会在场景加载前创建入口；SampleScene 中的 Bootstrap 仅作为兼容兜底。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoBuild()
        {
            //Build();
        }

        public static OutGameUI Build()
        {
            var existing = FindObjectOfType<OutGameUI>();
            if (existing != null) return existing;

            var go = new GameObject("OutGameUI", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(OutGameUI));
            DontDestroyOnLoad(go);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;
            Debug.Log("[OutGameUI] 局外界面入口已创建。", go);
            return go.GetComponent<OutGameUI>();
        }

        private void Awake()
        {
            canvas = GetComponent<Canvas>();
            canvasRect = (RectTransform)transform;
            Economy.Changed += UpdateEconomyHud;
            HouseGmConsole.FullResetRequested += OnGmFullReset;
        }

        private void OnDestroy()
        {
            // 应用退出时各常驻对象的销毁顺序不确定，GameManager 可能先没
            if (GameManager.Instance != null) Economy.Changed -= UpdateEconomyHud;
            HouseGmConsole.FullResetRequested -= OnGmFullReset;
        }

        /// <summary>直接关游戏/退出 Play 也不丢时钟与访客进度。</summary>
        private void OnApplicationQuit()
        {
            if (view == View.Hub) SaveCurrent(true);
        }

        /// <summary>GM「恢复初始态」：访客服务状态归零，背景重烘焙为默认布局，并把重置结果写入当前槽位。</summary>
        private void OnGmFullReset()
        {
            Visitor.ResetNew();
            guestIndex = 0;
            Clock.ResetNew();
            FurnitureSceneComposer.RequestBake(_ => { ApplySceneArt(); BuildFurnitureHotspots(); });
            UpdateEconomyHud();
            if (view == View.Hub && !furnitureModeOpen)
            {
                RebuildGuestChrome();
                BuildVisitorStage();
                AutoSave();
                ShowToast("GM · 已恢复所有状态到初始态");
            }
        }

        private void Start()
        {
            // AutoBuild 在场景加载前运行，EventSystem 必须等场景加载完成后再兜底创建，
            // 否则会和场景自带的 EventSystem 重复。
            if (EventSystem.current == null)
            {
                var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                DontDestroyOnLoad(eventSystem);
            }
            var palette = GameObject.Find("PaletteCanvas");
            if (palette != null) palette.SetActive(false);
            ShowTitle();
        }

        private void Update()
        {
            // 时钟推进已并入 GameManager 全局固定 tick（§16.4），这里只按页面状态开合闸门：
            // 时间只在 Hub 内流动（标题/开门过场暂停）；家具模式期间访客仍在走动，时间同样继续
            Clock.SetRunning(view == View.Hub);

            // 家具模式接管输入与画面，局外 UI 挂起等待回调恢复。
            if (furnitureModeOpen) return;

            if (clockLabel != null)
                clockLabel.text = Clock.Data.TimeText;
            RefreshHudPhase();

            // 时钟是持续流动的状态，只靠事件节点写档会丢挂机进度：Hub 内每 60 秒（=1 游戏小时）静默补一次档
            if (view == View.Hub)
            {
                autoSaveTimer += Time.unscaledDeltaTime;
                if (autoSaveTimer >= 60f)
                {
                    autoSaveTimer = 0f;
                    SaveCurrent(true);
                }
            }

            // 收起界面（观景模式）：只响应 ESC 展开与背景平移缩放，屏蔽其余快捷键
            if (view == View.Hub && hubImmersive)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) { SetHubImmersive(false); return; }
                HandleSceneBrowse();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (dialogueOpen) CloseDialogue();
                else if (openedPanel != SystemPanel.None) ClosePanel();
                else if (view == View.Hub) ShowTitle();
                else if (view == View.Title && modalRoot != null) ShowTitle();
            }

            if (view == View.Title && modalRoot == null && titleMenuButtons != null)
            {
                if (Input.GetKeyDown(KeyCode.UpArrow)) MoveTitleSelection(-1);
                if (Input.GetKeyDown(KeyCode.DownArrow)) MoveTitleSelection(1);
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                {
                    var selected = titleMenuButtons[titleMenuIndex];
                    if (selected != null && selected.interactable) selected.onClick.Invoke();
                }
            }

            if (view != View.Hub || openedPanel != SystemPanel.None || dialogueOpen) return;
            if (Input.GetKeyDown(KeyCode.LeftArrow)) SelectRoom((roomIndex + 3) % 4);
            if (Input.GetKeyDown(KeyCode.RightArrow)) SelectRoom((roomIndex + 1) % 4);
            if (Input.GetKeyDown(KeyCode.I)) OpenPanel(SystemPanel.Inventory);
        }

        private RectTransform NewView(string name, string prefabPath = null)
        {
            // 先停止所有页面级 Tween，再销毁层级；反过来会让 DOTween 在本帧末尾继续写入失效对象。
            DOTween.Kill(this);
            if (viewRoot != null)
            {
                KillViewTweens(viewRoot);
                viewRoot.gameObject.SetActive(false);
                Destroy(viewRoot.gameObject);
            }
            var prefab = string.IsNullOrEmpty(prefabPath) ? null : Resources.Load<GameObject>(prefabPath);
            if (prefab != null)
            {
                var instance = Instantiate(prefab, transform, false);
                instance.name = name;
                viewRoot = instance.transform as RectTransform;
                if (viewRoot != null)
                {
                    viewRoot.anchorMin = Vector2.zero;
                    viewRoot.anchorMax = Vector2.one;
                    viewRoot.offsetMin = Vector2.zero;
                    viewRoot.offsetMax = Vector2.zero;
                    viewRoot.localScale = Vector3.one;
                }
            }
            else
            {
                viewRoot = F.Stretch(transform, name);
                if (!string.IsNullOrEmpty(prefabPath))
                    Debug.LogWarning("[OutGameUI] Prefab 缺失，暂时回退代码布局：" + prefabPath);
            }
            sceneRoot = null;
            modalRoot = null;
            toastRoot = null;
            clockLabel = null;
            hudPhaseLabel = null;
            hudPhaseRangeLabel = null;
            creditHudLabel = null;
            economyChipLabel = null;
            hubImmersive = false;
            scenePanning = false;
            immersiveLabel = null;
            titleMenuButtons = null;
            activePaperView = null;
            activeHubView = null;
            activeSystemPanel = null;
            openedPanel = SystemPanel.None;
            dialogueOpen = false;
            return viewRoot;
        }

        private static void KillViewTweens(Transform root)
        {
            if (root == null) return;
            foreach (var target in root.GetComponentsInChildren<Transform>(true))
                DOTween.Kill(target);
            foreach (var target in root.GetComponentsInChildren<CanvasGroup>(true))
                DOTween.Kill(target);
            foreach (var target in root.GetComponentsInChildren<Graphic>(true))
                DOTween.Kill(target);
        }

        #region 标题与存档

        private void ShowTitle()
        {
            // 从 Hub 回标题前静默写档，保证游戏时钟与访客到访进度不丢
            if (view == View.Hub) SaveCurrent(true);
            view = View.Title;
            var root = NewView("TitleView", OutGamePrefabResourcePaths.Title);
            var prefabView = root.GetComponent<OutGameTitleView>();
            if (prefabView != null)
            {
                BindTitlePrefab(prefabView);
                return;
            }
            var cover = F.StretchTexture(root, "Cover", "OutGameUI/og-meros", new Color(1, 1, 1, 0));
            cover.raycastTarget = false;
            ConfigureCover(cover);
            cover.rectTransform.localScale = Vector3.one * 1.035f;
            cover.DOFade(1, 1.1f).SetTarget(this).SetEase(Ease.OutCubic).SetUpdate(true);
            cover.rectTransform.DOScale(Vector3.one, 1.1f).SetTarget(this).SetEase(Ease.OutCubic).SetUpdate(true);
            BuildTitleVignette(root);

            var menu = F.Stretch(root, "MainMenu");
            BuildTitleMenuGradient(menu);
            TitleRawImage(menu, "TopRule", titleRuleGradient, new Vector2(.264f, 1),
                new Vector2(0, -515), new Vector2(344, 1), Color.white);
            TitleRawImage(menu, "BottomRule", titleRuleGradient, new Vector2(.264f, 1),
                new Vector2(0, -1044), new Vector2(344, 1), new Color(1, 1, 1, .72f));
            var stateRow = F.Rect(menu, "SaveStateRow", new Vector2(.264f, 1), new Vector2(.264f, 1),
                new Vector2(0, -548), new Vector2(500, 28));
            var stateLayout = stateRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            stateLayout.childAlignment = TextAnchor.MiddleCenter;
            stateLayout.spacing = 12;
            stateLayout.childControlWidth = true;
            stateLayout.childControlHeight = false;
            stateLayout.childForceExpandWidth = false;
            stateLayout.childForceExpandHeight = false;
            var stateDot = F.Panel(stateRow, "Dot", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                Vector2.zero, new Vector2(6, 6), F.Hex("DD725A"));
            stateDot.raycastTarget = false;
            var dotLayout = stateDot.gameObject.AddComponent<LayoutElement>();
            dotLayout.minWidth = dotLayout.preferredWidth = 6;
            dotLayout.minHeight = dotLayout.preferredHeight = 6;
            var state = F.Label(stateRow, "Text",
                HasAnySave() ? "一段旅店记忆正在等待你" : "等待第一位住客",
                12, F.Hex("A99A91"), TextAnchor.MiddleCenter, FontStyle.Bold);
            state.gameObject.AddComponent<OutGameLetterSpacing>().spacing = .65f;

            var items = new[]
            {
                new MenuItem("继续游戏", HasAnySave() ? "CONTINUE" : "暂无存档", ContinueLatest, HasAnySave()),
                new MenuItem("新游戏", "NEW STORY", () => ShowSavePage(false), true),
                new MenuItem("读取存档", "LOAD GAME", () => ShowSavePage(true), true),
                new MenuItem("画廊", "LOG & ACHIEVEMENT", ShowGallery, true),
                new MenuItem("设置", "OPTIONS", ShowTitleSettings, true),
                new MenuItem("退出游戏", "QUIT", ShowExit, true),
            };

            titleMenuButtons = new Button[items.Length];
            for (var i = 0; i < items.Length; i++)
            {
                var item = items[i];
                var y = -584 - i * 76;
                var button = F.Button(menu, "Menu_" + item.cn, "", item.action,
                    new Vector2(.264f, 1), new Vector2(.264f, 1), new Vector2(0, y),
                    new Vector2(520, 70), Color.clear, Color.clear, 1, TextAnchor.MiddleCenter);
                button.interactable = item.enabled;
                titleMenuButtons[i] = button;
                var feedback = button.GetComponent<OutGameTweenButton>();
                if (feedback != null)
                {
                    feedback.hoverScale = 1.055f;
                    var hover = TitleRawImage(button.transform, "Hover", titleHoverGradient,
                        new Vector2(.5f, .5f), Vector2.zero, new Vector2(430, 58), new Color(1, 1, 1, 0));
                    hover.transform.SetAsFirstSibling();
                    feedback.hoverGraphic = hover;
                }
                var mainColor = i == 1 && item.enabled ? F.Hex("F0A080") : F.Hex("DBC9BD");
                var main = F.Label(button.transform, "Main", item.cn, 23, mainColor,
                    new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 8),
                    new Vector2(500, 34), TextAnchor.MiddleCenter, FontStyle.Bold);
                main.gameObject.AddComponent<OutGameLetterSpacing>().spacing = 3.2f;
                var subtitle = F.Label(button.transform, "Subtitle", item.en, 8, F.Hex("81736E"),
                    new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, -17),
                    new Vector2(500, 15), TextAnchor.MiddleCenter, FontStyle.Bold);
                subtitle.gameObject.AddComponent<OutGameLetterSpacing>().spacing = 1.5f;
                var group = F.Group(button.gameObject, 0);
                var targetAlpha = item.enabled ? 1f : .34f;
                DOTween.Sequence().SetTarget(this).SetUpdate(true)
                    .AppendInterval(.08f + i * .055f)
                    .Append(group.DOFade(targetAlpha, .42f).SetEase(Ease.OutCubic));
            }

            var hints = F.Label(menu, "Hints", "↑ ↓ 选择     ENTER 确认", 8, F.Hex("756B67"),
                new Vector2(.264f, 1), new Vector2(.264f, 1), new Vector2(0, -1063),
                new Vector2(500, 18), TextAnchor.MiddleCenter, FontStyle.Bold);
            hints.gameObject.AddComponent<OutGameLetterSpacing>().spacing = .8f;

            // 默认不选中任何菜单项：橙色 hover 渐变只在鼠标悬停或键盘导航后出现
            titleMenuIndex = HasAnySave() ? 0 : 1;
        }

        private void BindTitlePrefab(OutGameTitleView prefabView)
        {
            EnsureTitleTextures();
            if (prefabView.cover != null)
            {
                if (prefabView.cover.texture == null) prefabView.cover.texture = Resources.Load<Texture2D>("OutGameUI/og-meros");
                ConfigureCover(prefabView.cover);
                prefabView.cover.color = new Color(1, 1, 1, 0);
                prefabView.cover.rectTransform.localScale = Vector3.one * 1.035f;
                prefabView.cover.DOFade(1, 1.1f).SetTarget(this).SetEase(Ease.OutCubic).SetUpdate(true);
                prefabView.cover.rectTransform.DOScale(Vector3.one, 1.1f).SetTarget(this).SetEase(Ease.OutCubic).SetUpdate(true);
            }
            if (prefabView.horizontalVignette != null) prefabView.horizontalVignette.texture = titleHorizontalVignette;
            if (prefabView.verticalVignette != null) prefabView.verticalVignette.texture = titleVerticalVignette;
            if (prefabView.menuGradient != null) prefabView.menuGradient.texture = titleMenuGradient;
            if (prefabView.topRule != null) prefabView.topRule.texture = titleRuleGradient;
            if (prefabView.bottomRule != null) prefabView.bottomRule.texture = titleRuleGradient;

            var hasSave = HasAnySave();
            if (prefabView.saveState != null)
                prefabView.saveState.text = hasSave ? "一段旅店记忆正在等待你" : "等待第一位住客";
            var items = new[]
            {
                new MenuItem("继续游戏", hasSave ? "CONTINUE" : "暂无存档", ContinueLatest, hasSave),
                new MenuItem("新游戏", "NEW STORY", () => ShowSavePage(false), true),
                new MenuItem("读取存档", "LOAD GAME", () => ShowSavePage(true), true),
                new MenuItem("画廊", "LOG & ACHIEVEMENT", ShowGallery, true),
                new MenuItem("设置", "OPTIONS", ShowTitleSettings, true),
                new MenuItem("退出游戏", "QUIT", ShowExit, true),
            };
            titleMenuButtons = prefabView.menuButtons;
            for (var i = 0; i < items.Length && i < titleMenuButtons.Length; i++)
            {
                var item = items[i];
                var button = titleMenuButtons[i];
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => item.action());
                button.interactable = item.enabled;
                if (i < prefabView.menuMainLabels.Length && prefabView.menuMainLabels[i] != null)
                {
                    prefabView.menuMainLabels[i].text = item.cn;
                    prefabView.menuMainLabels[i].color = i == 1 ? F.Hex("F0A080") : F.Hex("DBC9BD");
                    EnsureLetterSpacing(prefabView.menuMainLabels[i], 3.2f);
                }
                if (i < prefabView.menuSubtitles.Length && prefabView.menuSubtitles[i] != null)
                {
                    prefabView.menuSubtitles[i].text = item.en;
                    EnsureLetterSpacing(prefabView.menuSubtitles[i], 1.5f);
                }
                if (i < prefabView.menuHoverImages.Length && prefabView.menuHoverImages[i] != null)
                {
                    prefabView.menuHoverImages[i].texture = titleHoverGradient;
                    // Prefab 中的 hover 图可能保存为可见状态，绑定时强制归零，默认不显示
                    var hoverColor = prefabView.menuHoverImages[i].color;
                    prefabView.menuHoverImages[i].color = new Color(hoverColor.r, hoverColor.g, hoverColor.b, 0f);
                }
                var feedback = button.GetComponent<OutGameTweenButton>();
                if (feedback == null) feedback = button.gameObject.AddComponent<OutGameTweenButton>();
                feedback.hoverScale = 1.055f;
                if (i < prefabView.menuHoverImages.Length) feedback.hoverGraphic = prefabView.menuHoverImages[i];
                var group = F.Group(button.gameObject, 0);
                var targetAlpha = item.enabled ? 1f : .34f;
                DOTween.Sequence().SetTarget(this).SetUpdate(true)
                    .AppendInterval(.08f + i * .055f)
                    .Append(group.DOFade(targetAlpha, .42f).SetEase(Ease.OutCubic));
            }
            EnsureLetterSpacing(prefabView.saveState, .65f);
            EnsureLetterSpacing(prefabView.hints, .8f);
            ApplyFallbackFont(prefabView.transform);
            // 默认不选中任何菜单项：橙色 hover 渐变只在鼠标悬停或键盘导航后出现
            titleMenuIndex = hasSave ? 0 : 1;
        }

        private static void ApplyFallbackFont(Transform root)
        {
            var legacyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            foreach (var label in root.GetComponentsInChildren<Text>(true))
            {
                if (label.font == null || label.font == legacyFont || label.font.name == "Arial" ||
                    label.font.name == "LegacyRuntime")
                    label.font = F.Font;
            }
        }

        private static void EnsureLetterSpacing(Text label, float spacing)
        {
            if (label == null) return;
            var effect = label.GetComponent<OutGameLetterSpacing>();
            if (effect == null) effect = label.gameObject.AddComponent<OutGameLetterSpacing>();
            effect.spacing = spacing;
            label.SetVerticesDirty();
        }

        private void MoveTitleSelection(int direction)
        {
            if (titleMenuButtons == null || titleMenuButtons.Length == 0) return;
            for (var step = 0; step < titleMenuButtons.Length; step++)
            {
                titleMenuIndex = (titleMenuIndex + direction + titleMenuButtons.Length) % titleMenuButtons.Length;
                var candidate = titleMenuButtons[titleMenuIndex];
                if (candidate == null || !candidate.interactable) continue;
                candidate.Select();
                return;
            }
        }

        /// <summary>复刻网页 title-cover 的 object-fit:cover，避免非 16:9 Game View 拉伸背景。</summary>
        private static void ConfigureCover(RawImage image)
        {
            if (image == null || image.texture == null) return;
            var fitter = image.GetComponent<AspectRatioFitter>();
            if (fitter == null) fitter = image.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = (float)image.texture.width / image.texture.height;
        }

        private static void BuildTitleVignette(Transform parent)
        {
            EnsureTitleTextures();
            var horizontal = TitleRawImage(parent, "HorizontalVignette", titleHorizontalVignette,
                new Vector2(.5f, .5f), Vector2.zero, Vector2.zero, Color.white, true);
            horizontal.transform.SetAsLastSibling();
            var vertical = TitleRawImage(parent, "VerticalVignette", titleVerticalVignette,
                new Vector2(.5f, .5f), Vector2.zero, Vector2.zero, Color.white, true);
            vertical.transform.SetAsLastSibling();
        }

        private static void BuildTitleMenuGradient(Transform parent)
        {
            EnsureTitleTextures();
            var gradient = TitleRawImage(parent, "MenuGradient", titleMenuGradient,
                new Vector2(.264f, 1), new Vector2(0, -780), new Vector2(520, 568), Color.white);
            gradient.transform.SetAsFirstSibling();
        }

        private static RawImage TitleRawImage(Transform parent, string name, Texture texture,
            Vector2 anchor, Vector2 position, Vector2 size, Color color, bool stretch = false)
        {
            RectTransform rect;
            if (stretch)
                rect = F.Stretch(parent, name);
            else
                rect = F.Rect(parent, name, anchor, anchor, position, size);
            var image = rect.gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static void EnsureTitleTextures()
        {
            if (titleHorizontalVignette != null && titleVerticalVignette != null &&
                titleMenuGradient != null && titleRuleGradient != null && titleHoverGradient != null) return;

            titleHorizontalVignette = NewTitleTexture("TitleHorizontalVignette", 512, 2);
            for (var x = 0; x < titleHorizontalVignette.width; x++)
            {
                var t = x / (titleHorizontalVignette.width - 1f);
                var alpha = t <= .43f
                    ? Mathf.Lerp(.72f, .18f, t / .43f)
                    : t <= .67f ? Mathf.Lerp(.18f, 0, (t - .43f) / .24f) : 0;
                SetColumn(titleHorizontalVignette, x, new Color(2f / 255, 5f / 255, 10f / 255, alpha));
            }
            titleHorizontalVignette.Apply(false, true);

            titleVerticalVignette = NewTitleTexture("TitleVerticalVignette", 2, 512);
            for (var y = 0; y < titleVerticalVignette.height; y++)
            {
                var t = y / (titleVerticalVignette.height - 1f);
                var alpha = t <= .37f
                    ? Mathf.Lerp(.74f, 0, t / .37f)
                    : Mathf.Lerp(0, .12f, (t - .37f) / .63f);
                SetRow(titleVerticalVignette, y, new Color(1f / 255, 3f / 255, 7f / 255, alpha));
            }
            titleVerticalVignette.Apply(false, true);

            titleMenuGradient = NewTitleTexture("TitleMenuGradient", 512, 2);
            for (var x = 0; x < titleMenuGradient.width; x++)
            {
                var t = x / (titleMenuGradient.width - 1f);
                float alpha;
                if (t <= .17f) alpha = Mathf.Lerp(0, .82f, t / .17f);
                else if (t <= .5f) alpha = Mathf.Lerp(.82f, .9f, (t - .17f) / .33f);
                else if (t <= .83f) alpha = Mathf.Lerp(.9f, .82f, (t - .5f) / .33f);
                else alpha = Mathf.Lerp(.82f, 0, (t - .83f) / .17f);
                SetColumn(titleMenuGradient, x, new Color(3f / 255, 6f / 255, 11f / 255, alpha));
            }
            titleMenuGradient.Apply(false, true);

            titleRuleGradient = NewTitleTexture("TitleRuleGradient", 256, 2);
            for (var x = 0; x < titleRuleGradient.width; x++)
            {
                var t = x / (titleRuleGradient.width - 1f);
                var alpha = Mathf.Clamp01(1 - Mathf.Abs(t - .5f) * 2) * .72f;
                SetColumn(titleRuleGradient, x, new Color(233f / 255, 137f / 255, 104f / 255, alpha));
            }
            titleRuleGradient.Apply(false, true);

            titleHoverGradient = NewTitleTexture("TitleHoverGradient", 256, 64);
            for (var y = 0; y < titleHoverGradient.height; y++)
            for (var x = 0; x < titleHoverGradient.width; x++)
            {
                var nx = (x / (titleHoverGradient.width - 1f) - .5f) * 2;
                var ny = (y / (titleHoverGradient.height - 1f) - .5f) * 2;
                var radius = Mathf.Sqrt(nx * nx + ny * ny);
                var alpha = .44f * Mathf.Clamp01(1 - radius / .68f);
                titleHoverGradient.SetPixel(x, y, new Color(150f / 255, 53f / 255, 52f / 255, alpha));
            }
            titleHoverGradient.Apply(false, true);
        }

        private static Texture2D NewTitleTexture(string name, int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            return texture;
        }

        private static void SetColumn(Texture2D texture, int x, Color color)
        {
            for (var y = 0; y < texture.height; y++) texture.SetPixel(x, y, color);
        }

        private static void SetRow(Texture2D texture, int y, Color color)
        {
            for (var x = 0; x < texture.width; x++) texture.SetPixel(x, y, color);
        }

        private void ShowSavePage(bool loadMode)
        {
            var savePage = OpenCompletePaperPage<OutGameSavePageView>("SavePageView", OutGamePrefabResourcePaths.SavePage);
            if (savePage != null)
            {
                savePage.eyebrow.text = loadMode ? "LOAD A MEMORY" : "START A NEW STORY";
                savePage.title.text = loadMode ? "读取存档" : "选择新游戏存档";
                savePage.description.text = loadMode
                    ? "选择一个已有存档，回到上一次离开旅店的位置。"
                    : "选择存档位后开始新的旅店故事。已有存档会在下一次保存时被覆盖。";
                for (var i = 0; i < savePage.slots.Length; i++)
                    BindSaveSlot(savePage.slots[i], i + 1, loadMode);
                return;
            }

            BuildPaperPage(loadMode ? "LOAD A MEMORY" : "START A NEW STORY",
                loadMode ? "读取存档" : "选择新游戏存档",
                loadMode ? "选择一个已有存档，回到上一次离开旅店的位置。" : "选择存档位后开始新的旅店故事。已有存档会在下一次保存时被覆盖。");

            var slotPrefab = Resources.Load<GameObject>(OutGamePrefabResourcePaths.SaveSlot);
            if (activePaperView != null && activePaperView.saveListRoot != null && slotPrefab != null)
            {
                activePaperView.saveListRoot.gameObject.SetActive(true);
                for (var i = 1; i <= 3; i++)
                {
                    var instance = Instantiate(slotPrefab, activePaperView.saveListRoot, false);
                    instance.name = "SaveSlot" + i;
                    var slotView = instance.GetComponent<OutGameSaveSlotView>();
                    BindSaveSlot(slotView, i, loadMode);
                }
                return;
            }

            for (var i = 1; i <= 3; i++)
            {
                var slot = i;
                var data = ReadSave(slot);
                var occupied = data != null;
                var info = occupied
                    ? $"WEEK 01 · {CountServed(data.served)}/4 委托 · {RoomName(data.room)}\n<size=16>{FormatSaveTime(data.savedAt)}</size>"
                    : loadMode ? "空存档\n<size=16>NO DATA</size>" : "空存档\n<size=16>从这里开始</size>";
                var button = F.Button(modalRoot, "SaveSlot" + slot,
                    $"<size=38>0{slot}</size>     <size=15>SAVE SLOT</size>\n          {info}                           {(loadMode ? "读取" : "选择")}",
                    () => EnterSlot(slot, loadMode), new Vector2(.5f, 1), new Vector2(.5f, 1),
                    new Vector2(0, -360 - (i - 1) * 135), new Vector2(1680, 112),
                    new Color(.95f, .9f, .82f, .55f), F.Hex("392A2D"), 24, TextAnchor.MiddleLeft);
                button.interactable = !loadMode || occupied;
                var icon = F.Panel(button.transform, "SlotMark", new Vector2(0, .5f), new Vector2(0, .5f),
                    new Vector2(55, 0), new Vector2(82, 82), occupied ? F.Wine : F.Hex("76505B"));
                icon.transform.SetAsFirstSibling();
            }
        }

        private void BindSaveSlot(OutGameSaveSlotView slotView, int slot, bool loadMode)
        {
            if (slotView == null) return;
            var data = ReadSave(slot);
            var occupied = data != null;
            slotView.slotNumber.text = "0" + slot;
            slotView.eyebrow.text = "SAVE SLOT";
            slotView.information.text = occupied
                ? $"WEEK 01 · {CountServed(data.served)}/4 委托 · {RoomName(data.room)}\n{FormatSaveTime(data.savedAt)}"
                : loadMode ? "空存档\nNO DATA" : "空存档\n从这里开始";
            slotView.actionLabel.text = loadMode ? occupied ? "读取" : "—" : occupied ? "选择 · 将覆盖" : "选择";
            slotView.mark.color = occupied ? F.Wine : F.Hex("76505B");
            slotView.button.interactable = !loadMode || occupied;
            slotView.button.onClick.RemoveAllListeners();
            slotView.button.onClick.AddListener(() => EnterSlot(slot, loadMode));
            ApplyFallbackFont(slotView.transform);
        }

        private T OpenCompletePaperPage<T>(string instanceName, string prefabPath) where T : OutGamePaperView
        {
            var root = NewView(instanceName, prefabPath);
            var page = root.GetComponent<T>();
            if (page == null) return null;
            activePaperView = page;
            page.backButton.onClick.RemoveAllListeners();
            page.backButton.onClick.AddListener(ShowTitle);
            modalRoot = page.contentRoot != null ? page.contentRoot : page.frame;
            ApplyFallbackFont(root);
            var target = page.frame.anchoredPosition;
            var group = F.Group(page.frame.gameObject, 0);
            page.frame.anchoredPosition = target + new Vector2(0, -30);
            group.DOFade(1, .28f).SetTarget(this).SetEase(Ease.OutQuad).SetUpdate(true);
            page.frame.DOAnchorPos(target, .42f).SetTarget(this).SetEase(Ease.OutCubic).SetUpdate(true);
            return page;
        }

        private void BuildPaperPage(string eyebrow, string title, string description)
        {
            var root = NewView("TitlePaperView", OutGamePrefabResourcePaths.Paper);
            activePaperView = root.GetComponent<OutGamePaperView>();
            if (activePaperView != null)
            {
                activePaperView.eyebrow.text = eyebrow;
                activePaperView.title.text = title;
                activePaperView.description.text = description;
                activePaperView.backButton.onClick.RemoveAllListeners();
                activePaperView.backButton.onClick.AddListener(ShowTitle);
                if (activePaperView.saveListRoot != null) activePaperView.saveListRoot.gameObject.SetActive(false);
                modalRoot = activePaperView.contentRoot != null ? activePaperView.contentRoot : activePaperView.frame;
                ApplyFallbackFont(root);
                var prefabGroup = F.Group(activePaperView.frame.gameObject, 0);
                activePaperView.frame.anchoredPosition = new Vector2(0, -30);
                prefabGroup.DOFade(1, .28f).SetTarget(this).SetEase(Ease.OutQuad).SetUpdate(true);
                activePaperView.frame.DOAnchorPosY(0, .42f).SetTarget(this).SetEase(Ease.OutCubic).SetUpdate(true);
                return;
            }
            F.StretchTexture(root, "Cover", "OutGameUI/og-meros", new Color(1, 1, 1, .2f));
            F.StretchPanel(root, "Paper", new Color(.84f, .79f, .7f, .93f));
            var frame = F.Panel(root, "PaperFrame", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                Vector2.zero, new Vector2(1870, 1030), new Color(1, .97f, .9f, .11f));
            modalRoot = (RectTransform)frame.transform;
            F.Outline(frame.gameObject, new Color(.45f, .28f, .3f, .22f), new Vector2(2, -2));
            F.Label(modalRoot, "Eyebrow", eyebrow, 17, F.Wine, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(165, -75), new Vector2(560, 30), TextAnchor.MiddleLeft, FontStyle.Bold);
            F.Label(modalRoot, "Title", title, 52, F.Hex("35282A"), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(390, -140), new Vector2(1020, 80), TextAnchor.MiddleLeft, FontStyle.Bold);
            F.Panel(modalRoot, "Rule", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -205),
                new Vector2(1680, 2), new Color(.3f, .18f, .2f, .23f));
            F.Label(modalRoot, "Description", description, 19, F.Hex("5B4948"),
                new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(-90, -260), new Vector2(1500, 60));
            F.Button(modalRoot, "Back", "← 返回主菜单", ShowTitle, new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-145, -70), new Vector2(190, 58), new Color(1, 1, 1, .15f), F.Hex("4A3738"), 18);
            var group = F.Group(frame.gameObject, 0);
            frame.rectTransform.anchoredPosition = new Vector2(0, -30);
            group.DOFade(1, .28f).SetTarget(this).SetEase(Ease.OutQuad).SetUpdate(true);
            frame.rectTransform.DOAnchorPosY(0, .42f).SetTarget(this).SetEase(Ease.OutCubic).SetUpdate(true);
        }

        private void ShowGallery()
        {
            var gallery = OpenCompletePaperPage<OutGameGalleryPageView>("GalleryPageView", OutGamePrefabResourcePaths.GalleryPage);
            if (gallery != null)
            {
                gallery.logTab.onClick.RemoveAllListeners();
                gallery.logTab.onClick.AddListener(() => { galleryAchievements = false; ShowGallery(); });
                gallery.achievementTab.onClick.RemoveAllListeners();
                gallery.achievementTab.onClick.AddListener(() => { galleryAchievements = true; ShowGallery(); });
                gallery.logRoot.gameObject.SetActive(!galleryAchievements);
                gallery.achievementRoot.gameObject.SetActive(galleryAchievements);
                SetPaperTabState(gallery.logTab, !galleryAchievements);
                SetPaperTabState(gallery.achievementTab, galleryAchievements);
                return;
            }
            BuildPaperPage("HOUSE MEMORY", "画廊", "回看旅店里已经发生的片段，以及尚未被揭开的秘密。");
            var log = F.Button(modalRoot, "LogTab", "游戏日志", () => { galleryAchievements = false; ShowGallery(); },
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(270, -320), new Vector2(220, 58),
                galleryAchievements ? new Color(1, 1, 1, .12f) : F.Wine, galleryAchievements ? F.Wine : F.White, 20);
            var achievement = F.Button(modalRoot, "AchievementTab", "成就系统", () => { galleryAchievements = true; ShowGallery(); },
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(510, -320), new Vector2(220, 58),
                galleryAchievements ? F.Wine : new Color(1, 1, 1, .12f), galleryAchievements ? F.White : F.Wine, 20);
            if (!galleryAchievements)
            {
                PaperArticle(modalRoot, new Vector2(460, -475), "WEEK 01 · 06/17", "窗户唱回来的那句话",
                    "赫墨说“今天糟透了”。琴弦回答：“但你还是走到了这里。”");
                PaperArticle(modalRoot, new Vector2(1250, -475), "WEEK 01 · 06/16", "风铃下的纸条",
                    "米娅没有说再见，只留下了一张画着胡萝卜的小纸条。");
            }
            else
            {
                var names = new[] { "初次相识", "夜的主人", "家的轮廓", "无人知晓" };
                var notes = new[] { "记录第一位访客", "在深夜完成服务", "解锁全部房间", "发现特殊访客的秘密" };
                for (var i = 0; i < names.Length; i++)
                {
                    var x = i % 2 == 0 ? 505 : 1255;
                    var y = i < 2 ? -450 : -650;
                    var done = i < 2;
                    F.Button(modalRoot, "Achievement" + i,
                        $"{(done ? "✓" : "0" + (i + 1))}     {names[i]}\n<size=17>          {notes[i]} · {(done ? "已完成" : "未解锁")}</size>",
                        null, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, y), new Vector2(650, 150),
                        done ? new Color(.45f, .18f, .25f, .18f) : new Color(1, 1, 1, .12f), F.Hex("3E3032"), 28, TextAnchor.MiddleLeft);
                }
            }
        }

        private void ShowTitleSettings()
        {
            var settings = OpenCompletePaperPage<OutGameSettingsPageView>("SettingsPageView", OutGamePrefabResourcePaths.SettingsPage);
            if (settings != null)
            {
                settings.dataSummary.text = "界面切换       沉浸式\n\n当前存档         Slot 0" + activeSlot;
                settings.saveButton.onClick.RemoveAllListeners();
                settings.saveButton.onClick.AddListener(SaveCurrent);
                settings.loadButton.onClick.RemoveAllListeners();
                settings.loadButton.onClick.AddListener(() => ShowSavePage(true));
                BindToggle(settings.autoDialogueToggle, autoDialogue, value => autoDialogue = value);
                BindToggle(settings.hintToggle, showInteractionHints, value => showInteractionHints = value);
                BindToggle(settings.cameraShakeToggle, cameraShake, value => cameraShake = value);
                return;
            }
            BuildPaperPage("OPTIONS", "设置", "调整显示、音量和界面偏好。所有设置会随当前存档保留。");
            var left = PaperSection(modalRoot, new Vector2(490, -505), new Vector2(720, 420), "INTERFACE & DATA", "界面与存档");
            F.Label(left, "Mode", "界面切换       沉浸式\n\n当前存档         Slot 0" + activeSlot, 22, F.Hex("514142"),
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, -40), new Vector2(610, 130));
            F.Button(left, "Save", "保存", SaveCurrent, new Vector2(0, 0), new Vector2(0, 0),
                new Vector2(190, 55), new Vector2(245, 62), F.Wine, F.White, 20);
            F.Button(left, "Load", "读取存档", () => ShowSavePage(true), new Vector2(1, 0), new Vector2(1, 0),
                new Vector2(-190, 55), new Vector2(245, 62), new Color(1, 1, 1, .08f), F.Wine, 20);

            var right = PaperSection(modalRoot, new Vector2(1270, -505), new Vector2(720, 420), "GAMEPLAY", "游戏性");
            F.Label(right, "Toggles", "□  对话自动播放\n\n■  显示交互提示\n\n■  镜头轻微晃动", 23, F.Hex("514142"),
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, -30), new Vector2(610, 220));
        }

        private void ShowExit()
        {
            var exit = OpenCompletePaperPage<OutGameExitPageView>("ExitPageView", OutGamePrefabResourcePaths.ExitPage);
            if (exit != null)
            {
                exit.confirmButton.onClick.RemoveAllListeners();
                exit.confirmButton.onClick.AddListener(QuitGame);
                return;
            }
            BuildPaperPage("LEAVE THE GUESTHOUSE?", "退出游戏", "网页 Demo 无法关闭浏览器；Unity 版本可安全退出运行模式，或返回主菜单继续体验。");
            F.Button(modalRoot, "ConfirmExit", "退出游戏", QuitGame,
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, -80),
                new Vector2(360, 84), F.Wine, F.White, 26);
        }

        private static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private static void SetPaperTabState(Button button, bool active)
        {
            if (button == null || button.targetGraphic == null) return;
            button.targetGraphic.color = active ? F.Wine : new Color(1, 1, 1, .12f);
            var label = button.GetComponentInChildren<Text>();
            if (label != null) label.color = active ? F.White : F.Wine;
        }

        private static void BindToggle(Toggle toggle, bool value, UnityEngine.Events.UnityAction<bool> action)
        {
            if (toggle == null) return;
            toggle.onValueChanged.RemoveAllListeners();
            toggle.isOn = value;
            toggle.onValueChanged.AddListener(action);
        }

        private void ContinueLatest()
        {
            for (var i = 1; i <= 3; i++)
            {
                if (ReadSave(i) == null) continue;
                EnterSlot(i, true);
                return;
            }
            ShowSavePage(false);
        }

        private void EnterSlot(int slot, bool loadExisting)
        {
            var data = ReadSave(slot);
            if (loadExisting && data == null)
            {
                ShowToast("这个存档位还没有数据");
                return;
            }
            activeSlot = slot;
            if (loadExisting) ApplySave(data);
            else ResetProgress();
            PlayOpening(loadExisting);
        }

        private void PlayOpening(bool loadExisting)
        {
            view = View.Opening;
            var root = NewView("OpeningView");
            F.StretchTexture(root, "HomeReveal", "OutGameUI/house-hub-v2");
            F.StretchPanel(root, "RevealVignette", new Color(.02f, .02f, .035f, .28f));
            var welcome = F.Panel(root, "Welcome", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                Vector2.zero, new Vector2(370, 126), new Color(.83f, .77f, .67f, .94f));
            F.Label(welcome.transform, "Text", "<size=15>THE DOOR IS OPEN</size>\n欢迎回家", 34, F.Hex("3F292E"), TextAnchor.MiddleCenter, FontStyle.Bold);
            var welcomeGroup = F.Group(welcome.gameObject, 0);

            var left = F.Rect(root, "DoorLeft", new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(480, 0), new Vector2(960, 1080));
            var leftArt = F.StretchTexture(left, "Cover", "OutGameUI/og-meros");
            leftArt.uvRect = new Rect(0, 0, .5f, 1);
            var right = F.Rect(root, "DoorRight", new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-480, 0), new Vector2(960, 1080));
            var rightArt = F.StretchTexture(right, "Cover", "OutGameUI/og-meros");
            rightArt.uvRect = new Rect(.5f, 0, .5f, 1);
            var light = F.Panel(root, "DoorLight", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero,
                new Vector2(8, 1080), new Color(1f, .77f, .48f, .95f));
            F.Shadow(light.gameObject, new Color(1f, .55f, .25f, .9f), new Vector2(18, 0));

            DOTween.Sequence().SetTarget(this).SetUpdate(true)
                .AppendInterval(.12f)
                .Append(left.DOAnchorPosX(-500, 1.35f).SetEase(Ease.InOutCubic))
                .Join(right.DOAnchorPosX(500, 1.35f).SetEase(Ease.InOutCubic))
                .Join(light.rectTransform.DOSizeDelta(new Vector2(500, 1080), 1.15f).SetEase(Ease.InQuad))
                .Join(light.DOFade(0, 1.15f).SetEase(Ease.InQuad))
                .Insert(.55f, welcomeGroup.DOFade(1, .45f))
                .AppendCallback(() => ShowHub(loadExisting ? $"Slot 0{activeSlot} 已读取 · 欢迎回来" : $"Slot 0{activeSlot} · 新的一周开始了"));
        }

        #endregion

        #region House HUD

        private void ShowHub(string notice = "欢迎回家。本周有 4 位访客。")
        {
            view = View.Hub;
            var root = NewView("HouseHubView", OutGamePrefabResourcePaths.Hub);
            activeHubView = root.GetComponent<OutGameHubView>();
            sceneRoot = activeHubView != null && activeHubView.sceneRoot != null
                ? activeHubView.sceneRoot
                : F.Stretch(root, "Scene");
            sceneArt = F.StretchTexture(sceneRoot, "SceneArt", Codex.rooms[roomIndex].artPath);
            sceneArt.raycastTarget = false; // 场景图不拦截指针，观景模式拖拽与家具热点都依赖穿透
            ApplySceneArt();
            var sceneWash = F.StretchPanel(sceneRoot, "SceneWash", new Color(.015f, .02f, .04f, .22f));
            sceneWash.raycastTarget = false;
            BuildFurnitureHotspots();
            BuildVisitorStage();
            var chrome = HubChromeRoot;
            if (HasPrefabHubComponents())
            {
                BindTopHud(activeHubView.topBar);
                BindTaskCard(activeHubView.taskCard);
                BindGuestRail(activeHubView.guestRail);
                BindRightDock(activeHubView.rightDock);
                BindRoomNavigation(activeHubView.roomNavigation);
                BindSceneOverlay(activeHubView.sceneOverlay);
            }
            else
            {
                BuildTopHud(chrome);
                BuildTaskCard(chrome);
                BuildGuestRail(chrome);
                BuildRightDock(chrome);
                BuildRoomNavigation(chrome);
                BuildSceneCaption(chrome);
            }
            BuildFurnitureEntry(chrome);
            BuildEconomyChip(chrome);
            BuildImmersiveToggle(chrome);
            if (activeHubView != null && activeHubView.footer != null)
                activeHubView.footer.text = "NEW LIFE, NEW HOME · UI/UX CONCEPT                                      ESC 返回 · ← → 切换房间 · I 仓库";
            else
                F.Label(chrome, "Footer", "NEW LIFE, NEW HOME · UI/UX CONCEPT                                      ESC 返回 · ← → 切换房间 · I 仓库",
                    12, new Color(1, 1, 1, .45f), new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 12), new Vector2(1800, 26), TextAnchor.MiddleCenter);
            ApplyFallbackFont(root);
            AnimateHubIn();
            ShowToast(notice);
        }

        private Transform HubChromeRoot => activeHubView != null && activeHubView.chromeRoot != null
            ? activeHubView.chromeRoot
            : viewRoot;

        private Transform HubOverlayRoot => activeHubView != null && activeHubView.modalRoot != null
            ? activeHubView.modalRoot
            : viewRoot;

        private bool HasPrefabHubComponents()
        {
            return activeHubView != null && activeHubView.topBar != null && activeHubView.taskCard != null &&
                   activeHubView.guestRail != null && activeHubView.rightDock != null &&
                   activeHubView.roomNavigation != null && activeHubView.sceneOverlay != null;
        }

        private void BindTopHud(OutGameHubTopBarView hud)
        {
            var phase = OutGameUIData.CurrentPhase;
            hud.weekDatePhase.text = $"<size=14>GAME TIME · 加速时间</size>\n<size=31>DAY {Clock.Data.Day:00}</size>    {HousePhaseText.Names[phase]}";
            hud.phaseRange.text = HousePhaseText.Ranges[phase];
            hud.clock.text = Clock.Data.TimeText;
            clockLabel = hud.clock;
            hudPhaseLabel = hud.weekDatePhase;
            hudPhaseRangeLabel = hud.phaseRange;
            hudPhaseShown = Clock.Data.Day * 10 + phase;
            creditHudLabel = hud.creditLabel;
            hud.creditLabel.text = $"<size=13>HOUSE CREDIT</size>\n◈ {Economy.Data.Currency:N0}     ＋";
            hud.welcomeLabel.text = "WELCOME HOME.\n本周将有 <color=#E22D76>" + RemainingGuests() + "</color> 位访客来访";
            BindButton(hud.timeButton, () => OpenPanel(SystemPanel.Calendar));
            BindButton(hud.creditButton, () => OpenPanel(SystemPanel.Market));
            BindButton(hud.brandButton, ShowTitle);
            BindButton(hud.optionsButton, () => OpenPanel(SystemPanel.Settings));
        }

        private void BindTaskCard(OutGameHubTaskCardView card)
        {
            var guest = Visitors.visitors[guestIndex];
            card.header.text = "CURRENT VISITOR TASK                         进行中";
            card.guestTitle.text = guest.displayName + " · " + guest.need;
            card.hint.text = guest.hint;
            card.progress.text = $"━━━━━━  {ProgressForGuest(guestIndex)}%     点击查看任务详情  →";
            BindButton(card.button, () => OpenPanel(SystemPanel.Tasks));
        }

        private void BindGuestRail(OutGameHubGuestRailView rail)
        {
            rail.title.text = "VISITOR EVENTS / 访客事件";
            rail.remaining.text = RemainingGuests().ToString("00");
            for (var i = 0; i < rail.cards.Length && i < Visitors.visitors.Count; i++)
            {
                var index = i;
                var guest = Visitors.visitors[i];
                var done = Visitor.Data.States[i].Served;
                var card = rail.cards[i];
                card.portrait.texture = Resources.Load<Texture2D>(guest.portraitPath);
                card.eventLabel.text = guest.special ? "SPECIAL EVENT" : "EVENT 0" + (i + 1);
                card.guestName.text = guest.displayName;
                card.status.text = done ? "事件已完成" : guest.special ? "特殊客人 · 可打断" : "一般客人 · 可接待";
                card.typeLabel.text = done ? "✓" : guest.special ? "特" : "普";
                card.background.color = done ? new Color(.03f, .03f, .045f, .55f) : new Color(.025f, .025f, .045f, .83f);
                var textColor = done ? new Color(1, 1, 1, .45f) : F.White;
                card.eventLabel.color = card.guestName.color = card.status.color = textColor;
                BindButton(card.button, () => SelectGuest(index));
            }
        }

        private void BindRightDock(OutGameHubRightDockView dock)
        {
            var icons = new[] { "器", "记", "录", "集" };
            var labels = new[] { "设备图鉴", "日记", "通讯录", "档案" };
            var panels = new[] { SystemPanel.Device, SystemPanel.Journal, SystemPanel.Contacts, SystemPanel.Archive };
            for (var i = 0; i < dock.entries.Length && i < labels.Length; i++)
            {
                var panel = panels[i];
                dock.entries[i].icon.text = icons[i];
                dock.entries[i].label.text = labels[i];
                BindButton(dock.entries[i].button, () => OpenPanel(panel));
            }
        }

        private void BindRoomNavigation(OutGameHubRoomNavigationView navigation)
        {
            for (var i = 0; i < navigation.rooms.Length && i < Codex.rooms.Count; i++)
            {
                var index = i;
                var room = Codex.rooms[i];
                var selected = roomIndex == i;
                var item = navigation.rooms[i];
                item.code.text = room.code;
                item.icon.text = RoomIcon(i);
                item.roomName.text = room.displayName;
                item.state.text = selected ? "CURRENT" : string.Empty;
                item.background.color = selected ? new Color(.45f, .08f, .3f, .77f) : new Color(1, 1, 1, .015f);
                var color = selected ? F.White : new Color(1, 1, 1, .72f);
                item.code.color = item.icon.color = item.roomName.color = color;
                BindButton(item.button, () => SelectRoom(index));
            }
            var locked = navigation.lockedRoom;
            locked.code.text = "LOCKED";
            locked.icon.text = "▣";
            locked.roomName.text = "地下仓库";
            locked.state.text = string.Empty;
            locked.background.color = Color.clear;
            locked.code.color = locked.icon.color = locked.roomName.color = new Color(1, 1, 1, .3f);
            BindButton(locked.button, () => ShowToast("仓库房间将在 House LV.04 解锁"));
        }

        private void BindSceneOverlay(OutGameHubSceneOverlayView overlay)
        {
            // Prefab 字段可能因手动编辑而缺失；绑定必须逐项判空，
            // 否则一次 NRE 会把 ShowHub 后续的运行时控件（数值条/家具摆放/收起按钮）全部截断。
            if (overlay == null) return;
            var room = Codex.rooms[roomIndex];
            if (overlay.captionHeader != null) overlay.captionHeader.text = "CURRENT ROOM / 04";
            if (overlay.roomName != null) overlay.roomName.text = room.displayName;
            if (overlay.roomNote != null) overlay.roomNote.text = room.note;
            var hotspotLabel = roomIndex == 2 ? "手冲咖啡台" : roomIndex == 3 ? "旧书检索机" : "黑胶唱机";
            if (overlay.hotspotTitle != null) overlay.hotspotTitle.text = "＋  " + hotspotLabel + "\n<size=13>查看设备</size>";
            if (overlay.hotspotButton != null) BindButton(overlay.hotspotButton, () => OpenPanel(SystemPanel.Device));
        }

        /// <summary>
        /// 「家具摆放」入口：追加在右侧 dock 下方的运行时按钮（不改动 Hub Prefab 既有布局）。
        /// 家具模式为世界空间独立舞台，打开期间禁用整个局外 Canvas，退出回调恢复。
        /// </summary>
        private void BuildFurnitureEntry(Transform root)
        {
            F.Button(root, "FurnitureMode", "家    家具摆放", OpenFurnitureMode,
                new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-120, -262), new Vector2(205, 78),
                new Color(.32f, .06f, .18f, .86f), F.White, 20, TextAnchor.MiddleLeft);
        }

        /// <summary>声望与装饰分展示条（流通数值三件套中，货币在顶栏 HOUSE CREDIT 显示）。</summary>
        private void BuildEconomyChip(Transform root)
        {
            var chip = F.Panel(root, "EconomyChip", new Vector2(.5f, 1), new Vector2(.5f, 1),
                new Vector2(-233, -160), new Vector2(400, 50), new Color(.025f, .025f, .045f, .77f));
            economyChipLabel = F.Label(chip.transform, "Value", string.Empty, 18, F.White,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UpdateEconomyHud();
        }

        /// <summary>起居室优先使用家具布局合成图：摆放完成后布局直接成为背景图。</summary>
        private void ApplySceneArt()
        {
            if (sceneArt == null || roomIndex != 0) return;
            var baked = FurnitureSceneComposer.Current;
            if (baked != null) sceneArt.texture = baked;
        }

        /// <summary>
        /// 背景中的已摆放家具热点：悬停弹出「＋ 家具名 / 查看设备」提示卡（对齐黑胶唱机热点样式），
        /// 点击暂接设备图鉴面板。热点区域按归一化锚点定位，与合成图像素对应。
        /// </summary>
        private void BuildFurnitureHotspots()
        {
            if (sceneRoot == null) return;
            var existing = sceneRoot.Find("FurnitureHotspots");
            if (existing != null) Destroy(existing.gameObject);
            furnitureHotspots.Clear();
            if (view != View.Hub || roomIndex != 0) return;
            var root = F.Stretch(sceneRoot, "FurnitureHotspots");
            foreach (var info in FurnitureSceneComposer.GetPlacedFurniture())
            {
                var viewport = info.ViewportRect;
                var hotspot = F.Rect(root, "Hotspot_" + info.Entry.id,
                    new Vector2(viewport.xMin, viewport.yMin), new Vector2(viewport.xMax, viewport.yMax),
                    Vector2.zero, Vector2.zero);
                furnitureHotspots.Add((hotspot, viewport));
                var image = hotspot.gameObject.AddComponent<Image>();
                image.sprite = F.WhiteSprite;
                image.color = Color.clear;
                var button = hotspot.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => OpenPanel(SystemPanel.Device));

                var card = F.Panel(hotspot, "Card", new Vector2(.5f, 1), new Vector2(.5f, 1),
                    new Vector2(0, 46), new Vector2(250, 76), new Color(.32f, .06f, .18f, .92f));
                F.Outline(card.gameObject, new Color(.85f, .15f, .45f, .5f), new Vector2(1, -1));
                F.Label(card.transform, "Text", $"＋  {info.Entry.displayName}\n<size=13>查看设备</size>",
                    19, F.White, TextAnchor.MiddleCenter, FontStyle.Bold);
                var cardGroup = F.Group(card.gameObject, 0f);
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
            UpdateFurnitureHotspotAnchors();
        }

        /// <summary>游戏时段变化时刷新顶栏的天数/时段文案（时钟文本每帧已单独更新）。</summary>
        private void RefreshHudPhase()
        {
            if (view != View.Hub || hudPhaseLabel == null) return;
            var phase = OutGameUIData.CurrentPhase;
            var key = Clock.Data.Day * 10 + phase; // 跨天时 DAY 文案也要刷新
            if (key == hudPhaseShown) return;
            hudPhaseShown = key;
            hudPhaseLabel.text = $"<size=14>GAME TIME · 加速时间</size>\n<size=31>DAY {Clock.Data.Day:00}</size>    {HousePhaseText.Names[phase]}";
            if (hudPhaseRangeLabel != null) hudPhaseRangeLabel.text = HousePhaseText.Ranges[phase];
        }

        /// <summary>重建场景访客 NPC 层（仅起居室）。演员自己跟随 uvRect 换算锚点，观景模式无需额外通知。</summary>
        private void BuildVisitorStage()
        {
            visitorStage = null;
            if (sceneRoot == null) return;
            if (view != View.Hub || roomIndex != 0)
            {
                var existing = sceneRoot.Find("VisitorStage");
                if (existing != null) Destroy(existing.gameObject);
                return;
            }
            // 到访判定已归 VisitorManager（§16.4）：舞台只读业务状态生成演员，旧版「生成回调写 guestArrived」的回写路线已废除
            visitorStage = OutGameVisitorStage.Build(sceneRoot, sceneArt, OnVisitorClicked);
        }

        /// <summary>点击场景中的访客 NPC → 触发对话（观景模式下先展开界面）。</summary>
        private void OnVisitorClicked(int index)
        {
            if (furnitureModeOpen || dialogueOpen || roomTransitioning) return;
            if (hubImmersive) SetHubImmersive(false);
            SelectGuest(index);
        }

        /// <summary>按当前画面平移缩放（uvRect）换算热点锚点，保证观景模式下热点始终贴住家具。</summary>
        private void UpdateFurnitureHotspotAnchors()
        {
            if (sceneArt == null) return;
            var uv = sceneArt.uvRect;
            foreach (var (rect, viewport) in furnitureHotspots)
            {
                if (rect == null) continue;
                rect.anchorMin = new Vector2((viewport.xMin - uv.x) / uv.width, (viewport.yMin - uv.y) / uv.height);
                rect.anchorMax = new Vector2((viewport.xMax - uv.x) / uv.width, (viewport.yMax - uv.y) / uv.height);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }
        }

        /// <summary>「收起界面」开关按钮（收起时唯一保留的控件）。Prefab 优先，缺失时回退代码布局。</summary>
        private void BuildImmersiveToggle(Transform root)
        {
            var prefab = Resources.Load<GameObject>(OutGamePrefabResourcePaths.HubImmersiveToggle);
            if (prefab != null)
            {
                var instance = Instantiate(prefab, root, false);
                instance.name = "ImmersiveToggle";
                if (instance.transform is RectTransform rect)
                {
                    rect.anchorMin = rect.anchorMax = new Vector2(1, 0);
                    rect.anchoredPosition = new Vector2(-110, 56);
                    rect.localScale = Vector3.one;
                }
                var view = instance.GetComponent<OutGameHubImmersiveToggleView>();
                if (view != null && view.button != null)
                {
                    BindButton(view.button, () => SetHubImmersive(!hubImmersive));
                    immersiveLabel = view.label;
                    ApplyFallbackFont(instance.transform);
                    return;
                }
                Destroy(instance);
            }
            Debug.LogWarning("[OutGameUI] Prefab 缺失，暂时回退代码布局：" + OutGamePrefabResourcePaths.HubImmersiveToggle);
            var button = F.Button(root, "ImmersiveToggle", "收起界面", () => SetHubImmersive(!hubImmersive),
                new Vector2(1, 0), new Vector2(1, 0), new Vector2(-110, 56), new Vector2(160, 58),
                new Color(.025f, .025f, .04f, .8f), F.White, 17);
            immersiveLabel = button.GetComponentInChildren<Text>();
        }

        /// <summary>收起/展开四周 UI。收起后进入观景模式：拖拽平移背景、滚轮缩放。</summary>
        private void SetHubImmersive(bool on)
        {
            if (view != View.Hub) return;
            if (on)
            {
                if (openedPanel != SystemPanel.None) ClosePanel();
                if (dialogueOpen) CloseDialogue();
            }
            hubImmersive = on;
            scenePanning = false;
            var root = HubChromeRoot;
            foreach (Transform child in root)
            {
                if (child == sceneRoot || child.name == "ImmersiveToggle" || child.name == "Toast") continue;
                var group = F.Group(child.gameObject);
                group.DOKill();
                group.DOFade(on ? 0f : 1f, .25f).SetUpdate(true);
                group.blocksRaycasts = !on;
                group.interactable = !on;
            }
            if (sceneRoot != null)
            {
                var wash = sceneRoot.Find("SceneWash");
                if (wash != null)
                {
                    var washGroup = F.Group(wash.gameObject);
                    washGroup.DOKill();
                    washGroup.DOFade(on ? 0f : 1f, .25f).SetUpdate(true);
                }
            }
            if (!on && sceneArt != null) sceneArt.uvRect = new Rect(0f, 0f, 1f, 1f);
            UpdateFurnitureHotspotAnchors();
            if (immersiveLabel != null)
                immersiveLabel.text = on ? "展开界面\n<size=12>ESC</size>" : "收起界面";
        }

        /// <summary>观景模式：滚轮以鼠标为中心缩放（1~3.5 倍），按住左键拖拽平移，边界钳制在图内。</summary>
        private void HandleSceneBrowse()
        {
            if (sceneArt == null) return;
            var uv = sceneArt.uvRect;
            var scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > .01f && Screen.width > 0 && Screen.height > 0)
            {
                var zoom = Mathf.Clamp(1f / uv.width + scroll * .12f / uv.width, 1f, 3.5f);
                var size = 1f / zoom;
                var nx = Mathf.Clamp01(Input.mousePosition.x / Screen.width);
                var ny = Mathf.Clamp01(Input.mousePosition.y / Screen.height);
                var pivotX = uv.x + nx * uv.width;
                var pivotY = uv.y + ny * uv.height;
                uv = new Rect(pivotX - nx * size, pivotY - ny * size, size, size);
            }
            if (Input.GetMouseButtonDown(0))
            {
                scenePanning = true;
                lastPanPosition = Input.mousePosition;
            }
            if (Input.GetMouseButtonUp(0)) scenePanning = false;
            if (scenePanning && Screen.width > 0 && Screen.height > 0)
            {
                var delta = Input.mousePosition - lastPanPosition;
                lastPanPosition = Input.mousePosition;
                uv.x -= delta.x / Screen.width * uv.width;
                uv.y -= delta.y / Screen.height * uv.height;
            }
            uv.x = Mathf.Clamp(uv.x, 0f, 1f - uv.width);
            uv.y = Mathf.Clamp(uv.y, 0f, 1f - uv.height);
            sceneArt.uvRect = uv;
            // 热点跟随平移缩放，收起状态下家具依然可悬停/点击
            UpdateFurnitureHotspotAnchors();
        }

        /// <summary>关键节点静默写档（家具摆放退出、服务/拒绝/周结算、商城购买），保证槽位始终是最新进度。</summary>
        private void AutoSave()
        {
            if (view != View.Hub) return;
            SaveCurrent(true);
        }

        /// <summary>流通数值变化后刷新顶栏货币与声望/装饰分展示。</summary>
        private void UpdateEconomyHud()
        {
            if (creditHudLabel != null)
                creditHudLabel.text = $"<size=13>HOUSE CREDIT</size>\n◈ {Economy.Data.Currency:N0}     ＋";
            if (economyChipLabel != null)
                economyChipLabel.text =
                    $"<color=#74D8D1>声望 {Economy.Data.Reputation}</color>      <color=#E22D76>装饰分 {Economy.DecorationScore}</color>";
        }

        private void OpenFurnitureMode()
        {
            if (furnitureModeOpen) return;
            furnitureModeOpen = true;
            canvas.enabled = false;
            var opened = FurnitureRoomController.Open(() =>
            {
                furnitureModeOpen = false;
                canvas.enabled = true;
                // 布局变化即时落档，并烘焙回起居室背景图
                AutoSave();
                FurnitureSceneComposer.RequestBake(_ => { ApplySceneArt(); BuildFurnitureHotspots(); });
            });
            if (!opened)
            {
                furnitureModeOpen = false;
                canvas.enabled = true;
                ShowToast("家具配置表缺失：请先执行菜单 MasterHouse → 家具系统 → 创建配置表");
            }
        }

        private static void BindButton(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null) return;
            var feedback = button.GetComponent<OutGameTweenButton>();
            if (feedback == null) feedback = button.gameObject.AddComponent<OutGameTweenButton>();
            feedback.hoverScale = 1.025f;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        private void BuildTopHud(Transform root)
        {
            var top = F.Panel(root, "TopHUD", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -62),
                new Vector2(1920, 124), new Color(.025f, .025f, .045f, .77f));

            var phase = OutGameUIData.CurrentPhase;
            var time = F.Button(top.transform, "Time", $"<size=14>GAME TIME · 加速时间</size>\n<size=31>DAY {Clock.Data.Day:00}</size>    {HousePhaseText.Names[phase]}",
                () => OpenPanel(SystemPanel.Calendar), new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(230, 0),
                new Vector2(410, 100), new Color(.17f, .06f, .12f, .74f), F.White, 23, TextAnchor.MiddleLeft);
            F.Label(time.transform, "Phase", $"{HousePhaseText.Ranges[phase]}", 12, new Color(1, 1, 1, .58f),
                new Vector2(1, 0), new Vector2(1, 0), new Vector2(-78, 14), new Vector2(150, 24), TextAnchor.MiddleRight);
            clockLabel = F.Label(time.transform, "Clock", Clock.Data.TimeText, 24, F.White,
                new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-62, -7), new Vector2(110, 42), TextAnchor.MiddleRight, FontStyle.Bold);
            hudPhaseLabel = time.GetComponentInChildren<Text>();
            hudPhaseRangeLabel = time.transform.Find("Phase") != null ? time.transform.Find("Phase").GetComponent<Text>() : null;
            hudPhaseShown = Clock.Data.Day * 10 + phase;

            var creditButton = F.Button(top.transform, "Credit", $"<size=13>HOUSE CREDIT</size>\n◈ {Economy.Data.Currency:N0}     ＋", () => OpenPanel(SystemPanel.Market),
                new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(625, 0), new Vector2(270, 82),
                new Color(.06f, .025f, .06f, .7f), F.White, 21, TextAnchor.MiddleLeft);
            creditHudLabel = creditButton.GetComponentInChildren<Text>();
            var brand = F.Button(top.transform, "Brand", "<i>The Guesthouse\nof Meros</i>     <size=14>N E W  C H A P T E R</size>", ShowTitle,
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(120, 0), new Vector2(600, 90),
                Color.clear, F.Rose, 29, TextAnchor.MiddleCenter, false);
            brand.targetGraphic.raycastTarget = true;
            F.Label(top.transform, "Welcome", "WELCOME HOME.\n本周将有 <color=#E22D76>4</color> 位访客来访", 19, F.White,
                new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-370, 0), new Vector2(330, 78), TextAnchor.MiddleCenter);
            F.Button(top.transform, "Options", "设\n<size=15>OPTIONS · 设置</size>", () => OpenPanel(SystemPanel.Settings),
                new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-70, 0), new Vector2(112, 104),
                new Color(.32f, .06f, .18f, .86f), F.White, 27);
        }

        private void BuildTaskCard(Transform root)
        {
            var guest = Visitors.visitors[guestIndex];
            var card = F.Button(root, "VisitorTask", $"<size=13>CURRENT VISITOR TASK                     进行中</size>\n\n{guest.displayName} · {guest.need}\n<size=16>{guest.hint}</size>\n\n<size=14>━━━━━━  {ProgressForGuest(guestIndex)}%     点击查看任务详情  →</size>",
                () => OpenPanel(SystemPanel.Tasks), new Vector2(0, 1), new Vector2(0, 1), new Vector2(228, -250),
                new Vector2(390, 255), new Color(.13f, .045f, .11f, .84f), F.White, 22, TextAnchor.UpperLeft);
            F.Outline(card.gameObject, new Color(.85f, .15f, .45f, .45f), new Vector2(1, -1));
        }

        private void BuildGuestRail(Transform root)
        {
            var rail = F.Rect(root, "GuestRail", new Vector2(0, 1), new Vector2(0, 1), new Vector2(228, -650), new Vector2(390, 535));
            F.Label(rail, "Title", $"VISITOR EVENTS / 访客事件                         {RemainingGuests():00}", 16, F.Rose,
                new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -20), new Vector2(390, 36), TextAnchor.MiddleLeft, FontStyle.Bold);
            for (var i = 0; i < Visitors.visitors.Count; i++)
            {
                var index = i;
                var guest = Visitors.visitors[i];
                var done = Visitor.Data.States[i].Served;
                var caption = $"<size=12>{(guest.special ? "SPECIAL EVENT" : "EVENT 0" + (i + 1))}</size>\n{guest.displayName}\n<size=15>{(done ? "事件已完成" : guest.special ? "特殊客人 · 可打断" : "一般客人 · 可接待")}</size>";
                var button = F.Button(rail, "Guest" + guest.id, caption, () => SelectGuest(index),
                    new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -90 - i * 112), new Vector2(390, 100),
                    done ? new Color(.03f, .03f, .045f, .55f) : new Color(.025f, .025f, .045f, .83f),
                    done ? new Color(1, 1, 1, .45f) : F.White, 21, TextAnchor.MiddleLeft);
                BuildPortrait(button.transform, guest.portraitPath, new Vector2(55, 0), new Vector2(76, 76), new Vector2(0, .5f), true);
                var label = button.GetComponentInChildren<Text>();
                label.rectTransform.offsetMin = new Vector2(110, 6);
                F.Label(button.transform, "Type", done ? "✓" : guest.special ? "特" : "普", 17, F.White,
                    new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-28, 0), new Vector2(46, 46), TextAnchor.MiddleCenter, FontStyle.Bold);
            }
        }

        private void BuildRightDock(Transform root)
        {
            var dock = F.Rect(root, "RightDock", new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-120, 10), new Vector2(205, 470));
            F.Label(dock, "Title", "HOUSE / MENU", 13, F.Rose, new Vector2(.5f, 1), new Vector2(.5f, 1),
                new Vector2(0, -20), new Vector2(200, 30), TextAnchor.MiddleCenter);
            var entries = new[]
            {
                new DockItem("器", "设备图鉴", SystemPanel.Device),
                new DockItem("记", "日记", SystemPanel.Journal),
                new DockItem("录", "通讯录", SystemPanel.Contacts),
                new DockItem("集", "档案", SystemPanel.Archive),
            };
            for (var i = 0; i < entries.Length; i++)
            {
                var item = entries[i];
                F.Button(dock, "Dock" + item.label, item.icon + "    " + item.label, () => OpenPanel(item.panel),
                    new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -82 - i * 92), new Vector2(205, 78),
                    new Color(.025f, .025f, .04f, .75f), F.White, 20, TextAnchor.MiddleLeft);
            }
        }

        private void BuildRoomNavigation(Transform root)
        {
            var nav = F.Panel(root, "RoomNav", new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(90, 90),
                new Vector2(1030, 150), new Color(.02f, .022f, .04f, .82f));
            F.Outline(nav.gameObject, new Color(.5f, .2f, .38f, .55f), new Vector2(1, -1));
            F.Label(nav.transform, "Title", "<color=#E22D76>MAKE IT HOME</color>\n<size=13>← → 快速切换</size>", 18, F.White,
                new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(112, 0), new Vector2(210, 105), TextAnchor.MiddleCenter, FontStyle.Bold);
            for (var i = 0; i < 4; i++)
            {
                var index = i;
                var room = Codex.rooms[i];
                var selected = roomIndex == i;
                F.Button(nav.transform, "Room" + room.id, $"<size=12>{room.code}</size>\n{RoomIcon(i)}\n{room.displayName}{(selected ? "\n<size=11>CURRENT</size>" : "")}",
                    () => SelectRoom(index), new Vector2(0, .5f), new Vector2(0, .5f),
                    new Vector2(305 + i * 175, 0), new Vector2(170, 150),
                    selected ? new Color(.45f, .08f, .3f, .77f) : new Color(1, 1, 1, .015f),
                    selected ? F.White : new Color(1, 1, 1, .72f), 20, TextAnchor.MiddleCenter);
            }
            F.Button(nav.transform, "LockedRoom", "<size=12>LOCKED</size>\n▣\n地下仓库", () => ShowToast("仓库房间将在 House LV.04 解锁"),
                new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-80, 0), new Vector2(160, 150),
                Color.clear, new Color(1, 1, 1, .3f), 18);
        }

        private void BuildSceneCaption(Transform root)
        {
            var room = Codex.rooms[roomIndex];
            var caption = F.Panel(root, "SceneCaption", new Vector2(0, 0), new Vector2(0, 0), new Vector2(390, 135),
                new Vector2(310, 84), new Color(.8f, .75f, .67f, .92f));
            F.Label(caption.transform, "Caption", $"<size=12>CURRENT ROOM / 04</size>\n{room.displayName}  <size=14>{room.note}</size>",
                23, F.Hex("3B2D31"), TextAnchor.MiddleCenter, FontStyle.Bold);
            var hotspotLabel = roomIndex == 2 ? "手冲咖啡台" : roomIndex == 3 ? "旧书检索机" : "黑胶唱机";
            F.Button(root, "Hotspot", "＋  " + hotspotLabel + "\n<size=13>查看设备</size>", () => OpenPanel(SystemPanel.Device),
                new Vector2(.72f, .5f), new Vector2(.72f, .5f), new Vector2(0, 30), new Vector2(220, 76),
                new Color(.2f, .03f, .15f, .75f), F.White, 19, TextAnchor.MiddleCenter);
        }

        private void AnimateHubIn()
        {
            foreach (Transform child in HubChromeRoot)
            {
                if (child.name == "Scene") continue;
                var rt = child as RectTransform;
                var group = F.Group(child.gameObject, 0);
                var target = rt.anchoredPosition;
                rt.anchoredPosition = target + new Vector2(0, child.name == "RoomNav" ? -35 : 22);
                DOTween.Sequence().SetTarget(this).SetUpdate(true)
                    .AppendInterval(UnityEngine.Random.Range(.03f, .22f))
                    .Append(group.DOFade(1, .3f))
                    .Join(rt.DOAnchorPos(target, .42f).SetEase(Ease.OutCubic));
            }
        }

        #endregion

        #region 房间与访客

        private void SelectRoom(int index)
        {
            if (index == roomIndex || roomTransitioning)
            {
                if (index == roomIndex) ShowToast($"当前位于{Codex.rooms[index].displayName} · {Codex.rooms[index].note}");
                return;
            }
            var usesDoor = index == 1 || roomIndex == 1;
            if (!usesDoor)
            {
                SwapRoom(index);
                ShowToast(index == 2 ? "镜头聚焦至厨房料理台" : index == 3 ? "视角旋转 90° · 已进入书房" : "已回到起居室");
                return;
            }

            roomTransitioning = true;
            var transition = F.Stretch(viewRoot, "RoomDoorTransition");
            transition.SetAsLastSibling();
            var left = F.Panel(transition, "LeftDoor", new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(-480, 0),
                new Vector2(960, 1080), F.Hex("251820"));
            var right = F.Panel(transition, "RightDoor", new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(480, 0),
                new Vector2(960, 1080), F.Hex("251820"));
            DOTween.Sequence().SetTarget(this).SetUpdate(true)
                .Append(left.rectTransform.DOAnchorPosX(480, .42f).SetEase(Ease.InCubic))
                .Join(right.rectTransform.DOAnchorPosX(-480, .42f).SetEase(Ease.InCubic))
                .AppendCallback(() => SwapRoom(index))
                .Append(left.rectTransform.DOAnchorPosX(-480, .72f).SetEase(Ease.OutCubic))
                .Join(right.rectTransform.DOAnchorPosX(480, .72f).SetEase(Ease.OutCubic))
                .OnComplete(() => { roomTransitioning = false; Destroy(transition.gameObject); });
        }

        private void SwapRoom(int index)
        {
            roomIndex = index;
            selectedDevice = 0;
            if (sceneArt != null)
            {
                var old = sceneArt;
                var next = F.StretchTexture(sceneRoot, "SceneArtNext", Codex.rooms[index].artPath, new Color(1, 1, 1, 0));
                next.raycastTarget = false;
                next.transform.SetAsFirstSibling();
                next.DOFade(1, .5f).SetTarget(this).SetUpdate(true);
                old.DOFade(0, .5f).SetTarget(this).SetUpdate(true).OnComplete(() => Destroy(old.gameObject));
                sceneArt = next;
                ApplySceneArt();
            }
            BuildFurnitureHotspots();
            BuildVisitorStage();
            RebuildHubChrome();
        }

        private void RebuildHubChrome()
        {
            if (view != View.Hub) return;
            if (HasPrefabHubComponents())
            {
                BindRoomNavigation(activeHubView.roomNavigation);
                BindSceneOverlay(activeHubView.sceneOverlay);
                return;
            }
            var chrome = HubChromeRoot;
            var names = new[] { "RoomNav", "SceneCaption", "Hotspot" };
            foreach (var name in names)
            {
                var child = chrome.Find(name);
                if (child != null) Destroy(child.gameObject);
            }
            BuildRoomNavigation(chrome);
            BuildSceneCaption(chrome);
        }

        private void SelectGuest(int index)
        {
            if (Visitor.Data.States[index].Served)
            {
                ShowToast(Visitors.visitors[index].displayName + " 已完成接待并离开旅店");
                return;
            }
            // 服务时间窗口由 VisitorManager 整数分钟判定（§16.4）；窗口外访客仍留在屋内，只是暂不开放服务
            if (!Visitor.CanServe(index))
            {
                var guest = Visitors.visitors[index];
                ShowToast($"{guest.displayName} 的可服务时间是 {guest.ServiceWindowText} · 现在 {Clock.Data.TimeText}，TA 先在屋里歇着");
                return;
            }
            guestIndex = index;
            ShowDialogue();
        }

        private void ShowDialogue()
        {
            ClosePanelImmediate();
            dialogueOpen = true;
            if (ShowDialogueFromPrefab()) return;
            Debug.LogWarning("[OutGameUI] Prefab 缺失，暂时回退代码布局：" + OutGamePrefabResourcePaths.DialogueView);
            var guest = Visitors.visitors[guestIndex];
            modalRoot = F.Stretch(HubOverlayRoot, "DialogueLayer");
            modalRoot.SetAsLastSibling();
            F.StretchTexture(modalRoot, "VisitorScene", "OutGameUI/house-hub-v2", new Color(.72f, .72f, .78f, 1));
            F.StretchPanel(modalRoot, "DialogueVignette", new Color(.01f, .01f, .025f, .48f));
            F.Button(modalRoot, "Close", "ESC · 结束交谈", CloseDialogue, new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-145, -55), new Vector2(240, 54), new Color(.08f, .02f, .06f, .78f), F.White, 17);

            var portrait = F.Panel(modalRoot, "CharacterCard", new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(390, 40),
                new Vector2(500, 700), new Color(.16f, .05f, .12f, .82f));
            BuildPortrait(portrait.transform, guest.portraitPath, Vector2.zero, new Vector2(460, 620), new Vector2(.5f, .5f), false);
            F.Label(portrait.transform, "Tag", "VISITOR / " + (guest.special ? "SPECIAL" : "WEEK 01"), 15, F.Rose,
                new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 25), new Vector2(420, 30), TextAnchor.MiddleCenter, FontStyle.Bold);

            var week = F.Panel(modalRoot, "WeekPanel", new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-245, 90),
                new Vector2(410, 540), new Color(.02f, .025f, .045f, .85f));
            F.Label(week.transform, "Title", "WEEK 01 / 本周访客", 16, F.Rose,
                new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -35), new Vector2(350, 40), TextAnchor.MiddleCenter, FontStyle.Bold);
            for (var i = 0; i < 4; i++)
            {
                var index = i;
                var item = Visitors.visitors[i];
                F.Button(week.transform, "WeekGuest" + i, item.displayName + "\n<size=13>" + (item.special ? "特殊事件 · 可打断" : "一般事件 · 无先后") + "</size>",
                    () => { guestIndex = index; CloseDialogue(); ShowDialogue(); }, new Vector2(.5f, 1), new Vector2(.5f, 1),
                    new Vector2(0, -105 - i * 100), new Vector2(355, 82),
                    i == guestIndex ? new Color(.45f, .08f, .28f, .75f) : new Color(1, 1, 1, .035f), F.White, 19, TextAnchor.MiddleLeft);
            }

            var box = F.Panel(modalRoot, "DialogueBox", new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(80, 190),
                new Vector2(1120, 250), new Color(.035f, .025f, .045f, .94f));
            var line = guest.transactionLine;
            F.Label(box.transform, "Dialogue", $"<size=15>{guest.type}{(guest.special ? " · 硬植入事件" : " · 无接待顺序")}</size>\n<size=31>{guest.displayName}</size>     <size=15>信赖 {guest.affinity}%</size>\n\n{line}",
                22, F.White, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-90, 10), new Vector2(850, 205), TextAnchor.UpperLeft);
            F.Button(box.transform, "Need", "查看需求家具", () => { CloseDialogue(); OpenPanel(SystemPanel.Archive); },
                new Vector2(1, 0), new Vector2(1, 0), new Vector2(-170, 82), new Vector2(250, 58), new Color(1, 1, 1, .05f), F.White, 18);
            var serve = F.Button(box.transform, "Serve", Visitor.Data.States[guestIndex].Served ? "事件已完成" : "回应访客事件", ServeGuest,
                new Vector2(1, 0), new Vector2(1, 0), new Vector2(-170, 25), new Vector2(250, 58), F.Wine, F.White, 18);
            serve.interactable = !Visitor.Data.States[guestIndex].Served;
            var refuse = F.Button(box.transform, "Refuse", $"拒绝接待 <size=13>声望 -{Economy.RefuseReputationPenalty}</size>", RefuseGuest,
                new Vector2(1, 0), new Vector2(1, 0), new Vector2(-425, 25), new Vector2(230, 58), new Color(1, 1, 1, .05f), F.White, 17);
            refuse.interactable = !Visitor.Data.States[guestIndex].Served;

            var furniture = F.Panel(modalRoot, "FurnitureDock", new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 45),
                new Vector2(1480, 90), new Color(.015f, .018f, .032f, .93f));
            F.Label(furniture.transform, "Title", "MAKE FOR VISITOR\n<size=11>根据来客需求制造并摆放</size>", 15, F.Rose,
                new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(130, 0), new Vector2(240, 62), TextAnchor.MiddleCenter, FontStyle.Bold);
            for (var i = 0; i < FurnitureArchives.Count; i++)
            {
                var index = i;
                var item = FurnitureArchives[i];
                F.Button(furniture.transform, "Furniture" + i, item.displayName, () => { placedFurniture = item.id; ShowToast("已摆放：" + item.displayName); },
                    new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(335 + i * 205, 0), new Vector2(195, 70),
                    placedFurniture == item.id ? new Color(.48f, .08f, .28f, .72f) : new Color(1, 1, 1, .035f), F.White, 15);
            }
            F.Button(furniture.transform, "EndWeek", "结束本周 →", EndWeek,
                new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-100, 0), new Vector2(180, 70), F.Wine, F.White, 17);

            var group = F.Group(modalRoot.gameObject, 0);
            group.DOFade(1, .28f).SetTarget(this).SetUpdate(true);
            portrait.rectTransform.anchoredPosition += new Vector2(-60, 0);
            portrait.rectTransform.DOAnchorPosX(390, .55f).SetTarget(this).SetEase(Ease.OutCubic).SetUpdate(true);
            box.rectTransform.anchoredPosition += new Vector2(0, -80);
            box.rectTransform.DOAnchorPosY(190, .5f).SetTarget(this).SetEase(Ease.OutCubic).SetUpdate(true);
        }

        /// <summary>访客对话优先走 DialogueView Prefab；文本、选中态与事件运行时绑定。</summary>
        private bool ShowDialogueFromPrefab()
        {
            var prefab = Resources.Load<GameObject>(OutGamePrefabResourcePaths.DialogueView);
            if (prefab == null) return false;
            var instance = Instantiate(prefab, HubOverlayRoot, false);
            instance.name = "DialogueLayer";
            var view = instance.GetComponent<OutGameDialogueView>();
            if (view == null)
            {
                Destroy(instance);
                return false;
            }
            modalRoot = instance.transform as RectTransform;
            modalRoot.SetAsLastSibling();
            var guest = Visitors.visitors[guestIndex];

            if (view.sceneArt != null)
            {
                var baked = FurnitureSceneComposer.Current;
                if (baked != null) view.sceneArt.texture = baked;
                else view.sceneArt.texture = Resources.Load<Texture2D>("OutGameUI/house-hub-v2");
            }
            if (view.closeButton != null) BindButton(view.closeButton, CloseDialogue);
            if (view.portrait != null) view.portrait.texture = Resources.Load<Texture2D>(guest.portraitPath);
            if (view.portraitTag != null)
                view.portraitTag.text = "VISITOR / " + (guest.special ? "SPECIAL" : "WEEK 01");

            for (var i = 0; i < 4; i++)
            {
                var index = i;
                var item = Visitors.visitors[i];
                if (view.weekGuestLabels != null && i < view.weekGuestLabels.Length && view.weekGuestLabels[i] != null)
                    view.weekGuestLabels[i].text = item.displayName + "\n<size=13>" + (item.special ? "特殊事件 · 可打断" : "一般事件 · 无先后") + "</size>";
                if (view.weekGuestBackgrounds != null && i < view.weekGuestBackgrounds.Length && view.weekGuestBackgrounds[i] != null)
                    view.weekGuestBackgrounds[i].color = i == guestIndex ? new Color(.45f, .08f, .28f, .75f) : new Color(1, 1, 1, .035f);
                if (view.weekGuestButtons != null && i < view.weekGuestButtons.Length && view.weekGuestButtons[i] != null)
                    BindButton(view.weekGuestButtons[i], () => { guestIndex = index; CloseDialogue(); ShowDialogue(); });
            }

            if (view.dialogueText != null)
                view.dialogueText.text = $"<size=15>{guest.type}{(guest.special ? " · 硬植入事件" : " · 无接待顺序")}</size>\n<size=31>{guest.displayName}</size>     <size=15>信赖 {guest.affinity}%</size>\n\n{guest.transactionLine}";
            if (view.needButton != null)
                BindButton(view.needButton, () => { CloseDialogue(); OpenPanel(SystemPanel.Archive); });
            if (view.serveButton != null)
            {
                if (view.serveLabel != null) view.serveLabel.text = Visitor.Data.States[guestIndex].Served ? "事件已完成" : "回应访客事件";
                BindButton(view.serveButton, ServeGuest);
                view.serveButton.interactable = !Visitor.Data.States[guestIndex].Served;
            }
            if (view.refuseButton != null)
            {
                if (view.refuseLabel != null)
                    view.refuseLabel.text = $"拒绝接待 <size=13>声望 -{Economy.RefuseReputationPenalty}</size>";
                BindButton(view.refuseButton, RefuseGuest);
                view.refuseButton.interactable = !Visitor.Data.States[guestIndex].Served;
            }

            for (var i = 0; i < FurnitureArchives.Count && i < 5; i++)
            {
                var item = FurnitureArchives[i];
                if (view.furnitureLabels != null && i < view.furnitureLabels.Length && view.furnitureLabels[i] != null)
                    view.furnitureLabels[i].text = item.displayName;
                if (view.furnitureBackgrounds != null && i < view.furnitureBackgrounds.Length && view.furnitureBackgrounds[i] != null)
                    view.furnitureBackgrounds[i].color = placedFurniture == item.id ? new Color(.48f, .08f, .28f, .72f) : new Color(1, 1, 1, .035f);
                if (view.furnitureButtons != null && i < view.furnitureButtons.Length && view.furnitureButtons[i] != null)
                {
                    var itemId = item.id;
                    var itemName = item.displayName;
                    var backgrounds = view.furnitureBackgrounds;
                    BindButton(view.furnitureButtons[i], () =>
                    {
                        placedFurniture = itemId;
                        for (var j = 0; j < FurnitureArchives.Count && j < backgrounds.Length; j++)
                            if (backgrounds[j] != null)
                                backgrounds[j].color = FurnitureArchives[j].id == itemId
                                    ? new Color(.48f, .08f, .28f, .72f)
                                    : new Color(1, 1, 1, .035f);
                        ShowToast("已摆放：" + itemName);
                    });
                }
            }
            if (view.endWeekButton != null) BindButton(view.endWeekButton, EndWeek);

            ApplyFallbackFont(instance.transform);

            // 入场动效与代码路径一致：整层淡入 + 立绘/对话框滑入
            var group = F.Group(modalRoot.gameObject, 0);
            group.DOFade(1, .28f).SetTarget(this).SetUpdate(true);
            if (view.characterCard != null)
            {
                view.characterCard.anchoredPosition = new Vector2(330, 40);
                view.characterCard.DOAnchorPosX(390, .55f).SetTarget(this).SetEase(Ease.OutCubic).SetUpdate(true);
            }
            if (view.dialogueBox != null)
            {
                view.dialogueBox.anchoredPosition = new Vector2(80, 110);
                view.dialogueBox.DOAnchorPosY(190, .5f).SetTarget(this).SetEase(Ease.OutCubic).SetUpdate(true);
            }
            return true;
        }

        private void CloseDialogue()
        {
            if (!dialogueOpen || modalRoot == null) return;
            dialogueOpen = false;
            var old = modalRoot;
            modalRoot = null;
            var group = F.Group(old.gameObject);
            group.DOFade(0, .2f).SetTarget(this).SetUpdate(true).OnComplete(() => Destroy(old.gameObject));
        }

        private void ServeGuest()
        {
            // 业务结算（置状态 + 货币/声望产出）归 VisitorManager（§16.3）；此处只剩表现刷新与落档
            if (!Visitor.Serve(guestIndex)) return;
            var name = Visitors.visitors[guestIndex].displayName;
            if (visitorStage != null) visitorStage.NotifyServed(guestIndex);
            CloseDialogue();
            RebuildGuestChrome();
            UpdateEconomyHud();
            AutoSave();
            ShowToast($"{name} 的服务已完成 · ◈ +{Economy.ServiceCurrencyReward} · 声望 +{Economy.ServiceReputationReward}");
        }

        private void RefuseGuest()
        {
            // 业务结算（置状态 + 声望扣除）归 VisitorManager（§16.3）；此处只剩表现刷新与落档
            if (!Visitor.Refuse(guestIndex)) return;
            var name = Visitors.visitors[guestIndex].displayName;
            if (visitorStage != null) visitorStage.NotifyRefused(guestIndex);
            CloseDialogue();
            RebuildGuestChrome();
            UpdateEconomyHud();
            AutoSave();
            ShowToast($"已婉拒 {name} 的委托 · 声望 -{Economy.RefuseReputationPenalty}");
        }

        private void EndWeek()
        {
            // 周结算业务（未完成扣声望 → 清空本周状态 → 时钟跳次日早晨）整体归 VisitorManager（§16.3）
            var missed = Visitor.EndWeek();
            CloseDialogue();
            RebuildGuestChrome();
            BuildVisitorStage(); // 新的一周 → 访客整体刷新，重新从大门进场
            UpdateEconomyHud();
            AutoSave();
            ShowToast(missed > 0
                ? $"本周结束 · {missed} 项服务未完成，声望 -{missed * Economy.FailReputationPenalty}"
                : "本周结束 · 所有访客服务全部完成！新的一周开始了");
        }

        private void RebuildGuestChrome()
        {
            if (HasPrefabHubComponents())
            {
                BindTaskCard(activeHubView.taskCard);
                BindGuestRail(activeHubView.guestRail);
                BindTopHud(activeHubView.topBar);
                return;
            }
            var chrome = HubChromeRoot;
            var task = chrome.Find("VisitorTask");
            var rail = chrome.Find("GuestRail");
            if (task != null) Destroy(task.gameObject);
            if (rail != null) Destroy(rail.gameObject);
            BuildTaskCard(chrome);
            BuildGuestRail(chrome);
        }

        #endregion

        #region 功能面板

        private void OpenPanel(SystemPanel panel)
        {
            if (view != View.Hub) return;
            CloseDialogue();
            ClosePanelImmediate();
            openedPanel = panel;
            if (TryOpenPanelPage(panel)) return;
            var panelPrefab = Resources.Load<GameObject>(OutGamePrefabResourcePaths.SystemPanel);
            if (panelPrefab != null)
            {
                var instance = Instantiate(panelPrefab, HubOverlayRoot, false);
                instance.name = "SystemPanelLayer";
                modalRoot = instance.transform as RectTransform;
                activeSystemPanel = instance.GetComponent<OutGameSystemPanelView>();
                if (activeSystemPanel != null)
                {
                    activeSystemPanel.scrimButton.onClick.RemoveAllListeners();
                    activeSystemPanel.scrimButton.onClick.AddListener(ClosePanel);
                    activeSystemPanel.scrim.color = new Color(.005f, .008f, .02f, 0);
                    activeSystemPanel.scrim.DOFade(.62f, .25f).SetTarget(this).SetUpdate(true);
                    activeSystemPanel.panel.rectTransform.anchoredPosition = new Vector2(PanelWidth / 2 + 80, 0);
                    activeSystemPanel.panel.rectTransform.DOAnchorPosX(-PanelWidth / 2, .42f).SetTarget(this)
                        .SetEase(Ease.OutCubic).SetUpdate(true);
                    PopulatePanelHeader(activeSystemPanel.headerRoot, panel);
                    PopulatePanelContent(activeSystemPanel.contentRoot, panel);
                    ApplyFallbackFont(instance.transform);
                    return;
                }
                Destroy(instance);
            }

            modalRoot = F.Stretch(HubOverlayRoot, "SystemPanelLayer");
            modalRoot.SetAsLastSibling();
            var scrim = F.StretchPanel(modalRoot, "Scrim", new Color(.005f, .008f, .02f, 0));
            var scrimButton = scrim.gameObject.AddComponent<Button>();
            scrimButton.targetGraphic = scrim;
            scrimButton.onClick.AddListener(ClosePanel);
            scrim.DOFade(.62f, .25f).SetTarget(this).SetUpdate(true);
            var panelImage = F.Panel(modalRoot, "SystemPanel", new Vector2(1, .5f), new Vector2(1, .5f),
                new Vector2(PanelWidth / 2 + 80, 0), new Vector2(PanelWidth, 1080), new Color(.055f, .045f, .06f, .98f));
            F.Outline(panelImage.gameObject, new Color(.75f, .18f, .42f, .5f), new Vector2(-2, 0));
            panelImage.rectTransform.DOAnchorPosX(-PanelWidth / 2, .42f).SetTarget(this).SetEase(Ease.OutCubic).SetUpdate(true);
            BuildPanelHeader(panelImage.transform, panel);
            BuildPanelContent(panelImage.transform, panel);
        }

        /// <summary>整页面板 Prefab 优先：外壳（遮罩/滑入/头部）来自 Prefab，内容按面板类型绑定。缺失时回退共享壳。</summary>
        private bool TryOpenPanelPage(SystemPanel panel)
        {
            string path;
            switch (panel)
            {
                case SystemPanel.Calendar: path = OutGamePrefabResourcePaths.CalendarPage; break;
                case SystemPanel.Tasks: path = OutGamePrefabResourcePaths.TasksPage; break;
                case SystemPanel.Device: path = OutGamePrefabResourcePaths.DevicePage; break;
                case SystemPanel.Journal: path = OutGamePrefabResourcePaths.JournalPage; break;
                case SystemPanel.Archive: path = OutGamePrefabResourcePaths.ArchivePage; break;
                default: return false;
            }
            var prefab = Resources.Load<GameObject>(path);
            if (prefab == null) return false;
            var instance = Instantiate(prefab, HubOverlayRoot, false);
            instance.name = "SystemPanelLayer";
            var page = instance.GetComponent<OutGamePanelPageView>();
            if (page == null)
            {
                Destroy(instance);
                return false;
            }
            modalRoot = instance.transform as RectTransform;
            modalRoot.SetAsLastSibling();
            activeSystemPanel = null;

            var meta = PanelMeta(panel);
            if (page.headerTitle != null) page.headerTitle.text = $"<size=14>{meta.eyebrow}</size>\n{meta.title}";
            if (page.headerMark != null) page.headerMark.text = meta.mark;
            if (page.backButton != null) BindButton(page.backButton, ClosePanel);
            if (page.scrimButton != null)
            {
                page.scrimButton.onClick.RemoveAllListeners();
                page.scrimButton.onClick.AddListener(ClosePanel);
            }
            if (page.scrim != null)
            {
                page.scrim.color = new Color(.005f, .008f, .02f, 0);
                page.scrim.DOFade(.62f, .25f).SetTarget(this).SetUpdate(true);
            }
            if (page.panel != null)
            {
                // 以 Prefab 作者摆放的位置为静止点，按面板实际宽度计算滑入距离——改 Prefab 尺寸后动画自动适配
                var panelRect = page.panel.rectTransform;
                var restingPosition = panelRect.anchoredPosition;
                panelRect.anchoredPosition = new Vector2(restingPosition.x + panelRect.rect.width + 80, restingPosition.y);
                panelRect.DOAnchorPosX(restingPosition.x, .42f).SetTarget(this)
                    .SetEase(Ease.OutCubic).SetUpdate(true);
            }

            switch (panel)
            {
                case SystemPanel.Calendar: BindCalendarPanel(instance.GetComponentInChildren<OutGameCalendarPanelView>()); break;
                case SystemPanel.Tasks: BindTasksPanel(instance.GetComponentInChildren<OutGameTasksPanelView>()); break;
                case SystemPanel.Device: BindDevicePanel(instance.GetComponentInChildren<OutGameDevicePanelView>()); break;
                case SystemPanel.Journal: BindJournalPanel(instance.GetComponentInChildren<OutGameJournalPanelView>()); break;
                case SystemPanel.Archive: BindArchivePanel(instance.GetComponentInChildren<OutGameArchivePanelView>()); break;
            }
            ApplyFallbackFont(instance.transform);
            return true;
        }

        private void ClosePanel()
        {
            if (openedPanel == SystemPanel.None || modalRoot == null) return;
            openedPanel = SystemPanel.None;
            var old = modalRoot;
            modalRoot = null;
            var panel = activeSystemPanel != null && activeSystemPanel.panel != null
                ? activeSystemPanel.panel.rectTransform
                : old.Find("SystemPanel") as RectTransform;
            activeSystemPanel = null;
            var group = F.Group(old.gameObject);
            group.DOFade(0, .25f).SetTarget(this).SetUpdate(true);
            if (panel != null)
                panel.DOAnchorPosX(PanelWidth / 2 + 120, .3f).SetTarget(this).SetEase(Ease.InCubic).SetUpdate(true)
                    .OnComplete(() => Destroy(old.gameObject));
            else Destroy(old.gameObject);
        }

        private void ClosePanelImmediate()
        {
            if (openedPanel == SystemPanel.None || modalRoot == null) return;
            openedPanel = SystemPanel.None;
            Destroy(modalRoot.gameObject);
            modalRoot = null;
            activeSystemPanel = null;
        }

        private void BuildPanelHeader(Transform panel, SystemPanel type)
        {
            var header = F.Panel(panel, "Header", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -75),
                new Vector2(1280, 150), new Color(.1f, .045f, .085f, .95f));
            PopulatePanelHeader(header.transform, type);
        }

        private void PopulatePanelHeader(Transform header, SystemPanel type)
        {
            var meta = PanelMeta(type);
            var background = header.GetComponent<Image>();
            if (background != null) background.color = new Color(.1f, .045f, .085f, .95f);
            F.Button(header.transform, "Back", "←\n<size=12>ESC</size>", ClosePanel, new Vector2(0, .5f), new Vector2(0, .5f),
                new Vector2(58, 0), new Vector2(86, 90), new Color(1, 1, 1, .04f), F.White, 25);
            F.Label(header.transform, "Title", $"<size=14>{meta.eyebrow}</size>\n{meta.title}", 34, F.White,
                new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(330, 0), new Vector2(430, 95), TextAnchor.MiddleLeft, FontStyle.Bold);
            F.Label(header.transform, "Mark", meta.mark, 54, new Color(1, .35f, .62f, .55f),
                new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-95, 0), new Vector2(100, 90), TextAnchor.MiddleCenter, FontStyle.Bold);
        }

        private void BuildPanelContent(Transform panel, SystemPanel type)
        {
            var content = F.Rect(panel, "Content", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                new Vector2(0, -75), new Vector2(1180, 830));
            PopulatePanelContent(content, type);
        }

        private void PopulatePanelContent(RectTransform content, SystemPanel type)
        {
            switch (type)
            {
                case SystemPanel.Tasks: BuildTasksPanel(content); break;
                case SystemPanel.Device: BuildDevicePanel(content); break;
                case SystemPanel.Journal: BuildJournalPanel(content); break;
                case SystemPanel.Contacts: BuildContactsPanel(content); break;
                case SystemPanel.Archive: BuildArchivePanel(content); break;
                case SystemPanel.Calendar: BuildCalendarPanel(content); break;
                case SystemPanel.Inventory: BuildInventoryPanel(content); break;
                case SystemPanel.Settings: BuildSettingsPanel(content); break;
                case SystemPanel.Profile: BuildProfilePanel(content); break;
                case SystemPanel.Market: BuildMarketPanel(content); break;
            }
        }

        private void BuildTasksPanel(Transform content)
        {
            var prefab = Resources.Load<GameObject>(OutGamePrefabResourcePaths.TasksPanel);
            if (prefab != null)
            {
                var instance = Instantiate(prefab, content, false);
                instance.name = "TasksContent";
                CenterPanelContent(instance);
                var view = instance.GetComponent<OutGameTasksPanelView>();
                if (view != null)
                {
                    BindTasksPanel(view);
                    ApplyFallbackFont(instance.transform);
                    return;
                }
                Destroy(instance);
            }
            Debug.LogWarning("[OutGameUI] Prefab 缺失，暂时回退代码布局：" + OutGamePrefabResourcePaths.TasksPanel);
            var guest = Visitors.visitors[guestIndex];
            var focus = DarkCard(content, "Focus", new Vector2(0, 270), new Vector2(1120, 220), new Color(.3f, .06f, .2f, .45f));
            F.Label(focus, "Text", $"<color=#E22D76>●  MAIN / {guest.type}</color>\n<size=28>{guest.displayName} · {guest.need}</size>\n<size=17>{guest.hint} 推荐使用「{guest.solution}」，完成后可能留下「{guest.gift}」。</size>",
                20, F.White, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-30, 0), new Vector2(970, 175), TextAnchor.MiddleLeft);
            var tasks = new[] { "为赫墨制造琴弦窗户", "把米娅的纸条挂上风铃", "检查明日访客预告" };
            for (var i = 0; i < tasks.Length; i++)
            {
                var task = tasks[i];
                F.Button(content, "Task" + i, $"0{i + 2}     {task}                         {(i == 2 ? "未解锁" : "进行中")}",
                    () => ShowToast("已追踪：" + task), new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                    new Vector2(0, 75 - i * 100), new Vector2(1120, 84), new Color(1, 1, 1, .035f), F.White, 20, TextAnchor.MiddleLeft);
            }
            var progress = DarkCard(content, "Progress", new Vector2(0, -305), new Vector2(1120, 105), new Color(.12f, .06f, .1f, .8f));
            F.Label(progress, "Text", "本周 House 进度                                      37%\n<color=#E22D76>━━━━━━━━━━━━━━━━━━━━</color>",
                19, F.White, TextAnchor.MiddleCenter, FontStyle.Bold);
        }

        /// <summary>面板内容 Prefab 统一按内容区中心对齐。</summary>
        private static void CenterPanelContent(GameObject instance)
        {
            if (instance.transform is RectTransform rect)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
                rect.anchoredPosition = Vector2.zero;
                rect.localScale = Vector3.one;
            }
        }

        private void BindTasksPanel(OutGameTasksPanelView view)
        {
            if (view == null) return;
            var guest = Visitors.visitors[guestIndex];
            if (view.focusText != null)
                view.focusText.text = $"<color=#E22D76>●  MAIN / {guest.type}</color>\n<size=28>{guest.displayName} · {guest.need}</size>\n<size=17>{guest.hint} 推荐使用「{guest.solution}」，完成后可能留下「{guest.gift}」。</size>";
            var tasks = new[] { "为赫墨制造琴弦窗户", "把米娅的纸条挂上风铃", "检查明日访客预告" };
            for (var i = 0; i < tasks.Length; i++)
            {
                var task = tasks[i];
                if (view.taskLabels != null && i < view.taskLabels.Length && view.taskLabels[i] != null)
                    view.taskLabels[i].text = $"0{i + 2}     {task}                         {(i == 2 ? "未解锁" : "进行中")}";
                if (view.taskButtons != null && i < view.taskButtons.Length && view.taskButtons[i] != null)
                    BindButton(view.taskButtons[i], () => ShowToast("已追踪：" + task));
            }
        }

        private void BuildDevicePanel(Transform content)
        {
            for (var i = 0; i < 4; i++)
            {
                var index = i;
                var room = Codex.rooms[i];
                F.Button(content, "DeviceRoom" + i, room.displayName + $"\n<size=12>{Codex.CountDevicesOfRoom(room.id)} DEVICES</size>",
                    () => { roomIndex = index; selectedDevice = 0; OpenPanel(SystemPanel.Device); },
                    new Vector2(0, 1), new Vector2(0, 1), new Vector2(115, -70 - i * 98), new Vector2(210, 82),
                    roomIndex == i ? F.Wine : new Color(1, 1, 1, .035f), F.White, 19);
            }
            var devices = new List<DeviceDef>();
            Codex.GetDevicesOfRoom(Codex.rooms[roomIndex].id, devices);
            for (var i = 0; i < devices.Count; i++)
            {
                var index = i;
                var device = devices[i];
                F.Button(content, "Device" + i, $"⚙\n<size=13>LV.{device.level} · {(device.owned ? "可使用" : "待修复")}</size>\n{device.displayName}\n<size=14>{device.effect}</size>",
                    () => { selectedDevice = index; OpenPanel(SystemPanel.Device); },
                    new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(-120 + i * 270, -155), new Vector2(245, 270),
                    selectedDevice == i ? new Color(.38f, .08f, .24f, .75f) : new Color(1, 1, 1, .045f), F.White, 21, TextAnchor.MiddleCenter);
            }
            var chosen = devices[Mathf.Clamp(selectedDevice, 0, devices.Count - 1)];
            var recipe = DarkCard(content, "Recipe", new Vector2(230, -230), new Vector2(610, 270), new Color(.18f, .07f, .14f, .82f));
            F.Label(recipe, "RecipeText", $"<size=13>当前设备</size>\n<size=30>{chosen.displayName}</size>\n{chosen.effect}\n\n咖啡豆 ×2     温水 ×1", 20, F.White,
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 35), new Vector2(540, 175), TextAnchor.MiddleLeft);
            var ready = chosen.owned;
            var make = F.Button(recipe, "Make", ready ? "开始制作" : "需要修复", () => ShowToast(chosen.displayName + " 已开始运作"),
                new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 35), new Vector2(280, 58), ready ? F.Wine : F.Hex("49434A"), F.White, 19);
            make.interactable = ready;
        }

        private void BuildJournalPanel(Transform content)
        {
            F.Button(content, "LogTab", "日记", () => { journalAchievements = false; OpenPanel(SystemPanel.Journal); },
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(140, -45), new Vector2(220, 58), journalAchievements ? new Color(1, 1, 1, .04f) : F.Wine, F.White, 20);
            F.Button(content, "AchTab", "成就", () => { journalAchievements = true; OpenPanel(SystemPanel.Journal); },
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(380, -45), new Vector2(220, 58), journalAchievements ? F.Wine : new Color(1, 1, 1, .04f), F.White, 20);
            if (!journalAchievements)
            {
                for (var i = 0; i < Codex.journalEntries.Count; i++)
                {
                    var entry = Codex.journalEntries[i];
                    DarkArticle(content, new Vector2(i % 2 == 0 ? -280 : 300, 90 - i / 2 * 420), entry.dateText, entry.title, entry.body);
                }
            }
            else
            {
                for (var i = 0; i < Codex.achievements.Count; i++)
                {
                    var achievement = Codex.achievements[i];
                    var done = i < 2; // 原型假状态：完成态是运行时数据，成就系统未实现前保持「前两项 ✓」
                    F.Button(content, "JournalAchievement" + i, $"{(done ? "✓" : (i + 1).ToString())}     {achievement.displayName}\n<size=15>          {achievement.note}</size>", null,
                        new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(i % 2 == 0 ? -280 : 300, 150 - i / 2 * 210),
                        new Vector2(520, 170), done ? new Color(.4f, .08f, .25f, .6f) : new Color(1, 1, 1, .035f), F.White, 23, TextAnchor.MiddleLeft);
                }
            }
        }

        private void BuildContactsPanel(Transform content)
        {
            for (var i = 0; i < 4; i++)
            {
                var index = i;
                var guest = Visitors.visitors[i];
                var button = F.Button(content, "Contact" + i, $"{guest.displayName}\n<size=13>{guest.type}                                  {guest.affinity}%</size>",
                    () => { guestIndex = index; OpenPanel(SystemPanel.Contacts); }, new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(170, -85 - i * 125), new Vector2(320, 105), guestIndex == i ? F.Wine : new Color(1, 1, 1, .035f), F.White, 20, TextAnchor.MiddleLeft);
                BuildPortrait(button.transform, guest.portraitPath, new Vector2(50, 0), new Vector2(76, 76), new Vector2(0, .5f), true);
                button.GetComponentInChildren<Text>().rectTransform.offsetMin = new Vector2(105, 6);
            }
            var current = Visitors.visitors[guestIndex];
            var profile = DarkCard(content, "ContactProfile", new Vector2(245, -40), new Vector2(720, 690), new Color(.09f, .045f, .08f, .86f));
            BuildPortrait(profile, current.portraitPath, new Vector2(-230, 160), new Vector2(210, 260), new Vector2(.5f, .5f), false);
            F.Label(profile, "ProfileText", $"<size=14>{current.type} / No. 0{guestIndex + 1}</size>\n<size=40>{current.displayName}</size>\n“{current.hint}”\n\n信赖  <color=#E22D76>━━━━━━━━</color>  {current.affinity}\n\n当前需求     {current.need}\n\n适配家具     {current.solution}\n\n可能留下     {current.gift}",
                19, F.White, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(120, 50), new Vector2(430, 520), TextAnchor.UpperLeft);
            F.Button(profile, "Talk", "与 TA 交谈", () => { ClosePanelImmediate(); ShowDialogue(); },
                new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(130, 52), new Vector2(310, 62), F.Wine, F.White, 20);
        }

        private void BuildArchivePanel(Transform content)
        {
            F.Button(content, "FurnitureTab", "叙事家具", () => { archiveWorld = false; selectedArchive = 0; OpenPanel(SystemPanel.Archive); },
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(130, -45), new Vector2(220, 58), archiveWorld ? new Color(1, 1, 1, .04f) : F.Wine, F.White, 19);
            F.Button(content, "WorldTab", "世界与角色", () => { archiveWorld = true; selectedArchive = 0; OpenPanel(SystemPanel.Archive); },
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(370, -45), new Vector2(220, 58), archiveWorld ? F.Wine : new Color(1, 1, 1, .04f), F.White, 19);
            var items = archiveWorld ? WorldArchives : FurnitureArchives;
            selectedArchive = Mathf.Clamp(selectedArchive, 0, items.Count - 1);
            for (var i = 0; i < items.Count; i++)
            {
                var index = i;
                var item = items[i];
                var card = F.Button(content, "Archive" + i, $"0{i + 1} / {item.type}\n{item.displayName}\n<size=13>{item.owner}</size>",
                    () => { selectedArchive = index; OpenPanel(SystemPanel.Archive); }, new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(135 + (i % 2) * 235, -165 - (i / 2) * 235), new Vector2(215, 215),
                    selectedArchive == i ? new Color(.42f, .08f, .28f, .72f) : new Color(1, 1, 1, .04f), F.White, 17, TextAnchor.LowerCenter);
                var art = F.Texture(card.transform, "Art", item.imagePath, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -70), new Vector2(180, 110));
                art.raycastTarget = false;
            }
            var selected = items[selectedArchive];
            var detail = DarkCard(content, "ArchiveDetail", new Vector2(300, -20), new Vector2(650, 730), new Color(.09f, .04f, .075f, .9f));
            F.Texture(detail, "Preview", selected.imagePath, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -180), new Vector2(590, 300));
            F.Label(detail, "DetailText", $"<size=13>{selected.type} · {selected.owner}</size>\n<size=32>{selected.displayName}</size>\n{(selected.id == "map" ? $"角色移动时，以当前位置为中心永久揭开迷雾。当前探索半径 {fogRadius} 米。" : selected.note)}",
                19, F.White, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, -120), new Vector2(560, 230), TextAnchor.UpperLeft);
            if (!archiveWorld)
            {
                F.Button(detail, "Place", "放入房间", () => { placedFurniture = selected.id; ShowToast(selected.displayName + " 已加入访客房间快捷栏"); },
                    new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 45), new Vector2(300, 62), F.Wine, F.White, 20);
            }
            else if (selected.id == "map")
            {
                for (var i = 0; i < 4; i++)
                {
                    var radius = (i + 1) * 5;
                    F.Button(detail, "Radius" + radius, radius + "m", () => { fogRadius = radius; OpenPanel(SystemPanel.Archive); },
                        new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(-180 + i * 120, 45), new Vector2(105, 55),
                        fogRadius == radius ? F.Wine : new Color(1, 1, 1, .04f), F.White, 17);
                }
            }
            else
            {
                F.Button(detail, "Track", "追踪这份资料", () => ShowToast(selected.displayName + " 已设为追踪资料"),
                    new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 45), new Vector2(300, 62), new Color(1, 1, 1, .04f), F.White, 19);
            }
        }

        private void BindDevicePanel(OutGameDevicePanelView view)
        {
            if (view == null) return;
            for (var i = 0; i < 4; i++)
            {
                var index = i;
                var room = Codex.rooms[i];
                if (view.roomLabels != null && i < view.roomLabels.Length && view.roomLabels[i] != null)
                    view.roomLabels[i].text = room.displayName + $"\n<size=12>{Codex.CountDevicesOfRoom(room.id)} DEVICES</size>";
                if (view.roomBackgrounds != null && i < view.roomBackgrounds.Length && view.roomBackgrounds[i] != null)
                    view.roomBackgrounds[i].color = roomIndex == i ? F.Wine : new Color(1, 1, 1, .035f);
                if (view.roomButtons != null && i < view.roomButtons.Length && view.roomButtons[i] != null)
                    BindButton(view.roomButtons[i], () => { roomIndex = index; selectedDevice = 0; OpenPanel(SystemPanel.Device); });
            }
            var devices = new List<DeviceDef>();
            Codex.GetDevicesOfRoom(Codex.rooms[roomIndex].id, devices);
            if (view.deviceCardsRoot != null)
            {
                for (var i = 0; i < devices.Count; i++)
                {
                    var index = i;
                    var device = devices[i];
                    F.Button(view.deviceCardsRoot, "Device" + i,
                        $"⚙\n<size=13>LV.{device.level} · {(device.owned ? "可使用" : "待修复")}</size>\n{device.displayName}\n<size=14>{device.effect}</size>",
                        () => { selectedDevice = index; OpenPanel(SystemPanel.Device); },
                        new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(-120 + i * 270, -155), new Vector2(245, 270),
                        selectedDevice == i ? new Color(.38f, .08f, .24f, .75f) : new Color(1, 1, 1, .045f), F.White, 21, TextAnchor.MiddleCenter);
                }
            }
            var chosen = devices[Mathf.Clamp(selectedDevice, 0, devices.Count - 1)];
            if (view.recipeText != null)
                view.recipeText.text = $"<size=13>当前设备</size>\n<size=30>{chosen.displayName}</size>\n{chosen.effect}\n\n咖啡豆 ×2     温水 ×1";
            var ready = chosen.owned;
            if (view.makeButton != null)
            {
                if (view.makeLabel != null) view.makeLabel.text = ready ? "开始制作" : "需要修复";
                var background = view.makeButton.targetGraphic as Image;
                if (background != null) background.color = ready ? F.Wine : F.Hex("49434A");
                BindButton(view.makeButton, () => ShowToast(chosen.displayName + " 已开始运作"));
                view.makeButton.interactable = ready;
            }
        }

        private void BindJournalPanel(OutGameJournalPanelView view)
        {
            if (view == null) return;
            for (var i = 0; i < 2; i++)
            {
                var toAchievements = i == 1;
                if (view.tabBackgrounds != null && i < view.tabBackgrounds.Length && view.tabBackgrounds[i] != null)
                    view.tabBackgrounds[i].color = journalAchievements == toAchievements ? F.Wine : new Color(1, 1, 1, .04f);
                if (view.tabButtons != null && i < view.tabButtons.Length && view.tabButtons[i] != null)
                    BindButton(view.tabButtons[i], () => { journalAchievements = toAchievements; OpenPanel(SystemPanel.Journal); });
            }
            if (view.bodyRoot == null) return;
            if (!journalAchievements)
            {
                for (var i = 0; i < Codex.journalEntries.Count; i++)
                {
                    var entry = Codex.journalEntries[i];
                    DarkArticle(view.bodyRoot, new Vector2(i % 2 == 0 ? -280 : 300, 90 - i / 2 * 420), entry.dateText, entry.title, entry.body);
                }
            }
            else
            {
                for (var i = 0; i < Codex.achievements.Count; i++)
                {
                    var achievement = Codex.achievements[i];
                    var done = i < 2; // 原型假状态：完成态是运行时数据，成就系统未实现前保持「前两项 ✓」
                    F.Button(view.bodyRoot, "JournalAchievement" + i,
                        $"{(done ? "✓" : (i + 1).ToString())}     {achievement.displayName}\n<size=15>          {achievement.note}</size>", null,
                        new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(i % 2 == 0 ? -280 : 300, 150 - i / 2 * 210),
                        new Vector2(520, 170), done ? new Color(.4f, .08f, .25f, .6f) : new Color(1, 1, 1, .035f), F.White, 23, TextAnchor.MiddleLeft);
                }
            }
        }

        private void BindArchivePanel(OutGameArchivePanelView view)
        {
            if (view == null) return;
            for (var i = 0; i < 2; i++)
            {
                var toWorld = i == 1;
                if (view.tabBackgrounds != null && i < view.tabBackgrounds.Length && view.tabBackgrounds[i] != null)
                    view.tabBackgrounds[i].color = archiveWorld == toWorld ? F.Wine : new Color(1, 1, 1, .04f);
                if (view.tabButtons != null && i < view.tabButtons.Length && view.tabButtons[i] != null)
                    BindButton(view.tabButtons[i], () => { archiveWorld = toWorld; selectedArchive = 0; OpenPanel(SystemPanel.Archive); });
            }
            var items = archiveWorld ? WorldArchives : FurnitureArchives;
            selectedArchive = Mathf.Clamp(selectedArchive, 0, items.Count - 1);
            if (view.gridRoot != null)
            {
                for (var i = 0; i < items.Count; i++)
                {
                    var index = i;
                    var item = items[i];
                    var card = F.Button(view.gridRoot, "Archive" + i, $"0{i + 1} / {item.type}\n{item.displayName}\n<size=13>{item.owner}</size>",
                        () => { selectedArchive = index; OpenPanel(SystemPanel.Archive); }, new Vector2(0, 1), new Vector2(0, 1),
                        new Vector2(135 + i % 2 * 235, -165 - i / 2 * 235), new Vector2(215, 215),
                        selectedArchive == i ? new Color(.42f, .08f, .28f, .72f) : new Color(1, 1, 1, .04f), F.White, 17, TextAnchor.LowerCenter);
                    var art = F.Texture(card.transform, "Art", item.imagePath, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -70), new Vector2(180, 110));
                    art.raycastTarget = false;
                }
            }
            var selected = items[selectedArchive];
            if (view.detailPreview != null) view.detailPreview.texture = Resources.Load<Texture2D>(selected.imagePath);
            if (view.detailText != null)
                view.detailText.text = $"<size=13>{selected.type} · {selected.owner}</size>\n<size=32>{selected.displayName}</size>\n{(selected.id == "map" ? $"角色移动时，以当前位置为中心永久揭开迷雾。当前探索半径 {fogRadius} 米。" : selected.note)}";
            if (view.actionRoot == null) return;
            if (!archiveWorld)
            {
                F.Button(view.actionRoot, "Place", "放入房间", () => { placedFurniture = selected.id; ShowToast(selected.displayName + " 已加入访客房间快捷栏"); },
                    new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 45), new Vector2(300, 62), F.Wine, F.White, 20);
            }
            else if (selected.id == "map")
            {
                for (var i = 0; i < 4; i++)
                {
                    var radius = (i + 1) * 5;
                    F.Button(view.actionRoot, "Radius" + radius, radius + "m", () => { fogRadius = radius; OpenPanel(SystemPanel.Archive); },
                        new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(-180 + i * 120, 45), new Vector2(105, 55),
                        fogRadius == radius ? F.Wine : new Color(1, 1, 1, .04f), F.White, 17);
                }
            }
            else
            {
                F.Button(view.actionRoot, "Track", "追踪这份资料", () => ShowToast(selected.displayName + " 已设为追踪资料"),
                    new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 45), new Vector2(300, 62), new Color(1, 1, 1, .04f), F.White, 19);
            }
        }

        private void BuildCalendarPanel(Transform content)
        {
            var prefab = Resources.Load<GameObject>(OutGamePrefabResourcePaths.CalendarPanel);
            if (prefab != null)
            {
                var instance = Instantiate(prefab, content, false);
                instance.name = "CalendarContent";
                CenterPanelContent(instance);
                var view = instance.GetComponent<OutGameCalendarPanelView>();
                if (view != null)
                {
                    BindCalendarPanel(view);
                    ApplyFallbackFont(instance.transform);
                    return;
                }
                Destroy(instance);
            }
            Debug.LogWarning("[OutGameUI] Prefab 缺失，暂时回退代码布局：" + OutGamePrefabResourcePaths.CalendarPanel);
            var now = DateTime.Now;
            var phase = OutGameUIData.CurrentPhase;
            var date = DarkCard(content, "BigDate", new Vector2(-385, 195), new Vector2(340, 330), new Color(.34f, .07f, .22f, .65f));
            F.Label(date, "DateText", $"{now:yyyy / MMMM}\n<size=100>{now:dd}</size>\n{now:dddd} · {HousePhaseText.Names[phase]}\n<size=28>{now:HH:mm}</size>",
                20, F.White, TextAnchor.MiddleCenter, FontStyle.Bold);
            var first = new DateTime(now.Year, now.Month, 1);
            var offset = ((int)first.DayOfWeek + 6) % 7;
            var days = DateTime.DaysInMonth(now.Year, now.Month);
            for (var day = 1; day <= days; day++)
            {
                var cell = offset + day - 1;
                var col = cell % 7;
                var row = cell / 7;
                F.Button(content, "Day" + day, day.ToString(), null, new Vector2(.5f, 1), new Vector2(.5f, 1),
                    new Vector2(-180 + col * 64, -90 - row * 64), new Vector2(58, 54), day == now.Day ? F.Wine : new Color(1, 1, 1, .035f), F.White, 16);
            }
            var schedule = DarkCard(content, "Schedule", new Vector2(405, 20), new Vector2(330, 690), new Color(.08f, .04f, .075f, .86f));
            F.Label(schedule, "ScheduleTitle", "现实时间阶段", 24, F.White, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -40), new Vector2(270, 40), TextAnchor.MiddleCenter, FontStyle.Bold);
            for (var i = 0; i < 6; i++)
            {
                F.Button(schedule, "Phase" + i, $"{HousePhaseText.Names[i]}   <size=13>{HousePhaseText.Ranges[i]}</size>       {(i == 5 ? "休息" : "可服务")}",
                    null, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -105 - i * 75), new Vector2(290, 62),
                    phase == i ? F.Wine : new Color(1, 1, 1, .035f), F.White, 16, TextAnchor.MiddleLeft);
            }
            F.Button(schedule, "Sync", "同步现实时间", () => { ShowToast("已同步现实时间 · " + DateTime.Now.ToString("HH:mm")); OpenPanel(SystemPanel.Calendar); },
                new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 68), new Vector2(260, 56), F.Wine, F.White, 18);
        }

        private void BindCalendarPanel(OutGameCalendarPanelView view)
        {
            if (view == null) return;
            var now = DateTime.Now;
            var phase = OutGameUIData.CurrentPhase;
            if (view.dateText != null)
                view.dateText.text = $"{now:yyyy / MMMM}\n<size=100>{now:dd}</size>\n{now:dddd} · {HousePhaseText.Names[phase]}\n<size=28>{now:HH:mm}</size>";
            var firstOfMonth = new DateTime(now.Year, now.Month, 1);
            var weekOffset = ((int)firstOfMonth.DayOfWeek + 6) % 7;
            var daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
            var hasBakedCells = view.dayCells != null && view.dayCells.Length > 0 && view.dayCells[0] != null;
            if (hasBakedCells)
            {
                // Prefab 烘焙槽位：只设置数字、显隐与今日高亮
                for (var i = 0; i < view.dayCells.Length; i++)
                {
                    if (view.dayCells[i] == null) continue;
                    var day = i - weekOffset + 1;
                    var visible = day >= 1 && day <= daysInMonth;
                    view.dayCells[i].gameObject.SetActive(visible);
                    if (!visible) continue;
                    if (view.dayCellLabels != null && i < view.dayCellLabels.Length && view.dayCellLabels[i] != null)
                        view.dayCellLabels[i].text = day.ToString();
                    if (view.dayCellBackgrounds != null && i < view.dayCellBackgrounds.Length && view.dayCellBackgrounds[i] != null)
                        view.dayCellBackgrounds[i].color = day == now.Day ? F.Wine : new Color(1, 1, 1, .035f);
                }
            }
            else if (view.dayGridRoot != null)
            {
                // 旧版 Prefab 兜底：运行时生成
                for (var day = 1; day <= daysInMonth; day++)
                {
                    var cell = weekOffset + day - 1;
                    F.Button(view.dayGridRoot, "Day" + day, day.ToString(), null, new Vector2(.5f, 1), new Vector2(.5f, 1),
                        new Vector2(-180 + cell % 7 * 64, -90 - cell / 7 * 64), new Vector2(58, 54),
                        day == now.Day ? F.Wine : new Color(1, 1, 1, .035f), F.White, 16);
                }
            }
            for (var i = 0; i < 6; i++)
            {
                if (view.phaseLabels != null && i < view.phaseLabels.Length && view.phaseLabels[i] != null)
                    view.phaseLabels[i].text = $"{HousePhaseText.Names[i]}   <size=13>{HousePhaseText.Ranges[i]}</size>       {(i == 5 ? "休息" : "可服务")}";
                if (view.phaseBackgrounds != null && i < view.phaseBackgrounds.Length && view.phaseBackgrounds[i] != null)
                    view.phaseBackgrounds[i].color = phase == i ? F.Wine : new Color(1, 1, 1, .035f);
            }
            if (view.syncButton != null)
                BindButton(view.syncButton, () =>
                {
                    ShowToast("已同步现实时间 · " + DateTime.Now.ToString("HH:mm"));
                    OpenPanel(SystemPanel.Calendar);
                });
        }

        private void BuildInventoryPanel(Transform content)
        {
            var filters = new[] { "全部", "材料", "线索", "消耗品" };
            for (var i = 0; i < filters.Length; i++)
                F.Button(content, "Filter" + i, filters[i], null, new Vector2(0, 1), new Vector2(0, 1), new Vector2(95 + i * 160, -45), new Vector2(145, 54), i == 0 ? F.Wine : new Color(1, 1, 1, .035f), F.White, 17);
            var items = new[] { "深烘咖啡豆", "空白磁带", "生锈的钥匙", "蓝色干花", "记忆碎片", "旧书页", "蜂蜜方糖", "损坏齿轮", "夜光粉末" };
            for (var i = 0; i < items.Length; i++)
            {
                var item = items[i];
                F.Button(content, "Item" + i, $"◆\n{item}\n<size=13>×{(i * 3) % 8 + 1}</size>", () => ShowToast(item + " · " + (Array.IndexOf(items, item) % 3 == 0 ? "可用于访客委托" : "House 收藏物")),
                    new Vector2(0, 1), new Vector2(0, 1), new Vector2(110 + (i % 3) * 220, -165 - (i / 3) * 190), new Vector2(200, 170),
                    new Color(.09f + i % 4 * .02f, .04f, .08f + i % 4 * .04f, .82f), F.White, 18);
            }
            var storage = DarkCard(content, "Storage", new Vector2(430, 40), new Vector2(310, 610), new Color(.3f, .07f, .2f, .45f));
            F.Label(storage, "StorageText", "<size=13>STORAGE</size>\n<size=48>12 / 40</size>\n\n部分物品可在设备中合成，也可能改变访客对话。\n\n快捷键：I", 20, F.White, TextAnchor.MiddleCenter, FontStyle.Bold);
        }

        private void BuildSettingsPanel(Transform content)
        {
            var save = DarkCard(content, "Save", new Vector2(-290, 185), new Vector2(520, 300), new Color(.26f, .06f, .18f, .52f));
            F.Label(save, "Text", $"<size=13>SAVE DATA</size>\n<size=36>Slot 0{activeSlot}</size>\nHouse LV.03 · WEEK 01 · {Visitor.CountServed()}/4 委托推进", 20, F.White,
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 45), new Vector2(440, 160), TextAnchor.MiddleCenter);
            F.Button(save, "SaveButton", "保存进度", SaveCurrent, new Vector2(0, 0), new Vector2(0, 0), new Vector2(145, 48), new Vector2(210, 58), F.Wine, F.White, 18);
            F.Button(save, "LoadButton", "读取存档", LoadCurrent, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-145, 48), new Vector2(210, 58), new Color(1, 1, 1, .045f), F.White, 18);
            var display = DarkCard(content, "Display", new Vector2(290, 185), new Vector2(520, 300), new Color(.08f, .04f, .075f, .86f));
            F.Label(display, "Text", $"<size=13>DISPLAY</size>\n<size=28>显示设置</size>\n\n视窗模式            {windowMode}\n分辨率                1920 × 1080", 19, F.White,
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 25), new Vector2(440, 210), TextAnchor.MiddleLeft);
            F.Button(display, "Mode", "切换模式", CycleWindowMode, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 42), new Vector2(240, 52), F.Wine, F.White, 17);
            var audio = DarkCard(content, "Audio", new Vector2(0, -205), new Vector2(1100, 350), new Color(.08f, .04f, .075f, .86f));
            F.Label(audio, "AudioTitle", "<size=13>AUDIO</size>\n音频", 28, F.White, new Vector2(0, 1), new Vector2(0, 1), new Vector2(120, -55), new Vector2(180, 70), TextAnchor.MiddleLeft, FontStyle.Bold);
            BuildSlider(audio, "BGM", bgm, new Vector2(0, 30), value => { bgm = Mathf.RoundToInt(value); });
            BuildSlider(audio, "SFX", sfx, new Vector2(0, -70), value => { sfx = Mathf.RoundToInt(value); });
            F.Button(audio, "Apply", "应用设置", () => ShowToast("设置已应用"), new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 42), new Vector2(280, 58), F.Wine, F.White, 18);
        }

        private void BuildProfilePanel(Transform content)
        {
            var portrait = DarkCard(content, "Keeper", new Vector2(-325, 0), new Vector2(430, 730), new Color(.28f, .06f, .19f, .58f));
            F.Label(portrait, "Y", "弈", 120, new Color(1, 1, 1, .82f), TextAnchor.MiddleCenter, FontStyle.Bold);
            var profile = DarkCard(content, "Profile", new Vector2(250, 0), new Vector2(650, 730), new Color(.08f, .04f, .075f, .86f));
            F.Label(profile, "Text", "<size=14>HOUSE KEEPER / 001</size>\n<size=52>弈</size>\n\n记忆修复师。每一次帮助访客，都会让他们看起来更像“人”，也让自己离答案更近一点。\n\n状态               稳定\n病情               雾化 18%\nHouse 等级     LV. 03\n服务次数         12\n\n<color=#E22D76> 细致      夜行      共感 </color>",
                22, F.White, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(550, 620), TextAnchor.UpperLeft);
        }

        private void BuildMarketPanel(Transform content)
        {
            var wallet = DarkCard(content, "Wallet", new Vector2(-370, 330), new Vector2(400, 130), new Color(.35f, .07f, .22f, .58f));
            F.Label(wallet, "WalletText",
                $"<size=13>流通数值</size>\n<size=28><color=#E3A869>◈ {Economy.Data.Currency:N0}</color></size>\n<color=#74D8D1>声望 {Economy.Data.Reputation}</color>    <color=#E22D76>装饰分 {Economy.DecorationScore}</color>",
                18, F.White, TextAnchor.MiddleCenter, FontStyle.Bold);
            F.Label(content, "MarketNote",
                "商城 · 装饰品货架：声望解禁货架（未解禁呈「？」），货币购买；已购家具会出现在「家具摆放」的收纳栏。设备货架待投放方式确定后开放。",
                16, new Color(1, 1, 1, .66f), new Vector2(.5f, 1), new Vector2(.5f, 1),
                new Vector2(210, -60), new Vector2(700, 70), TextAnchor.MiddleLeft);

            var table = Resources.Load<FurnitureTable>("OutGameUI/FurnitureTable");
            if (table == null || table.entries.Count == 0)
            {
                F.Label(content, "Missing", "家具配置表缺失：请执行菜单 MasterHouse → 家具系统 → 创建配置表",
                    20, F.White, TextAnchor.MiddleCenter);
                return;
            }
            for (var i = 0; i < table.entries.Count; i++)
            {
                var entry = table.entries[i];
                if (entry == null) continue;
                var position = new Vector2(-472 + i % 5 * 236, 130 - i / 5 * 235);
                BuildMarketCard(content, entry, position);
            }
        }

        private void BuildMarketCard(Transform content, FurnitureEntry entry, Vector2 position)
        {
            var owned = Economy.IsFurnitureOwned(entry.id);
            var revealed = Economy.IsFurnitureRevealed(entry);
            string caption;
            Color background;
            if (!revealed)
            {
                // 文档：未解禁 Item 在商城/图鉴中呈「？」状态
                caption = $"<size=42>？</size>\n<size=14>声望 {entry.unlockReputation} 解禁</size>";
                background = new Color(.06f, .05f, .08f, .85f);
            }
            else if (owned)
            {
                caption = $"\n\n\n<size=17>{entry.displayName}</size>\n<size=14><color=#9AE2B8>已拥有</color></size>";
                background = new Color(.05f, .07f, .06f, .8f);
            }
            else
            {
                caption = $"\n\n\n<size=17>{entry.displayName}</size>\n<color=#E3A869>◈ {entry.price}</color>";
                background = new Color(.1f, .04f, .09f, .86f);
            }
            var button = F.Button(content, "Market_" + entry.id, caption, () =>
                {
                    if (!Economy.IsFurnitureRevealed(entry))
                    {
                        ShowToast($"声望达到 {entry.unlockReputation} 后解禁（当前 {Economy.Data.Reputation}）");
                        return;
                    }
                    if (Economy.IsFurnitureOwned(entry.id))
                    {
                        ShowToast($"「{entry.displayName}」已拥有，可在「家具摆放」中使用");
                        return;
                    }
                    if (Economy.TryPurchaseFurniture(entry) == FurniturePurchaseResult.Success)
                    {
                        AutoSave();
                        ShowToast($"已购入「{entry.displayName}」 · ◈ -{entry.price}");
                        RefreshMarketPanel(content);
                    }
                    else
                    {
                        ShowToast("货币不足：完成客人服务可以获得 ◈");
                    }
                },
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), position, new Vector2(220, 215), background, F.White, 18);
            if (revealed && entry.sprite != null)
            {
                var thumb = F.Rect(button.transform, "Thumb", new Vector2(.5f, 1), new Vector2(.5f, 1),
                    new Vector2(0, -60), new Vector2(130, 95));
                var image = thumb.gameObject.AddComponent<Image>();
                image.sprite = entry.sprite;
                image.preserveAspect = true;
                image.raycastTarget = false;
                if (owned) image.color = new Color(1, 1, 1, .45f);
            }
        }

        private void RefreshMarketPanel(Transform content)
        {
            for (var i = content.childCount - 1; i >= 0; i--)
                Destroy(content.GetChild(i).gameObject);
            BuildMarketPanel(content);
        }

        #endregion

        #region 通用组件与存档

        private void ShowToast(string message)
        {
            if (string.IsNullOrEmpty(message) || viewRoot == null) return;
            if (toastRoot != null) Destroy(toastRoot.gameObject);
            toastRoot = (RectTransform)F.Panel(viewRoot, "Toast", new Vector2(.5f, 1), new Vector2(.5f, 1),
                new Vector2(0, -150), new Vector2(470, 58), new Color(.12f, .035f, .1f, .94f)).transform;
            toastRoot.SetAsLastSibling();
            F.Label(toastRoot, "ToastText", "●  " + message, 16, F.White, TextAnchor.MiddleCenter, FontStyle.Bold);
            toastGroup = F.Group(toastRoot.gameObject, 0);
            toastRoot.anchoredPosition += new Vector2(0, -18);
            toastTween?.Kill();
            toastTween = DOTween.Sequence().SetTarget(this).SetUpdate(true)
                .Append(toastGroup.DOFade(1, .18f))
                .Join(toastRoot.DOAnchorPosY(-132, .28f).SetEase(Ease.OutCubic))
                .AppendInterval(3f)
                .Append(toastGroup.DOFade(0, .25f))
                .OnComplete(() => { if (toastRoot != null) Destroy(toastRoot.gameObject); toastRoot = null; });
        }

        private static Transform DarkCard(Transform parent, string name, Vector2 position, Vector2 size, Color color)
        {
            var image = F.Panel(parent, name, new Vector2(.5f, .5f), new Vector2(.5f, .5f), position, size, color);
            F.Outline(image.gameObject, new Color(.7f, .2f, .45f, .28f), new Vector2(1, -1));
            return image.transform;
        }

        private static Transform PaperSection(Transform parent, Vector2 position, Vector2 size, string eyebrow, string title)
        {
            var section = F.Panel(parent, title, new Vector2(0, 1), new Vector2(0, 1), position, size, new Color(1, .97f, .9f, .18f));
            F.Outline(section.gameObject, new Color(.4f, .25f, .27f, .2f), new Vector2(1, -1));
            F.Label(section.transform, "Header", $"<size=14>{eyebrow}</size>\n{title}", 28, F.Hex("433234"),
                new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -62), new Vector2(size.x - 70, 100), TextAnchor.MiddleLeft, FontStyle.Bold);
            return section.transform;
        }

        private static void PaperArticle(Transform parent, Vector2 position, string date, string title, string body)
        {
            var article = F.Panel(parent, title, new Vector2(0, 1), new Vector2(0, 1), position, new Vector2(700, 320), new Color(1, .98f, .92f, .2f));
            F.Label(article.transform, "Text", $"<size=14>{date}</size>\n<size=30>{title}</size>\n\n{body}", 20, F.Hex("433234"),
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(620, 250), TextAnchor.UpperLeft);
        }

        private static void DarkArticle(Transform parent, Vector2 position, string date, string title, string body)
        {
            var article = DarkCard(parent, title, position, new Vector2(530, 540), new Color(.08f, .04f, .075f, .86f));
            F.Label(article, "Text", $"<color=#E22D76><size=14>{date}</size></color>\n<size=29>{title}</size>\n\n{body}", 20, F.White,
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(450, 450), TextAnchor.UpperLeft);
        }

        private static void BuildPortrait(Transform parent, string resource, Vector2 position, Vector2 size, Vector2 anchor, bool border)
        {
            if (border)
            {
                F.Panel(parent, "PortraitBorder", anchor, anchor, position, size + new Vector2(8, 8), new Color(.65f, .2f, .4f, .8f));
            }
            var image = F.Texture(parent, "Portrait", resource, anchor, anchor, position, size);
            image.raycastTarget = false;
        }

        private static void BuildSlider(Transform parent, string label, int value, Vector2 position, Action<float> onChanged)
        {
            F.Label(parent, label + "Label", label + "                                      " + value, 20, F.White,
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), position + new Vector2(0, 35), new Vector2(820, 40), TextAnchor.MiddleLeft);
            var bg = F.Panel(parent, label + "Slider", new Vector2(.5f, .5f), new Vector2(.5f, .5f), position, new Vector2(820, 18), new Color(1, 1, 1, .12f));
            var fillArea = F.Stretch(bg.transform, "Fill Area");
            var fill = F.StretchPanel(fillArea, "Fill", F.Rose);
            var handleArea = F.Stretch(bg.transform, "Handle Slide Area");
            var handle = F.Panel(handleArea, "Handle", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(28, 28), F.White);
            var slider = bg.gameObject.AddComponent<Slider>();
            slider.minValue = 0;
            slider.maxValue = 100;
            slider.value = value;
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.onValueChanged.AddListener(v => onChanged(v));
        }

        private static string RoomIcon(int index) => index switch { 0 => "▰", 1 => "▱", 2 => "▦", _ => "▥" };

        private int ProgressForGuest(int index) => Visitor.Data.States[index].Served ? 100 : index == 0 ? 35 : 20;

        private int RemainingGuests()
        {
            return Visitor.CountRemaining();
        }

        private static (string eyebrow, string title, string mark) PanelMeta(SystemPanel panel)
        {
            return panel switch
            {
                SystemPanel.Tasks => ("TODAY / 03", "今日委托", "任"),
                SystemPanel.Device => ("HOUSE INDEX", "设备图鉴", "器"),
                SystemPanel.Journal => ("MEMORY LOG", "日记与成就", "记"),
                SystemPanel.Contacts => ("VISITOR FILE", "访客通讯录", "录"),
                SystemPanel.Archive => ("HOUSE ARCHIVE", "叙事资源档案", "集"),
                SystemPanel.Calendar => ("REAL TIME", "日程与时间", "历"),
                SystemPanel.Inventory => ("STORAGE / 12", "House 仓库", "仓"),
                SystemPanel.Settings => ("SYSTEM", "设置与存档", "设"),
                SystemPanel.Profile => ("RESIDENT 001", "主角信息", "我"),
                SystemPanel.Market => ("NIGHT MARKET", "经济与商城", "店"),
                _ => ("", "", "")
            };
        }

        private static string RoomName(string id)
        {
            foreach (var room in Codex.rooms) if (room.id == id) return room.displayName;
            return "起居室";
        }

        private bool HasAnySave()
        {
            for (var i = 1; i <= 3; i++) if (PlayerPrefs.HasKey(SavePrefix + i)) return true;
            return false;
        }

        private static OutGameSaveData ReadSave(int slot)
        {
            var raw = PlayerPrefs.GetString(SavePrefix + slot, "");
            if (string.IsNullOrWhiteSpace(raw)) return null;
            try { return JsonUtility.FromJson<OutGameSaveData>(raw); }
            catch { return null; }
        }

        private void ApplySave(OutGameSaveData data)
        {
            if (data == null) return;
            roomIndex = 0;
            for (var i = 0; i < Codex.rooms.Count; i++) if (Codex.rooms[i].id == data.room) roomIndex = i;
            // v3 起存档包含游戏时钟与访客到访状态；旧档回落到第 1 天早晨、访客未到访（数组传 null = 全 false）
            Visitor.RestoreFromArrays(data.served,
                data.version >= 2 ? data.refused : null,
                data.version >= 3 ? data.guestArrived : null);
            if (data.version >= 3) Clock.RestoreFromMinutes(data.gameDay, data.gameMinute);
            else Clock.ResetNew();
            bgm = data.bgm;
            sfx = data.sfx;
            windowMode = string.IsNullOrEmpty(data.windowMode) ? "无边框" : data.windowMode;
            // v2 起存档包含流通数值与家具布局；旧档回落到配置表默认值，避免带入上一局的会话状态
            if (data.version >= 2)
            {
                Economy.Restore(data.economy);
                FurnitureRoomController.RestoreSessionPlacements(data.hasFurnitureLayout ? data.furniturePlacements : null);
                if (data.hasFurnitureLayout) FurnitureSceneComposer.RequestBake(_ => { ApplySceneArt(); BuildFurnitureHotspots(); });
                else FurnitureSceneComposer.ClearBaked();
            }
            else
            {
                Economy.ResetToDefaults();
                FurnitureRoomController.ResetSession();
                FurnitureSceneComposer.ClearBaked();
            }
            UpdateEconomyHud();
        }

        private void ResetProgress()
        {
            roomIndex = 0;
            guestIndex = 0;
            selectedDevice = 0;
            Visitor.ResetNew();
            Clock.ResetNew();
            // 新游戏必须重置会话级状态，避免上一局的货币/声望/家具污染新档
            Economy.ResetToDefaults();
            FurnitureRoomController.ResetSession();
            FurnitureSceneComposer.ClearBaked();
            UpdateEconomyHud();
        }

        private void SaveCurrent()
        {
            SaveCurrent(false);
        }

        private void SaveCurrent(bool silent)
        {
            var placements = FurnitureRoomController.CaptureSessionPlacements();
            var data = new OutGameSaveData
            {
                version = 3,
                slot = activeSlot,
                room = Codex.rooms[roomIndex].id,
                served = Visitor.CaptureServed(),
                refused = Visitor.CaptureRefused(),
                guestArrived = Visitor.CaptureArrived(),
                gameDay = Clock.Data.Day,
                gameMinute = Clock.Data.MinuteOfDayF,
                bgm = bgm,
                sfx = sfx,
                windowMode = windowMode,
                savedAt = DateTime.Now.ToString("O", CultureInfo.InvariantCulture),
                economy = Economy.Capture(),
                hasFurnitureLayout = placements != null,
                furniturePlacements = placements ?? new System.Collections.Generic.List<FurniturePlacementConfig>(),
            };
            PlayerPrefs.SetString(SavePrefix + activeSlot, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
            if (!silent) ShowToast($"进度已保存到本机 · Slot 0{activeSlot}");
        }

        private void LoadCurrent()
        {
            var data = ReadSave(activeSlot);
            if (data == null) { ShowToast($"Slot 0{activeSlot} 还没有存档"); return; }
            ApplySave(data);
            ClosePanelImmediate();
            ShowHub($"Slot 0{activeSlot} 已读取 · 欢迎回来");
        }

        private void CycleWindowMode()
        {
            windowMode = windowMode == "无边框" ? "全屏" : windowMode == "全屏" ? "窗口" : "无边框";
            OpenPanel(SystemPanel.Settings);
        }

        private static int CountServed(bool[] values)
        {
            if (values == null) return 0;
            var count = 0;
            foreach (var value in values) if (value) count++;
            return count;
        }

        private static string FormatSaveTime(string value)
        {
            return DateTime.TryParse(value, null, DateTimeStyles.RoundtripKind, out var time)
                ? time.ToString("MM/dd HH:mm") : "旧存档";
        }

        private readonly struct MenuItem
        {
            public readonly string cn;
            public readonly string en;
            public readonly Action action;
            public readonly bool enabled;
            public MenuItem(string cn, string en, Action action, bool enabled)
            {
                this.cn = cn; this.en = en; this.action = action; this.enabled = enabled;
            }
        }

        private readonly struct DockItem
        {
            public readonly string icon;
            public readonly string label;
            public readonly SystemPanel panel;
            public DockItem(string icon, string label, SystemPanel panel)
            {
                this.icon = icon; this.label = label; this.panel = panel;
            }
        }

        #endregion
    }
}

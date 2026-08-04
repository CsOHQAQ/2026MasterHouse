#if UNITY_EDITOR
using System.IO;
using MasterPotion;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 只在 Prefab 缺失时创建初始版本。自动入口绝不会覆盖美术/策划已经手调的 Prefab。
/// 如确实需要恢复默认布局，必须从 Tools 菜单显式确认重建。
/// </summary>
[InitializeOnLoad]
public static class OutGameUIPrefabGenerator
{
    public const string Folder = "Assets/Resources/OutGameUI/Prefabs";
    private const string TitlePath = Folder + "/TitlePage.prefab";
    private const string PaperPath = Folder + "/PaperPage.prefab";
    private const string SavePagePath = Folder + "/SavePage.prefab";
    private const string GalleryPagePath = Folder + "/GalleryPage.prefab";
    private const string SettingsPagePath = Folder + "/SettingsPage.prefab";
    private const string ExitPagePath = Folder + "/ExitPage.prefab";
    private const string SaveSlotPath = Folder + "/SaveSlot.prefab";
    private const string HubPath = Folder + "/HouseHubPage.prefab";
    private const string PanelPath = Folder + "/SystemPanel.prefab";

    static OutGameUIPrefabGenerator()
    {
        EditorApplication.delayCall += EnsureMissingPrefabs;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
            EditorApplication.delayCall += EnsureMissingPrefabs;
    }

    [MenuItem("Tools/MasterPotion/OutGame UI/Select Prefab Folder")]
    private static void SelectFolder()
    {
        EnsureMissingPrefabs();
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<Object>(Folder);
    }

    /// <summary>供 CI 或隔离 Unity 工程首次生成使用；同样只补缺失资产。</summary>
    public static void GenerateMissingFromBatch()
    {
        EnsureMissingPrefabs();
    }

    [MenuItem("Tools/MasterPotion/OutGame UI/Rebuild Default Prefabs...")]
    private static void RebuildAll()
    {
        if (!EditorUtility.DisplayDialog("重建局外 UI Prefab",
                "这会覆盖 Prefabs 文件夹内的手动布局修改。确定继续吗？", "覆盖重建", "取消")) return;
        EnsureFolder();
        BuildTitle(TitlePath);
        BuildSaveSlot(SaveSlotPath);
        BuildPaper(PaperPath);
        BuildSavePage(SavePagePath);
        BuildGalleryPage(GalleryPagePath);
        BuildSettingsPage(SettingsPagePath);
        BuildExitPage(ExitPagePath);
        BuildHub(HubPath);
        BuildSystemPanel(PanelPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[OutGameUI] 默认 Prefab 已显式重建。");
    }

    private static void EnsureMissingPrefabs()
    {
        // 这里只创建缺失资产，不覆盖既有 Prefab；允许在 Play 中落盘，避免长时间运行的
        // 调试会话阻止首次迁移。显式“重建默认 Prefab”仍必须由用户从菜单确认。
        EnsureFolder();
        var changed = false;
        if (!File.Exists(TitlePath)) { BuildTitle(TitlePath); changed = true; }
        if (!File.Exists(PaperPath)) { BuildPaper(PaperPath); changed = true; }
        if (!File.Exists(SaveSlotPath)) { BuildSaveSlot(SaveSlotPath); changed = true; }
        if (!File.Exists(SavePagePath)) { BuildSavePage(SavePagePath); changed = true; }
        if (!File.Exists(GalleryPagePath)) { BuildGalleryPage(GalleryPagePath); changed = true; }
        if (!File.Exists(SettingsPagePath)) { BuildSettingsPage(SettingsPagePath); changed = true; }
        if (!File.Exists(ExitPagePath)) { BuildExitPage(ExitPagePath); changed = true; }
        if (!File.Exists(HubPath)) { BuildHub(HubPath); changed = true; }
        if (!File.Exists(PanelPath)) { BuildSystemPanel(PanelPath); changed = true; }
        changed |= RepairExistingPrefabs();
        if (!changed) return;
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[OutGameUI] 已补齐或修复可编辑 Prefab；后续脚本刷新不会覆盖手动布局。");
    }

    /// <summary>
    /// 早期版本把多个 MonoBehaviour 写在同一个脚本文件里，Unity 重载后会把它们视为
    /// Missing Script。这里仅修复组件和引用，不改任何 RectTransform 或视觉参数。
    /// </summary>
    private static bool RepairExistingPrefabs()
    {
        var repaired = false;
        repaired |= RepairPrefab<OutGameTitleView>(TitlePath, RepairTitle);
        repaired |= RepairPrefab<OutGamePaperView>(PaperPath, RepairPaper);
        repaired |= RepairPrefab<OutGameSavePageView>(SavePagePath, RepairSavePage);
        repaired |= RepairPrefab<OutGameGalleryPageView>(GalleryPagePath, RepairGalleryPage);
        repaired |= RepairPrefab<OutGameSettingsPageView>(SettingsPagePath, RepairSettingsPage);
        repaired |= RepairPrefab<OutGameExitPageView>(ExitPagePath, RepairExitPage);
        repaired |= RepairPrefab<OutGameSaveSlotView>(SaveSlotPath, RepairSaveSlot);
        repaired |= RepairPrefab<OutGameHubView>(HubPath, RepairHub);
        repaired |= RepairPrefab<OutGameSystemPanelView>(PanelPath, RepairSystemPanel);
        return repaired;
    }

    private static bool RepairPrefab<T>(string path, System.Action<GameObject, T> bind)
        where T : MonoBehaviour
    {
        if (!File.Exists(path)) return false;
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var needsRepair = HasMissingScripts(root) || root.GetComponent<T>() == null;
            if (!needsRepair) return false;

            RemoveMissingScripts(root);
            var view = root.GetComponent<T>();
            if (view == null) view = root.AddComponent<T>();
            bind(root, view);
            EditorUtility.SetDirty(root);
            bool saveSucceeded;
            PrefabUtility.SaveAsPrefabAsset(root, path, out saveSucceeded);
            if (!saveSucceeded)
                throw new System.InvalidOperationException("Prefab 修复后保存失败：" + path);
            Debug.Log("[OutGameUI] 已修复 Prefab Missing Script，并保留布局：" + path);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static bool HasMissingScripts(GameObject root)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) > 0)
                return true;
        }
        return false;
    }

    private static void RemoveMissingScripts(GameObject root)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) > 0)
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject);
        }
    }

    private static void RepairTitle(GameObject root, OutGameTitleView view)
    {
        view.cover = Required<RawImage>(root.transform, "Cover");
        view.horizontalVignette = Required<RawImage>(root.transform, "HorizontalVignette");
        view.verticalVignette = Required<RawImage>(root.transform, "VerticalVignette");
        var menu = RequiredTransform(root.transform, "MainMenu");
        view.menuGradient = Required<RawImage>(menu, "MenuGradient");
        view.topRule = Required<RawImage>(menu, "TopRule");
        view.bottomRule = Required<RawImage>(menu, "BottomRule");
        view.saveState = Required<Text>(menu, "SaveStateRow/Text");
        view.hints = Required<Text>(menu, "Hints");

        var names = new[] { "继续游戏", "新游戏", "读取存档", "画廊", "设置", "退出游戏" };
        view.menuButtons = new Button[names.Length];
        view.menuMainLabels = new Text[names.Length];
        view.menuSubtitles = new Text[names.Length];
        view.menuHoverImages = new RawImage[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            var buttonRoot = RequiredTransform(menu, "Menu_" + names[i]);
            view.menuButtons[i] = Required<Button>(buttonRoot);
            view.menuMainLabels[i] = Required<Text>(buttonRoot, "Main");
            view.menuSubtitles[i] = Required<Text>(buttonRoot, "Subtitle");
            view.menuHoverImages[i] = Required<RawImage>(buttonRoot, "Hover");

            var feedback = buttonRoot.GetComponent<OutGameTweenButton>();
            if (feedback == null) feedback = buttonRoot.gameObject.AddComponent<OutGameTweenButton>();
            feedback.hoverScale = 1.055f;
            feedback.hoverGraphic = view.menuHoverImages[i];
            EnsureSpacing(view.menuMainLabels[i], 3.2f);
            EnsureSpacing(view.menuSubtitles[i], 1.5f);
        }
        EnsureSpacing(view.saveState, .65f);
        EnsureSpacing(view.hints, .8f);
    }

    private static void RepairPaper(GameObject root, OutGamePaperView view)
    {
        RepairPaperCommon(root, view);
        view.saveListRoot = RequiredTransform(view.frame, "SaveListRoot") as RectTransform;
    }

    private static void RepairPaperCommon(GameObject root, OutGamePaperView view)
    {
        view.cover = Required<RawImage>(root.transform, "Cover");
        view.paper = Required<Image>(root.transform, "Paper");
        view.frame = RequiredTransform(root.transform, "PaperFrame") as RectTransform;
        view.eyebrow = Required<Text>(view.frame, "Eyebrow");
        view.title = Required<Text>(view.frame, "Title");
        view.description = Required<Text>(view.frame, "Description");
        view.backButton = Required<Button>(view.frame, "Back");
        view.contentRoot = RequiredTransform(view.frame, "ContentRoot") as RectTransform;
    }

    private static void RepairSavePage(GameObject root, OutGameSavePageView view)
    {
        RepairPaperCommon(root, view);
        view.saveListRoot = RequiredTransform(view.contentRoot, "SaveListRoot") as RectTransform;
        view.slots = view.saveListRoot.GetComponentsInChildren<OutGameSaveSlotView>(true);
    }

    private static void RepairGalleryPage(GameObject root, OutGameGalleryPageView view)
    {
        RepairPaperCommon(root, view);
        view.logTab = Required<Button>(view.contentRoot, "LogTab");
        view.achievementTab = Required<Button>(view.contentRoot, "AchievementTab");
        view.logRoot = RequiredTransform(view.contentRoot, "LogRoot") as RectTransform;
        view.achievementRoot = RequiredTransform(view.contentRoot, "AchievementRoot") as RectTransform;
    }

    private static void RepairSettingsPage(GameObject root, OutGameSettingsPageView view)
    {
        RepairPaperCommon(root, view);
        view.dataSummary = Required<Text>(view.contentRoot, "InterfaceData/DataSummary");
        view.saveButton = Required<Button>(view.contentRoot, "InterfaceData/Save");
        view.loadButton = Required<Button>(view.contentRoot, "InterfaceData/Load");
        view.autoDialogueToggle = Required<Toggle>(view.contentRoot, "Gameplay/AutoDialogue");
        view.hintToggle = Required<Toggle>(view.contentRoot, "Gameplay/ShowHints");
        view.cameraShakeToggle = Required<Toggle>(view.contentRoot, "Gameplay/CameraShake");
    }

    private static void RepairExitPage(GameObject root, OutGameExitPageView view)
    {
        RepairPaperCommon(root, view);
        view.confirmButton = Required<Button>(view.contentRoot, "ConfirmExit");
    }

    private static void RepairSaveSlot(GameObject root, OutGameSaveSlotView view)
    {
        view.button = Required<Button>(root.transform);
        view.mark = Required<Image>(root.transform, "Mark");
        view.slotNumber = Required<Text>(root.transform, "Mark/Number");
        view.eyebrow = Required<Text>(root.transform, "Eyebrow");
        view.information = Required<Text>(root.transform, "Information");
        view.actionLabel = Required<Text>(root.transform, "Action");
    }

    private static void RepairHub(GameObject root, OutGameHubView view)
    {
        view.sceneRoot = RequiredTransform(root.transform, "SceneRoot") as RectTransform;
        view.chromeRoot = RequiredTransform(root.transform, "ChromeRoot") as RectTransform;
        view.modalRoot = RequiredTransform(root.transform, "ModalRoot") as RectTransform;
        view.footer = Required<Text>(view.chromeRoot, "Footer");
    }

    private static void RepairSystemPanel(GameObject root, OutGameSystemPanelView view)
    {
        view.scrim = Required<Image>(root.transform, "Scrim");
        view.scrimButton = Required<Button>(root.transform, "Scrim");
        view.panel = Required<Image>(root.transform, "Panel");
        view.headerRoot = RequiredTransform(root.transform, "Panel/HeaderRoot") as RectTransform;
        view.contentRoot = RequiredTransform(root.transform, "Panel/ContentRoot") as RectTransform;
    }

    private static Transform RequiredTransform(Transform root, string path)
    {
        var result = root.Find(path);
        if (result == null) throw new MissingReferenceException("Prefab 缺少节点：" + path);
        return result;
    }

    private static T Required<T>(Transform root, string path = null) where T : Component
    {
        var target = string.IsNullOrEmpty(path) ? root : RequiredTransform(root, path);
        var component = target.GetComponent<T>();
        if (component == null)
            throw new MissingReferenceException("Prefab 节点缺少组件 " + typeof(T).Name + "：" + target.name);
        return component;
    }

    private static void EnsureSpacing(Text label, float spacing)
    {
        var effect = label.GetComponent<OutGameLetterSpacing>();
        if (effect == null) effect = label.gameObject.AddComponent<OutGameLetterSpacing>();
        effect.spacing = spacing;
        label.SetVerticesDirty();
    }

    private static void EnsureFolder()
    {
        var parts = Folder.Split('/');
        var current = parts[0];
        for (var i = 1; i < parts.Length; i++)
        {
            var next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static void BuildTitle(string path)
    {
        var root = Root("TitlePage");
        var refs = root.AddComponent<OutGameTitleView>();
        refs.cover = Raw(root.transform, "Cover", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        refs.cover.texture = Resources.Load<Texture2D>("OutGameUI/og-meros");
        var fitter = refs.cover.gameObject.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        if (refs.cover.texture != null) fitter.aspectRatio = (float)refs.cover.texture.width / refs.cover.texture.height;
        refs.horizontalVignette = Raw(root.transform, "HorizontalVignette", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        refs.verticalVignette = Raw(root.transform, "VerticalVignette", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        var menu = Rect(root.transform, "MainMenu", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        refs.menuGradient = Raw(menu, "MenuGradient", new Vector2(.264f, 1), new Vector2(.264f, 1), new Vector2(0, -780), new Vector2(520, 568));
        refs.topRule = Raw(menu, "TopRule", new Vector2(.264f, 1), new Vector2(.264f, 1), new Vector2(0, -515), new Vector2(344, 1));
        refs.bottomRule = Raw(menu, "BottomRule", new Vector2(.264f, 1), new Vector2(.264f, 1), new Vector2(0, -1044), new Vector2(344, 1));

        var stateRow = Rect(menu, "SaveStateRow", new Vector2(.264f, 1), new Vector2(.264f, 1), new Vector2(0, -548), new Vector2(500, 28));
        var row = stateRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.childAlignment = TextAnchor.MiddleCenter;
        row.spacing = 12;
        row.childControlWidth = true;
        row.childControlHeight = false;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;
        var dot = Image(stateRow, "Dot", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(6, 6), Hex("DD725A"));
        var dotLayout = dot.gameObject.AddComponent<LayoutElement>();
        dotLayout.minWidth = dotLayout.preferredWidth = 6;
        dotLayout.minHeight = dotLayout.preferredHeight = 6;
        refs.saveState = Label(stateRow, "Text", "等待第一位住客", 12, Hex("A99A91"), TextAnchor.MiddleCenter, FontStyle.Bold);
        refs.saveState.gameObject.AddComponent<OutGameLetterSpacing>().spacing = .65f;

        refs.menuButtons = new Button[6];
        refs.menuMainLabels = new Text[6];
        refs.menuSubtitles = new Text[6];
        refs.menuHoverImages = new RawImage[6];
        var chinese = new[] { "继续游戏", "新游戏", "读取存档", "画廊", "设置", "退出游戏" };
        var english = new[] { "暂无存档", "NEW STORY", "LOAD GAME", "LOG & ACHIEVEMENT", "OPTIONS", "QUIT" };
        for (var i = 0; i < 6; i++)
        {
            var buttonImage = Image(menu, "Menu_" + chinese[i], new Vector2(.264f, 1), new Vector2(.264f, 1),
                new Vector2(0, -584 - i * 76), new Vector2(520, 70), Color.clear);
            var button = buttonImage.gameObject.AddComponent<Button>();
            button.targetGraphic = buttonImage;
            var feedback = buttonImage.gameObject.AddComponent<OutGameTweenButton>();
            feedback.hoverScale = 1.055f;
            var hover = Raw(buttonImage.transform, "Hover", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                Vector2.zero, new Vector2(430, 58));
            hover.color = new Color(1, 1, 1, 0);
            feedback.hoverGraphic = hover;
            var main = Label(buttonImage.transform, "Main", chinese[i], 23, i == 1 ? Hex("F0A080") : Hex("DBC9BD"),
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 8), new Vector2(500, 34), TextAnchor.MiddleCenter, FontStyle.Bold);
            main.gameObject.AddComponent<OutGameLetterSpacing>().spacing = 3.2f;
            var subtitle = Label(buttonImage.transform, "Subtitle", english[i], 8, Hex("81736E"),
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, -17), new Vector2(500, 15), TextAnchor.MiddleCenter, FontStyle.Bold);
            subtitle.gameObject.AddComponent<OutGameLetterSpacing>().spacing = 1.5f;
            buttonImage.gameObject.AddComponent<CanvasGroup>();
            refs.menuButtons[i] = button;
            refs.menuMainLabels[i] = main;
            refs.menuSubtitles[i] = subtitle;
            refs.menuHoverImages[i] = hover;
        }
        refs.hints = Label(menu, "Hints", "↑ ↓ 选择     ENTER 确认", 8, Hex("756B67"),
            new Vector2(.264f, 1), new Vector2(.264f, 1), new Vector2(0, -1063), new Vector2(500, 18), TextAnchor.MiddleCenter, FontStyle.Bold);
        refs.hints.gameObject.AddComponent<OutGameLetterSpacing>().spacing = .8f;
        Save(root, path);
    }

    private static void BuildPaper(string path)
    {
        var root = Root("PaperPage");
        var refs = root.AddComponent<OutGamePaperView>();
        refs.cover = Raw(root.transform, "Cover", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        refs.cover.texture = Resources.Load<Texture2D>("OutGameUI/og-meros");
        refs.cover.color = new Color(1, 1, 1, .2f);
        refs.paper = Image(root.transform, "Paper", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(.84f, .79f, .7f, .93f));
        refs.frame = Rect(root.transform, "PaperFrame", Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-50, -50));
        ImageOn(refs.frame, new Color(1, .97f, .9f, .11f));
        refs.eyebrow = Label(refs.frame, "Eyebrow", "START A NEW STORY", 17, Hex("6E243E"),
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(330, -75), new Vector2(560, 30), TextAnchor.MiddleLeft, FontStyle.Bold);
        refs.title = Label(refs.frame, "Title", "选择新游戏存档", 52, Hex("35282A"),
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(580, -140), new Vector2(1020, 80), TextAnchor.MiddleLeft, FontStyle.Bold);
        Image(refs.frame, "Rule", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -205), new Vector2(1680, 2), new Color(.3f, .18f, .2f, .23f));
        refs.description = Label(refs.frame, "Description", "页面说明", 19, Hex("5B4948"),
            new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(-90, -260), new Vector2(1500, 60), TextAnchor.MiddleLeft, FontStyle.Normal);
        var backImage = Image(refs.frame, "Back", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-145, -70), new Vector2(190, 58), new Color(1, 1, 1, .15f));
        refs.backButton = backImage.gameObject.AddComponent<Button>();
        refs.backButton.targetGraphic = backImage;
        Label(backImage.transform, "Label", "← 返回主菜单", 18, Hex("4A3738"), TextAnchor.MiddleCenter, FontStyle.Bold);
        refs.contentRoot = Rect(refs.frame, "ContentRoot", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        refs.contentRoot.SetAsLastSibling();
        refs.saveListRoot = Rect(refs.frame, "SaveListRoot", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -325), new Vector2(1680, 390));
        refs.saveListRoot.pivot = new Vector2(.5f, 1);
        var list = refs.saveListRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        list.childAlignment = TextAnchor.UpperCenter;
        list.spacing = 22;
        list.childControlWidth = false;
        list.childControlHeight = false;
        list.childForceExpandWidth = false;
        list.childForceExpandHeight = false;
        Save(root, path);
    }

    private static T BuildCompletePaperPage<T>(GameObject root, string eyebrow, string title, string description)
        where T : OutGamePaperView
    {
        var refs = root.AddComponent<T>();
        refs.cover = Raw(root.transform, "Cover", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        refs.cover.texture = Resources.Load<Texture2D>("OutGameUI/og-meros");
        refs.cover.color = new Color(1, 1, 1, .2f);
        refs.paper = Image(root.transform, "Paper", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
            new Color(.84f, .79f, .7f, .93f));
        refs.frame = Rect(root.transform, "PaperFrame", Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-50, -50));
        ImageOn(refs.frame, new Color(1, .97f, .9f, .11f));
        refs.eyebrow = Label(refs.frame, "Eyebrow", eyebrow, 17, Hex("6E243E"),
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(330, -75), new Vector2(560, 30), TextAnchor.MiddleLeft, FontStyle.Bold);
        refs.title = Label(refs.frame, "Title", title, 52, Hex("35282A"),
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(580, -140), new Vector2(1020, 80), TextAnchor.MiddleLeft, FontStyle.Bold);
        Image(refs.frame, "Rule", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -205),
            new Vector2(1680, 2), new Color(.3f, .18f, .2f, .23f));
        refs.description = Label(refs.frame, "Description", description, 19, Hex("5B4948"),
            new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(-90, -260), new Vector2(1500, 60), TextAnchor.MiddleLeft, FontStyle.Normal);
        var backImage = Image(refs.frame, "Back", new Vector2(1, 1), new Vector2(1, 1),
            new Vector2(-145, -70), new Vector2(190, 58), new Color(1, 1, 1, .15f));
        refs.backButton = backImage.gameObject.AddComponent<Button>();
        refs.backButton.targetGraphic = backImage;
        Label(backImage.transform, "Label", "← 返回主菜单", 18, Hex("4A3738"), TextAnchor.MiddleCenter, FontStyle.Bold);
        refs.contentRoot = Rect(refs.frame, "ContentRoot", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        refs.contentRoot.SetAsLastSibling();
        return refs;
    }

    private static void BuildSavePage(string path)
    {
        var root = Root("SavePage");
        var refs = BuildCompletePaperPage<OutGameSavePageView>(root, "START A NEW STORY", "选择新游戏存档",
            "选择存档位后开始新的旅店故事。已有存档会在下一次保存时被覆盖。");
        refs.saveListRoot = Rect(refs.contentRoot, "SaveListRoot", new Vector2(.5f, 1), new Vector2(.5f, 1),
            new Vector2(0, -325), new Vector2(1680, 390));
        refs.saveListRoot.pivot = new Vector2(.5f, 1);
        var list = refs.saveListRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        list.childAlignment = TextAnchor.UpperCenter;
        list.spacing = 22;
        list.childControlWidth = false;
        list.childControlHeight = false;
        list.childForceExpandWidth = false;
        list.childForceExpandHeight = false;

        refs.slots = new OutGameSaveSlotView[3];
        var slotAsset = AssetDatabase.LoadAssetAtPath<GameObject>(SaveSlotPath);
        for (var i = 0; i < refs.slots.Length; i++)
        {
            GameObject slot;
            if (slotAsset != null)
                slot = (GameObject)PrefabUtility.InstantiatePrefab(slotAsset, refs.saveListRoot);
            else
                slot = new GameObject("SaveSlot0" + (i + 1), typeof(RectTransform), typeof(OutGameSaveSlotView));
            slot.name = "SaveSlot0" + (i + 1);
            var rect = slot.transform as RectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(1680, 112);
            refs.slots[i] = slot.GetComponent<OutGameSaveSlotView>();
        }
        Save(root, path);
    }

    private static void BuildGalleryPage(string path)
    {
        var root = Root("GalleryPage");
        var refs = BuildCompletePaperPage<OutGameGalleryPageView>(root, "HOUSE MEMORY", "画廊",
            "回看旅店里已经发生的片段，以及尚未被揭开的秘密。");
        refs.logTab = PageButton(refs.contentRoot, "LogTab", "游戏日志", new Vector2(270, -320), new Vector2(220, 58), Hex("6E243E"), Hex("F3E8DD"));
        refs.achievementTab = PageButton(refs.contentRoot, "AchievementTab", "成就系统", new Vector2(510, -320), new Vector2(220, 58), new Color(1, 1, 1, .12f), Hex("6E243E"));
        refs.logRoot = Rect(refs.contentRoot, "LogRoot", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        PaperArticleEditor(refs.logRoot, "Log_01", new Vector2(460, -475), "WEEK 01 · 06/17", "窗户唱回来的那句话",
            "赫墨说“今天糟透了”。琴弦回答：“但你还是走到了这里。”");
        PaperArticleEditor(refs.logRoot, "Log_02", new Vector2(1250, -475), "WEEK 01 · 06/16", "风铃下的纸条",
            "米娅没有说再见，只留下了一张画着胡萝卜的小纸条。");
        refs.achievementRoot = Rect(refs.contentRoot, "AchievementRoot", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var names = new[] { "初次相识", "夜的主人", "家的轮廓", "无人知晓" };
        var notes = new[] { "记录第一位访客", "在深夜完成服务", "解锁全部房间", "发现特殊访客的秘密" };
        for (var i = 0; i < names.Length; i++)
        {
            var x = i % 2 == 0 ? 505 : 1255;
            var y = i < 2 ? -450 : -650;
            var done = i < 2;
            PageButton(refs.achievementRoot, "Achievement" + i,
                $"{(done ? "✓" : "0" + (i + 1))}     {names[i]}\n<size=17>          {notes[i]} · {(done ? "已完成" : "未解锁")}</size>",
                new Vector2(x, y), new Vector2(650, 150), done ? new Color(.45f, .18f, .25f, .18f) : new Color(1, 1, 1, .12f),
                Hex("3E3032"), 28, TextAnchor.MiddleLeft);
        }
        refs.achievementRoot.gameObject.SetActive(false);
        Save(root, path);
    }

    private static void BuildSettingsPage(string path)
    {
        var root = Root("SettingsPage");
        var refs = BuildCompletePaperPage<OutGameSettingsPageView>(root, "OPTIONS", "设置",
            "调整显示、音量和界面偏好。所有设置会随当前存档保留。");
        var left = PaperSectionEditor(refs.contentRoot, "InterfaceData", new Vector2(490, -505), new Vector2(720, 420), "INTERFACE & DATA", "界面与存档");
        refs.dataSummary = Label(left, "DataSummary", "界面切换       沉浸式\n\n当前存档         Slot 01", 22, Hex("514142"),
            new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, -40), new Vector2(610, 130), TextAnchor.MiddleLeft, FontStyle.Normal);
        refs.saveButton = PageButton(left, "Save", "保存", new Vector2(190, 55), new Vector2(245, 62), Hex("6E243E"), Hex("F3E8DD"), 20, TextAnchor.MiddleCenter, new Vector2(0, 0));
        refs.loadButton = PageButton(left, "Load", "读取存档", new Vector2(-190, 55), new Vector2(245, 62), new Color(1, 1, 1, .08f), Hex("6E243E"), 20, TextAnchor.MiddleCenter, new Vector2(1, 0));

        var right = PaperSectionEditor(refs.contentRoot, "Gameplay", new Vector2(1270, -505), new Vector2(720, 420), "GAMEPLAY", "游戏性");
        refs.autoDialogueToggle = PageToggle(right, "AutoDialogue", "对话自动播放", new Vector2(0, 25), false);
        refs.hintToggle = PageToggle(right, "ShowHints", "显示交互提示", new Vector2(0, -55), true);
        refs.cameraShakeToggle = PageToggle(right, "CameraShake", "镜头轻微晃动", new Vector2(0, -135), true);
        Save(root, path);
    }

    private static void BuildExitPage(string path)
    {
        var root = Root("ExitPage");
        var refs = BuildCompletePaperPage<OutGameExitPageView>(root, "LEAVE THE GUESTHOUSE?", "退出游戏",
            "Unity 版本可安全退出运行模式，或返回主菜单继续体验。");
        refs.confirmButton = PageButton(refs.contentRoot, "ConfirmExit", "退出游戏", new Vector2(0, -80),
            new Vector2(360, 84), Hex("6E243E"), Hex("F3E8DD"), 26, TextAnchor.MiddleCenter, new Vector2(.5f, .5f));
        Save(root, path);
    }

    private static void BuildSaveSlot(string path)
    {
        var root = Root("SaveSlot");
        var rect = (RectTransform)root.transform;
        rect.sizeDelta = new Vector2(1680, 112);
        var refs = root.AddComponent<OutGameSaveSlotView>();
        var background = ImageOn(rect, new Color(.95f, .9f, .82f, .55f));
        refs.button = root.AddComponent<Button>();
        refs.button.targetGraphic = background;
        refs.mark = Image(rect, "Mark", new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(55, 0), new Vector2(82, 82), Hex("76505B"));
        refs.slotNumber = Label(refs.mark.transform, "Number", "01", 38, Hex("392A2D"), TextAnchor.MiddleCenter, FontStyle.Bold);
        refs.eyebrow = Label(rect, "Eyebrow", "SAVE SLOT", 15, Hex("392A2D"),
            new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(230, 22), new Vector2(220, 24), TextAnchor.MiddleLeft, FontStyle.Bold);
        refs.information = Label(rect, "Information", "空存档\n从这里开始", 24, Hex("392A2D"),
            new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(590, -10), new Vector2(850, 62), TextAnchor.MiddleLeft, FontStyle.Bold);
        refs.actionLabel = Label(rect, "Action", "选择", 21, Hex("392A2D"),
            new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-120, 0), new Vector2(180, 50), TextAnchor.MiddleCenter, FontStyle.Bold);
        Save(root, path);
    }

    private static void BuildHub(string path)
    {
        var root = Root("HouseHubPage");
        var refs = root.AddComponent<OutGameHubView>();
        refs.sceneRoot = Rect(root.transform, "SceneRoot", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        refs.chromeRoot = Rect(root.transform, "ChromeRoot", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        refs.modalRoot = Rect(root.transform, "ModalRoot", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        refs.footer = Label(refs.chromeRoot, "Footer", "NEW LIFE, NEW HOME · UI/UX CONCEPT", 12, new Color(1, 1, 1, .45f),
            new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 12), new Vector2(1800, 26), TextAnchor.MiddleCenter, FontStyle.Normal);
        Save(root, path);
    }

    private static void BuildSystemPanel(string path)
    {
        var root = Root("SystemPanel");
        var refs = root.AddComponent<OutGameSystemPanelView>();
        refs.scrim = Image(root.transform, "Scrim", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(.005f, .008f, .02f, 0));
        refs.scrimButton = refs.scrim.gameObject.AddComponent<Button>();
        refs.scrimButton.targetGraphic = refs.scrim;
        refs.panel = Image(root.transform, "Panel", new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(720, 0), new Vector2(1280, 1080), new Color(.055f, .045f, .06f, .98f));
        refs.headerRoot = Rect(refs.panel.transform, "HeaderRoot", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -75), new Vector2(1280, 150));
        refs.contentRoot = Rect(refs.panel.transform, "ContentRoot", new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, -75), new Vector2(1180, 830));
        Save(root, path);
    }

    private static Button PageButton(Transform parent, string name, string caption, Vector2 position, Vector2 size,
        Color background, Color foreground, int fontSize = 20, TextAnchor alignment = TextAnchor.MiddleCenter,
        Vector2? anchor = null)
    {
        var point = anchor ?? new Vector2(0, 1);
        var image = Image(parent, name, point, point, position, size, background);
        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        var label = Label(image.transform, "Label", caption, fontSize, foreground, TextAnchor.MiddleCenter, FontStyle.Bold);
        label.alignment = alignment;
        label.rectTransform.offsetMin = new Vector2(14, 8);
        label.rectTransform.offsetMax = new Vector2(-14, -8);
        return button;
    }

    private static Transform PaperSectionEditor(Transform parent, string name, Vector2 position, Vector2 size,
        string eyebrow, string title)
    {
        var section = Image(parent, name, new Vector2(0, 1), new Vector2(0, 1), position, size,
            new Color(1, .97f, .9f, .18f));
        Label(section.transform, "Header", $"<size=14>{eyebrow}</size>\n{title}", 28, Hex("433234"),
            new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -62), new Vector2(size.x - 70, 100),
            TextAnchor.MiddleLeft, FontStyle.Bold);
        return section.transform;
    }

    private static void PaperArticleEditor(Transform parent, string name, Vector2 position, string date,
        string title, string body)
    {
        var article = Image(parent, name, new Vector2(0, 1), new Vector2(0, 1), position,
            new Vector2(700, 320), new Color(1, .98f, .92f, .2f));
        Label(article.transform, "Text", $"<size=14>{date}</size>\n<size=30>{title}</size>\n\n{body}",
            20, Hex("433234"), new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero,
            new Vector2(620, 250), TextAnchor.UpperLeft, FontStyle.Normal);
    }

    private static Toggle PageToggle(Transform parent, string name, string caption, Vector2 position, bool isOn)
    {
        var row = Rect(parent, name, new Vector2(.5f, .5f), new Vector2(.5f, .5f), position, new Vector2(610, 58));
        var toggle = row.gameObject.AddComponent<Toggle>();
        var box = Image(row, "Box", new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(24, 0), new Vector2(32, 32),
            new Color(1, 1, 1, .18f));
        var check = Image(box.transform, "Checkmark", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero,
            new Vector2(20, 20), Hex("6E243E"));
        toggle.targetGraphic = box;
        toggle.graphic = check;
        toggle.isOn = isOn;
        Label(row, "Label", caption, 23, Hex("514142"), new Vector2(0, 0), new Vector2(1, 1),
            new Vector2(45, 0), new Vector2(-90, 0), TextAnchor.MiddleLeft, FontStyle.Normal);
        return toggle;
    }

    private static GameObject Root(string name)
    {
        var root = new GameObject(name, typeof(RectTransform));
        root.layer = 5;
        var rect = (RectTransform)root.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return root;
    }

    private static RectTransform Rect(Transform parent, string name, Vector2 min, Vector2 max, Vector2 position, Vector2 size)
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

    private static Image Image(Transform parent, string name, Vector2 min, Vector2 max, Vector2 position, Vector2 size, Color color)
    {
        return ImageOn(Rect(parent, name, min, max, position, size), color);
    }

    private static Image ImageOn(RectTransform rect, Color color)
    {
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static RawImage Raw(Transform parent, string name, Vector2 min, Vector2 max, Vector2 position, Vector2 size)
    {
        var image = Rect(parent, name, min, max, position, size).gameObject.AddComponent<RawImage>();
        image.raycastTarget = false;
        return image;
    }

    private static Text Label(Transform parent, string name, string value, int size, Color color,
        TextAnchor alignment, FontStyle style)
    {
        return Label(parent, name, value, size, color, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, alignment, style);
    }

    private static Text Label(Transform parent, string name, string value, int size, Color color,
        Vector2 min, Vector2 max, Vector2 position, Vector2 dimensions, TextAnchor alignment, FontStyle style)
    {
        var text = Rect(parent, name, min, max, position, dimensions).gameObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.supportRichText = true;
        text.raycastTarget = false;
        text.text = value;
        return text;
    }

    private static Color Hex(string value, float alpha = 1)
    {
        if (!value.StartsWith("#")) value = "#" + value;
        ColorUtility.TryParseHtmlString(value, out var color);
        color.a = alpha;
        return color;
    }

    private static void Save(GameObject root, string path)
    {
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
    }
}
#endif

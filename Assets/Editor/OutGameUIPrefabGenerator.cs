#if UNITY_EDITOR
using System.IO;
using MasterHouse;
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
    private const string HubTopBarPath = Folder + "/HubTopBar.prefab";
    private const string HubTaskCardPath = Folder + "/HubTaskCard.prefab";
    private const string HubGuestRailPath = Folder + "/HubGuestRail.prefab";
    private const string HubGuestCardPath = Folder + "/HubGuestCard.prefab";
    private const string HubRightDockPath = Folder + "/HubRightDock.prefab";
    private const string HubDockButtonPath = Folder + "/HubDockButton.prefab";
    private const string HubRoomNavigationPath = Folder + "/HubRoomNavigation.prefab";
    private const string HubRoomButtonPath = Folder + "/HubRoomButton.prefab";
    private const string HubSceneOverlayPath = Folder + "/HubSceneOverlay.prefab";
    private const string PanelPath = Folder + "/SystemPanel.prefab";
    private const string HubImmersiveTogglePath = Folder + "/HubImmersiveToggle.prefab";
    private const string CalendarPanelPath = Folder + "/CalendarPanel.prefab";
    private const string TasksPanelPath = Folder + "/TasksPanel.prefab";
    private const string DevicePanelPath = Folder + "/DevicePanel.prefab";
    private const string JournalPanelPath = Folder + "/JournalPanel.prefab";
    private const string ArchivePanelPath = Folder + "/ArchivePanel.prefab";
    private const string DialogueViewPath = Folder + "/DialogueView.prefab";
    private const string CalendarPagePath = Folder + "/CalendarPage.prefab";
    private const string TasksPagePath = Folder + "/TasksPage.prefab";
    private const string DevicePagePath = Folder + "/DevicePage.prefab";
    private const string JournalPagePath = Folder + "/JournalPage.prefab";
    private const string ArchivePagePath = Folder + "/ArchivePage.prefab";
    // 3.5c：动态列表项模板（§16.2 列表项 = Prefab 模板 + 运行时实例化），供重写版 HouseUI 面板使用
    private const string DeviceCardPath = Folder + "/DeviceCard.prefab";
    private const string ArchiveCardPath = Folder + "/ArchiveCard.prefab";
    private const string JournalArticlePath = Folder + "/JournalArticle.prefab";
    private const string AchievementRowPath = Folder + "/AchievementRow.prefab";

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

    [MenuItem("Tools/MasterHouse/OutGame UI/Select Prefab Folder")]
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

    [MenuItem("Tools/MasterHouse/OutGame UI/Rebuild Default Prefabs...")]
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
        BuildHubGuestCard(HubGuestCardPath);
        BuildHubDockButton(HubDockButtonPath);
        BuildHubRoomButton(HubRoomButtonPath);
        BuildHubTopBar(HubTopBarPath);
        BuildHubTaskCard(HubTaskCardPath);
        BuildHubGuestRail(HubGuestRailPath);
        BuildHubRightDock(HubRightDockPath);
        BuildHubRoomNavigation(HubRoomNavigationPath);
        BuildHubSceneOverlay(HubSceneOverlayPath);
        BuildHub(HubPath);
        BuildSystemPanel(PanelPath);
        BuildHubImmersiveToggle(HubImmersiveTogglePath);
        BuildCalendarPanel(CalendarPanelPath);
        BuildTasksPanel(TasksPanelPath);
        BuildDevicePanelContent(DevicePanelPath);
        BuildJournalPanelContent(JournalPanelPath);
        BuildArchivePanelContent(ArchivePanelPath);
        BuildDialogueView(DialogueViewPath);
        BuildPanelPage(CalendarPagePath, "CalendarPage", "REAL TIME", "日程与时间", "历", CalendarPanelPath);
        BuildPanelPage(TasksPagePath, "TasksPage", "TODAY / 03", "今日委托", "任", TasksPanelPath);
        BuildPanelPage(DevicePagePath, "DevicePage", "HOUSE INDEX", "设备图鉴", "器", DevicePanelPath);
        BuildPanelPage(JournalPagePath, "JournalPage", "MEMORY LOG", "日记与成就", "记", JournalPanelPath);
        BuildPanelPage(ArchivePagePath, "ArchivePage", "HOUSE ARCHIVE", "叙事资源档案", "集", ArchivePanelPath);
        BuildDeviceCard(DeviceCardPath);
        BuildArchiveCard(ArchiveCardPath);
        BuildJournalArticle(JournalArticlePath);
        BuildAchievementRow(AchievementRowPath);
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
        if (!File.Exists(HubGuestCardPath)) { BuildHubGuestCard(HubGuestCardPath); changed = true; }
        if (!File.Exists(HubDockButtonPath)) { BuildHubDockButton(HubDockButtonPath); changed = true; }
        if (!File.Exists(HubRoomButtonPath)) { BuildHubRoomButton(HubRoomButtonPath); changed = true; }
        if (!File.Exists(HubTopBarPath)) { BuildHubTopBar(HubTopBarPath); changed = true; }
        if (!File.Exists(HubTaskCardPath)) { BuildHubTaskCard(HubTaskCardPath); changed = true; }
        if (!File.Exists(HubGuestRailPath)) { BuildHubGuestRail(HubGuestRailPath); changed = true; }
        if (!File.Exists(HubRightDockPath)) { BuildHubRightDock(HubRightDockPath); changed = true; }
        if (!File.Exists(HubRoomNavigationPath)) { BuildHubRoomNavigation(HubRoomNavigationPath); changed = true; }
        if (!File.Exists(HubSceneOverlayPath)) { BuildHubSceneOverlay(HubSceneOverlayPath); changed = true; }
        if (!File.Exists(HubPath)) { BuildHub(HubPath); changed = true; }
        if (!File.Exists(PanelPath)) { BuildSystemPanel(PanelPath); changed = true; }
        if (!File.Exists(HubImmersiveTogglePath)) { BuildHubImmersiveToggle(HubImmersiveTogglePath); changed = true; }
        if (!File.Exists(CalendarPanelPath)) { BuildCalendarPanel(CalendarPanelPath); changed = true; }
        if (!File.Exists(TasksPanelPath)) { BuildTasksPanel(TasksPanelPath); changed = true; }
        if (!File.Exists(DevicePanelPath)) { BuildDevicePanelContent(DevicePanelPath); changed = true; }
        if (!File.Exists(JournalPanelPath)) { BuildJournalPanelContent(JournalPanelPath); changed = true; }
        if (!File.Exists(ArchivePanelPath)) { BuildArchivePanelContent(ArchivePanelPath); changed = true; }
        if (!File.Exists(DialogueViewPath)) { BuildDialogueView(DialogueViewPath); changed = true; }
        if (!File.Exists(CalendarPagePath)) { BuildPanelPage(CalendarPagePath, "CalendarPage", "REAL TIME", "日程与时间", "历", CalendarPanelPath); changed = true; }
        if (!File.Exists(TasksPagePath)) { BuildPanelPage(TasksPagePath, "TasksPage", "TODAY / 03", "今日委托", "任", TasksPanelPath); changed = true; }
        if (!File.Exists(DevicePagePath)) { BuildPanelPage(DevicePagePath, "DevicePage", "HOUSE INDEX", "设备图鉴", "器", DevicePanelPath); changed = true; }
        if (!File.Exists(JournalPagePath)) { BuildPanelPage(JournalPagePath, "JournalPage", "MEMORY LOG", "日记与成就", "记", JournalPanelPath); changed = true; }
        if (!File.Exists(ArchivePagePath)) { BuildPanelPage(ArchivePagePath, "ArchivePage", "HOUSE ARCHIVE", "叙事资源档案", "集", ArchivePanelPath); changed = true; }
        if (!File.Exists(DeviceCardPath)) { BuildDeviceCard(DeviceCardPath); changed = true; }
        if (!File.Exists(ArchiveCardPath)) { BuildArchiveCard(ArchiveCardPath); changed = true; }
        if (!File.Exists(JournalArticlePath)) { BuildJournalArticle(JournalArticlePath); changed = true; }
        if (!File.Exists(AchievementRowPath)) { BuildAchievementRow(AchievementRowPath); changed = true; }
        changed |= RepairExistingPrefabs();
        changed |= RepairButtonFeedback();
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
        repaired |= RepairPrefab<OutGameHubView>(HubPath, RepairHub, view => view.topBar == null ||
            view.taskCard == null || view.guestRail == null || view.rightDock == null ||
            view.roomNavigation == null || view.sceneOverlay == null);
        repaired |= RepairPrefab<OutGameSystemPanelView>(PanelPath, RepairSystemPanel);
        return repaired;
    }

    /// <summary>
    /// 给旧版 Prefab 无损补回 DOTween 按钮反馈。只增加行为组件，不修改布局、颜色或层级；
    /// 嵌套 Prefab 的按钮由其源 Prefab 自己迁移，避免在父资源上制造多余 Override。
    /// </summary>
    private static bool RepairButtonFeedback()
    {
        var repaired = false;
        var paths = new[]
        {
            TitlePath, PaperPath, SaveSlotPath, SavePagePath, GalleryPagePath, SettingsPagePath, ExitPagePath,
            HubTopBarPath, HubTaskCardPath, HubGuestCardPath, HubGuestRailPath, HubDockButtonPath,
            HubRightDockPath, HubRoomButtonPath, HubRoomNavigationPath, HubSceneOverlayPath, HubPath,
            HubImmersiveTogglePath, CalendarPanelPath, TasksPanelPath, DialogueViewPath,
            DevicePanelPath, JournalPanelPath, ArchivePanelPath,
            CalendarPagePath, TasksPagePath, DevicePagePath, JournalPagePath, ArchivePagePath,
        };
        foreach (var path in paths)
            repaired |= RepairButtonFeedback(path);
        return repaired;
    }

    private static bool RepairButtonFeedback(string path)
    {
        if (!File.Exists(path)) return false;
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var changed = false;
            foreach (var button in root.GetComponentsInChildren<Button>(true))
            {
                if (PrefabUtility.IsPartOfPrefabInstance(button.gameObject)) continue;
                if (button.GetComponent<OutGameTweenButton>() != null) continue;
                AddTweenFeedback(button);
                changed = true;
            }
            if (!changed) return false;

            EditorUtility.SetDirty(root);
            bool saveSucceeded;
            PrefabUtility.SaveAsPrefabAsset(root, path, out saveSucceeded);
            if (!saveSucceeded)
                throw new System.InvalidOperationException("Prefab DOTween 迁移后保存失败：" + path);
            Debug.Log("[OutGameUI] 已补回 Prefab 按钮 DOTween 反馈，并保留布局：" + path);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static bool RepairPrefab<T>(string path, System.Action<GameObject, T> bind,
        System.Func<T, bool> requiresMigration = null)
        where T : MonoBehaviour
    {
        if (!File.Exists(path)) return false;
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var existing = root.GetComponent<T>();
            var needsRepair = HasMissingScripts(root) || existing == null ||
                              (requiresMigration != null && existing != null && requiresMigration(existing));
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
        EmbedHubComponents(view);
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

    private static void BuildHubTopBar(string path)
    {
        var root = ComponentRoot("HubTopBar", new Vector2(1920, 124));
        ImageOn((RectTransform)root.transform, new Color(.025f, .025f, .045f, .77f));
        var refs = root.AddComponent<OutGameHubTopBarView>();
        refs.timeButton = PageButton(root.transform, "Time", "WEEK 32 · 2026\n08 / 04    晚上",
            new Vector2(230, 0), new Vector2(410, 100), new Color(.17f, .06f, .12f, .74f), Hex("F3E8DD"), 23,
            TextAnchor.MiddleLeft, new Vector2(0, .5f));
        refs.weekDatePhase = refs.timeButton.GetComponentInChildren<Text>();
        refs.phaseRange = Label(refs.timeButton.transform, "PhaseRange", "18:00–22:00", 12, new Color(1, 1, 1, .58f),
            new Vector2(1, 0), new Vector2(1, 0), new Vector2(-78, 14), new Vector2(150, 24), TextAnchor.MiddleRight, FontStyle.Normal);
        refs.clock = Label(refs.timeButton.transform, "Clock", "18:46", 24, Hex("F3E8DD"),
            new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-62, -7), new Vector2(110, 42), TextAnchor.MiddleRight, FontStyle.Bold);
        refs.creditButton = PageButton(root.transform, "Credit", "HOUSE CREDIT\n◈ 2,480     ＋", new Vector2(625, 0),
            new Vector2(270, 82), new Color(.06f, .025f, .06f, .7f), Hex("F3E8DD"), 21, TextAnchor.MiddleLeft, new Vector2(0, .5f));
        refs.creditLabel = refs.creditButton.GetComponentInChildren<Text>();
        refs.brandButton = PageButton(root.transform, "Brand", "<i>The Guesthouse\nof Meros</i>     <size=14>N E W  C H A P T E R</size>",
            new Vector2(120, 0), new Vector2(600, 90), Color.clear, Hex("E22D76"), 29, TextAnchor.MiddleCenter, new Vector2(.5f, .5f));
        refs.brandLabel = refs.brandButton.GetComponentInChildren<Text>();
        refs.welcomeLabel = Label(root.transform, "Welcome", "WELCOME HOME.\n本周将有 <color=#E22D76>4</color> 位访客来访", 19, Hex("F3E8DD"),
            new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-370, 0), new Vector2(330, 78), TextAnchor.MiddleCenter, FontStyle.Normal);
        refs.optionsButton = PageButton(root.transform, "Options", "设\n<size=15>OPTIONS · 设置</size>", new Vector2(-70, 0),
            new Vector2(112, 104), new Color(.32f, .06f, .18f, .86f), Hex("F3E8DD"), 27, TextAnchor.MiddleCenter, new Vector2(1, .5f));
        refs.optionsLabel = refs.optionsButton.GetComponentInChildren<Text>();
        Save(root, path);
    }

    private static void BuildHubTaskCard(string path)
    {
        var root = ComponentRoot("HubTaskCard", new Vector2(390, 255));
        var image = ImageOn((RectTransform)root.transform, new Color(.13f, .045f, .11f, .84f));
        var refs = root.AddComponent<OutGameHubTaskCardView>();
        refs.button = root.AddComponent<Button>();
        refs.button.targetGraphic = image;
        AddTweenFeedback(refs.button);
        refs.header = Label(root.transform, "Header", "CURRENT VISITOR TASK                         进行中", 13, Hex("F3E8DD"),
            new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -22), new Vector2(350, 28), TextAnchor.MiddleLeft, FontStyle.Bold);
        refs.guestTitle = Label(root.transform, "GuestTitle", "洛恩 · 一杯温热的赤茶", 22, Hex("F3E8DD"),
            new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -78), new Vector2(350, 44), TextAnchor.MiddleLeft, FontStyle.Bold);
        refs.hint = Label(root.transform, "Hint", "需要关于这栋房子的答案", 16, Hex("F3E8DD"),
            new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -125), new Vector2(350, 48), TextAnchor.UpperLeft, FontStyle.Normal);
        refs.progress = Label(root.transform, "Progress", "━━━━━━  35%     点击查看任务详情  →", 14, Hex("F3E8DD"),
            new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 30), new Vector2(350, 32), TextAnchor.MiddleLeft, FontStyle.Normal);
        Save(root, path);
    }

    private static void BuildHubGuestCard(string path)
    {
        var root = ComponentRoot("HubGuestCard", new Vector2(390, 100));
        var refs = root.AddComponent<OutGameHubGuestCardView>();
        refs.background = ImageOn((RectTransform)root.transform, new Color(.025f, .025f, .045f, .83f));
        refs.button = root.AddComponent<Button>();
        refs.button.targetGraphic = refs.background;
        AddTweenFeedback(refs.button);
        refs.portrait = Raw(root.transform, "Portrait", new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(55, 0), new Vector2(76, 76));
        var portraitOutline = refs.portrait.gameObject.AddComponent<Outline>();
        portraitOutline.effectColor = new Color(.8f, .15f, .45f, .8f);
        portraitOutline.effectDistance = new Vector2(4, -4);
        refs.eventLabel = Label(root.transform, "Event", "SPECIAL EVENT", 12, Hex("F3E8DD"),
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(185, -22), new Vector2(220, 24), TextAnchor.MiddleLeft, FontStyle.Bold);
        refs.guestName = Label(root.transform, "Name", "洛恩", 21, Hex("F3E8DD"),
            new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(185, 2), new Vector2(220, 32), TextAnchor.MiddleLeft, FontStyle.Bold);
        refs.status = Label(root.transform, "Status", "特殊客人 · 可打断", 15, Hex("F3E8DD"),
            new Vector2(0, 0), new Vector2(0, 0), new Vector2(185, 18), new Vector2(220, 24), TextAnchor.MiddleLeft, FontStyle.Normal);
        refs.typeLabel = Label(root.transform, "Type", "特", 17, Hex("F3E8DD"),
            new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-28, 0), new Vector2(46, 46), TextAnchor.MiddleCenter, FontStyle.Bold);
        Save(root, path);
    }

    private static void BuildHubGuestRail(string path)
    {
        var root = ComponentRoot("HubGuestRail", new Vector2(390, 535));
        var refs = root.AddComponent<OutGameHubGuestRailView>();
        refs.title = Label(root.transform, "Title", "VISITOR EVENTS / 访客事件", 16, Hex("E22D76"),
            new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(-35, -20), new Vector2(320, 36), TextAnchor.MiddleLeft, FontStyle.Bold);
        refs.remaining = Label(root.transform, "Remaining", "04", 16, Hex("E22D76"),
            new Vector2(1, 1), new Vector2(1, 1), new Vector2(-22, -20), new Vector2(50, 36), TextAnchor.MiddleRight, FontStyle.Bold);
        refs.cards = new OutGameHubGuestCardView[4];
        for (var i = 0; i < refs.cards.Length; i++)
            refs.cards[i] = InstantiateNested<OutGameHubGuestCardView>(HubGuestCardPath, root.transform, "GuestCard0" + (i + 1),
                new Vector2(.5f, 1), new Vector2(0, -90 - i * 112), new Vector2(390, 100));
        Save(root, path);
    }

    private static void BuildHubDockButton(string path)
    {
        var root = ComponentRoot("HubDockButton", new Vector2(205, 78));
        var refs = root.AddComponent<OutGameHubDockButtonView>();
        refs.background = ImageOn((RectTransform)root.transform, new Color(.025f, .025f, .04f, .75f));
        refs.button = root.AddComponent<Button>();
        refs.button.targetGraphic = refs.background;
        AddTweenFeedback(refs.button);
        refs.icon = Label(root.transform, "Icon", "器", 20, Hex("F3E8DD"), new Vector2(0, 0), new Vector2(.32f, 1), Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter, FontStyle.Bold);
        refs.label = Label(root.transform, "Label", "设备图鉴", 20, Hex("F3E8DD"), new Vector2(.28f, 0), new Vector2(1, 1), Vector2.zero, Vector2.zero, TextAnchor.MiddleLeft, FontStyle.Bold);
        Save(root, path);
    }

    private static void BuildHubRightDock(string path)
    {
        var root = ComponentRoot("HubRightDock", new Vector2(205, 470));
        var refs = root.AddComponent<OutGameHubRightDockView>();
        refs.title = Label(root.transform, "Title", "HOUSE / MENU", 13, Hex("E22D76"), new Vector2(.5f, 1), new Vector2(.5f, 1),
            new Vector2(0, -20), new Vector2(200, 30), TextAnchor.MiddleCenter, FontStyle.Normal);
        refs.entries = new OutGameHubDockButtonView[4];
        for (var i = 0; i < refs.entries.Length; i++)
            refs.entries[i] = InstantiateNested<OutGameHubDockButtonView>(HubDockButtonPath, root.transform, "DockButton0" + (i + 1),
                new Vector2(.5f, 1), new Vector2(0, -82 - i * 92), new Vector2(205, 78));
        Save(root, path);
    }

    private static void BuildHubRoomButton(string path)
    {
        var root = ComponentRoot("HubRoomButton", new Vector2(170, 150));
        var refs = root.AddComponent<OutGameHubRoomButtonView>();
        refs.background = ImageOn((RectTransform)root.transform, new Color(1, 1, 1, .015f));
        refs.button = root.AddComponent<Button>();
        refs.button.targetGraphic = refs.background;
        AddTweenFeedback(refs.button);
        refs.code = Label(root.transform, "Code", "HOME", 12, new Color(1, 1, 1, .72f), new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -22), new Vector2(150, 24), TextAnchor.MiddleCenter, FontStyle.Bold);
        refs.icon = Label(root.transform, "Icon", "⌂", 20, new Color(1, 1, 1, .72f), new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 10), new Vector2(150, 30), TextAnchor.MiddleCenter, FontStyle.Normal);
        refs.roomName = Label(root.transform, "Name", "起居室", 20, new Color(1, 1, 1, .72f), new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 42), new Vector2(150, 30), TextAnchor.MiddleCenter, FontStyle.Bold);
        refs.state = Label(root.transform, "State", "CURRENT", 11, Hex("F3E8DD"), new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 17), new Vector2(150, 20), TextAnchor.MiddleCenter, FontStyle.Bold);
        Save(root, path);
    }

    private static void BuildHubRoomNavigation(string path)
    {
        var root = ComponentRoot("HubRoomNavigation", new Vector2(1030, 150));
        var refs = root.AddComponent<OutGameHubRoomNavigationView>();
        refs.background = ImageOn((RectTransform)root.transform, new Color(.02f, .022f, .04f, .82f));
        refs.title = Label(root.transform, "Title", "MAKE IT HOME", 18, Hex("E22D76"), new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(112, 15), new Vector2(210, 40), TextAnchor.MiddleCenter, FontStyle.Bold);
        refs.hint = Label(root.transform, "Hint", "← → 快速切换", 13, Hex("F3E8DD"), new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(112, -18), new Vector2(210, 30), TextAnchor.MiddleCenter, FontStyle.Normal);
        refs.rooms = new OutGameHubRoomButtonView[4];
        for (var i = 0; i < refs.rooms.Length; i++)
            refs.rooms[i] = InstantiateNested<OutGameHubRoomButtonView>(HubRoomButtonPath, root.transform, "RoomButton0" + (i + 1),
                new Vector2(0, .5f), new Vector2(305 + i * 175, 0), new Vector2(170, 150));
        refs.lockedRoom = InstantiateNested<OutGameHubRoomButtonView>(HubRoomButtonPath, root.transform, "LockedRoom",
            new Vector2(1, .5f), new Vector2(-80, 0), new Vector2(160, 150));
        Save(root, path);
    }

    private static void BuildHubSceneOverlay(string path)
    {
        var root = Root("HubSceneOverlay");
        var refs = root.AddComponent<OutGameHubSceneOverlayView>();
        refs.captionBackground = Image(root.transform, "SceneCaption", new Vector2(0, 0), new Vector2(0, 0), new Vector2(390, 135), new Vector2(310, 84), new Color(.8f, .75f, .67f, .92f));
        refs.captionHeader = Label(refs.captionBackground.transform, "Header", "CURRENT ROOM / 04", 12, Hex("3B2D31"), new Vector2(0, .5f), new Vector2(1, 1), new Vector2(0, -4), new Vector2(-20, -10), TextAnchor.MiddleCenter, FontStyle.Bold);
        refs.roomName = Label(refs.captionBackground.transform, "RoomName", "起居室", 23, Hex("3B2D31"), new Vector2(0, 0), new Vector2(.48f, .68f), Vector2.zero, new Vector2(-10, -5), TextAnchor.MiddleRight, FontStyle.Bold);
        refs.roomNote = Label(refs.captionBackground.transform, "RoomNote", "家人会在这里等待服务", 14, Hex("3B2D31"), new Vector2(.48f, 0), new Vector2(1, .68f), Vector2.zero, new Vector2(-10, -5), TextAnchor.MiddleLeft, FontStyle.Normal);
        refs.hotspotButton = PageButton(root.transform, "Hotspot", "＋  黑胶唱机\n<size=13>查看设备</size>", new Vector2(0, 30), new Vector2(220, 76), new Color(.2f, .03f, .15f, .75f), Hex("F3E8DD"), 19, TextAnchor.MiddleCenter, new Vector2(.72f, .5f));
        refs.hotspotTitle = refs.hotspotButton.GetComponentInChildren<Text>();
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
        EmbedHubComponents(refs);
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
        AddTweenFeedback(button);
        var label = Label(image.transform, "Label", caption, fontSize, foreground, TextAnchor.MiddleCenter, FontStyle.Bold);
        label.alignment = alignment;
        label.rectTransform.offsetMin = new Vector2(14, 8);
        label.rectTransform.offsetMax = new Vector2(-14, -8);
        return button;
    }

    private static OutGameTweenButton AddTweenFeedback(Button button, float hoverScale = 1.025f)
    {
        var feedback = button.GetComponent<OutGameTweenButton>();
        if (feedback == null) feedback = button.gameObject.AddComponent<OutGameTweenButton>();
        feedback.hoverScale = hoverScale;
        return feedback;
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

    /// <summary>Hub「收起界面」开关按钮。</summary>
    private static void BuildHubImmersiveToggle(string path)
    {
        var root = ComponentRoot("HubImmersiveToggle", new Vector2(160, 58));
        var view = root.AddComponent<OutGameHubImmersiveToggleView>();
        var image = ImageOn((RectTransform)root.transform, new Color(.025f, .025f, .04f, .8f));
        view.button = root.AddComponent<Button>();
        view.button.targetGraphic = image;
        AddTweenFeedback(view.button);
        view.label = Label(root.transform, "Label", "收起界面", 17, Hex("F3E8DD"), TextAnchor.MiddleCenter, FontStyle.Bold);
        view.label.rectTransform.offsetMin = new Vector2(8, 6);
        view.label.rectTransform.offsetMax = new Vector2(-8, -6);
        Save(root, path);
    }

    /// <summary>「日程与时间」面板内容。日期格子数量随月份变化，留 DayGrid 由运行时填充。</summary>
    private static void BuildCalendarPanel(string path)
    {
        var root = ComponentRoot("CalendarPanel", new Vector2(1180, 830));
        var view = root.AddComponent<OutGameCalendarPanelView>();
        var date = Image(root.transform, "BigDate", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
            new Vector2(-385, 195), new Vector2(340, 330), new Color(.34f, .07f, .22f, .65f));
        view.dateText = Label(date.transform, "DateText",
            "2026 / 八月\n<size=100>06</size>\n星期四 · 晚上\n<size=28>18:00</size>",
            20, Hex("F3E8DD"), TextAnchor.MiddleCenter, FontStyle.Bold);
        view.dayGridRoot = Rect(root.transform, "DayGrid", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        // 6×7=42 个日期槽位烘焙进 Prefab，布局可手调；运行时只设置数字/显隐/今日高亮
        view.dayCells = new Button[42];
        view.dayCellBackgrounds = new Image[42];
        view.dayCellLabels = new Text[42];
        for (var i = 0; i < 42; i++)
        {
            var col = i % 7;
            var row = i / 7;
            var cell = PageButton(view.dayGridRoot, "DayCell" + i, (i % 31 + 1).ToString(),
                new Vector2(-180 + col * 64, -90 - row * 64), new Vector2(58, 54),
                new Color(1, 1, 1, .035f), Hex("F3E8DD"), 16, TextAnchor.MiddleCenter, new Vector2(.5f, 1));
            view.dayCells[i] = cell;
            view.dayCellBackgrounds[i] = cell.targetGraphic as Image;
            view.dayCellLabels[i] = cell.GetComponentInChildren<Text>();
        }
        var schedule = Image(root.transform, "Schedule", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
            new Vector2(405, 20), new Vector2(330, 690), new Color(.08f, .04f, .075f, .86f));
        view.scheduleTitle = Label(schedule.transform, "ScheduleTitle", "现实时间阶段", 24, Hex("F3E8DD"),
            new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -40), new Vector2(270, 40),
            TextAnchor.MiddleCenter, FontStyle.Bold);
        view.phaseBackgrounds = new Image[6];
        view.phaseLabels = new Text[6];
        for (var i = 0; i < 6; i++)
        {
            var row = Image(schedule.transform, "Phase" + i, new Vector2(.5f, 1), new Vector2(.5f, 1),
                new Vector2(0, -105 - i * 75), new Vector2(290, 62), new Color(1, 1, 1, .035f));
            view.phaseBackgrounds[i] = row;
            var label = Label(row.transform, "Label", string.Empty, 16, Hex("F3E8DD"), TextAnchor.MiddleLeft, FontStyle.Bold);
            label.rectTransform.offsetMin = new Vector2(14, 6);
            label.rectTransform.offsetMax = new Vector2(-14, -6);
            view.phaseLabels[i] = label;
        }
        view.syncButton = PageButton(schedule.transform, "Sync", "同步现实时间", new Vector2(0, 68),
            new Vector2(260, 56), Hex("6E243E"), Hex("F3E8DD"), 18, TextAnchor.MiddleCenter, new Vector2(.5f, 0));
        Save(root, path);
    }

    /// <summary>「今日委托」面板内容。</summary>
    private static void BuildTasksPanel(string path)
    {
        var root = ComponentRoot("TasksPanel", new Vector2(1180, 830));
        var view = root.AddComponent<OutGameTasksPanelView>();
        var focus = Image(root.transform, "Focus", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
            new Vector2(0, 270), new Vector2(1120, 220), new Color(.3f, .06f, .2f, .45f));
        view.focusText = Label(focus.transform, "Text", string.Empty, 20, Hex("F3E8DD"),
            new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-30, 0), new Vector2(970, 175),
            TextAnchor.MiddleLeft, FontStyle.Normal);
        view.taskButtons = new Button[3];
        view.taskLabels = new Text[3];
        for (var i = 0; i < 3; i++)
        {
            var button = PageButton(root.transform, "Task" + i, string.Empty, new Vector2(0, 75 - i * 100),
                new Vector2(1120, 84), new Color(1, 1, 1, .035f), Hex("F3E8DD"), 20, TextAnchor.MiddleLeft, new Vector2(.5f, .5f));
            view.taskButtons[i] = button;
            view.taskLabels[i] = button.GetComponentInChildren<Text>();
        }
        var progress = Image(root.transform, "Progress", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
            new Vector2(0, -305), new Vector2(1120, 105), new Color(.12f, .06f, .1f, .8f));
        view.progressText = Label(progress.transform, "Text",
            "本周 House 进度                                      37%\n<color=#E22D76>━━━━━━━━━━━━━━━━━━━━</color>",
            19, Hex("F3E8DD"), TextAnchor.MiddleCenter, FontStyle.Bold);
        Save(root, path);
    }

    /// <summary>访客对话界面（整层）。</summary>
    private static void BuildDialogueView(string path)
    {
        var root = Root("DialogueView");
        var view = root.AddComponent<OutGameDialogueView>();
        view.sceneArt = Raw(root.transform, "VisitorScene", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        view.sceneArt.color = new Color(.72f, .72f, .78f, 1);
        Image(root.transform, "DialogueVignette", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
            new Color(.01f, .01f, .025f, .48f));
        view.closeButton = PageButton(root.transform, "Close", "ESC · 结束交谈", new Vector2(-145, -55),
            new Vector2(240, 54), new Color(.08f, .02f, .06f, .78f), Hex("F3E8DD"), 17, TextAnchor.MiddleCenter, new Vector2(1, 1));
        view.characterCard = Rect(root.transform, "CharacterCard", new Vector2(0, .5f), new Vector2(0, .5f),
            new Vector2(390, 40), new Vector2(500, 700));
        ImageOn(view.characterCard, new Color(.16f, .05f, .12f, .82f));
        view.portrait = Raw(view.characterCard, "Portrait", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
            Vector2.zero, new Vector2(460, 620));
        view.portraitTag = Label(view.characterCard, "Tag", "VISITOR / WEEK 01", 15, Hex("E22D76"),
            new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 25), new Vector2(420, 30),
            TextAnchor.MiddleCenter, FontStyle.Bold);
        var week = Image(root.transform, "WeekPanel", new Vector2(1, .5f), new Vector2(1, .5f),
            new Vector2(-245, 90), new Vector2(410, 540), new Color(.02f, .025f, .045f, .85f));
        view.weekTitle = Label(week.transform, "Title", "WEEK 01 / 本周访客", 16, Hex("E22D76"),
            new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -35), new Vector2(350, 40),
            TextAnchor.MiddleCenter, FontStyle.Bold);
        view.weekGuestButtons = new Button[4];
        view.weekGuestBackgrounds = new Image[4];
        view.weekGuestLabels = new Text[4];
        for (var i = 0; i < 4; i++)
        {
            var button = PageButton(week.transform, "WeekGuest" + i, string.Empty, new Vector2(0, -105 - i * 100),
                new Vector2(355, 82), new Color(1, 1, 1, .035f), Hex("F3E8DD"), 19, TextAnchor.MiddleLeft, new Vector2(.5f, 1));
            view.weekGuestButtons[i] = button;
            view.weekGuestBackgrounds[i] = button.targetGraphic as Image;
            view.weekGuestLabels[i] = button.GetComponentInChildren<Text>();
        }
        view.dialogueBox = Rect(root.transform, "DialogueBox", new Vector2(.5f, 0), new Vector2(.5f, 0),
            new Vector2(80, 190), new Vector2(1120, 250));
        ImageOn(view.dialogueBox, new Color(.035f, .025f, .045f, .94f));
        view.dialogueText = Label(view.dialogueBox, "Dialogue", string.Empty, 22, Hex("F3E8DD"),
            new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-90, 10), new Vector2(850, 205),
            TextAnchor.UpperLeft, FontStyle.Normal);
        view.needButton = PageButton(view.dialogueBox, "Need", "查看需求家具", new Vector2(-170, 82),
            new Vector2(250, 58), new Color(1, 1, 1, .05f), Hex("F3E8DD"), 18, TextAnchor.MiddleCenter, new Vector2(1, 0));
        view.serveButton = PageButton(view.dialogueBox, "Serve", "回应访客事件", new Vector2(-170, 25),
            new Vector2(250, 58), Hex("6E243E"), Hex("F3E8DD"), 18, TextAnchor.MiddleCenter, new Vector2(1, 0));
        view.serveLabel = view.serveButton.GetComponentInChildren<Text>();
        view.refuseButton = PageButton(view.dialogueBox, "Refuse", "拒绝接待", new Vector2(-425, 25),
            new Vector2(230, 58), new Color(1, 1, 1, .05f), Hex("F3E8DD"), 17, TextAnchor.MiddleCenter, new Vector2(1, 0));
        view.refuseLabel = view.refuseButton.GetComponentInChildren<Text>();
        var dock = Image(root.transform, "FurnitureDock", new Vector2(.5f, 0), new Vector2(.5f, 0),
            new Vector2(0, 45), new Vector2(1480, 90), new Color(.015f, .018f, .032f, .93f));
        view.furnitureTitle = Label(dock.transform, "Title", "MAKE FOR VISITOR\n<size=11>根据来客需求制造并摆放</size>",
            15, Hex("E22D76"), new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(130, 0), new Vector2(240, 62),
            TextAnchor.MiddleCenter, FontStyle.Bold);
        view.furnitureButtons = new Button[5];
        view.furnitureBackgrounds = new Image[5];
        view.furnitureLabels = new Text[5];
        for (var i = 0; i < 5; i++)
        {
            var button = PageButton(dock.transform, "Furniture" + i, string.Empty, new Vector2(335 + i * 205, 0),
                new Vector2(195, 70), new Color(1, 1, 1, .035f), Hex("F3E8DD"), 15, TextAnchor.MiddleCenter, new Vector2(0, .5f));
            view.furnitureButtons[i] = button;
            view.furnitureBackgrounds[i] = button.targetGraphic as Image;
            view.furnitureLabels[i] = button.GetComponentInChildren<Text>();
        }
        view.endWeekButton = PageButton(dock.transform, "EndWeek", "结束本周 →", new Vector2(-100, 0),
            new Vector2(180, 70), Hex("6E243E"), Hex("F3E8DD"), 17, TextAnchor.MiddleCenter, new Vector2(1, .5f));
        Save(root, path);
    }

    /// <summary>「设备图鉴」面板内容。设备卡数量随房间变化，留 DeviceCards 由运行时填充。</summary>
    private static void BuildDevicePanelContent(string path)
    {
        var root = ComponentRoot("DevicePanel", new Vector2(1180, 830));
        var view = root.AddComponent<OutGameDevicePanelView>();
        view.roomButtons = new Button[4];
        view.roomBackgrounds = new Image[4];
        view.roomLabels = new Text[4];
        for (var i = 0; i < 4; i++)
        {
            var button = PageButton(root.transform, "DeviceRoom" + i, string.Empty, new Vector2(115, -70 - i * 98),
                new Vector2(210, 82), new Color(1, 1, 1, .035f), Hex("F3E8DD"), 19, TextAnchor.MiddleCenter, new Vector2(0, 1));
            view.roomButtons[i] = button;
            view.roomBackgrounds[i] = button.targetGraphic as Image;
            view.roomLabels[i] = button.GetComponentInChildren<Text>();
        }
        view.deviceCardsRoot = Rect(root.transform, "DeviceCards", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var recipe = Image(root.transform, "Recipe", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
            new Vector2(230, -230), new Vector2(610, 270), new Color(.18f, .07f, .14f, .82f));
        view.recipeText = Label(recipe.transform, "RecipeText", string.Empty, 20, Hex("F3E8DD"),
            new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 35), new Vector2(540, 175),
            TextAnchor.MiddleLeft, FontStyle.Normal);
        view.makeButton = PageButton(recipe.transform, "Make", "开始制作", new Vector2(0, 35), new Vector2(280, 58),
            Hex("6E243E"), Hex("F3E8DD"), 19, TextAnchor.MiddleCenter, new Vector2(.5f, 0));
        view.makeLabel = view.makeButton.GetComponentInChildren<Text>();
        Save(root, path);
    }

    /// <summary>「日记与成就」面板内容。文章/成就列表随页签变化，留 Body 由运行时填充。</summary>
    private static void BuildJournalPanelContent(string path)
    {
        var root = ComponentRoot("JournalPanel", new Vector2(1180, 830));
        var view = root.AddComponent<OutGameJournalPanelView>();
        view.tabButtons = new Button[2];
        view.tabBackgrounds = new Image[2];
        view.tabLabels = new Text[2];
        var captions = new[] { "日记", "成就" };
        for (var i = 0; i < 2; i++)
        {
            var button = PageButton(root.transform, i == 0 ? "LogTab" : "AchTab", captions[i],
                new Vector2(140 + i * 240, -45), new Vector2(220, 58),
                i == 0 ? Hex("6E243E") : new Color(1, 1, 1, .04f), Hex("F3E8DD"), 20, TextAnchor.MiddleCenter, new Vector2(0, 1));
            view.tabButtons[i] = button;
            view.tabBackgrounds[i] = button.targetGraphic as Image;
            view.tabLabels[i] = button.GetComponentInChildren<Text>();
        }
        view.bodyRoot = Rect(root.transform, "Body", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Save(root, path);
    }

    /// <summary>「叙事资源档案」面板内容。条目格子与底部操作区随页签/选中项变化，留挂点由运行时填充。</summary>
    private static void BuildArchivePanelContent(string path)
    {
        var root = ComponentRoot("ArchivePanel", new Vector2(1180, 830));
        var view = root.AddComponent<OutGameArchivePanelView>();
        view.tabButtons = new Button[2];
        view.tabBackgrounds = new Image[2];
        view.tabLabels = new Text[2];
        var captions = new[] { "叙事家具", "世界与角色" };
        for (var i = 0; i < 2; i++)
        {
            var button = PageButton(root.transform, i == 0 ? "FurnitureTab" : "WorldTab", captions[i],
                new Vector2(130 + i * 240, -45), new Vector2(220, 58),
                i == 0 ? Hex("6E243E") : new Color(1, 1, 1, .04f), Hex("F3E8DD"), 19, TextAnchor.MiddleCenter, new Vector2(0, 1));
            view.tabButtons[i] = button;
            view.tabBackgrounds[i] = button.targetGraphic as Image;
            view.tabLabels[i] = button.GetComponentInChildren<Text>();
        }
        view.gridRoot = Rect(root.transform, "Grid", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var detail = Image(root.transform, "ArchiveDetail", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
            new Vector2(300, -20), new Vector2(650, 730), new Color(.09f, .04f, .075f, .9f));
        view.detailPreview = Raw(detail.transform, "Preview", new Vector2(.5f, 1), new Vector2(.5f, 1),
            new Vector2(0, -180), new Vector2(590, 300));
        view.detailText = Label(detail.transform, "DetailText", string.Empty, 19, Hex("F3E8DD"),
            new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, -120), new Vector2(560, 230),
            TextAnchor.UpperLeft, FontStyle.Normal);
        view.actionRoot = Rect(detail.transform, "Actions", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Save(root, path);
    }

    /// <summary>整页系统面板：公共外壳（遮罩/面板/头部）+ 嵌套内容 Prefab。</summary>
    private static void BuildPanelPage(string path, string pageName, string eyebrow, string title, string mark,
        string contentPrefabPath)
    {
        var root = Root(pageName);
        var view = root.AddComponent<OutGamePanelPageView>();
        view.scrim = Image(root.transform, "Scrim", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
            new Color(.005f, .008f, .02f, .62f));
        view.scrimButton = view.scrim.gameObject.AddComponent<Button>();
        view.scrimButton.targetGraphic = view.scrim;
        view.panel = Image(root.transform, "Panel", new Vector2(1, .5f), new Vector2(1, .5f),
            new Vector2(-640, 0), new Vector2(1280, 1080), new Color(.055f, .045f, .06f, .98f));
        var header = Image(view.panel.transform, "Header", new Vector2(.5f, 1), new Vector2(.5f, 1),
            new Vector2(0, -75), new Vector2(1280, 150), new Color(.1f, .045f, .085f, .95f));
        view.backButton = PageButton(header.transform, "Back", "←\n<size=12>ESC</size>", new Vector2(58, 0),
            new Vector2(86, 90), new Color(1, 1, 1, .04f), Hex("F3E8DD"), 25, TextAnchor.MiddleCenter, new Vector2(0, .5f));
        view.headerTitle = Label(header.transform, "Title", $"<size=14>{eyebrow}</size>\n{title}", 34, Hex("F3E8DD"),
            new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(330, 0), new Vector2(430, 95),
            TextAnchor.MiddleLeft, FontStyle.Bold);
        view.headerMark = Label(header.transform, "Mark", mark, 54, new Color(1, .35f, .62f, .55f),
            new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-95, 0), new Vector2(100, 90),
            TextAnchor.MiddleCenter, FontStyle.Bold);
        view.contentRoot = InstantiateNested<RectTransform>(contentPrefabPath, view.panel.transform, "Content",
            new Vector2(.5f, .5f), new Vector2(0, -75), new Vector2(1180, 830));
        Save(root, path);
    }

    // ── 3.5c 动态列表项模板（默认视觉与旧运行时代码逐参数一致，生成后可在编辑器手调）──

    private static void BuildDeviceCard(string path)
    {
        var root = ComponentRoot("DeviceCard", new Vector2(245, 270));
        var refs = root.AddComponent<DeviceCardView>();
        refs.background = ImageOn((RectTransform)root.transform, new Color(1, 1, 1, .045f));
        refs.button = root.AddComponent<Button>();
        refs.button.targetGraphic = refs.background;
        AddTweenFeedback(refs.button);
        refs.label = Label(root.transform, "Label", "⚙\n<size=13>LV.2 · 可使用</size>\n黑胶唱机\n<size=14>舒缓情绪</size>",
            21, Hex("F3E8DD"), TextAnchor.MiddleCenter, FontStyle.Bold);
        Save(root, path);
    }

    private static void BuildArchiveCard(string path)
    {
        var root = ComponentRoot("ArchiveCard", new Vector2(215, 215));
        var refs = root.AddComponent<ArchiveCardView>();
        refs.background = ImageOn((RectTransform)root.transform, new Color(1, 1, 1, .04f));
        refs.button = root.AddComponent<Button>();
        refs.button.targetGraphic = refs.background;
        AddTweenFeedback(refs.button);
        refs.label = Label(root.transform, "Label", "01 / 回应家具\n鲸声电话亭\n<size=13>洛恩</size>",
            17, Hex("F3E8DD"), TextAnchor.LowerCenter, FontStyle.Bold);
        refs.art = Raw(root.transform, "Art", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -70), new Vector2(180, 110));
        Save(root, path);
    }

    private static void BuildJournalArticle(string path)
    {
        var root = ComponentRoot("JournalArticle", new Vector2(530, 540));
        var refs = root.AddComponent<JournalArticleView>();
        refs.background = ImageOn((RectTransform)root.transform, new Color(.08f, .04f, .075f, .86f));
        var outline = root.AddComponent<Outline>();
        outline.effectColor = new Color(.7f, .2f, .45f, .28f);
        outline.effectDistance = new Vector2(1, -1);
        refs.text = Label(root.transform, "Text",
            "<color=#E22D76><size=14>06 / 17 · 雨转晴</size></color>\n<size=29>窗户唱回来的那句话</size>\n\n正文……",
            20, Hex("F3E8DD"), new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(450, 450),
            TextAnchor.UpperLeft, FontStyle.Normal);
        Save(root, path);
    }

    private static void BuildAchievementRow(string path)
    {
        var root = ComponentRoot("AchievementRow", new Vector2(520, 170));
        var refs = root.AddComponent<AchievementRowView>();
        refs.background = ImageOn((RectTransform)root.transform, new Color(1, 1, 1, .035f));
        refs.label = Label(root.transform, "Label", "✓     夜的主人\n<size=15>          在深夜完成一次服务</size>",
            23, Hex("F3E8DD"), TextAnchor.MiddleLeft, FontStyle.Bold);
        Save(root, path);
    }

    private static GameObject ComponentRoot(string name, Vector2 size)
    {
        var root = new GameObject(name, typeof(RectTransform));
        root.layer = 5;
        var rect = (RectTransform)root.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = size;
        return root;
    }

    private static T InstantiateNested<T>(string path, Transform parent, string name, Vector2 anchor,
        Vector2 position, Vector2 size) where T : Component
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null) throw new MissingReferenceException("生成父 Prefab 前缺少子 Prefab：" + path);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
        instance.name = name;
        var rect = instance.transform as RectTransform;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
        var view = instance.GetComponent<T>();
        if (view == null) throw new MissingReferenceException(path + " 缺少 " + typeof(T).Name);
        return view;
    }

    private static T EnsureHubNested<T>(Transform parent, string path, string name, Vector2 anchor,
        Vector2 position, Vector2 size) where T : Component
    {
        var existing = parent.Find(name);
        if (existing != null)
        {
            var component = existing.GetComponent<T>();
            if (component != null) return component;
            Object.DestroyImmediate(existing.gameObject);
        }
        return InstantiateNested<T>(path, parent, name, anchor, position, size);
    }

    private static void EmbedHubComponents(OutGameHubView view)
    {
        view.topBar = EnsureHubNested<OutGameHubTopBarView>(view.chromeRoot, HubTopBarPath, "TopHUD",
            new Vector2(.5f, 1), new Vector2(0, -62), new Vector2(1920, 124));
        view.taskCard = EnsureHubNested<OutGameHubTaskCardView>(view.chromeRoot, HubTaskCardPath, "VisitorTask",
            new Vector2(0, 1), new Vector2(228, -250), new Vector2(390, 255));
        view.guestRail = EnsureHubNested<OutGameHubGuestRailView>(view.chromeRoot, HubGuestRailPath, "GuestRail",
            new Vector2(0, 1), new Vector2(228, -650), new Vector2(390, 535));
        view.rightDock = EnsureHubNested<OutGameHubRightDockView>(view.chromeRoot, HubRightDockPath, "RightDock",
            new Vector2(1, .5f), new Vector2(-120, 10), new Vector2(205, 470));
        view.roomNavigation = EnsureHubNested<OutGameHubRoomNavigationView>(view.chromeRoot, HubRoomNavigationPath, "RoomNav",
            new Vector2(.5f, 0), new Vector2(90, 90), new Vector2(1030, 150));
        view.sceneOverlay = EnsureHubNested<OutGameHubSceneOverlayView>(view.chromeRoot, HubSceneOverlayPath, "SceneOverlay",
            new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
        var sceneOverlayRect = view.sceneOverlay.transform as RectTransform;
        sceneOverlayRect.anchorMin = Vector2.zero;
        sceneOverlayRect.anchorMax = Vector2.one;
        sceneOverlayRect.offsetMin = sceneOverlayRect.offsetMax = Vector2.zero;
        if (view.footer != null) view.footer.transform.SetAsLastSibling();
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

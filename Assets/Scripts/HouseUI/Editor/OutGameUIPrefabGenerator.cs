#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
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
        // 需求交付页（2026-08-12 落地）：整页 + 仓库条目模板（§16.2 动态列表项）
        private const string CalendarPagePath = Folder + "/CalendarPage.prefab";
        private const string TasksPagePath = Folder + "/TasksPage.prefab";
        private const string DevicePagePath = Folder + "/DevicePage.prefab";
        private const string JournalPagePath = Folder + "/JournalPage.prefab";
        private const string ArchivePagePath = Folder + "/ArchivePage.prefab";
        // 访客图鉴（2026-08-18 按 2.0 设计图）：整屏 Illustrated Guide 页，从档案页进入
        private const string CodexPagePath = Folder + "/CodexPage.prefab";
        // ESC 系统菜单（2026-08-19 按 2.0 设计图）
        private const string EscMenuPath = Folder + "/EscMenu.prefab";
        // 图鉴详情页（2026-08-19 按 2.0 设计图）
        private const string CodexDetailPath = Folder + "/CodexDetail.prefab";
        // 3.5c：动态列表项模板（§16.2 列表项 = Prefab 模板 + 运行时实例化），供重写版 HouseUI 面板使用
        private const string DeviceCardPath = Folder + "/DeviceCard.prefab";
        private const string ArchiveCardPath = Folder + "/ArchiveCard.prefab";
        private const string JournalArticlePath = Folder + "/JournalArticle.prefab";
        private const string AchievementRowPath = Folder + "/AchievementRow.prefab";
        // 3.6：商城补 Prefab 与统一占位页（§16.8）
        // 访客系统重做：当日结算面板（访客交付说明 §7）
        private const string DaySettlePanelPath = Folder + "/DaySettlePanel.prefab";
        // 商店重做（2026-08-11 美术示意图）：全屏 STORE 页 + 卡片模板，取代旧 MarketPage 三件套
        private const string StorePagePath = Folder + "/StorePage.prefab";
        private const string StoreCardPath = Folder + "/StoreCard.prefab";
        // 家具族化（2026-08-15）：配色色块模板，商城选色行 / 获得弹窗配色列 / 收纳栏槽位共用
        private const string ColorSwatchPath = Folder + "/ColorSwatch.prefab";
        // 家具模式 HUD 固化为 Prefab（2026-08-11）：页面 + 槽位模板
        private const string FurnitureHudPath = Folder + "/FurnitureHudPage.prefab";
        private const string FurnitureSlotPath = Folder + "/FurnitureSlot.prefab";
        private const string PlaceholderPanelPath = Folder + "/PlaceholderPanel.prefab";
        private const string PlaceholderPagePath = Folder + "/PlaceholderPage.prefab";
        // 结束今天确认弹窗 + 开始新一天日出过场（2026-08-14）
        private const string ConfirmPopupPath = Folder + "/ConfirmPopup.prefab";
        private const string DayTransitionPath = Folder + "/DayTransition.prefab";
        // Hub 场景世界层（主楼剖面 + 房间矩形，2026-08-16 场景固化）
        private const string HubSceneWorldPath = Folder + "/HubSceneWorld.prefab";

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
            BuildDaySettlePanel(DaySettlePanelPath);
            BuildConfirmPopup(ConfirmPopupPath);
            BuildDayTransition(DayTransitionPath);
            BuildHubSceneWorld(HubSceneWorldPath);
            BuildPanelPage(CalendarPagePath, "CalendarPage", "REAL TIME", "日程与时间", "历", CalendarPanelPath);
            BuildPanelPage(TasksPagePath, "TasksPage", "TODAY / 03", "今日委托", "任", TasksPanelPath);
            BuildPanelPage(DevicePagePath, "DevicePage", "HOUSE INDEX", "家具图鉴", "家", DevicePanelPath);
            BuildPanelPage(JournalPagePath, "JournalPage", "MEMORY LOG", "日记与成就", "记", JournalPanelPath);
            BuildPanelPage(ArchivePagePath, "ArchivePage", "HOUSE ARCHIVE", "叙事资源档案", "集", ArchivePanelPath);
            BuildDeviceCard(DeviceCardPath);
            BuildArchiveCard(ArchiveCardPath);
            BuildJournalArticle(JournalArticlePath);
            BuildAchievementRow(AchievementRowPath);
            BuildStoreCard(StoreCardPath);
            BuildStorePage(StorePagePath);
            BuildColorSwatch(ColorSwatchPath);
            BuildFurnitureSlot(FurnitureSlotPath);
            BuildFurnitureHudPage(FurnitureHudPath);
            BuildPlaceholderPanelContent(PlaceholderPanelPath);
            BuildPanelPage(PlaceholderPagePath, "PlaceholderPage", "COMING SOON", "尚未开放", "待", PlaceholderPanelPath);
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
            else
            {
                // 设置页 2.0 重做（2026-08-18 用户定案：按新设计图全部替换）：
                // 检测到旧结构（无 rowsRoot / 分页仍是 7 个 / tab 素材还是 1.0）时整页重建覆盖
                var settingsRoot = AssetDatabase.LoadAssetAtPath<GameObject>(SettingsPagePath);
                var settingsView = settingsRoot != null ? settingsRoot.GetComponent<OutGameSettingsPageView>() : null;
                if (settingsView == null || settingsView.rowsRoot == null ||
                    settingsView.tabButtons == null || settingsView.tabButtons.Length != 5 ||
                    settingsView.tabNormal == null || !settingsView.tabNormal.name.StartsWith("一级tab"))
                {
                    BuildSettingsPage(SettingsPagePath);
                    changed = true;
                }
            }
            if (!File.Exists(CodexPagePath)) { BuildCodexPage(CodexPagePath); changed = true; }
            if (!File.Exists(EscMenuPath)) { BuildEscMenu(EscMenuPath); changed = true; }
            if (!File.Exists(CodexDetailPath)) { BuildCodexDetail(CodexDetailPath); changed = true; }
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
            else
            {
                // 对话页 2.0 重做（2026-08-19 用户定案：按新设计图全部替换）：
                // 检测到 1.0 结构（缺 2.0 才有的键位按钮 / 选项槽还没有悬停底图）时整层重建覆盖。
                // 1.0 的 Prefab 引用的是已删掉的字段（GUEST 标题、撕边压暗层等），留着也是坏的
                var dialogueRoot = AssetDatabase.LoadAssetAtPath<GameObject>(DialogueViewPath);
                var dialogueView = dialogueRoot != null ? dialogueRoot.GetComponent<OutGameDialogueView>() : null;
                var firstSlot = dialogueView != null && dialogueView.optionsRoot != null
                    ? dialogueView.optionsRoot.GetComponentInChildren<DialogueOptionView>(true)
                    : null;
                if (dialogueView == null || dialogueView.cycleButton == null ||
                    dialogueView.confirmButton == null || firstSlot == null || firstSlot.hoverBackground == null)
                {
                    BuildDialogueView(DialogueViewPath);
                    changed = true;
                }
            }
            if (!File.Exists(DaySettlePanelPath)) { BuildDaySettlePanel(DaySettlePanelPath); changed = true; }
            if (!File.Exists(ConfirmPopupPath)) { BuildConfirmPopup(ConfirmPopupPath); changed = true; }
            if (!File.Exists(DayTransitionPath)) { BuildDayTransition(DayTransitionPath); changed = true; }
            if (!File.Exists(HubSceneWorldPath)) { BuildHubSceneWorld(HubSceneWorldPath); changed = true; }
            if (!File.Exists(CalendarPagePath)) { BuildPanelPage(CalendarPagePath, "CalendarPage", "REAL TIME", "日程与时间", "历", CalendarPanelPath); changed = true; }
            if (!File.Exists(TasksPagePath)) { BuildPanelPage(TasksPagePath, "TasksPage", "TODAY / 03", "今日委托", "任", TasksPanelPath); changed = true; }
            if (!File.Exists(DevicePagePath)) { BuildPanelPage(DevicePagePath, "DevicePage", "HOUSE INDEX", "家具图鉴", "家", DevicePanelPath); changed = true; }
            if (!File.Exists(JournalPagePath)) { BuildPanelPage(JournalPagePath, "JournalPage", "MEMORY LOG", "日记与成就", "记", JournalPanelPath); changed = true; }
            if (!File.Exists(ArchivePagePath)) { BuildPanelPage(ArchivePagePath, "ArchivePage", "HOUSE ARCHIVE", "叙事资源档案", "集", ArchivePanelPath); changed = true; }
            if (!File.Exists(DeviceCardPath)) { BuildDeviceCard(DeviceCardPath); changed = true; }
            if (!File.Exists(ArchiveCardPath)) { BuildArchiveCard(ArchiveCardPath); changed = true; }
            if (!File.Exists(JournalArticlePath)) { BuildJournalArticle(JournalArticlePath); changed = true; }
            if (!File.Exists(AchievementRowPath)) { BuildAchievementRow(AchievementRowPath); changed = true; }
            // 只补缺失，**绝不覆盖已存在的 Prefab**（手调布局是唯一真相源）。
            // 商店 2.0 的整页重建改由菜单显式触发：Tools → MasterHouse → OutGame UI → 重建商店页（2.0）
            if (!File.Exists(StoreCardPath)) { BuildStoreCard(StoreCardPath); changed = true; }
            if (!File.Exists(StorePagePath)) { BuildStorePage(StorePagePath); changed = true; }
            if (!File.Exists(ColorSwatchPath)) { BuildColorSwatch(ColorSwatchPath); changed = true; }
            if (!File.Exists(FurnitureSlotPath)) { BuildFurnitureSlot(FurnitureSlotPath); changed = true; }
            if (!File.Exists(FurnitureHudPath)) { BuildFurnitureHudPage(FurnitureHudPath); changed = true; }
            if (!File.Exists(PlaceholderPanelPath)) { BuildPlaceholderPanelContent(PlaceholderPanelPath); changed = true; }
            if (!File.Exists(PlaceholderPagePath)) { BuildPanelPage(PlaceholderPagePath, "PlaceholderPage", "COMING SOON", "尚未开放", "待", PlaceholderPanelPath); changed = true; }
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
            // \u6807\u9898\u9875 2.0\uff082026-08-19\uff09\uff1a\u68c0\u5230\u65e7\u7ed3\u6784\uff08\u5c01\u9762\u4e0d\u662f 2.0 \u90a3\u5f20 / \u83dc\u5355\u4e0d\u662f\u56db\u9879\uff09
            // \u5c31\u6574\u9875\u91cd\u5efa\u4e00\u6b21\u3002\u65e7\u7684 RepairTitle/MigrateTitleLogin \u5df2\u9000\u5f79\u2014\u2014\u4e24\u5957\u7ed3\u6784\u5b8c\u5168\u4e0d\u540c\uff0c
            // \u8ddf\u7740\u8dd1\u53ea\u4f1a\u628a\u65b0\u9875\u9762\u6539\u56de\u65e7\u6837\u5b50\u3002
            {
                var titleRoot = AssetDatabase.LoadAssetAtPath<GameObject>(TitlePath);
                var titleView = titleRoot != null ? titleRoot.GetComponent<OutGameTitleView>() : null;
                if (titleView == null || titleView.titleArt == null ||
                    titleView.menuButtons == null || titleView.menuButtons.Length != 4 ||
                    titleView.cover == null || titleView.cover.texture == null ||
                    titleView.cover.texture.name != "image 293")
                {
                    BuildTitle(TitlePath);
                    repaired = true;
                }
            }
            repaired |= RepairPrefab<OutGamePaperView>(PaperPath, RepairPaper);
            repaired |= RepairPrefab<OutGameSavePageView>(SavePagePath, RepairSavePage);
            repaired |= RepairPrefab<OutGameGalleryPageView>(GalleryPagePath, RepairGalleryPage);
            // 2.0 设置页只做「引用掉了就重绑」+ 两处行模板缺陷的定点修补：
            // 结构性升级交给菜单「重建设置页」，这里绝不整页覆盖手调布局
            repaired |= RepairPrefab<OutGameSettingsPageView>(SettingsPagePath,
                (root, view) =>
                {
                    if (view.rowsRoot == null || view.headerTemplate == null) RepairSettingsPage(root, view);
                    MigrateSettings2Rows(view);
                },
                view => view.rowsRoot != null && view.tabButtons != null && view.tabButtons.Length == 5 &&
                        (view.applyButton == null || view.headerTemplate == null || view.background == null ||
                         Settings2RowsNeedFix(view)));
            repaired |= RepairPrefab<OutGameExitPageView>(ExitPagePath, RepairExitPage);
            repaired |= RepairPrefab<OutGameSaveSlotView>(SaveSlotPath, RepairSaveSlot);
            repaired |= RepairPrefab<OutGameHubView>(HubPath, RepairHub, view => view.topBar == null ||
                view.taskCard == null || view.guestRail == null || view.rightDock == null ||
                view.roomNavigation == null || view.sceneOverlay == null);
            repaired |= RepairPrefab<OutGameSystemPanelView>(PanelPath, RepairSystemPanel);
            // 右侧 dock：把运行时生成的「家具摆放/结束今天」按钮收编进 Prefab（只补缺失节点，不动既有布局）
            repaired |= RepairPrefab<OutGameHubRightDockView>(HubRightDockPath,
                (root, view) => AppendDockActionButtons(root, view),
                view => view.furnitureButton == null || view.endDayButton == null);
            // 顶栏：声望/装饰分数值条收编进 Prefab（同上只补缺失）
            repaired |= RepairPrefab<OutGameHubTopBarView>(HubTopBarPath,
                (root, view) => AppendTopBarEconomyChip(root, view),
                view => view.economyChipLabel == null);
            // 家具 HUD：补「购买家具」按钮（仓库展示化后购买唯一入口；只补缺失不动既有布局）
            repaired |= RepairPrefab<OutGameFurnitureHudView>(FurnitureHudPath,
                (root, view) => AppendFurnitureStoreButton(root, view),
                view => view.storeButton == null);
            // 商店页：按 2026-08-14 设计稿补选色行/键位提示/弹窗配色列（只补缺失不动既有布局）
            repaired |= RepairPrefab<OutGameStorePageView>(StorePagePath,
                (root, view) => AppendStoreRedesignNodes(root, view),
                view => view.swatchRoot == null || view.colorKeycap == null || view.obtainedSwatchRoot == null ||
                        // 层序错位（补缺节点排在获得弹窗之后 → 画在弹窗上、不被弹窗遮罩挡）也触发迁移
                        (view.obtainedGroup != null && view.swatchRoot != null &&
                         view.swatchRoot.GetSiblingIndex() > view.obtainedGroup.transform.GetSiblingIndex()) ||
                        // 购买键改空格：旧回车键帽图触发换图迁移
                        (view.buyKeycap != null && view.buyKeycap.sprite != null && view.buyKeycap.sprite.name == "enter") ||
                        // 「X 改变颜色」缺悬停图引用（绑定层靠它做 SpriteSwap，2026-08-18 反馈）
                        view.colorKeycapHover == null ||
                        // 卡片网格还是旧的一行五张（太松，2026-08-18 反馈）
                        StoreGridNeedsTighten(view));
            // 商店卡片：Thumb 包进 ThumbArea 容器（图在手调框内保比例自适应；容器承接原 Thumb 的手调 Rect）
            repaired |= RepairPrefab<OutGameStoreCardView>(StoreCardPath,
                (root, view) => WrapStoreCardThumb(root, view),
                view => view.thumb != null && view.thumb.transform.parent == view.transform);
            // 家具图鉴卡：补缩略图容器（图鉴改列真实摆放家具）
            repaired |= RepairPrefab<DeviceCardView>(DeviceCardPath,
                (root, view) => AppendDeviceCardThumb(root, view),
                view => view.thumb == null);
            // 日历面板：时段行补 Button（点时段跳时间，2026-08-14）
            repaired |= RepairPrefab<OutGameCalendarPanelView>(CalendarPanelPath,
                (root, view) => AppendPhaseButtons(view),
                view => view.phaseButtons == null || view.phaseButtons.Length < 6 || view.phaseButtons[0] == null);
            // Hub 场景世界层：根节点补设计尺寸 + uvRect 重置为整图
            //（房间图已由美术裁成纯内容，旧的黑框裁切退役；显示比例由运行时按贴图内嵌保证，2026-08-16）
            repaired |= RepairPrefab<OutGameHubWorldView>(HubSceneWorldPath,
                (root, view) =>
                {
                    ((RectTransform)root.transform).sizeDelta = new Vector2(1920, 1080);
                    // 接待室贴地可走带（2026-08-18）：只补缺失，已有的不动
                    view.receptionWalkArea = AppendReceptionWalkArea(root.transform, view.receptionArea);
                    if (view.roomArts == null) return;
                    foreach (var art in view.roomArts)
                        if (art != null) art.uvRect = new Rect(0, 0, 1, 1);
                },
                view => ((RectTransform)view.transform).sizeDelta.x < 1f ||
                        view.receptionWalkArea == null ||
                        (view.roomArts != null && System.Array.Exists(view.roomArts,
                            art => art != null && (art.uvRect.width < .999f || art.uvRect.height < .999f))));
            // 确认弹窗：按钮补 ESC/空格键帽（2026-08-17 键位可视化）
            repaired |= RepairPrefab<OutGameConfirmPopupView>(ConfirmPopupPath,
                (root, view) => AppendConfirmKeycaps(view),
                view => (view.cancelButton != null && view.cancelButton.transform.Find("EscCap") == null) ||
                        (view.confirmButton != null && view.confirmButton.transform.Find("SpaceCap") == null));
            // 图鉴页：补焦点卡编号（卡面把 NO.001 画死了，2026-08-18）；按 Prefab 里焦点卡位的现值换算
            repaired |= RepairPrefab<OutGameCodexPageView>(CodexPagePath,
                (root, view) =>
                {
                    var slot = view.cardSlots != null && view.cardSlots.Length > 0
                        ? view.cardSlots[view.cardSlots.Length / 2]
                        : null;
                    if (slot == null) return;
                    MigrateCodexSlots(view);
                    // 早期版本把编号建成了卡片的兄弟节点（卡片缩放时不跟手），拆掉重挂到卡片身下
                    if (view.focusNumberRoot != null) Object.DestroyImmediate(view.focusNumberRoot.gameObject);
                    view.focusNumberRoot = BuildCodexFocusNumber(slot.rectTransform, ref view.focusNumber);
                },
                view => view.cardSlots != null && view.cardSlots.Length > 0 &&
                        (view.focusNumberRoot == null || CodexSlotsAreStale(view) ||
                         view.focusNumberRoot.parent != view.cardSlots[view.cardSlots.Length / 2].transform));
            // 档案面板：补「访客图鉴」入口按钮（2026-08-18）
            repaired |= RepairPrefab<OutGameArchivePanelView>(ArchivePanelPath,
                (root, view) => view.codexButton = AppendArchiveCodexButton(root.transform),
                view => view.codexButton == null);
            // 图鉴详情区：补「前往修理」按钮（2026-08-14）
            repaired |= RepairPrefab<OutGameDevicePanelView>(DevicePanelPath,
                (root, view) => AppendDeviceRepairButton(view),
                view => view.repairButton == null);
            // 日出过场：补夜幕结算正文与点击提示；顺带清掉已退役的太阳节点（2026-08-14）
            repaired |= RepairPrefab<OutGameDayTransitionView>(DayTransitionPath,
                (root, view) => AppendDayTransitionSettleNodes(view),
                view => view.bodyLabel == null || view.hintLabel == null || view.cycleFrames == null ||
                        view.transform.Find("SunDisc") != null);
            return repaired;
        }

        /// <summary>
        /// 旧结构 StoreCard 无损迁移：把 Thumb 当前的 Rect（含手调值）原样搬给新建的 ThumbArea 容器，
        /// Thumb 改为在容器内拉伸 + FitInParent——此后调「图片显示范围」= 调 ThumbArea 的 Rect。
        /// </summary>
        private static void WrapStoreCardThumb(GameObject root, OutGameStoreCardView view)
        {
            if (view.thumb == null || view.thumb.transform.parent != view.transform) return;
            var thumbRect = view.thumb.rectTransform;
            var area = Rect(root.transform, "ThumbArea", thumbRect.anchorMin, thumbRect.anchorMax,
                thumbRect.anchoredPosition, thumbRect.sizeDelta);
            area.pivot = thumbRect.pivot;
            area.SetSiblingIndex(thumbRect.GetSiblingIndex()); // 保持层序（框下、价格上）
            thumbRect.SetParent(area, false);
            thumbRect.anchorMin = Vector2.zero;
            thumbRect.anchorMax = Vector2.one;
            thumbRect.offsetMin = Vector2.zero;
            thumbRect.offsetMax = Vector2.zero;
            var fitter = view.thumb.GetComponent<AspectRatioFitter>();
            if (fitter == null) fitter = view.thumb.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        }

        /// <summary>
        /// 商店页设计稿增量（2026-08-14）：预览下方选色块行、底部「X 改变颜色 / ⏎ 购买」键位提示、
        /// 获得弹窗左缘配色列。全部只补缺失节点；分类圆标槽位为空时顺手填上 store/1~5.png。
        /// </summary>
        /// <summary>本生成器历史上产出过的网格排布（列数, 格子）：命中其中之一才迁移。</summary>
        private static readonly (int Columns, Vector2 Cell)[] StaleStoreGrids =
        {
            (5, new Vector2(176, 150)), // 初版：一行五张、格子偏大
            (6, new Vector2(148, 126)), // 2026-08-18 误改：收成六张且高压过头
        };

        /// <summary>
        /// 卡片网格还停在旧排布（2026-08-18 反馈太松）。判据是「精确等于某个历史生成值」，
        /// 手调过任何一项就不再命中，不会覆盖你自己调的网格。
        /// </summary>
        private static bool StoreGridNeedsTighten(OutGameStorePageView view)
        {
            var grid = view.gridContent != null ? view.gridContent.GetComponent<GridLayoutGroup>() : null;
            if (grid == null) return false;
            foreach (var stale in StaleStoreGrids)
                if (grid.constraintCount == stale.Columns &&
                    Mathf.Abs(grid.cellSize.x - stale.Cell.x) < 1f &&
                    Mathf.Abs(grid.cellSize.y - stale.Cell.y) < 1f) return true;
            return false;
        }

        /// <summary>只改 GridLayoutGroup 的格子/间距/列数，网格视口与其他手调布局一概不动。</summary>
        private static void TightenStoreGrid(OutGameStorePageView view)
        {
            if (!StoreGridNeedsTighten(view)) return;
            var grid = view.gridContent.GetComponent<GridLayoutGroup>();
            grid.cellSize = StoreGridCell;
            grid.spacing = StoreGridSpacing;
            grid.constraintCount = StoreGridColumns;
        }

        private static void AppendStoreRedesignNodes(GameObject root, OutGameStorePageView view)
        {
            // 「X 改变颜色」的悬停图：只补引用，位置尺寸一概不动
            if (view.colorKeycapHover == null) view.colorKeycapHover = Store2("X-悬停");
            TightenStoreGrid(view);
            if (view.swatchRoot == null)
            {
                // 选色块行：右侧信息区、描述文本下方（色块运行时实例化，容器只做定位）
                view.swatchRoot = Rect(root.transform, "SwatchRow", new Vector2(1, 1), new Vector2(1, 1),
                    new Vector2(-230, -470), new Vector2(360, 44));
            }
            if (view.colorKeycap == null)
            {
                view.colorKeycap = Image(root.transform, "ColorKeycap", new Vector2(1, 0), new Vector2(1, 0),
                    new Vector2(-620, 44), new Vector2(48, 48), Color.white);
                view.colorKeycap.sprite = Store2("X-默认"); // 2.0 整图（自带文字）
                view.colorKeycap.preserveAspect = true;
                view.colorKeycap.raycastTarget = false;
                view.colorKeycapLabel = Label(root.transform, "ColorKeycapHint", "改变颜色", 16,
                    new Color(1, 1, 1, .75f), new Vector2(1, 0), new Vector2(1, 0),
                    new Vector2(-540, 44), new Vector2(110, 30), TextAnchor.MiddleLeft, FontStyle.Normal);
            }
            if (view.buyKeycap == null)
            {
                view.buyKeycap = Image(root.transform, "BuyKeycap", new Vector2(1, 0), new Vector2(1, 0),
                    new Vector2(-400, 44), new Vector2(96, 44), Color.white);
                view.buyKeycap.sprite = Store2("space-默认");
                view.buyKeycap.preserveAspect = true;
                view.buyKeycap.raycastTarget = false;
                view.buyKeycapLabel = Label(root.transform, "BuyKeycapHint", "购买", 16,
                    new Color(1, 1, 1, .75f), new Vector2(1, 0), new Vector2(1, 0),
                    new Vector2(-320, 44), new Vector2(80, 30), TextAnchor.MiddleLeft, FontStyle.Normal);
            }
            // 购买键改空格（2026-08-14）：旧修补产物里的回车键帽图换成空格键帽
            if (view.buyKeycap != null && view.buyKeycap.sprite != null && view.buyKeycap.sprite.name == "enter")
                view.buyKeycap.sprite = Store2("space-默认");
            if (view.obtainedSwatchRoot == null && view.obtainedName != null)
            {
                var panel = view.obtainedName.transform.parent;
                view.obtainedSwatchRoot = Rect(panel, "ObtainedSwatches", new Vector2(0, .5f), new Vector2(0, .5f),
                    new Vector2(30, 0), new Vector2(40, 320));
            }
            if (view.categorySprites == null || view.categorySprites.Length < 5) view.categorySprites = new Sprite[5];
            for (var i = 0; i < 5; i++)
                if (view.categorySprites[i] == null)
                    view.categorySprites[i] = Store2((i + 1).ToString()); // 2.0 圆标

            // 层序：补缺节点必须排在获得弹窗**之前**——弹窗开着时才能盖住它们（视觉+射线一起被遮罩挡掉）
            if (view.obtainedGroup != null)
            {
                var popup = view.obtainedGroup.transform;
                var below = new Component[]
                    { view.swatchRoot, view.colorKeycap, view.colorKeycapLabel, view.buyKeycap, view.buyKeycapLabel };
                foreach (var node in below)
                    if (node != null && node.transform.parent == popup.parent &&
                        node.transform.GetSiblingIndex() > popup.GetSiblingIndex())
                        node.transform.SetSiblingIndex(popup.GetSiblingIndex());
            }
        }

        /// <summary>家具 HUD 旧 Prefab 无损补回「购买家具」按钮（挂在顶部容器里，随拖拽淡出）。</summary>
        private static void AppendFurnitureStoreButton(GameObject root, OutGameFurnitureHudView view)
        {
            if (view.storeButton != null) return;
            var chrome = root.transform.Find("TopChrome") ?? root.transform;
            view.storeButton = PageButton(chrome, "Store", "购买家具", new Vector2(-950, -60),
                new Vector2(160, 64), new Color(.32f, .06f, .18f, .9f), Hex("F3E8DD"), 20, TextAnchor.MiddleCenter, new Vector2(1, 1));
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

        /// <summary>
        /// 设置页 2.0 结构重绑（只找节点、只补引用，不动手调的位置尺寸）：
        /// 底板 + 顶部 5 个横向分页 + 滚动视口内的 Rows + 行模板 + 底部三个整图按钮。
        /// </summary>
        private static void RepairSettingsPage(GameObject root, OutGameSettingsPageView view)
        {
            view.background = Required<RawImage>(root.transform, "Background");
            var tabNames = new[] { "基础", "画面", "控制", "玩法", "制作组" };
            view.tabButtons = new Button[tabNames.Length];
            view.tabBackgrounds = new Image[tabNames.Length];
            view.tabLabels = new Text[tabNames.Length];
            for (var i = 0; i < tabNames.Length; i++)
            {
                var tab = RequiredTransform(root.transform, "Tab_" + tabNames[i]);
                view.tabButtons[i] = Required<Button>(tab);
                view.tabBackgrounds[i] = Required<Image>(tab);
                view.tabLabels[i] = Required<Text>(tab, "Label");
            }
            var viewport = RequiredTransform(root.transform, "RowsViewport");
            view.rowsRoot = RequiredTransform(viewport, "Rows") as RectTransform;
            var templates = RequiredTransform(root.transform, "Templates");
            view.headerTemplate = Required<OutGameSettingsHeaderRow>(templates, "HeaderRow");
            view.sliderTemplate = Required<OutGameSettingsSliderRow>(templates, "SliderRow");
            view.optionTemplate = Required<OutGameSettingsOptionRow>(templates, "OptionRow");
            view.tabNormal = Settings2("一级tab-默认");
            view.tabSelected = Settings2("一级tab-选中");
            view.tabHover = Settings2("一级tab-悬停");
            view.backButton = Required<Button>(root.transform, "BackButton");
            view.resetButton = Required<Button>(root.transform, "ResetButton");
            view.applyButton = Required<Button>(root.transform, "ApplyButton");
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

        private static void RemoveMissingScripts(GameObject root)
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) > 0)
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject);
            }
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

        private const string LoginDir = "Assets/PC ui 2.0/\u767b\u5f55/";

        private static Sprite Login(string name) =>
            AssetDatabase.LoadAssetAtPath<Sprite>(LoginDir + name + ".png");

        /// <summary>2.0 \u767b\u5f55\u9875\u7684\u56db\u9879\u83dc\u5355\uff08\u7d20\u6750\u540d, \u539f\u5bbd, \u539f\u9ad8\uff09\u3002</summary>
        private static readonly (string Art, float W, float H)[] LoginMenuArts =
        {
            ("NEW GAME", 550f, 81f),
            ("LOAD GAME", 593f, 81f),
            ("OPTIONS", 441f, 82f),
            ("EXIT", 238f, 81f),
        };

        /// <summary>
        /// \u6807\u9898\uff08\u767b\u5f55\uff09\u9875\uff082026-08-19 \u6309 2.0 \u8bbe\u8ba1\u56fe\u91cd\u505a\uff09\uff1a
        /// \u6574\u5c4f\u5c01\u9762 + \u53f3\u4fa7\u6e38\u620f\u540d + \u5206\u9694\u7ebf + \u56db\u9879\u83dc\u5355\u3002
        /// **\u6807\u9898\u4e0e\u83dc\u5355\u90fd\u662f\u5355\u72ec\u7684\u900f\u660e\u56fe**\uff0c\u4e0d\u518d\u50cf 1.0 \u90a3\u6837\u70d8\u5728\u5c01\u9762\u91cc\u9760\u900f\u660e\u70ed\u533a\u53bb\u78b0\u2014\u2014
        /// \u6362\u5c01\u9762\u4e0d\u7528\u91cd\u65b0\u5bf9\u70b9\uff0c\u60ac\u505c\u4e5f\u80fd\u76f4\u63a5\u67d3\u83dc\u5355\u56fe\u672c\u8eab\u3002
        /// \u5750\u6807\u6309 1920\u00d71080 \u53e3\u5f84\uff0c\u7531\u8bbe\u8ba1\u56fe\u7b49\u6bd4\u6298\u7b97\u3002
        /// </summary>
        private static void BuildTitle(string path)
        {
            var root = Root("TitlePage");
            var refs = root.AddComponent<OutGameTitleView>();
            refs.cover = Raw(root.transform, "Cover", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            refs.cover.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(LoginDir + "image 293.png");
            var fitter = refs.cover.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            if (refs.cover.texture != null)
                fitter.aspectRatio = (float)refs.cover.texture.width / refs.cover.texture.height;

            // \u6807\u9898\uff1a\u7d20\u6750 1439\u00d7394\uff0c\u53d6\u5bbd 523
            refs.titleArt = Image(root.transform, "TitleArt", new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(1531, -369), new Vector2(523, 523f * 394f / 1439f), Color.white);
            refs.titleArt.sprite = Login("The Guesthouse of Meros");
            refs.titleArt.preserveAspect = true;
            refs.titleArt.raycastTarget = false;
            refs.titleRule = Image(root.transform, "TitleRule", new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(1531, -489), new Vector2(523, 3), Color.white);
            refs.titleRule.sprite = Login("Group 109");
            refs.titleRule.raycastTarget = false;

            // \u56db\u9879\u83dc\u5355\uff1a\u6587\u5b57\u56fe\u672c\u8eab\u5c31\u662f\u6309\u94ae\uff0c\u60ac\u505c\u65f6\u7531\u7ed1\u5b9a\u5c42\u63d0\u4eae
            refs.menuButtons = new Button[LoginMenuArts.Length];
            refs.menuIcons = new Image[LoginMenuArts.Length];
            refs.menuMainLabels = new Text[LoginMenuArts.Length];
            refs.menuSubtitles = new Text[LoginMenuArts.Length];
            refs.menuHoverImages = new RawImage[LoginMenuArts.Length];
            for (var i = 0; i < LoginMenuArts.Length; i++)
            {
                var art = LoginMenuArts[i];
                var width = art.W * .373f;                 // \u8bbe\u8ba1\u56fe\u4e0a NEW GAME \u5bbd 205 / \u7d20\u6750 550
                var height = width * art.H / art.W;
                var icon = Image(root.transform, "Menu_" + art.Art, new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(1523, -558 - i * 84), new Vector2(width, height), Color.white);
                icon.sprite = Login(art.Art);
                icon.preserveAspect = true;
                var button = icon.gameObject.AddComponent<Button>();
                button.targetGraphic = icon;
                button.transition = Selectable.Transition.None; // \u60ac\u505c\u9ad8\u4eae\u7531\u7ed1\u5b9a\u5c42\u505a\uff08\u53ea\u6539\u989c\u8272\uff0c\u4e0d\u6362\u56fe\uff09
                refs.menuButtons[i] = button;
                refs.menuIcons[i] = icon;
            }
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

        private const string KeycapDir = "Assets/PC ui/button/default/";

        /// <summary>设置 2.0 素材目录（2026-08-18 按新设计图重做）。</summary>
        private const string Settings2Dir = "Assets/PC ui 2.0/settings/";

        private static Sprite Settings2(string name) =>
            AssetDatabase.LoadAssetAtPath<Sprite>(Settings2Dir + name + ".png");

        /// <summary>
        /// 设置页（2026-08-18 按 2.0 设计图重做）：整页底板 + 顶部横向分页（Q/E 切换）+
        /// 条目行滚动区（滑条/切换/分节标题三种模板）+ 底部 ESC/X/空格 整图按钮。坐标按 1920×1080 口径。
        /// </summary>
        private static void BuildSettingsPage(string path)
        {
            var root = Root("SettingsPage");
            var view = root.AddComponent<OutGameSettingsPageView>();
            view.background = Raw(root.transform, "Background", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            view.background.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Settings2Dir + "设置-底板.png");
            view.background.raycastTarget = true; // 整页承接点击（空白处不穿透到下层）

            Label(root.transform, "Title", "SETTINGS", 62, Hex("4A6FA5"),
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(300, -100), new Vector2(500, 90),
                TextAnchor.MiddleLeft, FontStyle.BoldAndItalic);

            view.tabNormal = Settings2("一级tab-默认");
            view.tabSelected = Settings2("一级tab-选中");
            view.tabHover = Settings2("一级tab-悬停");

            // 顶部横向分页：Q ◀ 基础/画面/控制/玩法/制作组 ▶ E
            SpriteButton(root.transform, "PrevTab", Store2("Q"), Store2("Q"),
                new Vector2(0, 1), new Vector2(97, -201), new Vector2(34, 36));
            var tabNames = new[] { "基础", "画面", "控制", "玩法", "制作组" };
            view.tabButtons = new Button[tabNames.Length];
            view.tabBackgrounds = new Image[tabNames.Length];
            view.tabLabels = new Text[tabNames.Length];
            for (var i = 0; i < tabNames.Length; i++)
            {
                var tab = Image(root.transform, "Tab_" + tabNames[i], new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(245 + i * 176, -201), new Vector2(176, 59), Color.white);
                tab.sprite = view.tabNormal;
                var button = tab.gameObject.AddComponent<Button>();
                button.targetGraphic = tab;
                button.transition = Selectable.Transition.None; // 选中态由绑定器 sprite swap，避免双重换肤打架
                var label = Label(tab.transform, "Label", tabNames[i], 24, Hex("4A6FA5"),
                    new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(160, 40),
                    TextAnchor.MiddleCenter, FontStyle.Bold);
                view.tabButtons[i] = button;
                view.tabBackgrounds[i] = tab;
                view.tabLabels[i] = label;
            }
            SpriteButton(root.transform, "NextTab", Store2("E"), Store2("E"),
                new Vector2(0, 1), new Vector2(1114, -201), new Vector2(34, 36));

            // 条目滚动区：视口（裁剪）+ 内容层（行由模板实例化，pivot 顶部向下排）
            var viewport = Rect(root.transform, "RowsViewport", new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(600, -620), new Vector2(1035, 600));
            var viewportImage = ImageOn(viewport, new Color(1, 1, 1, .002f));
            viewportImage.raycastTarget = true; // 空隙处也能滚
            viewport.gameObject.AddComponent<RectMask2D>();
            view.rowsRoot = Rect(viewport, "Rows", new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
            view.rowsRoot.pivot = new Vector2(.5f, 1);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = view.rowsRoot;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 26f;
            var barRect = Rect(root.transform, "RowsScrollbar", new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(1131, -620), new Vector2(10, 600));
            ImageOn(barRect, new Color(.29f, .44f, .65f, .12f));
            var bar = barRect.gameObject.AddComponent<Scrollbar>();
            bar.direction = Scrollbar.Direction.BottomToTop;
            var handle = Rect(barRect, "Handle", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            bar.handleRect = handle;
            bar.targetGraphic = ImageOn(handle, Hex("4A6FA5", .75f));
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            // 行模板（未激活）
            var templates = Rect(root.transform, "Templates", new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(600, -620), new Vector2(1035, 90));
            templates.gameObject.SetActive(false);
            view.headerTemplate = BuildSettingsHeaderTemplate(templates);
            view.sliderTemplate = BuildSettingsSliderTemplate(templates);
            view.optionTemplate = BuildSettingsOptionTemplate(templates);

            // 底部整图按钮（素材自带键帽与文字）
            view.backButton = SpriteButton(root.transform, "BackButton",
                Settings2("ESC-默认"), Settings2("ESC-悬停"),
                new Vector2(0, 0), new Vector2(175, 57), new Vector2(186, 76));
            view.resetButton = SpriteButton(root.transform, "ResetButton",
                Settings2("X-默认"), Settings2("X-悬停"),
                new Vector2(1, 0), new Vector2(-337, 57), new Vector2(186, 76));
            view.applyButton = SpriteButton(root.transform, "ApplyButton",
                Settings2("space-默认"), Settings2("space-悬停"),
                new Vector2(1, 0), new Vector2(-140, 57), new Vector2(186, 76));
            Save(root, path);
        }


        /// <summary>透明热区用的白图（编辑器语境不能用运行时 WhiteSprite，取内置资源）。</summary>
        private static Sprite HouseUIRuntimeWhite() =>
            AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        private static OutGameSettingsHeaderRow BuildSettingsHeaderTemplate(RectTransform parent)
        {
            var row = Rect(parent, "HeaderRow", new Vector2(.5f, 1), new Vector2(.5f, 1), Vector2.zero, new Vector2(1035, 46));
            row.pivot = new Vector2(.5f, 1);
            var refs = row.gameObject.AddComponent<OutGameSettingsHeaderRow>();
            // 文本框 pivot 居中：x=200/宽400 → 左缘落在 0，与条目行标签（x=150/宽300）同一条竖线；
            // 原来的 x=96/宽300 会把左缘顶到 -54，分节标题整个被视口裁掉（看不见）
            refs.title = Label(row, "Title", "<color=#4A6FA5>|</color> 通用", 22, Hex("6B5B4E"),
                new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(200, 0), new Vector2(400, 34),
                TextAnchor.MiddleLeft, FontStyle.Bold);
            return refs;
        }

        /// <summary>
        /// 2.0 行模板的两处缺陷（2026-08-18 反馈）：①分节标题文本框左缘为负被视口裁掉，整行看不见；
        /// ②滑块 sizeDelta.y 非 0，被 Slider 的垂直拉伸撑成椭圆。只判这两项，别的一律不管。
        /// </summary>
        private static bool Settings2RowsNeedFix(OutGameSettingsPageView view)
        {
            var header = view.headerTemplate;
            if (header != null && header.title != null)
            {
                var rect = header.title.rectTransform;
                if (rect.anchoredPosition.x - rect.sizeDelta.x * rect.pivot.x < -1f) return true;
            }
            var slider = view.sliderTemplate != null ? view.sliderTemplate.slider : null;
            if (slider != null && slider.handleRect != null &&
                Mathf.Abs(slider.handleRect.sizeDelta.y) > .01f) return true;
            return false;
        }

        /// <summary>把上面两处缺陷就地改掉（只动这两个 RectTransform）。</summary>
        private static void MigrateSettings2Rows(OutGameSettingsPageView view)
        {
            var header = view.headerTemplate;
            if (header != null && header.title != null)
            {
                var rect = header.title.rectTransform;
                if (rect.anchoredPosition.x - rect.sizeDelta.x * rect.pivot.x < -1f)
                {
                    rect.sizeDelta = new Vector2(400, rect.sizeDelta.y);
                    rect.anchoredPosition = new Vector2(400 * rect.pivot.x, rect.anchoredPosition.y);
                }
            }
            var slider = view.sliderTemplate != null ? view.sliderTemplate.slider : null;
            if (slider == null || slider.handleRect == null) return;
            var handle = slider.handleRect;
            if (Mathf.Abs(handle.sizeDelta.y) <= .01f) return;
            var diameter = Mathf.Max(handle.sizeDelta.x, handle.sizeDelta.y);
            var area = handle.parent as RectTransform;
            if (area != null)
            {
                // 滑道高度即直径：把手高度由父级给，自身只留宽度
                var trackHeight = ((RectTransform)area.parent).rect.height;
                var pad = (diameter - trackHeight) * .5f;
                area.offsetMin = new Vector2(area.offsetMin.x, -pad);
                area.offsetMax = new Vector2(area.offsetMax.x, pad);
            }
            handle.sizeDelta = new Vector2(diameter, 0f);
        }

        private static OutGameSettingsSliderRow BuildSettingsSliderTemplate(RectTransform parent)
        {
            var row = Rect(parent, "SliderRow", new Vector2(.5f, 1), new Vector2(.5f, 1), Vector2.zero, new Vector2(1035, 71));
            row.pivot = new Vector2(.5f, 1);
            var refs = row.gameObject.AddComponent<OutGameSettingsSliderRow>();
            refs.background = ImageOn(row, Color.white);
            refs.background.sprite = Settings2("条目-默认");
            refs.label = Label(row, "Label", "音量", 22, Hex("4A6FA5"),
                new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(150, 0), new Vector2(300, 38),
                TextAnchor.MiddleLeft, FontStyle.Bold);

            var sliderRect = Rect(row, "Slider", new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-190, 0), new Vector2(288, 24));
            var track = ImageOn(Rect(sliderRect, "Background", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero), Color.white);
            track.sprite = Settings2("进度条-底");
            var fillArea = Rect(sliderRect, "FillArea", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var fill = ImageOn(Rect(fillArea, "Fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero), Color.white);
            fill.sprite = Settings2("进度条-进度");
            // 滑块保正圆：Slider 会把把手的垂直锚点撑成 0~1 拉伸，高度 = 父高 + sizeDelta.y，
            // 所以直径只能靠「滑道高 = 27」给出，把手自身 sizeDelta.y 必须留 0（写 27 会变成 51 高的椭圆）
            var handleArea = Rect(sliderRect, "HandleArea", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            handleArea.offsetMin = new Vector2(0, -1.5f);
            handleArea.offsetMax = new Vector2(0, 1.5f);
            var handle = ImageOn(Rect(handleArea, "Handle", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(27, 0)), Color.white);
            handle.sprite = Settings2("进度条-滑块-默认");
            var slider = sliderRect.gameObject.AddComponent<Slider>();
            slider.fillRect = (RectTransform)fill.transform;
            slider.handleRect = (RectTransform)handle.transform;
            slider.targetGraphic = handle;
            slider.spriteState = new SpriteState
            {
                highlightedSprite = Settings2("进度条-滑块-hover"),
                pressedSprite = Settings2("进度条-滑块-hover"),
            };
            slider.transition = Selectable.Transition.SpriteSwap;
            slider.minValue = 0;
            slider.maxValue = 100;
            slider.wholeNumbers = true;
            refs.slider = slider;

            refs.value = Label(row, "Value", "45", 22, Hex("6B5B4E"),
                new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-58, 0), new Vector2(70, 36),
                TextAnchor.MiddleCenter, FontStyle.Bold);
            return refs;
        }

        private static OutGameSettingsOptionRow BuildSettingsOptionTemplate(RectTransform parent)
        {
            var row = Rect(parent, "OptionRow", new Vector2(.5f, 1), new Vector2(.5f, 1), Vector2.zero, new Vector2(1035, 71));
            row.pivot = new Vector2(.5f, 1);
            var refs = row.gameObject.AddComponent<OutGameSettingsOptionRow>();
            refs.background = ImageOn(row, Color.white);
            refs.background.sprite = Settings2("条目-默认");
            refs.label = Label(row, "Label", "选项", 22, Hex("4A6FA5"),
                new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(150, 0), new Vector2(300, 38),
                TextAnchor.MiddleLeft, FontStyle.Bold);
            // 值框（切换栏素材）+ 左右箭头
            refs.indicator = Image(row, "ValueBox", new Vector2(1, .5f), new Vector2(1, .5f),
                new Vector2(-190, 0), new Vector2(228, 42), Color.white);
            refs.indicator.sprite = Settings2("切换栏");
            refs.indicator.raycastTarget = false;
            refs.value = Label(refs.indicator.transform, "Value", "开", 22, Hex("4A6FA5"),
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(200, 34),
                TextAnchor.MiddleCenter, FontStyle.Bold);
            var leftImage = Image(row, "Left", new Vector2(1, .5f), new Vector2(1, .5f),
                new Vector2(-338, 0), new Vector2(23, 26), Color.white);
            leftImage.sprite = Settings2("切换-默认");
            leftImage.preserveAspect = true;
            refs.left = leftImage.gameObject.AddComponent<Button>();
            refs.left.targetGraphic = leftImage;
            refs.left.transition = Selectable.Transition.SpriteSwap;
            refs.left.spriteState = new SpriteState
            {
                highlightedSprite = Settings2("切换-hover"),
                pressedSprite = Settings2("切换-hover"),
                disabledSprite = Settings2("切换-禁用"),
            };
            var rightImage = Image(row, "Right", new Vector2(1, .5f), new Vector2(1, .5f),
                new Vector2(-42, 0), new Vector2(23, 26), Color.white);
            rightImage.sprite = Settings2("切换-默认");
            rightImage.preserveAspect = true;
            rightImage.transform.localScale = new Vector3(-1, 1, 1); // 右箭头 = 左箭头镜像
            refs.right = rightImage.gameObject.AddComponent<Button>();
            refs.right.targetGraphic = rightImage;
            refs.right.transition = Selectable.Transition.SpriteSwap;
            refs.right.spriteState = new SpriteState
            {
                highlightedSprite = Settings2("切换-hover"),
                pressedSprite = Settings2("切换-hover"),
                disabledSprite = Settings2("切换-禁用"),
            };
            return refs;
        }

        private static void BuildSettingsKeycap(Transform parent, string name, string key, string caption,
            Vector2 anchor, Vector2 position, Vector2 keySize)
        {
            var cap = Image(parent, name, anchor, anchor, position, keySize, Color.white);
            cap.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(KeycapDir + key + ".png");
            cap.preserveAspect = true;
            cap.raycastTarget = false;
            Label(parent, name + "Label", caption, 20, Hex("EDE6E6"),
                anchor, anchor, position + new Vector2(keySize.x * .5f + 55, 0), new Vector2(120, 32),
                TextAnchor.MiddleLeft, FontStyle.Bold);
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
            refs.welcomeLabel = Label(root.transform, "Welcome", "WELCOME HOME.\n当前在场 <color=#E22D76>0</color> 位访客", 19, Hex("F3E8DD"),
                new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-370, 0), new Vector2(330, 78), TextAnchor.MiddleCenter, FontStyle.Normal);
            refs.optionsButton = PageButton(root.transform, "Options", "设\n<size=15>OPTIONS · 设置</size>", new Vector2(-70, 0),
                new Vector2(112, 104), new Color(.32f, .06f, .18f, .86f), Hex("F3E8DD"), 27, TextAnchor.MiddleCenter, new Vector2(1, .5f));
            refs.optionsLabel = refs.optionsButton.GetComponentInChildren<Text>();
            AppendTopBarEconomyChip(root, refs);
            Save(root, path);
        }

        /// <summary>声望/装饰分数值条：自运行时动态件收编进 HubTopBar Prefab（新建与修复共用，挂在顶栏下缘）。</summary>
        private static void AppendTopBarEconomyChip(GameObject root, OutGameHubTopBarView refs)
        {
            if (refs.economyChipLabel != null) return;
            var chip = Image(root.transform, "EconomyChip", new Vector2(.5f, 0), new Vector2(.5f, 0),
                new Vector2(-233, -36), new Vector2(400, 50), new Color(.025f, .025f, .045f, .77f));
            refs.economyChipLabel = Label(chip.transform, "Value",
                "<color=#74D8D1>声望 40</color>      <color=#E22D76>装饰分 0</color>", 18, Hex("F3E8DD"),
                TextAnchor.MiddleCenter, FontStyle.Bold);
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
            refs.label = Label(root.transform, "Label", "家具图鉴", 20, Hex("F3E8DD"), new Vector2(.28f, 0), new Vector2(1, 1), Vector2.zero, Vector2.zero, TextAnchor.MiddleLeft, FontStyle.Bold);
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
            AppendDockActionButtons(root, refs);
            Save(root, path);
        }

        /// <summary>「家具摆放」「结束今天」两个入口按钮：自运行时按钮收编进 Prefab（新建与修复共用）。</summary>
        private static void AppendDockActionButtons(GameObject root, OutGameHubRightDockView refs)
        {
            if (refs.furnitureButton == null)
                refs.furnitureButton = PageButton(root.transform, "FurnitureMode", "家    家具摆放", new Vector2(0, -450),
                    new Vector2(205, 78), new Color(.32f, .06f, .18f, .86f), Hex("F3E8DD"), 20, TextAnchor.MiddleLeft,
                    new Vector2(.5f, 1));
            if (refs.endDayButton == null)
                refs.endDayButton = PageButton(root.transform, "EndDay", "结    结束今天", new Vector2(0, -542),
                    new Vector2(205, 78), new Color(.06f, .18f, .32f, .86f), Hex("F3E8DD"), 20, TextAnchor.MiddleLeft,
                    new Vector2(.5f, 1));
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
            refs.hint = Label(root.transform, "Hint", "↑↓←→ 移动切换", 13, Hex("F3E8DD"), new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(112, -18), new Vector2(210, 30), TextAnchor.MiddleCenter, FontStyle.Normal);
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
            // 「同步现实时间」按钮已退役（2026-08-14）：新建 Prefab 不再生成，旧 Prefab 的节点由 Binder 就地隐藏
            AppendPhaseButtons(view); // 时段行可点：跳时间（2026-08-14）
            Save(root, path);
        }

        /// <summary>日历面板增量（2026-08-14 选择时间）：给时段行补 Button 组件（不动任何 Rect，纯加交互）。</summary>
        private static void AppendPhaseButtons(OutGameCalendarPanelView view)
        {
            if (view.phaseBackgrounds == null) return;
            if (view.phaseButtons == null || view.phaseButtons.Length != view.phaseBackgrounds.Length)
                view.phaseButtons = new Button[view.phaseBackgrounds.Length];
            for (var i = 0; i < view.phaseBackgrounds.Length; i++)
            {
                var row = view.phaseBackgrounds[i];
                if (row == null) continue;
                var button = row.GetComponent<Button>();
                if (button == null)
                {
                    button = row.gameObject.AddComponent<Button>();
                    button.targetGraphic = row;
                    AddTweenFeedback(button);
                }
                view.phaseButtons[i] = button;
            }
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

        // ══════════ 访客对话界面（2.0 设计图） ══════════

        /// <summary>2.0 对白板：整张素材（含透明边）落到 1920×1080 画布的尺寸与中心（锚左下）。</summary>
        private static readonly Vector2 DialoguePlateSize = new Vector2(1417, 304);
        private static readonly Vector2 DialoguePlateCenter = new Vector2(1106, 207);

        /// <summary>选项槽位：最下一格中心 y、相邻中心间距、预摆格数、右锚横向偏移。</summary>
        private const float DialogueOptionBottomY = 357f;
        private const float DialogueOptionPitch = 94f;
        private const int DialogueOptionSlotCount = 5;
        private const float DialogueOptionRightInset = -399.5f;
        private static readonly Vector2 DialogueOptionSlotSize = new Vector2(589, 80);
        private static readonly Vector2 DialogueOptionHoverSize = new Vector2(717, 107);
        private static readonly Vector2 DialogueOptionHoverOffset = new Vector2(28, -14);

        /// <summary>2.0 对话配色，取自设计图的笔画芯色（名字与键位是同一支蓝，正文是铅灰）。</summary>
        private static Color DialogueNameInk => Hex("5676A6");
        private static Color DialogueBodyInk => Hex("585856");
        private static Color DialogueOptionInk => Hex("5C5C5A");

        /// <summary>
        /// 访客对话界面（GVN/视觉小说式整层）。2026-08-19 按 2.0 设计图重做
        /// （素材 Assets/PC ui 2.0/conversation，版式见 Docs/待办工作流/新版对话UI样例.png）：
        /// 明亮水彩外景全屏底图 + 左下立绘 + 底部纸质对白板（名字凸台与分隔线都烘在素材里）+
        /// 右侧「贴着对白板往上长」的选项列 + 底部 ESC/中键/space 三颗整图键位条。
        ///
        /// 同批退役的 1.0 元素：左上 GUEST 标题、正文尾部继续箭头、右侧撕边压暗层、独立分隔线——
        /// 2.0 设计图里都不存在。1.0 那套「大画布 ×0.45」的统一缩放也一并撤掉：
        /// 2.0 的坐标全部按 1920×1080 直接写，不再靠缩放凑。
        ///
        /// 【坐标口径】设计图是 2175×1225 手工拼版，按 1920/2175 ≈ 0.882 换算到画布；三类素材各自
        /// 反推出的缩放比（选项 0.369 / 对白板 0.365 / 键位 0.350）互相吻合，说明出自同一比例。
        /// 键位条直接沿用图鉴页 2.0 已落地的那组坐标，跨页保持同一条底边。
        /// </summary>
        private static void BuildDialogueView(string path)
        {
            var root = Root("DialogueView");
            var view = root.AddComponent<OutGameDialogueView>();

            // 全屏场景底图（5120×2880，正好 16:9，直接铺满）
            view.sceneArt = Raw(root.transform, "Scene", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            view.sceneArt.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(ConversationDir + "对话-底板.png");

            // 整屏推进热区（点击推进台词 / 立即全文，§5.1）。
            // 紧跟场景之后创建 = 兄弟序最靠前 = 被其余控件盖住，
            // 所以点选项/关闭按钮不会误触推进（UGUI 射线取最上层）
            var advance = Image(root.transform, "AdvanceHotZone", Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, new Color(0, 0, 0, 0));
            advance.raycastTarget = true; // alpha 0 但照常吃射线
            view.advanceButton = advance.gameObject.AddComponent<Button>();
            view.advanceButton.targetGraphic = advance;
            view.advanceButton.transition = Selectable.Transition.None; // 整屏热区不该有任何视觉反馈

            // ── 底部对白板 ──
            // 素材 3846×824、可见区 x 29..3734 / y 101..801，按 ×0.3685 落到画布：整张 1417×304，
            // 可见右缘落在 1815（与选项列右缘同一条线）、可见底沿在 y≈63。
            // 不开 preserveAspect：rect 已按素材比例给足，让贴图严丝合缝填满，子节点坐标才对得上
            view.dialogueBar = Rect(root.transform, "DialogueBar", new Vector2(0, 0), new Vector2(0, 0),
                DialoguePlateCenter, DialoguePlateSize);
            var plate = ImageOn(view.dialogueBar, Color.white);
            plate.sprite = Conversation("对白底板");
            // 对白板不吃射线：否则它会挡住底下的整屏推进热区，玩家点正文推不动对话——
            // 而点正文恰恰是最自然的推进动作
            plate.raycastTarget = false;

            // 说话人名：写进素材自带的左上凸台（凸台在素材内约 x 265..1950 / y 101..290）。
            // 以下子节点都锚在对白板左下角，局部坐标 = 屏幕坐标 −(397.5, 55)
            view.speakerName = Label(view.dialogueBar, "SpeakerName", string.Empty, 44, DialogueNameInk,
                new Vector2(0, 0), new Vector2(0, 0), new Vector2(457.5f, 231), new Vector2(500, 60),
                TextAnchor.MiddleLeft, FontStyle.Bold);

            // 正文：屏幕坐标左 647 / 右 1713、上沿 240、高 170，按 30 号 1.5 行距够排 3 行
            view.dialogueText = Label(view.dialogueBar, "DialogueText", string.Empty, 30, DialogueBodyInk,
                new Vector2(0, 0), new Vector2(0, 0), new Vector2(782.5f, 100), new Vector2(1066, 170),
                TextAnchor.UpperLeft, FontStyle.Normal);
            view.dialogueText.lineSpacing = 1.5f;

            // ── 左下立绘 ──（压在对白板之上：设计图里访客的袖子/腰带盖着板的左端）
            // 素材 1600×1800，整张画布落到 600×675、底边贴屏幕底。
            // 运行时 DialogueOverlay 会按贴图真实比例回算宽度，八张立绘同尺寸故结果不变
            view.portrait = Raw(root.transform, "Portrait", new Vector2(0, 0), new Vector2(0, 0),
                new Vector2(381, 337.5f), new Vector2(600, 675));
            view.portrait.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/Resources/OutGameUI/Portraits/cat.png"); // 仅供 Prefab 里预览，运行时按立绘ID 换
            view.portrait.raycastTarget = false;

            // ── 右侧选项列 ──
            // 自下而上预摆 5 个槽位（最下一个紧贴对白板顶沿），运行时**底对齐**填充：
            // 选项越多越往上堆，永远压不到对白板上（2.0 设计图口径）
            view.optionsRoot = Rect(root.transform, "OptionsRoot", Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero);
            for (var i = 0; i < DialogueOptionSlotCount; i++)
                BuildDialogueOptionSlot(view.optionsRoot, "Option" + i,
                    DialogueOptionBottomY + DialogueOptionPitch * i);

            // ── 底部键位条 ──（整图素材自带键名与文案；坐标沿用图鉴页 2.0）
            view.closeButton = SpriteButton(root.transform, "CloseButton",
                Conversation("ESC-默认"), Conversation("ESC-hover"),
                new Vector2(0, 0), new Vector2(152, 57), new Vector2(186, 76));
            view.cycleButton = SpriteButton(root.transform, "CycleButton",
                Conversation("中键-默认"), Conversation("中键-悬停"),
                new Vector2(1, 0), new Vector2(-359, 57), new Vector2(186, 76));
            view.confirmButton = SpriteButton(root.transform, "ConfirmButton",
                Conversation("space-默认"), Conversation("space-悬停"),
                new Vector2(1, 0), new Vector2(-163, 57), new Vector2(186, 76));

            Save(root, path);
        }

        /// <summary>
        /// 对话选项槽位（2.0 二图叠放）。两张底图各按自己的原始比例摆：
        ///   选项-默认 1598×218 ×0.3685 = 589×80（即槽位根尺寸）
        ///   选项-悬停 1946×290 ×0.3685 = 717×107，相对根偏 (+28, −14)，让两张的**主体中心**重合
        /// 之所以不做 SpriteSwap：两张的主体几乎一样大（1536×176 vs 1622×180），悬停大出来的
        /// 那一大块全是右侧那条尖尾；塞进同一个 rect 会把蓝条主体缩掉 13%、尾巴还压扁。
        /// 选中态只切两张的显隐，统一由 DialogueOptionView.SetSelected 管。
        /// </summary>
        private static void BuildDialogueOptionSlot(RectTransform parent, string name, float centerY)
        {
            var rect = Rect(parent, name, new Vector2(1, 0), new Vector2(1, 0),
                new Vector2(DialogueOptionRightInset, centerY), DialogueOptionSlotSize);
            var view = rect.gameObject.AddComponent<DialogueOptionView>();

            // 根节点：透明但吃射线，专职当 Button 的 targetGraphic——
            // 底图要随选中态切显隐，不能兼任射线目标（一关就点不动了）
            var hit = ImageOn(rect, new Color(0, 0, 0, 0));
            hit.raycastTarget = true;
            view.button = rect.gameObject.AddComponent<Button>();
            view.button.targetGraphic = hit;
            view.button.transition = Selectable.Transition.None; // 两态由 SetSelected 换图，不叠一层染色
            AddTweenFeedback(view.button);

            view.background = Image(rect, "BgDefault", Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, Color.white);
            view.background.sprite = Conversation("选项-默认");
            view.background.raycastTarget = false;

            view.hoverBackground = Image(rect, "BgHover", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                DialogueOptionHoverOffset, DialogueOptionHoverSize, Color.white);
            view.hoverBackground.sprite = Conversation("选项-悬停");
            view.hoverBackground.raycastTarget = false;
            view.hoverBackground.enabled = false; // 默认未选中

            // 文字：贴纸条左内边距 56、右留 59（素材左右透明边不对称）
            view.label = Label(rect, "Label", string.Empty, 28, DialogueOptionInk,
                Vector2.zero, Vector2.one, new Vector2(-1.5f, 0), new Vector2(-115, 0),
                TextAnchor.MiddleLeft, FontStyle.Normal);
        }


        /// <summary>
        /// 生成期给 Image 贴全局面板底图（PC ui/common/Secondary-bg，9 宫格）。
        /// 与运行时的 HouseUIUtil.ApplyPanelSkin 同一张图，区别只是走 AssetDatabase 而非 Resources.Load。
        /// </summary>
        private static void ApplyPanelSkinAsset(Image panel, float alpha = 1f, float borderScale = 1f)
        {
            if (panel == null) return;
            var skin = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/PC ui/common/Secondary-bg.png");
            if (skin == null) return;
            panel.sprite = skin;
            panel.color = new Color(1f, 1f, 1f, alpha);
            panel.type = UnityEngine.UI.Image.Type.Sliced;
            panel.pixelsPerUnitMultiplier = Mathf.Max(.01f, borderScale);
        }

        /// <summary>当日结算面板（访客交付说明 §7）：整屏遮罩 + 居中卡片（标题/结算正文/确认按钮），只展示不惩罚。</summary>
        private static void BuildDaySettlePanel(string path)
        {
            var root = Root("DaySettlePanel");
            var view = root.AddComponent<OutGameDaySettleView>();
            view.scrim = Image(root.transform, "Scrim", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Color(.005f, .008f, .02f, .72f));
            view.panel = Rect(root.transform, "Panel", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                Vector2.zero, new Vector2(680, 460));
            ImageOn(view.panel, new Color(.035f, .025f, .045f, .96f));
            view.title = Label(view.panel, "Title", "DAY 01 结算", 30, Hex("E22D76"),
                new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -60), new Vector2(600, 50),
                TextAnchor.MiddleCenter, FontStyle.Bold);
            view.body = Label(view.panel, "Body", string.Empty, 20, Hex("F3E8DD"),
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 10), new Vector2(580, 250),
                TextAnchor.UpperLeft, FontStyle.Normal);
            view.confirmButton = PageButton(view.panel, "Confirm", "开始新的一天 →", new Vector2(0, 55),
                new Vector2(300, 62), Hex("6E243E"), Hex("F3E8DD"), 20, TextAnchor.MiddleCenter, new Vector2(.5f, 0));
            view.confirmLabel = view.confirmButton.GetComponentInChildren<Text>();
            Save(root, path);
        }

        /// <summary>
        /// Hub 场景世界层（2026-08-16 场景固化）：主楼剖面底图 + 四间房画面矩形 + 接待室区域标记。
        /// 锚点/uvRect 初值取 HubWorldGrid 的标定常量，之后在 Prefab 里手调即为真相（运行时反读同步）。
        /// </summary>
        private static void BuildHubSceneWorld(string path)
        {
            var root = new GameObject("HubSceneWorld", typeof(RectTransform));
            root.layer = 5;
            var rootRect = (RectTransform)root.transform;
            rootRect.pivot = Vector2.zero; // 相机数学以左下角为原点
            rootRect.anchorMin = rootRect.anchorMax = Vector2.zero;
            rootRect.sizeDelta = new Vector2(1920, 1080); // 设计尺寸：Prefab 模式可视化编辑用；运行时按视口重设
            var view = root.AddComponent<OutGameHubWorldView>();

            view.houseBackdrop = Raw(root.transform, "HouseBackdrop", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            view.houseBackdrop.texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/OutGameUI/house-main.png");

            view.roomArts = new RawImage[HubWorldGrid.RoomCount];
            for (var room = 0; room < HubWorldGrid.RoomCount; room++)
            {
                var region = HubWorldGrid.RegionOf(room);
                var art = Raw(root.transform, "RoomArt" + room, region.min, region.max, Vector2.zero, Vector2.zero);
                art.uvRect = HubWorldGrid.ContentCropOf(room);
                view.roomArts[room] = art;
            }
            var reception = HubWorldGrid.RegionOf(HubWorldGrid.Reception);
            view.receptionArea = Rect(root.transform, "ReceptionArea", reception.min, reception.max, Vector2.zero, Vector2.zero);
            view.receptionWalkArea = AppendReceptionWalkArea(root.transform, view.receptionArea);
            Save(root, path);
        }

        /// <summary>
        /// 接待室的贴地可走带（2026-08-18 反馈「底层需要贴着地走」）：
        /// 接待室区域矩形的下沿并不是地板线，访客照区域比例站会浮在半空。这里补一个独立矩形，
        /// **拖到地板上就是可走范围**。默认值按主楼剖面里底层地板的位置估的，仍以 Prefab 手调为准。
        /// 已存在则复用（不动手调）。
        /// </summary>
        private static RectTransform AppendReceptionWalkArea(Transform root, RectTransform receptionArea)
        {
            var existing = root.Find("ReceptionWalkArea") as RectTransform;
            if (existing != null) return existing;
            var area = receptionArea != null
                ? UnityEngine.Rect.MinMaxRect(receptionArea.anchorMin.x, receptionArea.anchorMin.y,
                    receptionArea.anchorMax.x, receptionArea.anchorMax.y)
                : HubWorldGrid.RegionOf(HubWorldGrid.Reception);
            // 横向从区域两侧各收 2%，纵向落在区域下沿附近的地板一带
            var inset = area.width * .02f;
            var min = new Vector2(area.xMin + inset, area.yMin - area.height * .11f);
            var max = new Vector2(area.xMax - inset, area.yMin + area.height * .05f);
            return Rect(root, "ReceptionWalkArea", min, max, Vector2.zero, Vector2.zero);
        }

        /// <summary>通用确认弹窗（首用例：结束今天，2026-08-14）。文本由 ConfirmOverlay 运行时绑定。</summary>
        private static void BuildConfirmPopup(string path)
        {
            var root = Root("ConfirmPopup");
            var view = root.AddComponent<OutGameConfirmPopupView>();
            view.scrim = Image(root.transform, "Scrim", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Color(.005f, .008f, .02f, .72f));
            view.panel = Rect(root.transform, "Panel", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                Vector2.zero, new Vector2(560, 300));
            ImageOn(view.panel, new Color(.035f, .025f, .045f, .96f));
            view.title = Label(view.panel, "Title", "确认", 28, Hex("E22D76"),
                new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -52), new Vector2(480, 44),
                TextAnchor.MiddleCenter, FontStyle.Bold);
            view.body = Label(view.panel, "Body", string.Empty, 20, Hex("F3E8DD"),
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 12), new Vector2(460, 110),
                TextAnchor.MiddleCenter, FontStyle.Normal);
            view.cancelButton = PageButton(view.panel, "Cancel", "再想想", new Vector2(-125, 48),
                new Vector2(210, 58), new Color(1, 1, 1, .08f), Hex("F3E8DD"), 20, TextAnchor.MiddleCenter, new Vector2(.5f, 0));
            view.cancelLabel = view.cancelButton.GetComponentInChildren<Text>();
            view.confirmButton = PageButton(view.panel, "Confirm", "确认", new Vector2(125, 48),
                new Vector2(210, 58), Hex("6E243E"), Hex("F3E8DD"), 20, TextAnchor.MiddleCenter, new Vector2(.5f, 0));
            view.confirmLabel = view.confirmButton.GetComponentInChildren<Text>();
            AppendConfirmKeycaps(view); // ESC/空格键帽（2026-08-17）
            Save(root, path);
        }

        /// <summary>确认弹窗增量（2026-08-17 键位可视化）：取消钮左侧补 ESC 键帽、确认钮左侧补空格键帽，只补缺失。</summary>
        private static void AppendConfirmKeycaps(OutGameConfirmPopupView view)
        {
            if (view.cancelButton != null && view.cancelButton.transform.Find("EscCap") == null)
            {
                var cap = Image(view.cancelButton.transform, "EscCap", new Vector2(0, .5f), new Vector2(0, .5f),
                    new Vector2(32, 0), new Vector2(44, 28), Color.white);
                cap.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(KeycapDir + "ESC.png");
                cap.preserveAspect = true;
                cap.raycastTarget = false;
            }
            if (view.confirmButton != null && view.confirmButton.transform.Find("SpaceCap") == null)
            {
                var cap = Image(view.confirmButton.transform, "SpaceCap", new Vector2(0, .5f), new Vector2(0, .5f),
                    new Vector2(36, 0), new Vector2(54, 26), Color.white);
                cap.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(KeycapDir + "space.png");
                cap.preserveAspect = true;
                cap.raycastTarget = false;
            }
        }

        /// <summary>开始新一天的日出过场层（2026-08-14）。Prefab 存入夜静态状态，破晓推移在 DayTransitionFx。</summary>
        private static void BuildDayTransition(string path)
        {
            var root = Root("DayTransition");
            var view = root.AddComponent<OutGameDayTransitionView>();
            // 夜空盖屏（raycastTarget 默认开：过场期间挡输入）
            view.sky = Image(root.transform, "Sky", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                Hex("05071A"));
            // 地平线光晕：复用 soft-shadow 的软椭圆渐变，夜里是幽蓝月光
            view.glow = Image(root.transform, "HorizonGlow", new Vector2(.5f, 0), new Vector2(.5f, 0),
                new Vector2(0, -60), new Vector2(1700, 560), new Color(.16f, .2f, .42f, .55f));
            view.glow.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/OutGameUI/soft-shadow.png");
            view.glow.raycastTarget = false;
            view.dayLabel = Label(root.transform, "DayLabel", "DAY 01", 52, Hex("F3E8DD"),
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 46), new Vector2(600, 70),
                TextAnchor.MiddleCenter, FontStyle.Bold);
            view.subLabel = Label(root.transform, "SubLabel", "新的一天，开门迎客", 21, Hex("F3E8DD", .8f),
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, -12), new Vector2(600, 40),
                TextAnchor.MiddleCenter, FontStyle.Normal);
            AppendDayTransitionSettleNodes(view); // 结算并入过场（2026-08-14）
            Save(root, path);
        }

        /// <summary>日出过场增量（2026-08-14 结算并入过场）：夜幕结算正文 + 「点击任意处」提示，只补缺失节点。</summary>
        private static void AppendDayTransitionSettleNodes(OutGameDayTransitionView view)
        {
            if (view.bodyLabel == null)
                view.bodyLabel = Label(view.transform, "SettleBody", string.Empty, 20, Hex("F3E8DD"),
                    new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, -95), new Vector2(720, 190),
                    TextAnchor.UpperCenter, FontStyle.Normal);
            if (view.hintLabel == null)
                view.hintLabel = Label(view.transform, "SettleHint", "点击任意处 · 开始新的一天", 18, Hex("F3E8DD", .65f),
                    new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, -235), new Vector2(500, 34),
                    TextAnchor.MiddleCenter, FontStyle.Normal);
            RemoveDayTransitionSunNodes(view);
            AppendDayTransitionCycleNodes(view);
        }

        /// <summary>
        /// 日出过场增量（2026-08-14 视频分帧背景）：全屏分帧画布 + 结算文案迁到左上角（用户定案）。
        /// 画布默认隐藏，运行时有帧素材才启用；文案只在仍处于旧版居中位时迁移，手调过不动。
        /// </summary>
        private static void AppendDayTransitionCycleNodes(OutGameDayTransitionView view)
        {
            if (view.cycleFrames != null) return;
            var raw = Raw(view.transform, "CycleFrames", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            raw.raycastTarget = true; // 过场期间挡输入 + 充当「点击任意处」的点击面
            view.cycleFrames = raw;
            // 层序：夜空/光晕之上、结算文字之下
            if (view.glow != null) raw.transform.SetSiblingIndex(view.glow.transform.GetSiblingIndex() + 1);
            raw.gameObject.SetActive(false);
            // 结算讯息迁往左上角（帧画面主体在中央，文字避让）；深色衬底保证白字可读
            var scrim = Image(view.transform, "SettleScrim", new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(390, -195), new Vector2(720, 330), new Color(0, 0, 0, .45f));
            scrim.raycastTarget = false;
            scrim.transform.SetSiblingIndex(raw.transform.GetSiblingIndex() + 1);
            MoveToTopLeft(view.dayLabel, new Vector2(340, -80), TextAnchor.MiddleLeft);
            MoveToTopLeft(view.bodyLabel, new Vector2(390, -240), TextAnchor.UpperLeft);
        }

        /// <summary>把仍在屏幕中央默认位的文字迁到左上角锚区；已手调过（锚点非中心）则不动。</summary>
        private static void MoveToTopLeft(Text label, Vector2 position, TextAnchor alignment)
        {
            if (label == null) return;
            var rect = label.rectTransform;
            if (rect.anchorMin != new Vector2(.5f, .5f)) return; // 已被手调离开默认锚，尊重现状
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.anchoredPosition = position;
            label.alignment = alignment;
        }

        /// <summary>太阳特效已退役（2026-08-14 用户定案）：清掉此前增量补进 Prefab 的太阳节点。</summary>
        private static void RemoveDayTransitionSunNodes(OutGameDayTransitionView view)
        {
            var disc = view.transform.Find("SunDisc");
            if (disc != null) Object.DestroyImmediate(disc.gameObject);
            var rays = view.transform.Find("SunRays");
            if (rays != null) Object.DestroyImmediate(rays.gameObject);
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
            view.makeButton = PageButton(recipe.transform, "Make", "开始制作", new Vector2(-152, 35), new Vector2(280, 58),
                Hex("6E243E"), Hex("F3E8DD"), 19, TextAnchor.MiddleCenter, new Vector2(.5f, 0));
            view.makeLabel = view.makeButton.GetComponentInChildren<Text>();
            AppendDeviceRepairButton(view); // 「前往修理」并排按钮（2026-08-14）
            Save(root, path);
        }

        /// <summary>
        /// 图鉴详情区增量（2026-08-14）：补「前往修理」按钮，与「前往摆放」左右并排。
        /// 「摆放」若还停在旧版居中位（x=0）则挪到左位；已手调过（x≠0）就不动，把「修理」放到它的镜像位。
        /// </summary>
        private static void AppendDeviceRepairButton(OutGameDevicePanelView view)
        {
            if (view.repairButton != null || view.makeButton == null) return;
            var makeRect = (RectTransform)view.makeButton.transform;
            if (Mathf.Approximately(makeRect.anchoredPosition.x, 0f))
                makeRect.anchoredPosition = new Vector2(-152, makeRect.anchoredPosition.y);
            var mirrored = new Vector2(-makeRect.anchoredPosition.x, makeRect.anchoredPosition.y);
            view.repairButton = PageButton(makeRect.parent, "Repair", "前往修理", mirrored, makeRect.sizeDelta,
                Hex("24466E"), Hex("F3E8DD"), 19, TextAnchor.MiddleCenter, new Vector2(.5f, 0));
            view.repairLabel = view.repairButton.GetComponentInChildren<Text>();
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
            view.codexButton = AppendArchiveCodexButton(root.transform);
            Save(root, path);
        }

        /// <summary>档案页的「访客图鉴」入口：排在两个页签右侧；已存在则复用（不动手调）。</summary>
        private static Button AppendArchiveCodexButton(Transform root)
        {
            var existing = root.Find("CodexEntry");
            if (existing != null) return existing.GetComponent<Button>();
            return PageButton(root, "CodexEntry", "访客图鉴",
                new Vector2(610, -45), new Vector2(220, 58),
                new Color(1, 1, 1, .04f), Hex("F3E8DD"), 19, TextAnchor.MiddleCenter, new Vector2(0, 1));
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
            // 家具缩略图（图鉴列真实摆放家具，2026-08-14）：容器定显示范围，图在其内保比例自适应
            var thumbArea = Rect(root.transform, "ThumbArea", new Vector2(.5f, 1), new Vector2(.5f, 1),
                new Vector2(0, -92), new Vector2(200, 150));
            refs.thumb = Raw(thumbArea, "Thumb", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var fitter = refs.thumb.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            refs.label = Label(root.transform, "Label", "家具名\n<size=13>分类 · 装饰分</size>",
                19, Hex("F3E8DD"), new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 52),
                new Vector2(225, 88), TextAnchor.MiddleCenter, FontStyle.Bold);
            Save(root, path);
        }

        /// <summary>旧版设备卡无损补缺：加缩略图容器（家具图鉴改列真实摆放家具）。</summary>
        private static void AppendDeviceCardThumb(GameObject root, DeviceCardView view)
        {
            if (view.thumb != null) return;
            var thumbArea = Rect(root.transform, "ThumbArea", new Vector2(.5f, 1), new Vector2(.5f, 1),
                new Vector2(0, -92), new Vector2(200, 150));
            view.thumb = Raw(thumbArea, "Thumb", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var fitter = view.thumb.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
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

        /// <summary>
        /// 键帽按钮（PC ui/button 素材，键名如 "Q"/"E"/"ESC"/"enter"）：
        /// 默认/悬停/禁用三态 SpriteSwap，键帽画面自带文字，不再生成 Text。
        /// </summary>
        private static Button KeycapButton(Transform parent, string name, string key,
            Vector2 anchor, Vector2 position, Vector2 size)
        {
            const string keyDir = "Assets/PC ui/button/";
            var normal = AssetDatabase.LoadAssetAtPath<Sprite>(keyDir + "default/" + key + ".png");
            var hover = AssetDatabase.LoadAssetAtPath<Sprite>(keyDir + "hover/" + key + ".png");
            var disabled = AssetDatabase.LoadAssetAtPath<Sprite>(keyDir + "Disable/" + key + ".png");
            var image = Image(parent, name, anchor, anchor, position, size, Color.white);
            image.sprite = normal;
            image.preserveAspect = true;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = hover,
                pressedSprite = hover,
                selectedSprite = normal,
                disabledSprite = disabled != null ? disabled : normal,
            };
            AddTweenFeedback(button);
            return button;
        }

        /// <summary>家具收纳栏槽位模板：四种状态元素全部烘上，运行时按状态显隐。</summary>
        /// <summary>
        /// 配色色块模板（家具族体系说明 §4.3）：外框走 store/color-* 三态素材、内芯填家具表色值。
        /// 尺寸由 <see cref="ColorSwatchStrip"/> 按使用场合覆写（商城 26、获得弹窗 30、收纳栏更小），
        /// 这里给的是商城那档的初值。
        /// </summary>
        private static void BuildColorSwatch(string path)
        {
            var root = ComponentRoot("ColorSwatch", new Vector2(26, 26));
            var view = root.AddComponent<OutGameColorSwatchView>();
            view.frame = ImageOn((RectTransform)root.transform, Color.white);
            view.frame.preserveAspect = true;
            var frameSprite = Resources.Load<Sprite>("OutGameUI/store/color-deault");
            if (frameSprite != null) view.frame.sprite = frameSprite;

            var fill = Image(root.transform, "Fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Color.white);
            var fillRect = (RectTransform)fill.transform;
            fillRect.offsetMin = new Vector2(4, 4);
            fillRect.offsetMax = new Vector2(-4, -4);
            fill.sprite = HouseUIRuntime.WhiteSprite;
            fill.raycastTarget = false;
            view.fill = fill;

            view.button = root.AddComponent<Button>();
            view.button.transition = Selectable.Transition.None;
            view.button.targetGraphic = view.frame;
            Save(root, path);
        }

        private static void BuildFurnitureSlot(string path)
        {
            var root = ComponentRoot("FurnitureSlot", new Vector2(104, 122));
            var slot = root.AddComponent<OutGameFurnitureSlotView>();
            slot.background = ImageOn((RectTransform)root.transform, new Color(1, 1, 1, .05f));
            var outline = root.AddComponent<Outline>();
            outline.effectColor = new Color(1, 1, 1, .14f);
            outline.effectDistance = new Vector2(1, -1);
            slot.thumb = Image(root.transform, "Thumb", new Vector2(.5f, 1), new Vector2(.5f, 1),
                new Vector2(0, -46), new Vector2(84, 76), Color.white);
            slot.thumb.preserveAspect = true;
            slot.thumb.raycastTarget = false;
            slot.nameLabel = Label(root.transform, "Name", "家具名", 15, Hex("F3E8DD"),
                new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 18), new Vector2(96, 24),
                TextAnchor.MiddleCenter, FontStyle.Normal);
            slot.placedLabel = Label(root.transform, "Placed", "已摆放", 13, new Color(1, 1, 1, .5f),
                new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -12), new Vector2(96, 20),
                TextAnchor.MiddleCenter, FontStyle.Normal);
            slot.placedLabel.gameObject.SetActive(false);

            var lockMask = Image(root.transform, "LockMask", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Color(.05f, .03f, .06f, .45f));
            lockMask.raycastTarget = false;
            slot.lockMask = lockMask.gameObject;
            slot.priceLabel = Label(lockMask.transform, "Price", "可购买\n<color=#D4A46B>◈ 100</color>", 15, Hex("F3E8DD"),
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 10), new Vector2(96, 48),
                TextAnchor.MiddleCenter, FontStyle.Bold);
            slot.lockMask.SetActive(false);

            var unknownMask = Image(root.transform, "UnknownMask", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Color(.05f, .03f, .06f, .6f));
            unknownMask.raycastTarget = false;
            slot.unknownMask = unknownMask.gameObject;
            slot.unknownMark = Label(unknownMask.transform, "Mark", "？", 38, new Color(1, 1, 1, .55f),
                new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -44), new Vector2(96, 52),
                TextAnchor.MiddleCenter, FontStyle.Bold);
            slot.unknownRequirement = Label(unknownMask.transform, "Req", "声望 0 解禁", 13, new Color(1, 1, 1, .55f),
                new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 40), new Vector2(98, 20),
                TextAnchor.MiddleCenter, FontStyle.Normal);
            slot.unknownMask.SetActive(false);
            Save(root, path);
        }

        /// <summary>家具模式 HUD（原型期运行时 uGUI 固化）：顶栏 + 收纳栏（页签/翻页/槽位容器）+ 提示条 + 购买弹窗。</summary>
        private static void BuildFurnitureHudPage(string path)
        {
            var root = Root("FurnitureHudPage");
            var view = root.AddComponent<OutGameFurnitureHudView>();

            // ── 顶部容器（拖拽时淡出 / 隐藏界面时隐藏）──
            var chrome = Rect(root.transform, "TopChrome", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            view.topGroup = chrome.gameObject.AddComponent<CanvasGroup>();

            var title = Image(chrome, "Title", new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(280, -74), new Vector2(500, 104), new Color(.03f, .03f, .05f, .8f));
            var titleOutline = title.gameObject.AddComponent<Outline>();
            titleOutline.effectColor = new Color(.85f, .15f, .45f, .4f);
            titleOutline.effectDistance = new Vector2(1, -1);
            Label(title.transform, "Eyebrow", "FURNITURE MODE", 13, Hex("E22D76"),
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(150, -22), new Vector2(280, 24),
                TextAnchor.MiddleCenter, FontStyle.Normal);
            Label(title.transform, "Name", "家具摆放", 27, Hex("F3E8DD"),
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(150, -52), new Vector2(280, 36),
                TextAnchor.MiddleLeft, FontStyle.Bold);
            Label(title.transform, "Hint", "拖拽摆放 · F 左右翻转 · 双击收纳 · 滚轮缩放 · 右键平移 · ESC 退出", 14,
                new Color(1, 1, 1, .55f), new Vector2(0, 1), new Vector2(0, 1), new Vector2(245, -84),
                new Vector2(470, 24), TextAnchor.MiddleCenter, FontStyle.Normal);

            var economy = Image(chrome, "Economy", new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-680, -60), new Vector2(460, 64), new Color(.03f, .03f, .05f, .8f));
            view.creditLabel = Label(economy.transform, "Value", string.Empty, 20, Hex("F3E8DD"),
                TextAnchor.MiddleCenter, FontStyle.Bold);

            // 购买家具：仓库只展示已拥有（2026-08-14），购买入口在这里 → 退出摆放模式并打开商店
            view.storeButton = PageButton(chrome, "Store", "购买家具", new Vector2(-950, -60),
                new Vector2(160, 64), new Color(.32f, .06f, .18f, .9f), Hex("F3E8DD"), 20, TextAnchor.MiddleCenter, new Vector2(1, 1));
            view.hideUiButton = PageButton(chrome, "HideUi", "隐藏界面", new Vector2(-560, -60),
                new Vector2(160, 64), new Color(.025f, .025f, .04f, .8f), Hex("F3E8DD"), 20, TextAnchor.MiddleCenter, new Vector2(1, 1));
            view.gridToggleButton = PageButton(chrome, "GridToggle", "显示网格", new Vector2(-360, -60),
                new Vector2(160, 64), new Color(.025f, .025f, .04f, .8f), Hex("F3E8DD"), 20, TextAnchor.MiddleCenter, new Vector2(1, 1));
            view.gridToggleLabel = view.gridToggleButton.GetComponentInChildren<Text>();
            view.exitButton = PageButton(chrome, "Exit", "完成 · ESC", new Vector2(-160, -60),
                new Vector2(200, 64), new Color(.32f, .06f, .18f, .9f), Hex("F3E8DD"), 20, TextAnchor.MiddleCenter, new Vector2(1, 1));

            // ── 「显示界面」小按钮（隐藏态唯一入口）──
            view.restoreButton = PageButton(root.transform, "ShowUi", "显示界面", new Vector2(-100, -46),
                new Vector2(140, 52), new Color(.03f, .03f, .05f, .72f), new Color(1, 1, 1, .85f), 17,
                TextAnchor.MiddleCenter, new Vector2(1, 1));
            view.restoreGroup = view.restoreButton.gameObject.AddComponent<CanvasGroup>();
            view.restoreGroup.alpha = 0f;
            view.restoreGroup.blocksRaycasts = false;
            view.restoreGroup.interactable = false;

            // ── 收纳栏（12 槽/页 + 页签 + 翻页）──
            var inventory = Image(root.transform, "Inventory", new Vector2(.5f, 0), new Vector2(.5f, 0),
                new Vector2(0, 96), new Vector2(1476, 168), new Color(.03f, .03f, .05f, .84f));
            var inventoryOutline = inventory.gameObject.AddComponent<Outline>();
            inventoryOutline.effectColor = new Color(1, 1, 1, .12f);
            inventoryOutline.effectDistance = new Vector2(1, -1);
            view.inventoryRect = inventory.rectTransform;
            view.inventoryGroup = inventory.gameObject.AddComponent<CanvasGroup>();
            view.dropHint = Image(inventory.transform, "DropHint", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Color(.89f, .4f, .56f, 0f));
            view.dropHint.raycastTarget = false;

            var tabCaptions = new[] { "地面家具", "桌面家具", "壁挂家具" };
            view.tabButtons = new Button[3];
            view.tabBackgrounds = new Image[3];
            view.tabLabels = new Text[3];
            for (var i = 0; i < 3; i++)
            {
                var tab = PageButton(inventory.transform, "Tab" + i, tabCaptions[i],
                    new Vector2(90 + i * 138, 15), new Vector2(132, 38),
                    new Color(.025f, .025f, .04f, .92f), Hex("F3E8DD"), 16, TextAnchor.MiddleCenter, new Vector2(0, 1));
                view.tabButtons[i] = tab;
                view.tabBackgrounds[i] = tab.targetGraphic as Image;
                view.tabLabels[i] = tab.GetComponentInChildren<Text>();
            }
            view.prevPageButton = PageButton(inventory.transform, "PrevPage", "◀", new Vector2(28, 0),
                new Vector2(40, 96), new Color(1, 1, 1, .07f), Hex("F3E8DD"), 22, TextAnchor.MiddleCenter, new Vector2(0, .5f));
            view.nextPageButton = PageButton(inventory.transform, "NextPage", "▶", new Vector2(-28, 0),
                new Vector2(40, 96), new Color(1, 1, 1, .07f), Hex("F3E8DD"), 22, TextAnchor.MiddleCenter, new Vector2(1, .5f));
            view.pageLabel = Label(inventory.transform, "PageLabel", "1 / 1", 14, new Color(1, 1, 1, .6f),
                new Vector2(1, 1), new Vector2(1, 1), new Vector2(-64, -16), new Vector2(120, 22),
                TextAnchor.MiddleRight, FontStyle.Normal);
            view.slotsRoot = Rect(inventory.transform, "Slots", new Vector2(.5f, 0), new Vector2(.5f, 0),
                new Vector2(0, 84), new Vector2(1368, 168));

            // ── 提示条（Toast 皮肤）──
            var toast = Image(root.transform, "Toast", new Vector2(.5f, 0), new Vector2(.5f, 0),
                new Vector2(0, 300), new Vector2(680, 52), Color.white);
            var toastSkin = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/PC ui/common/Toast.png");
            if (toastSkin != null) toast.sprite = toastSkin;
            else toast.color = new Color(.05f, .04f, .07f, .92f);
            toast.raycastTarget = false;
            view.toastLabel = Label(toast.transform, "Label", string.Empty, 18, Hex("F3E8DD"),
                TextAnchor.MiddleCenter, FontStyle.Normal);
            view.toastGroup = toast.gameObject.AddComponent<CanvasGroup>();
            view.toastGroup.alpha = 0f;

            // ── 购买确认弹窗（默认隐藏）──
            var scrim = Image(root.transform, "PurchasePopup", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Color(0, 0, 0, .45f));
            view.purchaseGroup = scrim.gameObject.AddComponent<CanvasGroup>();
            view.purchaseGroup.alpha = 0f;
            view.purchaseGroup.blocksRaycasts = false;
            view.purchaseGroup.interactable = false;
            view.purchaseScrimButton = scrim.gameObject.AddComponent<Button>();
            view.purchaseScrimButton.transition = Selectable.Transition.None;
            var popup = Image(scrim.transform, "Panel", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                new Vector2(0, 40), new Vector2(430, 240), new Color(.06f, .045f, .08f, .97f));
            var popupOutline = popup.gameObject.AddComponent<Outline>();
            popupOutline.effectColor = new Color(.85f, .15f, .45f, .55f);
            popupOutline.effectDistance = new Vector2(1, -1);
            view.purchaseTitle = Label(popup.transform, "Name", "购买「」", 24, Hex("F3E8DD"),
                new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -46), new Vector2(390, 36),
                TextAnchor.MiddleCenter, FontStyle.Bold);
            view.purchaseDesc = Label(popup.transform, "Desc", string.Empty, 18, new Color(1, 1, 1, .72f),
                new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -96), new Vector2(390, 34),
                TextAnchor.MiddleCenter, FontStyle.Normal);
            view.purchaseConfirmButton = PageButton(popup.transform, "Confirm", "购买", new Vector2(-95, 52),
                new Vector2(160, 58), new Color(.32f, .06f, .18f, .95f), Hex("F3E8DD"), 21, TextAnchor.MiddleCenter, new Vector2(.5f, 0));
            view.purchaseCancelButton = PageButton(popup.transform, "Cancel", "取消", new Vector2(95, 52),
                new Vector2(160, 58), new Color(1, 1, 1, .08f), Hex("F3E8DD"), 21, TextAnchor.MiddleCenter, new Vector2(.5f, 0));
            Save(root, path);
        }

        /// <summary>商店卡片模板（美术三态框：默认 defaul / 悬停 hover / 选中 selected）。</summary>
        /// <summary>商店 2.0 素材目录（2026-08-18 按新设计图重做）。</summary>
        private const string Store2Dir = "Assets/PC ui 2.0/store/";

        /// <summary>
        /// 商店 2.0 整页重建：**显式菜单触发**，会覆盖手调布局，所以先弹确认。
        /// 自动补缺流程（EnsureMissingPrefabs）永远不会调它。
        /// </summary>
        [MenuItem("Tools/MasterHouse/OutGame UI/重建商店页（2.0 设计图）")]
        private static void RebuildStore2()
        {
            if (!EditorUtility.DisplayDialog("按 2.0 设计图重建商店",
                    "会覆盖 StorePage 与 StoreCard 的现有布局（包括手动调整）。确定继续吗？",
                    "覆盖重建", "取消")) return;
            EnsureFolder();
            BuildStoreCard(StoreCardPath);
            BuildStorePage(StorePagePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[OutGameUI] 商店页与卡片已按 2.0 设计图重建。");
        }

        // 商店网格（2026-08-18 反馈「一行五张太开了」）：**仍是一行五张**，把格子与间距收紧，
        // 卡片彼此靠得更近（176×150/间距 12 → 148×140/间距 8）。
        // 高度只收 10：卡片内部的缩略框 112×96 是定尺的，压太狠会顶出卡面。
        private static readonly Vector2 StoreGridCell = new Vector2(148, 140);
        private static readonly Vector2 StoreGridSpacing = new Vector2(8, 8);
        private const int StoreGridColumns = 5;

        /// <summary>
        /// 图鉴卡位（2026-08-18）：**卡位算的是「可见卡面」的重叠，不是矩形的重叠**——
        /// 卡面素材四周有大片透明边距（侧卡可见部分只占矩形的 76.7%×76.0%、
        /// 焦点卡占 81.0%×85.9%），按矩形排出来的「重叠」全被边距吃掉，看着还是各摆各的。
        /// 现在按可见宽反推：五张卡的可见部分首尾相接铺满 0~1920，相邻两张各压 120px，
        /// 纵向高度与倾角逐张错开——「随手摊在墙上」靠的是错落不是间距。
        /// 层序由外向内叠（见 buildOrder），焦点卡压最上。坐标按 1920×1080、锚左上、pivot 居中。
        /// </summary>
        private static readonly (Vector2 Position, Vector2 Size, float Tilt)[] CodexSlots =
        {
            (new Vector2(215, -600), new Vector2(574, 698), -9f),
            (new Vector2(535, -505), new Vector2(574, 698), 3f),
            (new Vector2(937, -575), new Vector2(789, 946), -1.5f),
            (new Vector2(1374, -585), new Vector2(574, 698), -5f),
            (new Vector2(1694, -495), new Vector2(574, 698), 6f),
        };

        /// <summary>
        /// 本生成器历史上产出过的侧卡尺寸：命中其中之一才迁移卡位，手调过就不再命中。
        /// </summary>
        private static readonly Vector2[] CodexStaleSideSizes =
        {
            new Vector2(470, 572), // 初版：偏小、均匀
            new Vector2(620, 754), // 第二版：放大过头，侧卡比设计图大了快一半
            new Vector2(473, 576), // 第三版：尺寸对了，但按矩形算重叠，被透明边距吃光
        };

        private const string CodexDir = "Assets/PC ui 2.0/图鉴/";
        private const string ConversationDir = "Assets/PC ui 2.0/conversation/";

        private static Sprite Codex(string name) =>
            AssetDatabase.LoadAssetAtPath<Sprite>(CodexDir + name + ".png");

        private static Sprite Conversation(string name) =>
            AssetDatabase.LoadAssetAtPath<Sprite>(ConversationDir + name + ".png");

        /// <summary>
        /// 图鉴条目：种族资产 ↔ 卡面素材名。顺序即翻页顺序（跟访客种族表行序）。
        /// 牦牛（yak）暂无卡面素材，故不收录——素材补齐后在这里加一行即可。
        /// </summary>
        private static readonly (string Race, string Art, string Portrait, string Avatar)[] CodexEntries =
        {
            //  种族      图鉴卡面   详情页右页立绘  详情页证件照
            ("rabbit",  "兔子",  "兔",   "兔-2"),
            ("goat",    "羊",    "羊",   "羊-1"),
            ("wolf",    "狼",    "狼",   "狼-1"),
            ("leopard", "豹1",   "豹",   "豹1"),
            ("cheetah", "豹2",   "豹2",  "豹2-1"),
            ("ox",      "牛1",   "牛1",  "牛"),
            ("cat",     "猫",    "猫",   "猫-1"),
        };

        /// <summary>
        /// 访客图鉴页（2026-08-18 按 2.0 设计图新建）：整屏底图 + 左上 Illustrated Guide 标题 +
        /// 一排 5 个卡位（正中为焦点位，两侧越外越靠边、带轻微倾斜）+ 底部 ESC/中键/空格键位条。
        /// 卡面由绑定层按焦点与解锁态填，这里只摆位置并把素材烘进 Prefab。坐标按 1920×1080 口径。
        /// </summary>
        private static void BuildCodexPage(string path)
        {
            var root = Root("CodexPage");
            var view = root.AddComponent<OutGameCodexPageView>();
            view.background = Raw(root.transform, "Background", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            view.background.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(CodexDir + "image 333.png");
            view.background.raycastTarget = true; // 整页承接点击，空白处不穿透到下层

            view.title = Label(root.transform, "Title", "Illustrated Guide", 62, Hex("3E6FA8"),
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(424, -128), new Vector2(700, 84),
                TextAnchor.MiddleLeft, FontStyle.BoldAndItalic);
            var rule = Image(root.transform, "TitleRule", new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(338, -184), new Vector2(529, 44), Color.white);
            rule.sprite = Codex("Mask group");
            rule.preserveAspect = true;
            rule.raycastTarget = false;

            // 卡位：左→右 5 个，正中放大。建节点的顺序 = 层序，故从外向内建，焦点卡最后建（画在最上）
            var cards = Rect(root.transform, "Cards", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var buildOrder = new[] { 0, 4, 1, 3, 2 }; // 越靠外越先建（被里面的压住），焦点卡压最上
            view.cardSlots = new Image[CodexSlots.Length];
            view.cardButtons = new Button[CodexSlots.Length];
            foreach (var i in buildOrder)
            {
                var layout = CodexSlots[i];
                var slot = Image(cards, "Card" + i, new Vector2(0, 1), new Vector2(0, 1),
                    layout.Position, layout.Size, Color.white);
                slot.preserveAspect = true;
                slot.rectTransform.localEulerAngles = new Vector3(0, 0, layout.Tilt);
                var button = slot.gameObject.AddComponent<Button>();
                button.targetGraphic = slot;
                button.transition = Selectable.Transition.None; // 卡面三态由绑定层换图，别叠一层染色
                view.cardSlots[i] = slot;
                view.cardButtons[i] = button;
            }

            view.focusNumberRoot = BuildCodexFocusNumber(view.cardSlots[2].rectTransform, ref view.focusNumber);

            // 图鉴条目：种族资产 + 两张卡面（彩色/剪影）一并烘进 Prefab
            var races = new System.Collections.Generic.List<VisitorRaceDef>();
            var revealed = new System.Collections.Generic.List<Sprite>();
            var hidden = new System.Collections.Generic.List<Sprite>();
            foreach (var entry in CodexEntries)
            {
                var race = AssetDatabase.LoadAssetAtPath<VisitorRaceDef>(
                    "Assets/Resources/OutGameUI/VisitorRaces/Race_" + entry.Race + ".asset");
                var on = Codex(entry.Art + "-显示");
                var off = Codex(entry.Art + "-不显示");
                if (race == null || on == null || off == null)
                {
                    Debug.LogWarning($"[OutGameUI] 图鉴条目素材不全，已跳过：{entry.Race}/{entry.Art}");
                    continue;
                }
                races.Add(race);
                revealed.Add(on);
                hidden.Add(off);
            }
            view.races = races.ToArray();
            view.revealedCards = revealed.ToArray();
            view.hiddenCards = hidden.ToArray();

            // 底部键位条（整图素材自带键名与文案）
            view.backButton = SpriteButton(root.transform, "BackButton",
                Settings2("ESC-默认"), Settings2("ESC-悬停"),
                new Vector2(0, 0), new Vector2(152, 57), new Vector2(186, 76));
            view.switchButton = SpriteButton(root.transform, "SwitchButton",
                Conversation("中键-默认"), Conversation("中键-悬停"),
                new Vector2(1, 0), new Vector2(-359, 57), new Vector2(186, 76));
            // 设计图上这颗写的是「查看」，2.0 目前只有「确认」那张空格图，先用它顶上
            view.viewButton = SpriteButton(root.transform, "ViewButton",
                Conversation("space-默认"), Conversation("space-悬停"),
                new Vector2(1, 0), new Vector2(-163, 57), new Vector2(186, 76));
            Save(root, path);
        }

        // 卡面素材（1984×2378）里 NO.001 那行的实测位置：字形盒 x 410~628、y 538~585，
        // 基线随卡面倾斜约 4.1° 上扬；周围纸色恒为 #E3D7CC、墨色 #5B79A8（七张卡一致）。
        private static readonly Vector2 CodexNumberSourceCenter = new Vector2(519f, 561f);
        private static readonly Vector2 CodexNumberSourceSize = new Vector2(246f, 72f);
        private const float CodexCardSourceWidth = 1984f;
        private const float CodexCardSourceHeight = 2378f;
        private const float CodexNumberTiltDegrees = 4.1f;

        /// <summary>
        /// 焦点卡编号：卡面把 NO.001 画死在图里（七张全是 001），这里在原位盖一块纸色补丁，
        /// 再按当前条目写真实编号。位置由素材实测坐标换算，随卡面倾角一起转。
        /// **挂在焦点卡自己身下**——卡片点击缩放/位移时，编号跟着一起动（2026-08-18 反馈）。
        /// </summary>
        /// <summary>卡位还停在初版那套「偏小、均匀」的排布上（判据是侧卡尺寸恰为初版值）。</summary>
        private static bool CodexSlotsAreStale(OutGameCodexPageView view)
        {
            if (view.cardSlots == null || view.cardSlots.Length != CodexSlots.Length) return false;
            var side = view.cardSlots[0];
            if (side == null) return false;
            var size = side.rectTransform.sizeDelta;
            foreach (var stale in CodexStaleSideSizes)
                if (Mathf.Abs(size.x - stale.x) < 1f && Mathf.Abs(size.y - stale.y) < 1f) return true;
            return false;
        }

        /// <summary>把卡位换成新排布（放大 + 压边 + 错落）。只在命中初版值时调用，不覆盖手调。</summary>
        private static void MigrateCodexSlots(OutGameCodexPageView view)
        {
            if (!CodexSlotsAreStale(view)) return;
            for (var i = 0; i < view.cardSlots.Length; i++)
            {
                var slot = view.cardSlots[i];
                if (slot == null) continue;
                var layout = CodexSlots[i];
                var rect = slot.rectTransform;
                rect.anchoredPosition = layout.Position;
                rect.sizeDelta = layout.Size;
                rect.localEulerAngles = new Vector3(0, 0, layout.Tilt);
            }
        }

        private static RectTransform BuildCodexFocusNumber(RectTransform card, ref Text label)
        {
            var slotSize = card.sizeDelta;
            var scale = slotSize.x / CodexCardSourceWidth;
            // 素材坐标以卡面左上角为原点；卡片 pivot 居中，故换算成「相对卡心」的偏移
            var offset = new Vector2(
                (CodexNumberSourceCenter.x - CodexCardSourceWidth * .5f) * scale,
                -(CodexNumberSourceCenter.y - CodexCardSourceHeight * .5f) * scale);
            var size = CodexNumberSourceSize * scale;

            var root = Rect(card, "FocusNumber", new Vector2(.5f, .5f), new Vector2(.5f, .5f), offset, size);
            root.localEulerAngles = new Vector3(0, 0, CodexNumberTiltDegrees);
            var patch = ImageOn(root, Hex("E3D7CC")); // 盖掉画死的 NO.001
            patch.raycastTarget = false;
            label = Label(root, "Value", "NO.001", 20, Hex("5B79A8"),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                TextAnchor.MiddleCenter, FontStyle.Normal);
            label.raycastTarget = false;
            return root;
        }

        private const string CodexDetailDir = "Assets/PC ui 2.0/图鉴-详情/";

        private static Sprite Detail(string name) =>
            AssetDatabase.LoadAssetAtPath<Sprite>(CodexDetailDir + name + ".png");

        private static Texture2D DetailTex(string sub, string name) =>
            AssetDatabase.LoadAssetAtPath<Texture2D>(CodexDetailDir + sub + "/" + name + ".png");

        /// <summary>
        /// 图鉴详情页（2026-08-19 按 2.0 设计图）：整屏书页底图，左页档案文字、右页立绘。
        /// 各块都按素材原比例摆（证件卡 1384×812、徽记框 856×519、QUOTE 1090×676、
        /// 称号牌 908×174、帆船 1182×1046、右页立绘 2802×2612）。坐标按 1920×1080 口径，
        /// 由设计图（2959×1662）等比折算。文案空着，由绑定层按种族写。
        /// </summary>
        private static void BuildCodexDetail(string path)
        {
            var root = Root("CodexDetail");
            var view = root.AddComponent<OutGameCodexDetailView>();
            view.background = Raw(root.transform, "Background", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            view.background.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(CodexDetailDir + "底图.png");
            view.background.raycastTarget = true;

            // 右页立绘先建（压在左页文字之下，立绘边缘会越到中缝）
            view.portrait = Raw(root.transform, "Portrait", new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(1435, -595), new Vector2(950, 885));
            var ship = Image(root.transform, "ShipDecor", new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(1790, -941), new Vector2(240, 212), Color.white);
            ship.sprite = Detail("船");
            ship.preserveAspect = true;
            ship.raycastTarget = false;
            view.shipDecor = ship;

            view.title = Label(root.transform, "Title", "CHARACTER", 58, Hex("3E6FA8"),
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(440, -224), new Vector2(360, 80),
                TextAnchor.MiddleLeft, FontStyle.Bold);
            var rule = Image(root.transform, "TitleRule", new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(437, -262), new Vector2(346, 28), Color.white);
            rule.sprite = Detail("Mask group");
            rule.preserveAspect = true;
            rule.raycastTarget = false;

            view.nameLabel = Label(root.transform, "Name", string.Empty, 34, Hex("3E6FA8"),
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(388, -355), new Vector2(200, 46),
                TextAnchor.MiddleLeft, FontStyle.Bold);
            view.aliasLabel = Label(root.transform, "Alias", string.Empty, 24, Hex("6E8FBF"),
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(560, -358), new Vector2(180, 36),
                TextAnchor.MiddleLeft, FontStyle.BoldAndItalic);

            // 称号牌：整张图（左边星区 + 右边牌子），三颗星单独压上去按星级显隐
            var plate = Image(root.transform, "RatingPlate", new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(446, -403), new Vector2(317, 61), Color.white);
            plate.sprite = Detail("等级");
            plate.preserveAspect = true;
            plate.raycastTarget = false;
            view.ratingPlate = plate;
            view.stars = new Image[3];
            for (var i = 0; i < 3; i++)
            {
                var star = Image(plate.transform, "Star" + i, new Vector2(0, .5f), new Vector2(0, .5f),
                    new Vector2(30 + i * 36, 0), new Vector2(30, 30), new Color(1f, 1f, 1f, 0f));
                star.raycastTarget = false;
                view.stars[i] = star; // 素材里星已画好，这里只做「多余的星藏掉」的开关位
            }
            view.titleLabel = Label(plate.transform, "TitleText", string.Empty, 22, Hex("6B5B4E"),
                new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-88, 0), new Vector2(150, 34),
                TextAnchor.MiddleCenter, FontStyle.Bold);

            view.hobbiesLabel = Label(root.transform, "Hobbies", string.Empty, 22, Hex("4A4038"),
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(600, -472), new Vector2(560, 34),
                TextAnchor.MiddleLeft, FontStyle.Normal);
            view.introLabel = Label(root.transform, "Intro", string.Empty, 22, Hex("4A4038"),
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(600, -600), new Vector2(560, 190),
                TextAnchor.UpperLeft, FontStyle.Normal);

            var quote = Image(root.transform, "QuotePaper", new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(410, -773), new Vector2(312, 194), Color.white);
            quote.sprite = Detail("QUOTE1");
            quote.preserveAspect = true;
            quote.raycastTarget = false;
            view.quotePaper = quote;
            view.quotePapers = new[] { Detail("QUOTE1"), Detail("QUOTE2"), Detail("QUOTE3"), Detail("QUOTE4") };
            view.quoteLabel = Label(quote.transform, "QuoteText", string.Empty, 20, Hex("4A6FA5"),
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(6, -18), new Vector2(240, 92),
                TextAnchor.MiddleCenter, FontStyle.Normal);
            var emblem = Image(root.transform, "EmblemBox", new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(749, -749), new Vector2(240, 145), Color.white);
            emblem.sprite = Detail("Group 111");
            emblem.preserveAspect = true;
            emblem.raycastTarget = false;
            view.emblemBox = emblem;

            var card = Image(root.transform, "IdCard", new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(821, -243), new Vector2(298, 175), Color.white);
            card.sprite = Detail("Group 110");
            card.preserveAspect = true;
            card.raycastTarget = false;
            view.idCard = card;
            view.idAvatar = Raw(card.transform, "Avatar", new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(96, -46), new Vector2(58, 58));
            view.idName = Label(card.transform, "IdName", string.Empty, 18, Hex("3E6FA8"),
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(206, -58), new Vector2(120, 30),
                TextAnchor.MiddleLeft, FontStyle.Bold);

            // 条目：与图鉴页同一份种族顺序，右页立绘与头像按族烘进来
            var races = new System.Collections.Generic.List<VisitorRaceDef>();
            var portraits = new System.Collections.Generic.List<Texture2D>();
            var avatars = new System.Collections.Generic.List<Texture2D>();
            foreach (var entry in CodexEntries)
            {
                var race = AssetDatabase.LoadAssetAtPath<VisitorRaceDef>(
                    "Assets/Resources/OutGameUI/VisitorRaces/Race_" + entry.Race + ".asset");
                if (race == null) continue;
                races.Add(race);
                portraits.Add(DetailTex("右侧立绘", entry.Portrait));
                avatars.Add(DetailTex("头像", entry.Avatar));
            }
            view.races = races.ToArray();
            view.portraits = portraits.ToArray();
            view.avatars = avatars.ToArray();

            view.backButton = SpriteButton(root.transform, "BackButton",
                Detail("ESC-默认"), Detail("ESC-hover"),
                new Vector2(0, 0), new Vector2(152, 57), new Vector2(186, 76));
            view.switchButton = SpriteButton(root.transform, "SwitchButton",
                Detail("中键-默认"), Detail("中键-悬停"),
                new Vector2(1, 0), new Vector2(-163, 57), new Vector2(196, 80));
            Save(root, path);
        }

        [MenuItem("Tools/MasterHouse/OutGame UI/重建图鉴详情页（2.0 设计图）")]
        private static void RebuildCodexDetail()
        {
            if (!EditorUtility.DisplayDialog("按 2.0 设计图重建图鉴详情页",
                    "会覆盖 CodexDetail 的现有布局（包括手动调整）。确定继续吗？", "覆盖重建", "取消")) return;
            EnsureFolder();
            BuildCodexDetail(CodexDetailPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[OutGameUI] 图鉴详情页已按 2.0 设计图重建。");
        }

        private const string EscDir = "Assets/PC ui 2.0/ESC/";

        private static Sprite Esc(string name) =>
            AssetDatabase.LoadAssetAtPath<Sprite>(EscDir + name + ".png");

        /// <summary>
        /// ESC 系统菜单（2026-08-19 按 2.0 设计图）：整屏遮罩 + 纸面板 + 六个条目。
        /// 面板与条目都按素材原比例摆（底板 1968×2120、条目 1114×217），条目文案由绑定层写。
        /// 坐标按 1920×1080 口径。
        /// </summary>
        private static void BuildEscMenu(string path)
        {
            var root = Root("EscMenu");
            var view = root.AddComponent<OutGameEscMenuView>();

            view.scrim = Image(root.transform, "Scrim", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Color(0f, 0f, 0f, .6f)); // 与素材「遮罩」同浓度（alpha 153/255）
            view.scrim.raycastTarget = true;

            // 纸面板：素材 1968×2120（比例 0.928），取屏高的 80%
            var panelHeight = 864f;
            var panelWidth = panelHeight * 1968f / 2120f;
            view.panel = Image(root.transform, "Panel", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                Vector2.zero, new Vector2(panelWidth, panelHeight), Color.white);
            view.panel.sprite = Esc("ESC底板");
            view.panel.raycastTarget = true; // 面板内的空白不穿透到遮罩（免得点边框就退出）

            view.itemNormal = Esc("默认");
            view.itemHover = Esc("悬停");

            // 六个条目：宽 = 面板宽的 60%，高按条目素材比例（1114×217）
            var itemWidth = panelWidth * .6f;
            var itemHeight = itemWidth * 217f / 1114f;
            var gap = itemHeight * .16f;
            var pitch = itemHeight + gap;
            var top = (pitch * 6f - gap) * .5f; // 六条整体在面板内垂直居中
            view.buttons = new Button[6];
            view.buttonFrames = new Image[6];
            view.buttonLabels = new Text[6];
            for (var i = 0; i < 6; i++)
            {
                var frame = Image(view.panel.transform, "Item" + i, new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                    new Vector2(0f, top - itemHeight * .5f - i * pitch), new Vector2(itemWidth, itemHeight),
                    Color.white);
                frame.sprite = view.itemNormal;
                var button = frame.gameObject.AddComponent<Button>();
                button.targetGraphic = frame;
                button.transition = Selectable.Transition.SpriteSwap;
                button.spriteState = new SpriteState
                {
                    highlightedSprite = view.itemHover,
                    pressedSprite = view.itemHover,
                    selectedSprite = view.itemNormal,
                };
                var label = Label(frame.transform, "Label", string.Empty, 30, Hex("4A6FA5"),
                    new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero,
                    new Vector2(itemWidth * .9f, itemHeight * .8f), TextAnchor.MiddleCenter, FontStyle.Bold);
                label.raycastTarget = false;
                view.buttons[i] = button;
                view.buttonFrames[i] = frame;
                view.buttonLabels[i] = label;
            }
            Save(root, path);
        }

        [MenuItem("Tools/MasterHouse/OutGame UI/重建 ESC 菜单（2.0 设计图）")]
        private static void RebuildEscMenu()
        {
            if (!EditorUtility.DisplayDialog("按 2.0 设计图重建 ESC 菜单",
                    "会覆盖 EscMenu 的现有布局（包括手动调整）。确定继续吗？", "覆盖重建", "取消")) return;
            EnsureFolder();
            BuildEscMenu(EscMenuPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[OutGameUI] ESC 菜单已按 2.0 设计图重建。");
        }

        [MenuItem("Tools/MasterHouse/OutGame UI/重建图鉴页（2.0 设计图）")]
        private static void RebuildCodex2()
        {
            if (!EditorUtility.DisplayDialog("按 2.0 设计图重建图鉴页",
                    "会覆盖 CodexPage 的现有布局（包括手动调整）。确定继续吗？",
                    "覆盖重建", "取消")) return;
            EnsureFolder();
            BuildCodexPage(CodexPagePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[OutGameUI] 图鉴页已按 2.0 设计图重建。");
        }

        [MenuItem("Tools/MasterHouse/OutGame UI/重建对话页（2.0 设计图）")]
        private static void RebuildDialogue2()
        {
            if (!EditorUtility.DisplayDialog("按 2.0 设计图重建对话页",
                    "会覆盖 DialogueView 的现有布局（包括手动调整）。确定继续吗？",
                    "覆盖重建", "取消")) return;
            EnsureFolder();
            BuildDialogueView(DialogueViewPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[OutGameUI] 对话页已按 2.0 设计图重建。");
        }

        [MenuItem("Tools/MasterHouse/OutGame UI/重建设置页（2.0 设计图）")]
        private static void RebuildSettings2()
        {
            if (!EditorUtility.DisplayDialog("按 2.0 设计图重建设置页",
                    "会覆盖 SettingsPage 的现有布局（包括手动调整）。确定继续吗？",
                    "覆盖重建", "取消")) return;
            EnsureFolder();
            BuildSettingsPage(SettingsPagePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[OutGameUI] 设置页已按 2.0 设计图重建。");
        }

        private static Sprite Store2(string name) =>
            AssetDatabase.LoadAssetAtPath<Sprite>(Store2Dir + name + ".png");

        /// <summary>整图按钮：素材自带键帽与文字，只做默认/悬停两态 SpriteSwap。</summary>
        private static Button SpriteButton(Transform parent, string name, Sprite normal, Sprite hover,
            Vector2 anchor, Vector2 position, Vector2 size)
        {
            var image = Image(parent, name, anchor, anchor, position, size, Color.white);
            image.sprite = normal;
            image.preserveAspect = true;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = hover, pressedSprite = hover, selectedSprite = normal, disabledSprite = normal,
            };
            AddTweenFeedback(button);
            return button;
        }

        /// <summary>
        /// 商店卡片模板（2026-08-18 新设计图）：商品底板三态 + 居中缩略图 + 底部价格 + 已售罄标签。
        /// 底板素材 452×454，卡片按 176×150 展示（设计图口径 1920×1080）。
        /// </summary>
        private static void BuildStoreCard(string path)
        {
            var normal = Store2("商品底板-默认");
            var hover = Store2("商品底板-hover");
            var selected = Store2("商品底板-选中");

            var root = ComponentRoot("StoreCard", new Vector2(176, 150));
            var card = root.AddComponent<OutGameStoreCardView>();
            card.normalSprite = normal;
            card.hoverSprite = hover;
            card.selectedSprite = selected;
            card.frame = ImageOn((RectTransform)root.transform, Color.white);
            card.frame.sprite = normal;
            card.button = root.AddComponent<Button>();
            card.button.targetGraphic = card.frame;
            card.button.transition = Selectable.Transition.SpriteSwap;
            card.button.spriteState = new SpriteState
            {
                highlightedSprite = hover, pressedSprite = hover, selectedSprite = normal, disabledSprite = normal,
            };
            AddTweenFeedback(card.button);
            // 缩略图区域容器：Prefab 里调它的 Rect = 调图片显示范围；Thumb 在其内保比例自适应
            var thumbArea = Rect(root.transform, "ThumbArea", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                new Vector2(0, 16), new Vector2(112, 96));
            card.thumb = Raw(thumbArea, "Thumb", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var thumbFitter = card.thumb.gameObject.AddComponent<AspectRatioFitter>();
            thumbFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            card.priceLabel = Label(root.transform, "Price", "3,200", 17, Hex("6E8FBF"),
                new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 20), new Vector2(150, 24),
                TextAnchor.MiddleCenter, FontStyle.BoldAndItalic);
            // 已售罄标签：整块盖住卡片中部，绑定层按库存显隐
            var soldOut = Image(root.transform, "SoldOutTag", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                new Vector2(0, 12), new Vector2(120, 33), Color.white);
            soldOut.sprite = Store2("已售罄tag");
            soldOut.preserveAspect = true;
            soldOut.raycastTarget = false;
            soldOut.gameObject.SetActive(false);
            card.soldOutTag = soldOut;
            card.mark = Label(root.transform, "Mark", string.Empty, 24, Hex("6E243E"),
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 14), new Vector2(150, 40),
                TextAnchor.MiddleCenter, FontStyle.Bold);
            Save(root, path);
        }

        /// <summary>
        /// 商店页（STORE，美术示意图重做）：bg 全屏底 + 分类页签（圆标 + Q/E）+
        /// 左侧卡片滚动网格（GridLayout + 滚动条）+ 右侧大预览/描述/价格购买 + 获得弹窗。
        /// </summary>
        private static void BuildStorePage(string path)
        {
            // 2026-08-18 按新设计图重做：整页书页底图 + 左列表右详情，坐标按 1920×1080 口径
            var bgTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Store2Dir + "image 320.png");
            var popupSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/PC ui/common/Secondary-bg.png");

            var root = Root("StorePage");
            var view = root.AddComponent<OutGameStorePageView>();
            for (var i = 0; i < 5; i++)
                view.categorySprites[i] = Store2((i + 1).ToString());

            view.background = Raw(root.transform, "Background", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            view.background.texture = bgTexture;
            view.background.raycastTarget = true; // 挡住下层 Hub 交互

            view.title = Label(root.transform, "Title", "STORE", 62, Hex("4A6FA5"),
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(330, -158), new Vector2(420, 90),
                TextAnchor.MiddleLeft, FontStyle.BoldAndItalic);

            // 分类行：Q 键帽 · 圆标 · FURNITURE 标题 + 描述 · E 键帽
            view.prevCategory = SpriteButton(root.transform, "PrevCategory", Store2("Q"), Store2("Q"),
                new Vector2(0, 1), new Vector2(228, -262), new Vector2(30, 32));
            view.categoryIcon = Image(root.transform, "CategoryIcon", new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(324, -258), new Vector2(76, 76), Color.white);
            view.categoryIcon.preserveAspect = true;
            view.categoryName = Label(root.transform, "CategoryName", "FURNITURE", 30, Hex("4A6FA5"),
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(505, -243), new Vector2(300, 40),
                TextAnchor.MiddleLeft, FontStyle.BoldAndItalic);
            view.categoryDesc = Label(root.transform, "CategoryDesc", string.Empty, 16, Hex("6B5B4E"),
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(590, -280), new Vector2(500, 26),
                TextAnchor.MiddleLeft, FontStyle.Normal);
            view.nextCategory = SpriteButton(root.transform, "NextCategory", Store2("E"), Store2("E"),
                new Vector2(0, 1), new Vector2(1051, -268), new Vector2(30, 32));

            // 右下：COST / YOUR MONEY 面板（Group 35）
            var costPanel = Image(root.transform, "CostPanel", new Vector2(1, 0), new Vector2(1, 0),
                new Vector2(-292, 246), new Vector2(428, 136), Color.white);
            costPanel.sprite = Store2("Group 35");
            costPanel.raycastTarget = false;

            // 左侧卡片滚动网格：视口 + GridLayout 内容 + 竖向滚动条
            var viewport = Rect(root.transform, "GridViewport", new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(575, -640), new Vector2(950, 620));
            var viewportImage = ImageOn(viewport, new Color(1, 1, 1, .01f));
            viewportImage.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Rect(viewport, "Content", new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, new Vector2(0, 0));
            content.pivot = new Vector2(.5f, 1);
            var gridLayout = content.gameObject.AddComponent<GridLayoutGroup>();
            gridLayout.cellSize = StoreGridCell;
            gridLayout.spacing = StoreGridSpacing;
            gridLayout.padding = new RectOffset(4, 4, 4, 4);
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = StoreGridColumns;
            gridLayout.childAlignment = TextAnchor.UpperLeft;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;
            var scrollbarRect = Rect(root.transform, "GridScrollbar", new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(1068, -640), new Vector2(8, 620));
            ImageOn(scrollbarRect, new Color(1, 1, 1, .06f));
            var scrollbar = scrollbarRect.gameObject.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            var handleRect = Rect(scrollbarRect, "Handle", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var handleImage = ImageOn(handleRect, Hex("E22D76", .55f));
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleImage;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            view.scroll = scroll;
            view.gridContent = content;
            view.emptyLabel = Label(root.transform, "EmptyLabel", "——— 检索不到相关家具 ———", 18, Hex("6B5B4E"),
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(575, -640), new Vector2(500, 40),
                TextAnchor.MiddleCenter, FontStyle.Normal);

            // 右侧详情页：大预览 + 名称 + 分割线 + 描述（色块行由 AppendStoreRedesignNodes 补）
            view.preview = Raw(root.transform, "Preview", new Vector2(1, .5f), new Vector2(1, .5f),
                new Vector2(-618, 62), new Vector2(520, 620));
            var previewFitter = view.preview.gameObject.AddComponent<AspectRatioFitter>();
            previewFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            view.itemName = Label(root.transform, "ItemName", string.Empty, 30, Hex("4A6FA5"),
                new Vector2(1, 1), new Vector2(1, 1), new Vector2(-278, -406), new Vector2(400, 40),
                TextAnchor.MiddleLeft, FontStyle.BoldAndItalic);
            var nameLine = Image(root.transform, "ItemNameLine", new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-278, -444), new Vector2(240, 20), Color.white);
            nameLine.sprite = Store2("分割线");
            nameLine.preserveAspect = true;
            nameLine.raycastTarget = false;
            view.itemDesc = Label(root.transform, "ItemDesc", string.Empty, 17, Hex("6B5B4E"),
                new Vector2(1, 1), new Vector2(1, 1), new Vector2(-278, -516), new Vector2(400, 110),
                TextAnchor.UpperLeft, FontStyle.Normal);

            // COST 值贴在右下面板上；YOUR MONEY 用 tokenLabel（同一面板第二行）
            view.priceLabel = Label(costPanel.transform, "CostValue", string.Empty, 26, Hex("F3E8DD"),
                new Vector2(1, 1), new Vector2(1, 1), new Vector2(-60, -34), new Vector2(160, 36),
                TextAnchor.MiddleRight, FontStyle.BoldAndItalic);
            view.tokenLabel = Label(costPanel.transform, "MoneyValue", "0", 26, Hex("F3E8DD"),
                new Vector2(1, 1), new Vector2(1, 1), new Vector2(-60, -96), new Vector2(160, 36),
                TextAnchor.MiddleRight, FontStyle.BoldAndItalic);

            // 底部键位条：ESC 返回 / X 改变颜色 / 空格 购买（整图素材自带文字）
            view.closeButton = SpriteButton(root.transform, "Close",
                Store2("ESC-默认"), Store2("ESC-hover"),
                new Vector2(0, 0), new Vector2(228, 66), new Vector2(186, 76));
            view.buyButton = SpriteButton(root.transform, "Buy",
                Store2("space-默认"), Store2("space-悬停"),
                new Vector2(1, 0), new Vector2(-238, 66), new Vector2(186, 76));
            // X 改变颜色：整图自带文字，故 Label 置空只留引用（StoreOverlay 靠它做无商品时的显隐）
            view.colorKeycap = Image(root.transform, "ColorKeycap", new Vector2(1, 0), new Vector2(1, 0),
                new Vector2(-470, 66), new Vector2(186, 76), Color.white);
            view.colorKeycap.sprite = Store2("X-默认");
            view.colorKeycapHover = Store2("X-悬停");
            view.colorKeycap.preserveAspect = true;
            view.colorKeycap.raycastTarget = true; // 绑定层会补 Button：可点、可悬停
            view.colorKeycapLabel = Label(root.transform, "ColorKeycapHint", string.Empty, 16, Hex("6B5B4E"),
                new Vector2(1, 0), new Vector2(1, 0), new Vector2(-470, 24), new Vector2(186, 24),
                TextAnchor.MiddleCenter, FontStyle.Normal);
            view.buyKeycap = view.buyButton.targetGraphic as Image;
            view.buyKeycapLabel = Label(root.transform, "BuyKeycapHint", string.Empty, 16, Hex("6B5B4E"),
                new Vector2(1, 0), new Vector2(1, 0), new Vector2(-238, 24), new Vector2(186, 24),
                TextAnchor.MiddleCenter, FontStyle.Normal);
            // 选色块行：详情描述下方（色块运行时实例化，容器只做定位）
            view.swatchRoot = Rect(root.transform, "SwatchRow", new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-278, -646), new Vector2(400, 44));
            // COST 面板提到预览图之上（它建得早，否则会被右页那张大预览压住）
            costPanel.transform.SetAsLastSibling();

            // 获得弹窗（NEW ITEM OBTAINED）：默认隐藏，绑定层开合
            var popupScrim = Image(root.transform, "ObtainedPopup", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Color(0, 0, 0, .62f));
            view.obtainedGroup = popupScrim.gameObject.AddComponent<CanvasGroup>();
            view.obtainedGroup.alpha = 0f;
            view.obtainedGroup.blocksRaycasts = false;
            view.obtainedGroup.interactable = false;
            var popupPanel = Image(popupScrim.transform, "Panel", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                new Vector2(0, 20), new Vector2(940, 320), Color.white);
            popupPanel.sprite = popupSprite;
            view.obtainedThumb = Raw(popupPanel.transform, "Thumb", new Vector2(0, .5f), new Vector2(0, .5f),
                new Vector2(170, 10), new Vector2(180, 180));
            var obtainedFitter = view.obtainedThumb.gameObject.AddComponent<AspectRatioFitter>();
            obtainedFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            Label(popupPanel.transform, "Header", "NEW ITEM OBTAINED", 30, Hex("E22D76"),
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(620, -70), new Vector2(560, 44),
                TextAnchor.MiddleLeft, FontStyle.BoldAndItalic);
            view.obtainedName = Label(popupPanel.transform, "Name", string.Empty, 22, Hex("E22D76"),
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(620, -118), new Vector2(560, 34),
                TextAnchor.MiddleLeft, FontStyle.Bold);
            view.obtainedDesc = Label(popupPanel.transform, "Desc", string.Empty, 16, new Color(1, 1, 1, .8f),
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(620, -178), new Vector2(560, 80),
                TextAnchor.UpperLeft, FontStyle.Normal);
            view.obtainedClose = PageButton(popupPanel.transform, "CloseObtained", "收下了", new Vector2(0, 44),
                new Vector2(180, 52), Hex("6E243E"), Hex("F3E8DD"), 19, TextAnchor.MiddleCenter, new Vector2(.5f, 0));
            Save(root, path);
        }

        private static void BuildPlaceholderPanelContent(string path)
        {
            var root = ComponentRoot("PlaceholderPanel", new Vector2(1180, 830));
            Label(root.transform, "Text",
                "<size=64>尚未开放</size>\n\n<size=20>这个功能会在后续版本加入，敬请期待。</size>",
                24, new Color(1, 1, 1, .8f), TextAnchor.MiddleCenter, FontStyle.Bold);
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
}
#endif

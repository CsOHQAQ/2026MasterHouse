using DG.Tweening;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>系统面板。仓库/个人/通讯录为统一占位页（§16.8 明示豁免）；设置走 SettingsOverlay 复用标题设置 Prefab。</summary>
    public enum EHousePanel
    {
        Tasks,
        Device,
        Journal,
        Archive,
        Calendar,
        Market,
        Inventory,
        Profile,
        Contacts,
    }

    /// <summary>
    /// 系统面板叠加层外壳：加载整页面板 Prefab（遮罩/右侧滑入/头部返回），内容交给对应 Binder。
    /// Prefab 缺失是报错不回退（§16.2）；ESC/遮罩/返回都走壳的叠加层栈（先弹栈再问页面）。
    /// </summary>
    public sealed class PanelHost : IHouseOverlay
    {
        private readonly HouseUIManager ui;
        private readonly RectTransform root;
        private readonly OutGamePanelPageView view;
        private bool closing;

        private PanelHost(HouseUIManager ui, RectTransform root, OutGamePanelPageView view)
        {
            this.ui = ui;
            this.root = root;
            this.view = view;
        }

        public static void Open(HouseUIManager ui, HubPage page, EHousePanel panel)
        {
            var path = PagePrefabPath(panel);
            var prefab = Resources.Load<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogError("[HouseUI] 面板 Prefab 缺失，无法打开（§16.2 不回退代码布局）：" + path);
                return;
            }
            var instance = Object.Instantiate(prefab, ui.PageRoot, false);
            instance.name = "PanelLayer_" + panel;
            var pageView = instance.GetComponent<OutGamePanelPageView>();
            if (pageView == null)
            {
                Debug.LogError("[HouseUI] 面板 Prefab 缺少外壳组件 OutGamePanelPageView：" + path);
                Object.Destroy(instance);
                return;
            }
            var rect = (RectTransform)instance.transform;
            rect.SetAsLastSibling();
            var host = new PanelHost(ui, rect, pageView);
            host.BindShell(panel);
            switch (panel)
            {
                case EHousePanel.Tasks:
                    TasksPanelBinder.Bind(instance.GetComponentInChildren<OutGameTasksPanelView>(true), page);
                    break;
                case EHousePanel.Device:
                    DevicePanelBinder.Bind(instance.GetComponentInChildren<OutGameDevicePanelView>(true), page);
                    break;
                case EHousePanel.Journal:
                    JournalPanelBinder.Bind(instance.GetComponentInChildren<OutGameJournalPanelView>(true), page);
                    break;
                case EHousePanel.Archive:
                    ArchivePanelBinder.Bind(instance.GetComponentInChildren<OutGameArchivePanelView>(true), page);
                    break;
                case EHousePanel.Calendar:
                    CalendarPanelBinder.Bind(instance.GetComponentInChildren<OutGameCalendarPanelView>(true), page);
                    break;
                case EHousePanel.Market:
                    MarketPanelBinder.Bind(instance.GetComponentInChildren<MarketPanelView>(true), page);
                    break;
                // Inventory/Profile/Contacts：统一占位页，内容烘焙在 Prefab 内，仅外壳头部按 PanelMeta 区分
            }
            HouseUIUtil.ApplyFallbackFont(instance.transform);
            ui.PushOverlay(host);
        }

        /// <summary>弹栈回调：滑出 + 淡出后销毁。</summary>
        public void Close()
        {
            if (closing || root == null) return;
            closing = true;
            var group = HouseUIUtil.Group(root.gameObject);
            group.blocksRaycasts = false;
            group.DOFade(0, .25f).SetUpdate(true);
            if (view.panel != null)
            {
                var panelRect = view.panel.rectTransform;
                panelRect.DOAnchorPosX(panelRect.anchoredPosition.x + panelRect.rect.width + 120, .3f)
                    .SetEase(Ease.InCubic).SetUpdate(true)
                    .OnComplete(DestroyRoot);
            }
            else DestroyRoot();
        }

        private void DestroyRoot()
        {
            if (root == null) return;
            HouseUIUtil.KillTweensUnder(root);
            Object.Destroy(root.gameObject);
        }

        private void BindShell(EHousePanel panel)
        {
            var meta = PanelMeta(panel);
            if (view.headerTitle != null) view.headerTitle.text = $"<size=14>{meta.eyebrow}</size>\n{meta.title}";
            if (view.headerMark != null) view.headerMark.text = meta.mark;
            if (view.backButton != null) HouseUIUtil.BindButton(view.backButton, ui.PopOverlay);
            if (view.scrimButton != null)
            {
                view.scrimButton.onClick.RemoveAllListeners();
                view.scrimButton.onClick.AddListener(ui.PopOverlay);
            }
            if (view.scrim != null)
            {
                view.scrim.color = new Color(.005f, .008f, .02f, 0);
                view.scrim.DOFade(.62f, .25f).SetUpdate(true);
            }
            if (view.panel != null)
            {
                // 以 Prefab 作者摆放的位置为静止点，按面板实际宽度计算滑入距离——改 Prefab 尺寸后动画自动适配
                var panelRect = view.panel.rectTransform;
                var restingPosition = panelRect.anchoredPosition;
                panelRect.anchoredPosition = new Vector2(restingPosition.x + panelRect.rect.width + 80, restingPosition.y);
                panelRect.DOAnchorPosX(restingPosition.x, .42f).SetEase(Ease.OutCubic).SetUpdate(true);
            }
        }

        private static string PagePrefabPath(EHousePanel panel) => panel switch
        {
            EHousePanel.Tasks => OutGamePrefabResourcePaths.TasksPage,
            EHousePanel.Device => OutGamePrefabResourcePaths.DevicePage,
            EHousePanel.Journal => OutGamePrefabResourcePaths.JournalPage,
            EHousePanel.Archive => OutGamePrefabResourcePaths.ArchivePage,
            EHousePanel.Calendar => OutGamePrefabResourcePaths.CalendarPage,
            EHousePanel.Market => OutGamePrefabResourcePaths.MarketPage,
            _ => OutGamePrefabResourcePaths.PlaceholderPage,
        };

        private static (string eyebrow, string title, string mark) PanelMeta(EHousePanel panel) => panel switch
        {
            EHousePanel.Tasks => ("TODAY / 03", "今日委托", "任"),
            EHousePanel.Device => ("HOUSE INDEX", "设备图鉴", "器"),
            EHousePanel.Journal => ("MEMORY LOG", "日记与成就", "记"),
            EHousePanel.Archive => ("HOUSE ARCHIVE", "叙事资源档案", "集"),
            EHousePanel.Calendar => ("REAL TIME", "日程与时间", "历"),
            EHousePanel.Market => ("NIGHT MARKET", "经济与商城", "店"),
            EHousePanel.Inventory => ("STORAGE", "House 仓库", "仓"),
            EHousePanel.Profile => ("RESIDENT 001", "主角信息", "我"),
            _ => ("VISITOR FILE", "访客通讯录", "录"),
        };
    }
}

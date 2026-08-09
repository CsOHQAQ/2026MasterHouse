using DG.Tweening;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// Hub 内设置叠加层：复用标题设置 Prefab（§16.8），不另做面板布局。
    /// 返回/ESC 弹栈回 Hub；内容绑定与标题设置页共用 TitleSettingsPage.BindContent。
    /// </summary>
    public sealed class SettingsOverlay : IHouseOverlay
    {
        private readonly RectTransform root;
        private bool closing;

        private SettingsOverlay(RectTransform root)
        {
            this.root = root;
        }

        public static void Open(HouseUIManager ui)
        {
            var prefab = Resources.Load<GameObject>(OutGamePrefabResourcePaths.SettingsPage);
            if (prefab == null)
            {
                Debug.LogError("[HouseUI] 设置页 Prefab 缺失，无法打开（§16.2 不回退代码布局）：" + OutGamePrefabResourcePaths.SettingsPage);
                return;
            }
            var instance = Object.Instantiate(prefab, ui.PageRoot, false);
            instance.name = "SettingsOverlay";
            var view = instance.GetComponent<OutGameSettingsPageView>();
            if (view == null)
            {
                Debug.LogError("[HouseUI] 设置页 Prefab 缺少视图组件：OutGameSettingsPageView");
                Object.Destroy(instance);
                return;
            }
            var rect = (RectTransform)instance.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.SetAsLastSibling();

            var overlay = new SettingsOverlay(rect);
            view.backButton.onClick.RemoveAllListeners();
            view.backButton.onClick.AddListener(ui.PopOverlay);
            TitleSettingsPage.BindContent(view, ui);
            HouseUIUtil.ApplyFallbackFont(rect);

            var target = view.frame.anchoredPosition;
            var group = HouseUIUtil.Group(view.frame.gameObject, 0);
            view.frame.anchoredPosition = target + new Vector2(0, -30);
            group.DOFade(1, .28f).SetEase(Ease.OutQuad).SetUpdate(true);
            view.frame.DOAnchorPos(target, .42f).SetEase(Ease.OutCubic).SetUpdate(true);

            ui.PushOverlay(overlay);
        }

        public void Close()
        {
            if (closing || root == null) return;
            closing = true;
            var group = HouseUIUtil.Group(root.gameObject);
            group.blocksRaycasts = false;
            group.DOFade(0, .2f).SetUpdate(true).OnComplete(() =>
            {
                if (root == null) return;
                HouseUIUtil.KillTweensUnder(root);
                Object.Destroy(root.gameObject);
            });
        }
    }
}

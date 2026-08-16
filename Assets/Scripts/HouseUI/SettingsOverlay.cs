using DG.Tweening;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// Hub 内设置叠加层：复用标题设置 Prefab（§16.8），不另做面板布局。
    /// 返回/ESC 弹栈回 Hub（丢弃未应用改动）；内容绑定与标题设置页共用 SettingsPageBinder；
    /// R/回车键位由 SettingsHotkeys 组件转发（叠加层打开时页面输入被壳拦下）。
    /// </summary>
    public sealed class SettingsOverlay : IHouseOverlay
    {
        private readonly RectTransform root;
        private readonly SettingsPageBinder binder;
        private bool closing;

        private SettingsOverlay(RectTransform root, SettingsPageBinder binder)
        {
            this.root = root;
            this.binder = binder;
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

            var binder = new SettingsPageBinder();
            binder.Bind(view, ui, ui.PopOverlay);
            instance.AddComponent<SettingsHotkeys>().Init(binder);

            var overlay = new SettingsOverlay(rect, binder);
            // 确认弹窗压顶时挂起本层 R/空格热键，避免连开多个弹窗
            binder.HotkeyGate = () => ui.IsTopOverlay(overlay);
            var group = HouseUIUtil.Group(rect.gameObject, 0);
            group.DOFade(1, .28f).SetEase(Ease.OutQuad).SetUpdate(true).SetLink(instance);
            ui.PushOverlay(overlay);
        }

        public void Close()
        {
            if (closing || root == null) return;
            closing = true;
            binder.DiscardUnsaved(); // ESC/返回 = 放弃未应用的改动
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

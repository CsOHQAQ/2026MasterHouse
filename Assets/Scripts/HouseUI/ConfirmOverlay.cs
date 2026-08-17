using System;
using DG.Tweening;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 通用确认弹窗：标题 + 说明 + 确认/取消两键。取消、ESC、点确认前关窗都只收面板不触发回调；
    /// 只有点确认才执行 onConfirm。Prefab 缺失是报错（§16.2 不回退代码布局）。
    /// 首个用例：结束今天（2026-08-14）。
    /// </summary>
    public sealed class ConfirmOverlay : IHouseOverlay
    {
        private readonly RectTransform root;
        private bool closing;

        private ConfirmOverlay(RectTransform root)
        {
            this.root = root;
        }

        public static void Open(HouseUIManager ui, string title, string body, string confirmText, Action onConfirm)
        {
            var prefab = Resources.Load<GameObject>(OutGamePrefabResourcePaths.ConfirmPopup);
            if (prefab == null)
            {
                Debug.LogError("[HouseUI] 确认弹窗 Prefab 缺失，无法打开（§16.2 不回退代码布局）：" + OutGamePrefabResourcePaths.ConfirmPopup);
                return;
            }
            var instance = UnityEngine.Object.Instantiate(prefab, ui.PageRoot, false);
            instance.name = "ConfirmLayer";
            var view = instance.GetComponent<OutGameConfirmPopupView>();
            if (view == null)
            {
                Debug.LogError("[HouseUI] 确认弹窗 Prefab 缺少视图组件：OutGameConfirmPopupView");
                UnityEngine.Object.Destroy(instance);
                return;
            }
            var rect = (RectTransform)instance.transform;
            rect.SetAsLastSibling();
            var overlay = new ConfirmOverlay(rect);

            if (view.panel != null) HouseUIUtil.ApplyPanelSkin(view.panel.GetComponent<UnityEngine.UI.Image>());
            if (view.title != null) view.title.text = title;
            if (view.body != null) view.body.text = body;
            if (view.confirmLabel != null) view.confirmLabel.text = confirmText;
            // 先出栈（走 Close 收面板），再执行确认回调——回调里可以安全地推新的叠加层
            if (view.confirmButton != null) HouseUIUtil.BindButton(view.confirmButton, () =>
            {
                ui.PopOverlay();
                onConfirm?.Invoke();
            });
            if (view.cancelButton != null) HouseUIUtil.BindButton(view.cancelButton, ui.PopOverlay, ESfx.None);
            HouseUIUtil.ApplyFallbackFont(instance.transform);

            // 键位（2026-08-17）：空格确认；ESC 取消走壳的叠加层弹栈
            instance.AddComponent<ConfirmHotkeys>().Init(view.confirmButton, () => ui.IsTopOverlay(overlay));

            var group = HouseUIUtil.Group(rect.gameObject, 0);
            group.DOFade(1, .22f).SetUpdate(true).SetLink(instance);
            if (view.panel != null)
            {
                var resting = view.panel.anchoredPosition;
                view.panel.anchoredPosition = resting + new Vector2(0, -28);
                view.panel.DOAnchorPos(resting, .3f).SetEase(Ease.OutCubic).SetUpdate(true).SetLink(instance);
            }
            ui.PushOverlay(overlay);
        }

        public void Close()
        {
            if (closing || root == null) return;
            closing = true;
            var group = HouseUIUtil.Group(root.gameObject);
            group.blocksRaycasts = false;
            group.DOFade(0, .18f).SetUpdate(true).OnComplete(() =>
            {
                if (root == null) return;
                HouseUIUtil.KillTweensUnder(root);
                UnityEngine.Object.Destroy(root.gameObject);
            });
        }
    }
}

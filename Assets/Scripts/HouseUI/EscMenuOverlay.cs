using DG.Tweening;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// ESC 系统菜单（2026-08-19 按 2.0 设计图新建）：Hub 里按 ESC 弹出的那一页。
    ///
    /// 在这之前 ESC 是**直接弹「退出到主菜单」确认框**的，等于把一个高危动作绑在最常按的键上。
    /// 现在 ESC 只负责开这张菜单，具体去哪由玩家在菜单里选；再按一次 ESC（走壳的叠加层弹栈）
    /// 就是「继续游戏」。
    ///
    /// 存储/加载两项挂着但不可用——局外存档功能已移除、只留接缝（CLAUDE.md 待定 #9），
    /// 这里按置灰 + 提示处理，等存档接回来时把回调换掉即可。
    /// </summary>
    public sealed class EscMenuOverlay : IHouseOverlay
    {
        private readonly RectTransform root;
        private readonly HouseUIManager ui;
        private bool closing;

        private EscMenuOverlay(RectTransform root, HouseUIManager ui)
        {
            this.root = root;
            this.ui = ui;
        }

        public static void Open(HouseUIManager ui, HubPage page)
        {
            var prefab = Resources.Load<GameObject>(OutGamePrefabResourcePaths.EscMenu);
            if (prefab == null)
            {
                Debug.LogError("[HouseUI] ESC 菜单 Prefab 缺失（§16.2 不回退代码布局）：" +
                               OutGamePrefabResourcePaths.EscMenu);
                return;
            }
            var instance = Object.Instantiate(prefab, ui.PageRoot, false);
            instance.name = "EscMenuLayer";
            var view = instance.GetComponent<OutGameEscMenuView>();
            if (view == null)
            {
                Debug.LogError("[HouseUI] ESC 菜单 Prefab 缺少视图组件：OutGameEscMenuView");
                Object.Destroy(instance);
                return;
            }
            var rect = (RectTransform)instance.transform;
            rect.SetAsLastSibling();
            var overlay = new EscMenuOverlay(rect, ui);
            overlay.Bind(view, page);
            HouseUIUtil.ApplyFallbackFont(instance.transform);
            var group = HouseUIUtil.Group(rect.gameObject, 0);
            group.DOFade(1, .18f).SetUpdate(true);
            ui.PushOverlay(overlay);
        }

        public void Close()
        {
            if (closing || root == null) return;
            closing = true;
            var group = HouseUIUtil.Group(root.gameObject);
            group.blocksRaycasts = false;
            group.DOFade(0, .15f).SetUpdate(true).OnComplete(() =>
            {
                if (root == null) return;
                HouseUIUtil.KillTweensUnder(root);
                Object.Destroy(root.gameObject);
            });
        }

        private void Bind(OutGameEscMenuView view, HubPage page)
        {
            // 条目顺序与 Prefab 里的排布一致：继续 / 存储 / 加载 / 选项 / 返回主菜单 / 退出
            BindItem(view, 0, "继续游戏", ui.PopOverlay, ESfx.None);
            BindItem(view, 1, "存储游戏", () => ui.ShowToast("存档功能开发中"), ESfx.None, enabled: false);
            BindItem(view, 2, "加载游戏", () => ui.ShowToast("存档功能开发中"), ESfx.None, enabled: false);
            BindItem(view, 3, "选项", () => page.OpenSettings());
            BindItem(view, 4, "返回主菜单", () =>
            {
                ui.PopOverlay();      // 先收菜单，确认框才是最顶层
                page.BackToTitle();   // 自带「进度会丢」的确认
            });
            BindItem(view, 5, "退出", RequestQuit);
            if (view.scrim != null) HouseUIUtil.BindButton(EnsureScrimButton(view), ui.PopOverlay, ESfx.None);
        }

        private void BindItem(OutGameEscMenuView view, int index, string label,
            System.Action action, ESfx sfx = ESfx.UiClick, bool enabled = true)
        {
            if (view.buttonLabels != null && index < view.buttonLabels.Length && view.buttonLabels[index] != null)
            {
                view.buttonLabels[index].text = label;
                // 不可用的条目压暗文字，视觉上先说明白，点了再给提示
                if (!enabled) view.buttonLabels[index].color = new Color32(0x9E, 0xA9, 0xB8, 0xFF);
            }
            if (view.buttons == null || index >= view.buttons.Length || view.buttons[index] == null) return;
            HouseUIUtil.BindButton(view.buttons[index], () => action(), sfx);
        }

        /// <summary>点遮罩 = 继续游戏（与 ESC 同义）。遮罩本身是 Image，运行时补个 Button 承接点击。</summary>
        private static UnityEngine.UI.Button EnsureScrimButton(OutGameEscMenuView view)
        {
            var button = view.scrim.GetComponent<UnityEngine.UI.Button>();
            if (button == null) button = view.scrim.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.transition = UnityEngine.UI.Selectable.Transition.None;
            button.targetGraphic = view.scrim;
            return button;
        }

        /// <summary>退出游戏：与标题页的退出页同口径，先确认再退（进度会丢）。</summary>
        private void RequestQuit() =>
            ConfirmOverlay.Open(ui, "退出游戏",
                "暂时没有存档功能，退出后本局进度将丢失。\n确定退出吗？", "确定退出", Quit);

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}

using DG.Tweening;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 当日结算面板（访客交付说明 §7）：【结束今天】确认后展示当日逐次结算的累计——
    /// **只展示不惩罚**，惩罚已在超时/拒绝当时结清，这里不重复扣。
    /// 打开时时间已跳到次日开门时刻；关闭仅收面板。Prefab 缺失是报错（§16.2）。
    /// </summary>
    public sealed class DaySettleOverlay : IHouseOverlay
    {
        private readonly RectTransform root;
        private bool closing;

        private DaySettleOverlay(RectTransform root)
        {
            this.root = root;
        }

        public static void Open(HouseUIManager ui, int endedDay, VisitorDaySummary summary)
        {
            var prefab = Resources.Load<GameObject>(OutGamePrefabResourcePaths.DaySettlePanel);
            if (prefab == null)
            {
                Debug.LogError("[HouseUI] 日结面板 Prefab 缺失，无法打开（§16.2 不回退代码布局）：" + OutGamePrefabResourcePaths.DaySettlePanel);
                return;
            }
            var instance = Object.Instantiate(prefab, ui.PageRoot, false);
            instance.name = "DaySettleLayer";
            var view = instance.GetComponent<OutGameDaySettleView>();
            if (view == null)
            {
                Debug.LogError("[HouseUI] 日结面板 Prefab 缺少视图组件：OutGameDaySettleView");
                Object.Destroy(instance);
                return;
            }
            var rect = (RectTransform)instance.transform;
            rect.SetAsLastSibling();
            var overlay = new DaySettleOverlay(rect);

            if (view.title != null) view.title.text = $"DAY {endedDay:00} 结算";
            if (view.body != null) view.body.text = BuildBody(summary);
            if (view.confirmLabel != null) view.confirmLabel.text = "开始新的一天 →";
            if (view.confirmButton != null) HouseUIUtil.BindButton(view.confirmButton, ui.PopOverlay);
            HouseUIUtil.ApplyFallbackFont(instance.transform);

            var group = HouseUIUtil.Group(rect.gameObject, 0);
            group.DOFade(1, .28f).SetUpdate(true);
            if (view.panel != null)
            {
                var resting = view.panel.anchoredPosition;
                view.panel.anchoredPosition = resting + new Vector2(0, -40);
                view.panel.DOAnchorPos(resting, .4f).SetEase(Ease.OutCubic).SetUpdate(true);
            }
            ui.PushOverlay(overlay);
        }

        private static string BuildBody(VisitorDaySummary summary)
        {
            return
                $"完成服务　{summary.ServedTotal} 位" +
                $"（完美 {summary.ServedBySatisfaction[(int)EServeSatisfaction.Perfect]}" +
                $" · 满意 {summary.ServedBySatisfaction[(int)EServeSatisfaction.Satisfied]}" +
                $" · 一般 {summary.ServedBySatisfaction[(int)EServeSatisfaction.Plain]}" +
                $" · 不对味 {summary.ServedBySatisfaction[(int)EServeSatisfaction.Mismatch]}）\n" +
                $"拒绝 / 超时　{summary.RefusedCount} 位\n" +
                $"闲逛后离场　{summary.WanderDepartCount} 位 · 跨天留宿 {summary.StayOvernightCount} 位\n\n" +
                $"<color=#D4A46B>货币 +{summary.CurrencyEarned:N0}</color>　" +
                $"<color=#74D8D1>声望 +{summary.ReputationEarned}</color>　" +
                $"<color=#E22D76>声望 -{summary.ReputationLost}</color>\n" +
                "<size=14>以上均为当日逐次结算的累计，日结不重复扣减。</size>";
        }

        public void Close()
        {
            if (closing || root == null) return;
            closing = true;
            var group = HouseUIUtil.Group(root.gameObject);
            group.blocksRaycasts = false;
            group.DOFade(0, .22f).SetUpdate(true).OnComplete(() =>
            {
                if (root == null) return;
                HouseUIUtil.KillTweensUnder(root);
                Object.Destroy(root.gameObject);
            });
        }
    }
}

using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// Hub 访客列表绑定：语义为「当前在场访客」（访客交付说明 §10，周制退役）。
    /// Prefab 内既有 4 张卡位，按在场实例（InstanceId 升序）填充，超出卡位的实例暂不上墙、空卡隐藏；
    /// 实例进离场/状态变化时由 HubPage 订阅业务事件整体刷新。
    /// </summary>
    public sealed class HubGuestRailBinder
    {
        private OutGameHubGuestRailView view;
        private HubPage page;

        public void Bind(OutGameHubGuestRailView rail, HubPage owner)
        {
            view = rail;
            page = owner;
            Refresh();
        }

        /// <summary>在场实例变化后整体重绑（数量少，全量刷新）。</summary>
        public void Refresh()
        {
            if (view == null) return;
            var visitor = GameManager.Instance.VisitorManager;
            var instances = visitor.Data.Instances;
            view.title.text = "VISITOR EVENTS / 当前在场访客";
            view.remaining.text = visitor.CountOnStage.ToString("00");
            for (var i = 0; i < view.cards.Length; i++)
            {
                var card = view.cards[i];
                if (card == null) continue;
                if (i >= instances.Count)
                {
                    card.gameObject.SetActive(false);
                    continue;
                }
                card.gameObject.SetActive(true);
                var instance = instances[i];
                var instanceId = instance.InstanceId;
                card.portrait.texture = Resources.Load<Texture2D>(instance.Race.GetPortraitPath());
                card.eventLabel.text = $"VISITOR {instance.InstanceId:00}";
                card.guestName.text = instance.DisplayName;
                card.status.text = StatusText(instance.State);
                card.typeLabel.text = TypeMark(instance.State);
                card.background.color = new Color(.025f, .025f, .045f, .83f);
                card.eventLabel.color = card.guestName.color = card.status.color = HouseUIUtil.White;
                HouseUIUtil.BindButton(card.button, () => page.SelectGuest(instanceId));
            }
        }

        private static string StatusText(EVisitorState state) => state switch
        {
            EVisitorState.FrontDesk => "前台等待接待 · 点击交谈",
            EVisitorState.Serving => "服务中 · 等待递上物品",
            EVisitorState.Wandering => "心满意足 · 屋内闲逛",
            _ => "正在离开",
        };

        private static string TypeMark(EVisitorState state) => state switch
        {
            EVisitorState.FrontDesk => "待",
            EVisitorState.Serving => "服",
            EVisitorState.Wandering => "逛",
            _ => "…",
        };
    }
}

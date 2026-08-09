using UnityEngine;

namespace MasterHouse
{
    /// <summary>Hub 访客事件列表绑定：四张访客卡（Prefab 内既有实例），点击选中访客。</summary>
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

        /// <summary>服务状态变化后整体重绑（数量少，全量刷新）。</summary>
        public void Refresh()
        {
            if (view == null) return;
            var visitors = GameManager.Instance.VisitorTable.visitors;
            var states = GameManager.Instance.VisitorManager.Data.States;
            view.title.text = "VISITOR EVENTS / 访客事件";
            view.remaining.text = GameManager.Instance.VisitorManager.CountRemaining().ToString("00");
            for (var i = 0; i < view.cards.Length && i < visitors.Count; i++)
            {
                var index = i;
                var guest = visitors[i];
                var done = states[i].Served;
                var card = view.cards[i];
                card.portrait.texture = Resources.Load<Texture2D>(guest.portraitPath);
                card.eventLabel.text = guest.special ? "SPECIAL EVENT" : "EVENT 0" + (i + 1);
                card.guestName.text = guest.displayName;
                card.status.text = done ? "事件已完成" : guest.special ? "特殊客人 · 可打断" : "一般客人 · 可接待";
                card.typeLabel.text = done ? "✓" : guest.special ? "特" : "普";
                card.background.color = done ? new Color(.03f, .03f, .045f, .55f) : new Color(.025f, .025f, .045f, .83f);
                var textColor = done ? new Color(1, 1, 1, .45f) : HouseUIUtil.White;
                card.eventLabel.color = card.guestName.color = card.status.color = textColor;
                HouseUIUtil.BindButton(card.button, () => page.SelectGuest(index));
            }
        }
    }
}

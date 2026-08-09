namespace MasterHouse
{
    /// <summary>Hub 当前访客任务卡绑定：展示选中访客的需求与假进度，点击进委托面板（3.5c）。</summary>
    public sealed class HubTaskCardBinder
    {
        private OutGameHubTaskCardView view;
        private HubPage page;

        public void Bind(OutGameHubTaskCardView card, HubPage owner)
        {
            view = card;
            page = owner;
            HouseUIUtil.BindButton(card.button, () => page.OpenPanelPlaceholder("委托"));
            Refresh();
        }

        /// <summary>选中访客或服务状态变化后刷新。</summary>
        public void Refresh()
        {
            if (view == null) return;
            var guest = GameManager.Instance.VisitorTable.visitors[page.GuestIndex];
            view.header.text = "CURRENT VISITOR TASK                         进行中";
            view.guestTitle.text = guest.displayName + " · " + guest.need;
            view.hint.text = guest.hint;
            view.progress.text = $"━━━━━━  {ProgressForGuest(page.GuestIndex)}%     点击查看任务详情  →";
        }

        /// <summary>原型假进度：已处理 100%，首位访客 35%，其余 20%（与旧壳一致）。</summary>
        private static int ProgressForGuest(int index) =>
            GameManager.Instance.VisitorManager.Data.States[index].Served ? 100 : index == 0 ? 35 : 20;
    }
}

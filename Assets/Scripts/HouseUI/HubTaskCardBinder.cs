namespace MasterHouse
{
    /// <summary>
    /// Hub 当前访客任务卡绑定：展示选中访客的状态与需求（服务中才亮出需求句，§5——需求是接待之后才提的）。
    /// 原「信赖度」显示位改显示满意度语义（访客交付说明 §10 待确认表：affinity 本期移除）。
    /// </summary>
    public sealed class HubTaskCardBinder
    {
        private OutGameHubTaskCardView view;
        private HubPage page;

        public void Bind(OutGameHubTaskCardView card, HubPage owner)
        {
            view = card;
            page = owner;
            HouseUIUtil.BindButton(card.button, () => page.OpenPanel(EHousePanel.Tasks));
            Refresh();
        }

        /// <summary>选中访客或状态变化后刷新。</summary>
        public void Refresh()
        {
            if (view == null) return;
            var instance = page.SelectedInstance;
            view.header.text = "CURRENT VISITOR                              当前访客";
            if (instance == null)
            {
                view.guestTitle.text = "暂无访客在场";
                view.hint.text = "访客会按日程表在营业时段到访。";
                view.progress.text = "━━━━━━  等待下一位访客     点击查看详情  →";
                return;
            }
            view.guestTitle.text = instance.DisplayName + " · " + StatusText(instance);
            view.hint.text = instance.State == EVisitorState.FrontDesk
                ? "接待后才会说出需求（点击场景中的访客交谈）。"
                : instance.BuildNeedSentence();
            view.progress.text = $"━━━━━━  {StageText(instance)}     点击查看任务详情  →";
        }

        private static string StatusText(VisitorInstance instance) => instance.State switch
        {
            EVisitorState.FrontDesk => "前台等待接待",
            EVisitorState.Serving => "服务中",
            EVisitorState.Wandering => $"闲逛中（{ServeSatisfactionText.NameOf(instance.Satisfaction)}）",
            _ => "正在离开",
        };

        private static string StageText(VisitorInstance instance) => instance.State switch
        {
            EVisitorState.FrontDesk => "等待接待",
            EVisitorState.Serving => "等待提交物品",
            EVisitorState.Wandering => "服务完成 · " + ServeSatisfactionText.NameOf(instance.Satisfaction),
            _ => "已离场",
        };
    }
}

namespace MasterHouse
{
    /// <summary>委托面板绑定：焦点委托展示当前选中访客的状态与需求；三条假任务是 P2 委托系统的占位内容（3.3 决策不建 Def）。</summary>
    public static class TasksPanelBinder
    {
        public static void Bind(OutGameTasksPanelView view, HubPage page)
        {
            if (view == null) return;
            var instance = page.SelectedInstance;
            if (view.focusText != null)
            {
                if (instance == null)
                {
                    view.focusText.text = "<color=#E22D76>●  MAIN / 访客接待</color>\n<size=28>暂无访客在场</size>\n<size=17>访客会按日程表在营业时段到访，接待后会说出需求。</size>";
                }
                else
                {
                    var needLine = instance.State == EVisitorState.FrontDesk
                        ? "接待后才会说出需求。"
                        : instance.BuildNeedSentence();
                    view.focusText.text = $"<color=#E22D76>●  MAIN / 访客接待</color>\n<size=28>{instance.DisplayName}</size>\n<size=17>{needLine}</size>";
                }
            }
            var tasks = new[] { "为赫墨制造琴弦窗户", "把米娅的纸条挂上风铃", "检查明日访客预告" };
            for (var i = 0; i < tasks.Length; i++)
            {
                var task = tasks[i];
                if (view.taskLabels != null && i < view.taskLabels.Length && view.taskLabels[i] != null)
                    view.taskLabels[i].text = $"0{i + 2}     {task}                         {(i == 2 ? "未解锁" : "进行中")}";
                if (view.taskButtons != null && i < view.taskButtons.Length && view.taskButtons[i] != null)
                    HouseUIUtil.BindButton(view.taskButtons[i], () => page.Toast("已追踪：" + task));
            }
        }
    }
}

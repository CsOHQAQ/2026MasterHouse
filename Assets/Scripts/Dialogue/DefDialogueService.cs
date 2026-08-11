namespace MasterHouse
{
    /// <summary>
    /// 对话接缝默认实现（§16.9）：对话系统未落地期间返回占位单句。
    /// 台词按触发点固定造句（含 §8 要求的程序化需求句）；闲逛台词从小池子按确定性键选取（逻辑层禁无种子随机，§11.1）。
    /// 对话系统交付后整类替换（种族对话池 VisitorRaceDef.dialoguePool 届时接入）。
    /// </summary>
    public sealed class DefDialogueService : IDialogueService
    {
        private static readonly string[] WanderLines =
        {
            "这间屋子住起来一定很舒服吧。",
            "刚才的招待真不错，多谢啦。",
            "我再逛一小会儿就回去。",
            "窗外的光线真好啊。",
            "下次我还会再来的。",
        };

        public string RequestVisitorLine(VisitorInstance visitor, EVisitorDialogueTrigger trigger, EServeSatisfaction satisfaction)
        {
            if (visitor == null) return string.Empty;
            switch (trigger)
            {
                case EVisitorDialogueTrigger.FirstMeeting:
                    return $"你好，我是{visitor.DisplayName}。今天可以接待我吗？";
                case EVisitorDialogueTrigger.ServiceStart:
                    return visitor.BuildNeedSentence(); // 程序化需求句（§8）
                case EVisitorDialogueTrigger.Rejected:
                    return "……这样啊。那我先走了。";
                case EVisitorDialogueTrigger.ServiceDone:
                    switch (satisfaction)
                    {
                        case EServeSatisfaction.Mismatch: return "这不是我想要的……我还是走吧。";
                        case EServeSatisfaction.Plain: return "唔，勉强可以吧。";
                        case EServeSatisfaction.Satisfied: return "不错不错，我挺喜欢的。";
                        default: return "太完美了！就是它！";
                    }
                case EVisitorDialogueTrigger.WanderChat:
                    // 确定性选句：以实例 id 与冒泡排程 tick 作键，同一存档重放结果一致
                    var key = (visitor.InstanceId * 31L + visitor.NextBubbleTick) % WanderLines.Length;
                    if (key < 0) key += WanderLines.Length;
                    return WanderLines[(int)key];
                default:
                    return string.Empty;
            }
        }
    }
}

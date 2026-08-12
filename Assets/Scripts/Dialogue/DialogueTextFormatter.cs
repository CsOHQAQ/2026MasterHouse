namespace MasterHouse
{
    /// <summary>
    /// 台词占位符替换（设计说明 §9）。播放时对 DialogueLine.text 与选项文本各跑一遍。
    ///
    /// 只做字符串替换、不做表达式求值——占位符是给策划的便利，不是脚本语言。
    /// 需要条件文案就用分支（§4.3），不要往这里加语法。
    /// </summary>
    public static class DialogueTextFormatter
    {
        public const string TokenNeed = "{需求}";
        public const string TokenVisitorName = "{访客名}";
        public const string TokenItemName = "{物品名}";

        /// <summary>
        /// 替换占位符。text 为空或不含任何占位符时原样返回（不含时连需求短语都不组装，省掉无谓开销）。
        /// 未知占位符**原样保留**——策划一眼能在游戏里看见自己写错了，比静默吞掉好。
        /// </summary>
        public static string Format(string text, GameplayContext ctx, INeedPhraseBuilder needPhrase)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var visitor = ctx != null ? ctx.Visitor : null;

            if (text.Contains(TokenNeed))
            {
                var phrase = visitor != null && needPhrase != null ? needPhrase.Build(visitor.Needs) : string.Empty;
                if (string.IsNullOrEmpty(phrase)) phrase = "点什么";
                text = text.Replace(TokenNeed, phrase);
            }

            if (text.Contains(TokenVisitorName))
                text = text.Replace(TokenVisitorName, visitor != null ? visitor.DisplayName : "访客");

            if (text.Contains(TokenItemName))
            {
                // 预览候选优先于已提交物品：交付预览发生在 Submit 之前，那时 SubmittedItem 还是空的
                // （见 GameplayContext.PreviewItem 注释）。两者都没有时才回落「这个」。
                var item = ctx != null && ctx.PreviewItem != null
                    ? ctx.PreviewItem
                    : visitor != null ? visitor.SubmittedItem : null;
                text = text.Replace(TokenItemName, item != null ? item.DisplayName : "这个");
            }

            return text;
        }
    }
}

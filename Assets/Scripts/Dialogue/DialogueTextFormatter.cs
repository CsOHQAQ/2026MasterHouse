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

        /// <summary>
        /// 替换占位符。text 为空或不含任何占位符时原样返回。
        /// 未知占位符**原样保留**——策划一眼能在游戏里看见自己写错了，比静默吞掉好。
        /// </summary>
        public static string Format(string text, GameplayContext ctx)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var visitor = ctx != null ? ctx.Visitor : null;

            if (text.Contains(TokenNeed))
            {
                // {需求} 现在**直接取 NeedDef.description**（需求重做说明 §9.1）：需求是策划写死的一句话，
                // 不再是一组 tag，基于 tag 森林的造句器 INeedPhraseBuilder 已随之退役。
                // 需求资产漏填描述时回落一句中性的，不让台词渲染成半句话（校验器会报错指名是哪条）
                var phrase = visitor != null && visitor.Need != null ? visitor.Need.description : string.Empty;
                if (string.IsNullOrWhiteSpace(phrase)) phrase = "有点事想麻烦你";
                text = text.Replace(TokenNeed, phrase);
            }

            if (text.Contains(TokenVisitorName))
                text = text.Replace(TokenVisitorName, visitor != null ? visitor.DisplayName : "访客");

            // {物品名} 已随 Item 链退役删除（§9.1）：访客不再交付物品，没有「那件东西」可指。
            // 写了这个占位符的老台词会原样显示出来，正好提示策划去改。

            return text;
        }
    }
}

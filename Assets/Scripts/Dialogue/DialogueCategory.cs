namespace MasterHouse
{
    /// <summary>
    /// 对话池的触发分类（2026-08-14 重构定案，取代旧的 EVisitorDialogueTrigger + EServeSatisfaction 二维切分）。
    ///
    /// 八个分类一一对应访客生命周期上的八个说话时机，Excel 第一页的「所属对话池」列填的就是它们的英文 key
    /// （见 DialogueCategoryText.Keys）。
    ///
    /// **枚举值显式赋值、新增只能追加**：值参与派生种子（DialogueManager 的选取），改动会让同一 runSeed
    /// 下抽到的对话变掉。
    /// </summary>
    public enum EDialogueCategory
    {
        /// <summary>初次见面：首次点击前台队首访客。组内 Branch 给出「接待 / 拒绝」，**绝不透露需求**。</summary>
        FirstMeeting = 0,

        /// <summary>等待接待：同一位前台访客的二次点击（首次已正常播完，见 VisitorInstance.MetPlayer）。
        /// 结构与初次见面相同——**同样要带接待/拒绝分支**，否则玩家 ESC 之后再也接待不了他。</summary>
        WaitingReception = 1,

        /// <summary>
        /// 需求对话：访客入住后「已示意」（头顶提示亮起）时点击他。
        /// 说出需求 + Branch[交付 / 我这就去弄 / 抱歉我办不到]，条件类的验收与小游戏类的开局都在这一类。
        /// **需求ID 必填**——一条需求配自己的一套说辞，校验器强制。
        /// </summary>
        NeedTalk = 2,

        /// <summary>需求反馈·失望：服务超时，需求没办到（EServeSatisfaction.Mismatch）。</summary>
        FeedbackDisappointed = 3,

        /// <summary>需求反馈·一般：小游戏拿了低分（EServeSatisfaction.Plain）。条件类走不到这一档。</summary>
        FeedbackPlain = 4,

        /// <summary>需求反馈·还行：小游戏拿了中间分（EServeSatisfaction.Satisfied）。条件类走不到这一档。</summary>
        FeedbackFine = 5,

        /// <summary>需求反馈·完美：条件类交付成功 / 小游戏满分（EServeSatisfaction.Perfect）。</summary>
        FeedbackPerfect = 6,

        /// <summary>闲聊：停留期由冒泡调度器定期请求，走场景气泡、不开模态、不碰闸门。</summary>
        SmallTalk = 7,

        /// <summary>
        /// 告别：停留时长到点后转【待告别】，玩家点他才播（2026-08-20 定案）。
        ///
        /// **不由 tick 自动弹**：自动弹模态会在玩家逛商店 / 摆家具时冷不丁盖上来，而家具模式
        /// 禁着整个壳 Canvas——那是「看不见的对话框 + 关不掉的闸门」硬卡死。口径与「进屋不再自动
        /// 弹需求对话」完全一致：到点只亮头顶提示，说不说话由玩家点。
        ///
        /// **组末尾必须配一条 `Action | Leave`**，客人才会真的走。没配就一直等在场上占着房间
        /// （已定案可接受：玩家随时能再点他重播一次），校验器给警告。
        /// </summary>
        Farewell = 8,
    }

    /// <summary>
    /// 分类的英文 key 与中文名（Excel 列值 ↔ 枚举 ↔ 日志文案）。
    ///
    /// Excel 里用**英文 key**（策划从数据校验下拉里选，不手打），是为了让列值与代码标识一致、
    /// 改中文文案不会断表（第 12 题定案）。中文名只出现在报错与日志里。
    /// </summary>
    public static class DialogueCategoryText
    {
        /// <summary>下标 = (int)EDialogueCategory。</summary>
        public static readonly string[] Keys =
        {
            "firstMeeting", "waitingReception", "needTalk",
            "feedbackDisappointed", "feedbackPlain", "feedbackFine", "feedbackPerfect",
            "smallTalk", "farewell",
        };

        /// <summary>下标 = (int)EDialogueCategory。</summary>
        public static readonly string[] Names =
        {
            "初次见面", "等待接待", "需求对话",
            "需求反馈·失望", "需求反馈·一般", "需求反馈·还行", "需求反馈·完美",
            "闲聊", "告别",
        };

        public static string KeyOf(EDialogueCategory category)
        {
            var index = (int)category;
            return index >= 0 && index < Keys.Length ? Keys[index] : category.ToString();
        }

        public static string NameOf(EDialogueCategory category)
        {
            var index = (int)category;
            return index >= 0 && index < Names.Length ? Names[index] : category.ToString();
        }

        /// <summary>英文 key → 枚举；无法识别时返回 false（导入器据此报出 Excel 行号）。</summary>
        public static bool TryParse(string key, out EDialogueCategory category)
        {
            category = EDialogueCategory.FirstMeeting;
            if (string.IsNullOrWhiteSpace(key)) return false;
            var trimmed = key.Trim();
            for (var i = 0; i < Keys.Length; i++)
                if (string.Equals(Keys[i], trimmed, System.StringComparison.OrdinalIgnoreCase))
                {
                    category = (EDialogueCategory)i;
                    return true;
                }
            // 也认中文名：策划从别处复制粘贴时不至于卡住
            for (var i = 0; i < Names.Length; i++)
                if (Names[i] == trimmed)
                {
                    category = (EDialogueCategory)i;
                    return true;
                }
            return false;
        }

        /// <summary>需求ID 是否必填：只有【需求对话】这一类要求（一条需求配自己的一套说辞）。</summary>
        public static bool RequiresNeedId(EDialogueCategory category) => category == EDialogueCategory.NeedTalk;

        /// <summary>
        /// 需求ID 是否允许填写：需求对话（必填）、四档反馈与告别（选填，专属优先）。其余三类填了是配错。
        /// 告别允许专属，是因为「谢谢你修好我的电路」这种告别词本来就该按需求写。
        /// </summary>
        public static bool AllowsNeedId(EDialogueCategory category) =>
            category == EDialogueCategory.NeedTalk ||
            category == EDialogueCategory.Farewell ||
            (category >= EDialogueCategory.FeedbackDisappointed && category <= EDialogueCategory.FeedbackPerfect);

        /// <summary>满意度档位 → 对应的反馈分类。</summary>
        public static EDialogueCategory FeedbackOf(EServeSatisfaction satisfaction)
        {
            switch (satisfaction)
            {
                case EServeSatisfaction.Plain: return EDialogueCategory.FeedbackPlain;
                case EServeSatisfaction.Satisfied: return EDialogueCategory.FeedbackFine;
                case EServeSatisfaction.Perfect: return EDialogueCategory.FeedbackPerfect;
                default: return EDialogueCategory.FeedbackDisappointed;
            }
        }
    }
}

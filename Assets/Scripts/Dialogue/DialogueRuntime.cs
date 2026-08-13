namespace MasterHouse
{
    /// <summary>
    /// 单次对话播放的运行状态。由 DialogueManager 独占创建与推进；
    /// 播放结束即丢弃——没有跨播放的状态，也就没有「上一次没清干净」这类 bug。
    ///
    /// 位置由三个下标表达（2026-08-14 重构：分支子句取代了跨组跳转）：
    ///   OptionIndex &lt; 0  → 在主线上，位置是 Group.steps[StepIndex]
    ///   OptionIndex ≥ 0  → 正在播主线第 StepIndex 步那个分支的第 OptionIndex 个选项的子句，
    ///                       位置是该选项的 steps[SubIndex]；子句播完汇合回主线 StepIndex + 1
    ///
    /// 跨组跳转与 MaxGroupJumps 防环上限已删除：分类拆细之后接待/拒绝/交付全在组内用子句解决，
    /// 组间不再有边，也就不可能成环。
    /// </summary>
    public sealed class DialogueRuntime
    {
        /// <summary>触发本次播放的分类（写 recent 环、日志用）。</summary>
        public EDialogueCategory Category;

        /// <summary>recent 环的分类键。</summary>
        public string CategoryKey;

        /// <summary>正在播的对话组（从池里抽中的那个；本次播放期间不会变）。</summary>
        public DialogueGroup Group;

        /// <summary>主线步下标。</summary>
        public int StepIndex;

        /// <summary>正在播第几个选项的子句；-1 = 在主线上。</summary>
        public int OptionIndex = -1;

        /// <summary>子句内下标（OptionIndex ≥ 0 时有效）。</summary>
        public int SubIndex;

        /// <summary>事件与条件的执行上下文（播放期间只读）。</summary>
        public GameplayContext Context;

        /// <summary>当前主线步；越界返回 null。</summary>
        public DialogueStep MainStep
        {
            get
            {
                var steps = Group != null ? Group.steps : null;
                if (steps == null || StepIndex < 0 || StepIndex >= steps.Count) return null;
                return steps[StepIndex];
            }
        }

        /// <summary>当前正在播的那个选项；不在子句里时为 null。</summary>
        public DialogueOption CurrentOption
        {
            get
            {
                if (OptionIndex < 0) return null;
                var step = MainStep;
                var options = step != null ? step.options : null;
                if (options == null || OptionIndex >= options.Count) return null;
                return options[OptionIndex];
            }
        }

        /// <summary>当前子句步；不在子句里或越界时返回 null。</summary>
        public DialogueSubStep CurrentSubStep
        {
            get
            {
                var option = CurrentOption;
                if (option == null || option.steps == null) return null;
                if (SubIndex < 0 || SubIndex >= option.steps.Count) return null;
                return option.steps[SubIndex];
            }
        }

        /// <summary>当前是否停在分支上（停在分支时点击不推进，必须选一个选项）。</summary>
        public bool IsAtBranch
        {
            get
            {
                if (OptionIndex >= 0) return false; // 子句里不会再有分支（嵌套分支不支持）
                var step = MainStep;
                return step != null && step.kind == EDialogueStepKind.Branch;
            }
        }

        /// <summary>选中一个选项，位置切进它的子句。</summary>
        public void EnterOption(int index)
        {
            OptionIndex = index;
            SubIndex = 0;
        }

        /// <summary>子句播完：汇合回主线的下一步。</summary>
        public void LeaveOption()
        {
            OptionIndex = -1;
            SubIndex = 0;
            StepIndex++;
        }
    }
}

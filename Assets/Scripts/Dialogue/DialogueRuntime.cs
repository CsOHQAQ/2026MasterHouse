using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 单次对话播放的运行状态（设计说明 §3）。由 DialogueManager 独占创建与推进；
    /// 播放结束即丢弃——没有跨播放的状态，也就没有「上一次没清干净」这类 bug。
    /// </summary>
    public sealed class DialogueRuntime
    {
        /// <summary>
        /// 一次播放内允许的跨组跳转上限。
        /// 组内不可能成环（跳转无位置寻址，§4.3），但**跨组可以**：A 跳 B、B 跳 A。
        /// 静态校验能查出显式环，查不出条件驱动的环，所以这里留一道运行时兜底。
        /// </summary>
        public const int MaxGroupJumps = 64;

        /// <summary>触发本次播放的分类（写 recent 环、日志用）。</summary>
        public EVisitorDialogueTrigger Trigger;

        public EServeSatisfaction Satisfaction;

        /// <summary>recent 环的分类键（§4.6）。</summary>
        public string CategoryKey;

        /// <summary>
        /// 从池里抽中的那个组。**只有它写 recent 环**——
        /// 跳转到的组不是池成员，把它们也记进去会污染去重。
        /// </summary>
        public DialogueGroupDef RootGroup;

        /// <summary>当前正在播的组（跳转后会变）。</summary>
        public DialogueGroupDef CurrentGroup;

        /// <summary>当前步下标。</summary>
        public int StepIndex;

        /// <summary>已发生的跨组跳转次数（防环，见 MaxGroupJumps）。</summary>
        public int JumpCount;

        /// <summary>事件与条件的执行上下文（播放期间只读）。</summary>
        public GameplayContext Context;

        /// <summary>当前步；越界返回 null。</summary>
        public DialogueStep CurrentStep
        {
            get
            {
                var steps = CurrentGroup != null ? CurrentGroup.steps : null;
                if (steps == null || StepIndex < 0 || StepIndex >= steps.Count) return null;
                return steps[StepIndex];
            }
        }

        /// <summary>当前是否停在分支上（停在分支时点击不推进，必须选一个选项）。</summary>
        public bool IsAtBranch
        {
            get
            {
                var step = CurrentStep;
                return step != null && step.kind == EDialogueStepKind.Branch;
            }
        }

        /// <summary>切到另一个组从头播；超过跳转上限时返回 false（调用方按结束处理）。</summary>
        public bool JumpTo(DialogueGroupDef group)
        {
            if (group == null) return false;
            if (++JumpCount > MaxGroupJumps)
            {
                Debug.LogError($"[对话] 跨组跳转超过 {MaxGroupJumps} 次，疑似对话组之间成环（最后一组：{group.DisplayId}）；" +
                               "已强制结束本次播放，请检查分支的「跳到组」配置");
                return false;
            }
            CurrentGroup = group;
            StepIndex = 0;
            return true;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>对话组内一步的种类，三选一（设计说明 §4.3）。</summary>
    public enum EDialogueStepKind
    {
        /// <summary>说一句话（打字机逐字显现，点击推进）。</summary>
        Line = 0,
        /// <summary>执行事件，然后自动继续下一步（不停顿、不需要玩家点击）。</summary>
        Action = 1,
        /// <summary>出选项，必须选（不满足条件的选项置灰保留可见，§12 待确认默认值）。</summary>
        Branch = 2,
    }

    /// <summary>
    /// 分支选项选完之后往哪走（设计说明 §4.3）。
    /// **跳转不使用任何位置寻址**——没有行索引、没有标签。插行删行绝对安全，组内不可能成环。
    /// 「分支后各说各的再汇合」的写法：两个选项跳到不同组，那两个组末尾再跳到同一个汇合组。
    /// </summary>
    public enum EBranchNext
    {
        /// <summary>继续本组：回到本 Branch 之后的下一步。</summary>
        ContinueGroup = 0,
        /// <summary>跳到组：切到 nextGroup 从头播。</summary>
        JumpToGroup = 1,
        /// <summary>结束：整段对话到此为止。</summary>
        End = 2,
    }

    /// <summary>分支的一个选项（设计说明 §4.3）。</summary>
    [Serializable]
    public sealed class BranchOption
    {
        [SerializeReference, SubclassSelector]
        [Tooltip("可选条件，多条之间 AND（§4.2）。留空 = 无条件。\n" +
                 "不满足时选项置灰但保留可见——让玩家知道存在别的可能（§12 待确认默认值）")]
        public List<IGameplayCondition> conditions = new List<IGameplayCondition>();

        [Tooltip("选项文本（支持占位符，§9）")]
        public string text;

        [SerializeReference, SubclassSelector]
        [Tooltip("选中后执行的事件（§4.2）。奖励类事件放在选项上是允许的（§5.3 铁律②）")]
        public List<IGameplayAction> actions = new List<IGameplayAction>();

        [Tooltip("执行完事件之后往哪走")]
        public EBranchNext next;

        [Tooltip("next == 跳到组 时才填")]
        public DialogueGroupDef nextGroup;

        /// <summary>
        /// 是否为无条件选项。
        /// **硬校验：每个 Branch 至少要有一个无条件选项**——否则条件全不满足时对话卡死，
        /// 玩家只能 ESC 退出。编辑器与资产校验器都要拦（§4.3）。
        /// </summary>
        public bool IsUnconditional => conditions == null || conditions.Count == 0;
    }

    /// <summary>
    /// 对话组内的一步（设计说明 §4.3）：Line / Action / Branch 三选一。
    /// 用「种类枚举 + 三个字段」而不是多态，是因为这层结构固定不扩展，
    /// 多态只会给策划的 Inspector 平添一次选类型的操作。
    /// </summary>
    [Serializable]
    public sealed class DialogueStep
    {
        public EDialogueStepKind kind;

        [Tooltip("kind == 说一句话 时有效")]
        public DialogueLine line = new DialogueLine();

        [SerializeReference, SubclassSelector]
        [Tooltip("kind == 执行事件 时有效；多个事件按列表顺序依次执行")]
        public List<IGameplayAction> actions = new List<IGameplayAction>();

        [Tooltip("kind == 出选项 时有效。分支可以出现在 steps 的任意位置，不限于末尾（§4.3）")]
        public List<BranchOption> options = new List<BranchOption>();
    }
}

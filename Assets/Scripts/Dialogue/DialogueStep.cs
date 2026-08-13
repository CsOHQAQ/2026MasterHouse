using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>对话组内一步的种类，三选一（Excel 第二页「类型」列）。</summary>
    public enum EDialogueStepKind
    {
        /// <summary>说一句话（打字机逐字显现，点击推进）。</summary>
        Line = 0,
        /// <summary>执行事件，然后自动继续下一步（不停顿、不需要玩家点击）。</summary>
        Action = 1,
        /// <summary>出选项，必须选（不满足条件的选项置灰保留可见）。**只能出现在主线上，子句里不允许**。</summary>
        Branch = 2,
    }

    /// <summary>
    /// 分支选项子句里的一步（Excel 第二页里「选项」与「句序」两列都填了的那些行）。
    ///
    /// **类型上就不能再有 Branch**——嵌套分支明确不做（第 15 题定案），
    /// 所以子句用一个独立的类型而不是复用 DialogueStep。顺带解决了 Unity 序列化的递归问题：
    /// DialogueStep → DialogueOption → DialogueSubStep 是一条不回头的链，
    /// 不会触发「Serialization depth limit exceeded」那条警告。
    /// </summary>
    [Serializable]
    public sealed class DialogueSubStep
    {
        [Tooltip("只允许 Line / Action（导入器会拒绝 Branch）")]
        public EDialogueStepKind kind;

        [Tooltip("kind == Line 时有效")]
        public DialogueLine line = new DialogueLine();

        [Tooltip("kind == Action 时有效；按列表顺序依次执行")]
        public List<DialogueCall> actions = new List<DialogueCall>();

        [Tooltip("来源 Excel 行号，报错指路用；不参与玩法")]
        public int sourceRow;
    }

    /// <summary>
    /// 分支的一个选项（Excel 第二页里「选项」列填了、「句序」列空着的那一行）。
    ///
    /// **没有跳转字段**：跨组跳转已于 2026-08-14 重构删除。分类拆细之后
    /// 「接待 / 拒绝 / 交付」全在组内用子句解决，不再需要跳到另一组，
    /// 也因此不再需要跳转上限与跨组成环检测。将来真需要的话做成一条 Action 事件即可（第 15 题定案）。
    ///
    /// 子句播完 → **汇合到主线的下一步**。实际内容里分支都在组尾，所以「汇合」天然等于「结束」，
    /// 不需要额外的结束标记（同上）。
    /// </summary>
    [Serializable]
    public sealed class DialogueOption
    {
        [Tooltip("选项文本（支持占位符 {需求} {访客名}）")]
        public string text;

        [Tooltip("可选条件，多条之间 AND。留空 = 无条件。\n" +
                 "不满足时选项置灰但保留可见——让玩家知道存在别的可能")]
        public List<DialogueCall> conditions = new List<DialogueCall>();

        [Tooltip("选中后播的子句（Line / Action，一层，不能再有分支）")]
        public List<DialogueSubStep> steps = new List<DialogueSubStep>();

        [Tooltip("来源 Excel 行号，报错指路用；不参与玩法")]
        public int sourceRow;

        /// <summary>
        /// 是否为无条件选项。
        /// **硬校验：每个 Branch 至少要有一个无条件选项**——否则条件全不满足时对话卡死，
        /// 玩家只能 ESC 退出。资产校验器会拦。
        /// </summary>
        public bool IsUnconditional => conditions == null || conditions.Count == 0;
    }

    /// <summary>
    /// 对话组主线上的一步（Excel 第二页里「选项」「句序」两列都空着的那些行）。
    /// 用「种类枚举 + 三个字段」而不是多态：这层结构固定不扩展，
    /// 多态只会给导入器和策划平添一次选类型的操作。
    /// </summary>
    [Serializable]
    public sealed class DialogueStep
    {
        public EDialogueStepKind kind;

        [Tooltip("kind == Line 时有效")]
        public DialogueLine line = new DialogueLine();

        [Tooltip("kind == Action 时有效；按列表顺序依次执行")]
        public List<DialogueCall> actions = new List<DialogueCall>();

        [Tooltip("kind == Branch 时有效。分支可以出现在主线的任意位置，不限于末尾")]
        public List<DialogueOption> options = new List<DialogueOption>();

        [Tooltip("来源 Excel 行号，报错指路用；不参与玩法。\n" +
                 "**报错一律报这个，不报 List 下标**——「步骤」列允许留空隙（10/20/30），" +
                 "说「第 1 步」策划在表里找不到对应的行")]
        public int sourceRow;
    }
}

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MasterHouse.EditorTools
{
    /// <summary>一条校验结果。</summary>
    public struct DialogueIssue
    {
        /// <summary>true = 阻断性错误（内容跑不起来）；false = 提示性警告（能跑，但多半配错了）。</summary>
        public bool IsError;

        public string Message;

        /// <summary>点击日志能定位到的资产。</summary>
        public Object Context;
    }

    /// <summary>
    /// 对话资产校验器（设计说明 §11.3）。
    ///
    /// 错误（跑不起来，必须改）：
    ///   · 分支缺无条件选项——条件全不满足时对话卡死，玩家只能 ESC（§4.3 硬校验）
    ///   · 分类列表为空——该触发点没话可说（§4.5）
    ///   · 跨组引用断链——选了「跳到组」却没填目标
    /// 警告（能跑，但很可能是事故）：
    ///   · 奖励类事件放在对话组中途——中途给奖励 + 玩家 ESC = 反复领取（§5.3 铁律②）
    ///   · 对话组之间成环——运行时有跳转上限兜底，但成环基本是配错了
    ///   · 闲逛组含分支/事件或多条台词——气泡只显示第一句，其余内容永远走不到
    ///   · 空台词、空步骤列表
    /// </summary>
    public static class DialogueAssetValidator
    {
        [MenuItem("MasterHouse/对话系统/校验全部对话资产")]
        public static void ValidateAllFromMenu()
        {
            var issues = ValidateAll();
            var errors = 0;
            foreach (var issue in issues)
            {
                if (issue.IsError) errors++;
                if (issue.IsError) Debug.LogError("[对话校验] " + issue.Message, issue.Context);
                else Debug.LogWarning("[对话校验] " + issue.Message, issue.Context);
            }
            if (issues.Count == 0) Debug.Log("[对话校验] 全部对话资产通过校验。");
            else Debug.Log($"[对话校验] 完成：{errors} 个错误、{issues.Count - errors} 个警告。");
        }

        /// <summary>扫描工程里全部对话池与对话组。</summary>
        public static List<DialogueIssue> ValidateAll()
        {
            var issues = new List<DialogueIssue>();

            // 按资产路径排序后再校验，保证同一份工程每次得到同样顺序的报告（便于 diff）
            var poolPaths = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:DialoguePoolDef"))
                poolPaths.Add(AssetDatabase.GUIDToAssetPath(guid));
            poolPaths.Sort(System.StringComparer.Ordinal);
            foreach (var path in poolPaths)
            {
                var pool = AssetDatabase.LoadAssetAtPath<DialoguePoolDef>(path);
                if (pool != null) Validate(pool, issues);
            }

            var groupPaths = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:DialogueGroupDef"))
                groupPaths.Add(AssetDatabase.GUIDToAssetPath(guid));
            groupPaths.Sort(System.StringComparer.Ordinal);
            var groups = new List<DialogueGroupDef>();
            foreach (var path in groupPaths)
            {
                var group = AssetDatabase.LoadAssetAtPath<DialogueGroupDef>(path);
                if (group == null) continue;
                groups.Add(group);
                Validate(group, issues);
            }

            DetectCycles(groups, issues);
            return issues;
        }

        // ══════════ 对话池 ══════════

        public static void Validate(DialoguePoolDef pool, List<DialogueIssue> issues)
        {
            if (pool == null) return;
            var name = pool.name;
            Category(issues, pool, name, "初次见面", pool.firstMeeting);
            Category(issues, pool, name, "开始等待服务", pool.serviceStart);
            Category(issues, pool, name, "服务中交谈", pool.serviceCheck);
            Category(issues, pool, name, "被拒绝", pool.rejected);
            Category(issues, pool, name, "完成服务·不对味", pool.doneMismatch);
            Category(issues, pool, name, "完成服务·一般", pool.donePlain);
            Category(issues, pool, name, "完成服务·满意", pool.doneSatisfied);
            Category(issues, pool, name, "完成服务·完美", pool.donePerfect);
            Category(issues, pool, name, "满意后闲逛", pool.wanderChat);

            // 闲逛组的内容形态另有约束：气泡只显示第一条台词
            foreach (var entry in pool.wanderChat)
            {
                if (entry == null || entry.group == null) continue;
                ValidateWanderGroup(entry.group, issues);
            }
        }

        private static void Category(List<DialogueIssue> issues, Object context, string poolName,
            string categoryName, List<DialogueGroupEntry> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                Error(issues, context, $"对话池「{poolName}」的分类「{categoryName}」是空的——该触发点没话可说（§4.5）");
                return;
            }
            var usable = 0;
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null || entry.group == null)
                {
                    Error(issues, context, $"对话池「{poolName}」·「{categoryName}」第 {i} 行没有引用对话组");
                    continue;
                }
                if (entry.weight <= 0)
                {
                    Warn(issues, context, $"对话池「{poolName}」·「{categoryName}」的「{entry.group.DisplayId}」权重为 {entry.weight}，不会被抽中");
                    continue;
                }
                if (entry.conditions == null || entry.conditions.Count == 0) usable++;
            }
            if (usable == 0)
                Warn(issues, context, $"对话池「{poolName}」·「{categoryName}」里每一条都带条件——" +
                                      "条件同时不满足时该分类会没有候选，建议至少留一条无条件的兜底");
        }

        // ══════════ 对话组 ══════════

        public static void Validate(DialogueGroupDef group, List<DialogueIssue> issues)
        {
            if (group == null) return;
            var id = group.DisplayId;
            if (group.steps == null || group.steps.Count == 0)
            {
                Warn(issues, group, $"对话组「{id}」没有任何步骤");
                return;
            }

            for (var i = 0; i < group.steps.Count; i++)
            {
                var step = group.steps[i];
                if (step == null)
                {
                    Error(issues, group, $"对话组「{id}」第 {i} 步是空的");
                    continue;
                }
                var isLast = i == group.steps.Count - 1;
                switch (step.kind)
                {
                    case EDialogueStepKind.Line:
                        if (step.line == null || string.IsNullOrWhiteSpace(step.line.text))
                            Warn(issues, group, $"对话组「{id}」第 {i} 步是空台词");
                        break;

                    case EDialogueStepKind.Action:
                        if (step.actions == null || step.actions.Count == 0)
                        {
                            Warn(issues, group, $"对话组「{id}」第 {i} 步是事件步却没有配任何事件");
                            break;
                        }
                        for (var a = 0; a < step.actions.Count; a++)
                        {
                            if (step.actions[a] == null)
                            {
                                Warn(issues, group, $"对话组「{id}」第 {i} 步的第 {a} 个事件没有选类型");
                                continue;
                            }
                            // §5.3 铁律②：奖励只允许放在组末尾或分支选项上。
                            // 中途给奖励 + 玩家 ESC 重进 = 反复领取。提示性校验，不阻断。
                            if (!isLast && step.actions[a] is IRewardAction)
                                Warn(issues, group, $"对话组「{id}」第 {i} 步的奖励事件" +
                                                    $"（{step.actions[a].GetType().Name}）不在组末尾——" +
                                                    "玩家中途 ESC 可能反复领取（§5.3 铁律②）");
                        }
                        break;

                    case EDialogueStepKind.Branch:
                        ValidateBranch(group, i, step, issues);
                        break;
                }
            }
        }

        private static void ValidateBranch(DialogueGroupDef group, int index, DialogueStep step,
            List<DialogueIssue> issues)
        {
            var id = group.DisplayId;
            if (step.options == null || step.options.Count == 0)
            {
                Error(issues, group, $"对话组「{id}」第 {index} 步是分支却没有任何选项");
                return;
            }

            var hasUnconditional = false;
            for (var i = 0; i < step.options.Count; i++)
            {
                var option = step.options[i];
                if (option == null)
                {
                    Error(issues, group, $"对话组「{id}」第 {index} 步的第 {i} 个选项是空的");
                    continue;
                }
                if (option.IsUnconditional) hasUnconditional = true;
                if (string.IsNullOrWhiteSpace(option.text))
                    Warn(issues, group, $"对话组「{id}」第 {index} 步的第 {i} 个选项没有文本");
                if (option.next == EBranchNext.JumpToGroup && option.nextGroup == null)
                    Error(issues, group, $"对话组「{id}」第 {index} 步的选项「{option.text}」选了「跳到组」却没填目标组（断链）");
                if (option.actions != null)
                    for (var a = 0; a < option.actions.Count; a++)
                        if (option.actions[a] == null)
                            Warn(issues, group, $"对话组「{id}」第 {index} 步选项「{option.text}」的第 {a} 个事件没有选类型");
            }

            // §4.3 硬校验：没有无条件选项时，条件全不满足就会卡死——玩家只能 ESC 退出
            if (!hasUnconditional)
                Error(issues, group, $"对话组「{id}」第 {index} 步的分支**没有无条件选项**：" +
                                     "条件全不满足时对话会卡死（§4.3 硬校验，必须至少留一个无条件选项）");
        }

        /// <summary>闲逛组的额外约束：气泡只显示第一条台词，其余内容永远走不到。</summary>
        private static void ValidateWanderGroup(DialogueGroupDef group, List<DialogueIssue> issues)
        {
            if (group.steps == null) return;
            var lineCount = 0;
            var hasOther = false;
            foreach (var step in group.steps)
            {
                if (step == null) continue;
                if (step.kind == EDialogueStepKind.Line) lineCount++;
                else hasOther = true;
            }
            if (lineCount == 0)
                Error(issues, group, $"闲逛对话组「{group.DisplayId}」没有任何台词行，冒不出泡");
            else if (lineCount > 1)
                Warn(issues, group, $"闲逛对话组「{group.DisplayId}」有 {lineCount} 条台词，" +
                                    "但气泡只显示第一条——想要多句请拆成多个单句组（靠 recent 环轮换）");
            if (hasOther)
                Warn(issues, group, $"闲逛对话组「{group.DisplayId}」含有事件或分支步骤，" +
                                    "但气泡没有点击推进与选项列，这些步骤永远走不到");
        }

        // ══════════ 跨组成环 ══════════

        /// <summary>
        /// 对话组之间的跳转成环检测。组内不可能成环（跳转无位置寻址，§4.3），但跨组可以：A 跳 B、B 跳 A。
        /// 运行时有跳转次数上限兜底（DialogueRuntime.MaxGroupJumps），所以这里报警告而非错误——
        /// 环也可能是策划有意做的循环菜单，只是多半不是。
        /// </summary>
        private static void DetectCycles(List<DialogueGroupDef> groups, List<DialogueIssue> issues)
        {
            var visiting = new HashSet<DialogueGroupDef>();
            var settled = new HashSet<DialogueGroupDef>();
            foreach (var group in groups) // groups 已按资产路径排序，报告顺序稳定
                Walk(group, visiting, settled, issues);
        }

        private static void Walk(DialogueGroupDef group, HashSet<DialogueGroupDef> visiting,
            HashSet<DialogueGroupDef> settled, List<DialogueIssue> issues)
        {
            if (group == null || settled.Contains(group)) return;
            if (!visiting.Add(group))
            {
                Warn(issues, group, $"对话组「{group.DisplayId}」参与了一个跨组跳转环——" +
                                    $"运行时超过 {DialogueRuntime.MaxGroupJumps} 次跳转会被强制结束");
                return;
            }
            if (group.steps != null)
                foreach (var step in group.steps)
                {
                    if (step == null || step.kind != EDialogueStepKind.Branch || step.options == null) continue;
                    foreach (var option in step.options)
                        if (option != null && option.next == EBranchNext.JumpToGroup)
                            Walk(option.nextGroup, visiting, settled, issues);
                }
            visiting.Remove(group);
            settled.Add(group);
        }

        // ══════════ 小工具 ══════════

        private static void Error(List<DialogueIssue> issues, Object context, string message) =>
            issues.Add(new DialogueIssue { IsError = true, Message = message, Context = context });

        private static void Warn(List<DialogueIssue> issues, Object context, string message) =>
            issues.Add(new DialogueIssue { IsError = false, Message = message, Context = context });
    }
}

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// 对话内容校验器（2026-08-14 重构：报错口径从「SO 里第几步」改成「Excel 第几行」）。
    ///
    /// 对话编辑器已退役、Excel 是唯一源，所以策划能改的只有 Excel——报错必须指回那里，
    /// 说「对话组 Group_xxx 第 3 步」对他没有任何用处。
    ///
    /// 与导表共用同一份规则（DialogueCsvImporter.CrossValidate 直接调 ValidateContent），
    /// 「导表能过」等价于「校验能过」，不会出现两套口径。
    ///
    /// 错误（跑不起来，必须改）：
    ///   · 分支缺无条件选项——条件全不满足时对话卡死，玩家只能 ESC
    ///   · 分支没有任何选项 / 选项没有文本
    ///   · 某条需求没有配【需求对话】——那位访客点开什么都不会发生，只能等超时
    ///   · 某个种族的必备分类整个是空的
    /// 警告（能跑，但多半是事故）：
    ///   · 奖励事件不在所在路径的末尾——中途给奖励 + 玩家 ESC = 反复领取（§5.3 铁律②）
    ///   · 闲聊组含分支/事件或多条台词——气泡只显示第一句，其余永远走不到
    ///   · 空台词、空组
    /// </summary>
    public static class DialogueAssetValidator
    {
        /// <summary>四档反馈之外，每个种族都必须配到的分类（缺了对应时机就没话可说）。</summary>
        private static readonly EDialogueCategory[] RequiredPerRace =
        {
            EDialogueCategory.FirstMeeting,
            EDialogueCategory.WaitingReception,
            EDialogueCategory.SmallTalk,
        };

        [MenuItem("MasterHouse/对话系统/校验对话表")]
        public static void ValidateFromMenu()
        {
            var table = AssetDatabase.LoadAssetAtPath<DialogueTable>(DialogueCsvImporter.TableAssetPath);
            if (table == null)
            {
                Debug.LogError("[对话校验] 找不到对话整表：" + DialogueCsvImporter.TableAssetPath +
                               "；请先执行菜单 MasterHouse → 对话系统 → 从 CSV 导入对话");
                return;
            }
            var report = new DialogueReport();
            ValidateContent(table.groups, table.entries, report);
            foreach (var message in report.ErrorMessages) Debug.LogError("[对话校验] " + message);
            foreach (var message in report.WarningMessages) Debug.LogWarning("[对话校验] " + message);
            Debug.Log(report.Errors == 0 && report.Warnings == 0
                ? "[对话校验] 对话表通过校验。"
                : $"[对话校验] 完成：{report.Errors} 个错误、{report.Warnings} 条警告。");
        }

        /// <summary>
        /// 内容层校验。导表流程与菜单校验共用本方法，规则只有一份。
        /// </summary>
        public static void ValidateContent(List<DialogueGroup> groups, List<DialoguePoolEntry> entries,
            DialogueReport report)
        {
            foreach (var group in groups) ValidateGroup(group, report);
            ValidateCoverage(entries, report);
            ValidateSmallTalkShape(groups, entries, report);
        }

        // ══════════ 单组结构 ══════════

        private static void ValidateGroup(DialogueGroup group, DialogueReport report)
        {
            if (group.steps == null || group.steps.Count == 0)
            {
                report.Warn("对话内容", group.sourceRow, $"对话组 {group.id} 没有任何步骤");
                return;
            }

            for (var i = 0; i < group.steps.Count; i++)
            {
                var step = group.steps[i];
                if (step == null) continue;
                var where = $"对话组 {group.id} 第 {i + 1} 步";

                switch (step.kind)
                {
                    case EDialogueStepKind.Line:
                        if (step.line == null || string.IsNullOrWhiteSpace(step.line.text))
                            report.Warn("对话内容", group.sourceRow, $"{where} 是空台词");
                        break;

                    case EDialogueStepKind.Action:
                        // 主线上的奖励事件必须是主线的最后一步（否则 ESC 重进可反复领取）
                        if (i != group.steps.Count - 1) WarnIfReward(step.actions, report, group.sourceRow, where);
                        break;

                    case EDialogueStepKind.Branch:
                        ValidateBranch(group, i, step, report);
                        break;
                }
            }
        }

        private static void ValidateBranch(DialogueGroup group, int index, DialogueStep step, DialogueReport report)
        {
            var where = $"对话组 {group.id} 第 {index + 1} 步";
            if (step.options == null || step.options.Count == 0)
            {
                report.Error("对话内容", group.sourceRow, $"{where} 是分支却没有任何选项");
                return;
            }

            var hasUnconditional = false;
            for (var i = 0; i < step.options.Count; i++)
            {
                var option = step.options[i];
                if (option == null) continue;
                if (option.IsUnconditional) hasUnconditional = true;
                if (string.IsNullOrWhiteSpace(option.text))
                    report.Warn("对话内容", group.sourceRow, $"{where} 第 {i + 1} 个选项没有文本");

                // 子句里的奖励事件必须是这条路径的最后一个事件位
                if (option.steps == null) continue;
                var lastActionAt = -1;
                for (var s = option.steps.Count - 1; s >= 0; s--)
                    if (option.steps[s] != null && option.steps[s].kind == EDialogueStepKind.Action)
                    { lastActionAt = s; break; }
                for (var s = 0; s < option.steps.Count; s++)
                {
                    var sub = option.steps[s];
                    if (sub == null || sub.kind != EDialogueStepKind.Action || s == lastActionAt) continue;
                    WarnIfReward(sub.actions, report, group.sourceRow,
                        $"{where} 第 {i + 1} 个选项的第 {s + 1} 句");
                }
            }

            // 硬校验：没有无条件选项时，条件全不满足就会卡死——玩家只能 ESC 退出
            if (!hasUnconditional)
                report.Error("对话内容", group.sourceRow,
                    $"{where} 的分支**没有无条件选项**：条件全不满足时对话会卡死，必须至少留一个不填条件的选项");
        }

        private static void WarnIfReward(List<DialogueCall> actions, DialogueReport report, int row, string where)
        {
            if (actions == null) return;
            foreach (var call in actions)
                if (DialogueFuncs.IsReward(call))
                    report.Warn("对话内容", row,
                        $"{where} 的奖励事件 {call.func} 不在这条路径的最后一个事件位——" +
                        "玩家播到这里按 ESC 再点开可能反复领取（§5.3 铁律②）");
        }

        // ══════════ 覆盖度 ══════════

        /// <summary>
        /// 每个种族的必备分类不能空；每条需求必须有【需求对话】。
        /// 后者是新模型下最容易踩的坑——加了一条需求却忘了写台词，那位访客点开什么都不会发生。
        /// </summary>
        private static void ValidateCoverage(List<DialoguePoolEntry> entries, DialogueReport report)
        {
            var covered = new HashSet<string>();
            var needTalkNeeds = new HashSet<string>();
            var races = new HashSet<string>();
            foreach (var entry in entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.raceId)) continue;
                races.Add(entry.raceId);
                covered.Add($"{entry.raceId}|{(int)entry.category}");
                if (entry.category == EDialogueCategory.NeedTalk && !string.IsNullOrEmpty(entry.needId))
                    needTalkNeeds.Add($"{entry.raceId}|{entry.needId}");
            }

            var allRaces = DialogueCsvImporter.AllRaceIds();
            foreach (var raceId in allRaces)
            {
                if (!races.Contains(raceId))
                {
                    report.Error("对话组", 0, $"种族 {raceId} 在对话表里一行都没有——他一句话都说不出来");
                    continue;
                }
                foreach (var category in RequiredPerRace)
                    if (!covered.Contains($"{raceId}|{(int)category}"))
                        report.Error("对话组", 0,
                            $"种族 {raceId} 缺少分类「{DialogueCategoryText.NameOf(category)}」" +
                            $"（{DialogueCategoryText.KeyOf(category)}）的对话组");
                // 四档反馈：至少要有「失望」与「完美」两档兜底（条件类只会走到这两档）
                foreach (var category in new[] { EDialogueCategory.FeedbackDisappointed, EDialogueCategory.FeedbackPerfect })
                    if (!covered.Contains($"{raceId}|{(int)category}"))
                        report.Error("对话组", 0,
                            $"种族 {raceId} 缺少分类「{DialogueCategoryText.NameOf(category)}」" +
                            $"（{DialogueCategoryText.KeyOf(category)}）的对话组");
            }

            foreach (var needName in DialogueCsvImporter.AllNeedNames())
            foreach (var raceId in allRaces)
                if (!needTalkNeeds.Contains($"{raceId}|{needName}"))
                    report.Error("对话组", 0,
                        $"需求「{needName}」没有给种族 {raceId} 配【需求对话】（needTalk）——" +
                        "带这条需求的访客点开不会有任何反应，只能等超时");
        }

        // ══════════ 闲聊组的形态 ══════════

        /// <summary>闲聊走场景气泡：只显示第一条台词，事件与分支都走不到。</summary>
        private static void ValidateSmallTalkShape(List<DialogueGroup> groups, List<DialoguePoolEntry> entries,
            DialogueReport report)
        {
            var smallTalkIds = new HashSet<int>();
            foreach (var entry in entries)
                if (entry != null && entry.category == EDialogueCategory.SmallTalk)
                    smallTalkIds.Add(entry.groupId);

            foreach (var group in groups)
            {
                if (!smallTalkIds.Contains(group.id) || group.steps == null) continue;
                var lines = 0;
                var hasOther = false;
                foreach (var step in group.steps)
                {
                    if (step == null) continue;
                    if (step.kind == EDialogueStepKind.Line) lines++;
                    else hasOther = true;
                }
                if (lines == 0)
                    report.Error("对话内容", group.sourceRow, $"闲聊组 {group.id} 没有任何台词行，冒不出泡");
                else if (lines > 1)
                    report.Warn("对话内容", group.sourceRow,
                        $"闲聊组 {group.id} 有 {lines} 条台词，但气泡只显示第一条——" +
                        "想要多句请拆成多个单句组（靠 recent 环轮换）");
                if (hasOther)
                    report.Warn("对话内容", group.sourceRow,
                        $"闲聊组 {group.id} 含有事件或分支，但气泡没有点击推进与选项列，这些步骤永远走不到");
            }
        }
    }
}

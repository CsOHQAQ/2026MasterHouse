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
    /// 错误（这一组跑不起来，必须改；导表会因此整表不落盘）：
    ///   · 分支缺无条件选项——条件全不满足时对话卡死，玩家只能 ESC
    ///   · 分支没有任何选项
    ///   · 闲聊组一句台词都没有，冒不出泡
    /// 警告（能跑，但多半是事故）：
    ///   · 奖励事件不在所在路径的末尾——中途给奖励 + 玩家 ESC = 反复领取（§5.3 铁律②）
    ///   · 闲聊组含分支/事件或多条台词——气泡只显示第一句，其余永远走不到
    ///   · 告别组里没有 Leave 事件——客人道完别也不会走，一直占着那间房
    ///   · Leave 配在【告别】以外的分类里——那一态调不动它，等于白写
    ///   · 覆盖度：某条需求没配【需求对话】、某个种族缺必备分类（见 ValidateCoverage 的注释，
    ///     刻意不是错误——不该因为新建一个需求资产就把整张已调好的表废掉）
    ///   · 空台词、空组、选项没有文本
    /// </summary>
    public static class DialogueAssetValidator
    {
        /// <summary>四档反馈之外，每个种族都必须配到的分类（缺了对应时机就没话可说）。</summary>
        private static readonly EDialogueCategory[] RequiredPerRace =
        {
            EDialogueCategory.FirstMeeting,
            EDialogueCategory.WaitingReception,
            EDialogueCategory.SmallTalk,
            // 缺了它客人到点会一直卡在【待告别】占着房间（运行时有兜底放行，但那是事故路径）
            EDialogueCategory.Farewell,
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
            ValidateFarewellShape(groups, entries, report);
        }

        // ══════════ 单组结构 ══════════

        private static void ValidateGroup(DialogueGroup group, DialogueReport report)
        {
            if (group.steps == null || group.steps.Count == 0)
            {
                report.Warn("对话内容", group.sourceRow, $"对话组 {group.id} 没有任何步骤");
                return;
            }

            // **报错一律带该步自己的 Excel 行号**（step.sourceRow），不用 List 下标：
            // 「步骤」列允许留空隙（10/20/30），说「第 1 步」策划在表里找不到对应的行。
            for (var i = 0; i < group.steps.Count; i++)
            {
                var step = group.steps[i];
                if (step == null) continue;
                var row = step.sourceRow > 0 ? step.sourceRow : group.sourceRow;
                var where = $"对话组 {group.id}";

                switch (step.kind)
                {
                    case EDialogueStepKind.Line:
                        if (step.line == null || string.IsNullOrWhiteSpace(step.line.text))
                            report.Warn("对话内容", row, $"{where} 这一步是空台词");
                        break;

                    case EDialogueStepKind.Action:
                        // 主线上的奖励事件必须是主线的最后一步（否则 ESC 重进可反复领取）
                        if (i != group.steps.Count - 1) WarnIfMustBeLast(step.actions, report, row, where);
                        break;

                    case EDialogueStepKind.Branch:
                        ValidateBranch(group, step, report, row);
                        break;
                }
            }
        }

        private static void ValidateBranch(DialogueGroup group, DialogueStep step, DialogueReport report, int stepRow)
        {
            var where = $"对话组 {group.id}";
            if (step.options == null || step.options.Count == 0)
            {
                report.Error("对话内容", stepRow, $"{where} 这一步是分支却没有任何选项");
                return;
            }

            var hasUnconditional = false;
            foreach (var option in step.options)
            {
                if (option == null) continue;
                var optionRow = option.sourceRow > 0 ? option.sourceRow : stepRow;
                if (option.IsUnconditional) hasUnconditional = true;
                if (string.IsNullOrWhiteSpace(option.text))
                    report.Warn("对话内容", optionRow, $"{where} 这个选项没有文本");

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
                    WarnIfMustBeLast(sub.actions, report, sub.sourceRow > 0 ? sub.sourceRow : optionRow,
                        $"{where} 选项「{option.text}」的这一句");
                }
            }

            // 硬校验：没有无条件选项时，条件全不满足就会卡死——玩家只能 ESC 退出
            if (!hasUnconditional)
                report.Error("对话内容", stepRow,
                    $"{where} 这个分支**没有无条件选项**：条件全不满足时对话会卡死，必须至少留一个不填条件的选项");
        }

        /// <summary>
        /// 「只能放路径末尾」的事件位置校验。判据是 MustBeLast 而不是 IsReward——
        /// Leave 不给奖励却同样只能放末尾（见 DialogueActionDef.MustBeLast），措辞按两种理由分开。
        /// </summary>
        private static void WarnIfMustBeLast(List<DialogueCall> actions, DialogueReport report, int row, string where)
        {
            if (actions == null) return;
            foreach (var call in actions)
            {
                if (!DialogueFuncs.MustBeLast(call)) continue;
                report.Warn("对话内容", row, DialogueFuncs.IsReward(call)
                    ? $"{where} 的奖励事件 {call.func} 不在这条路径的最后一个事件位——" +
                      "玩家播到这里按 ESC 再点开可能反复领取（§5.3 铁律②）"
                    : $"{where} 的 {call.func} 不在这条路径的最后一个事件位——" +
                      "它一执行访客就离场、演员开始走向门口，后面的台词会读成「人都走了还在说话」");
            }
        }

        // ══════════ 覆盖度 ══════════

        /// <summary>
        /// 覆盖度：每个种族的必备分类、每条需求的【需求对话】。
        /// 后者是新模型下最容易踩的坑——加了一条需求却忘了写台词，那位访客点开什么都不会发生。
        ///
        /// **一律是警告而不是错误**（2026-08-14 审查后改）。理由是错误会让导表整表不落盘：
        /// 新建一个 NeedDef 资产（哪怕只是试验用）立刻产生 N 条错误，把已经调好的台词一起废掉，
        /// 而"某位访客没话说"根本不影响其余内容跑起来——它正是"跑得起来但多半是事故"这一档。
        /// 真跑到那一步时运行时还有 LogError + 玩家侧 Toast 兜底。
        ///
        /// 【立绘 ID 化之后这里会更吵】一组一种族之后，覆盖度是「种族 × 分类」和「种族 × 需求」两张
        /// 笛卡尔积——**新加一个种族会立刻刷出一整屏警告**（他一句台词都还没有）。这是预期行为，
        /// 也正是「警告而非错误」的价值所在：加种族的那一刻工程仍然跑得起来，照着警告一条条补台词即可。
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
                    report.Warn("对话组", 0, $"种族 {raceId} 在对话表里一行都没有——他一句话都说不出来");
                    continue;
                }
                // 必备分类 + 两档兜底反馈（条件类只会走到失望与完美）
                foreach (var category in RequiredPerRace)
                    WarnIfMissing(report, covered, raceId, category);
                WarnIfMissing(report, covered, raceId, EDialogueCategory.FeedbackDisappointed);
                WarnIfMissing(report, covered, raceId, EDialogueCategory.FeedbackPerfect);
            }

            foreach (var needName in DialogueCsvImporter.AllNeedNames())
            foreach (var raceId in allRaces)
                if (!needTalkNeeds.Contains($"{raceId}|{needName}"))
                    report.Warn("对话组", 0,
                        $"需求「{needName}」没有给种族 {raceId} 配【需求对话】（needTalk）——" +
                        "带这条需求的访客点开不会有任何反应，只能等超时");
        }

        private static void WarnIfMissing(DialogueReport report, HashSet<string> covered, string raceId,
            EDialogueCategory category)
        {
            if (covered.Contains($"{raceId}|{(int)category}")) return;
            report.Warn("对话组", 0,
                $"种族 {raceId} 缺少分类「{DialogueCategoryText.NameOf(category)}」" +
                $"（{DialogueCategoryText.KeyOf(category)}）的对话组");
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

        // ══════════ 告别组的形态（2026-08-20）══════════

        /// <summary>
        /// 【告别】是**唯一**由内容决定「访客什么时候离开」的分类：组里那条 `Action | Leave`
        /// 执行了他才走。两条都是警告而不是错误——两种配错都还跑得起来：
        ///
        ///   · 告别组没有 Leave → 客人道完别继续站在【待告别】，那间客房一直被占；
        ///     玩家可以再点他重播，所以不是死局，但多半不是策划想要的。
        ///   · Leave 出现在别的分类 → 那些状态下 VisitorManager.Leave 直接返回 false，
        ///     运行时打 Warning、对话照播，等于白写一行。
        ///
        /// **分支不拦**：告别按「纯台词 + 末尾一条 Leave」配就够了，但真配了分支也能跑
        /// （模态播放本来就支持），所以这里连子句一起扫，不因为 Leave 写在某个选项里就误报。
        /// </summary>
        private static void ValidateFarewellShape(List<DialogueGroup> groups, List<DialoguePoolEntry> entries,
            DialogueReport report)
        {
            var farewellIds = new HashSet<int>();
            foreach (var entry in entries)
                if (entry != null && entry.category == EDialogueCategory.Farewell)
                    farewellIds.Add(entry.groupId);

            foreach (var group in groups)
            {
                if (group.steps == null) continue;
                var isFarewell = farewellIds.Contains(group.id);
                var hasLeave = false;
                foreach (var step in group.steps)
                {
                    if (step == null) continue;
                    if (step.kind == EDialogueStepKind.Action && ContainsLeave(step.actions)) hasLeave = true;
                    if (step.kind != EDialogueStepKind.Branch || step.options == null) continue;
                    foreach (var option in step.options)
                    {
                        if (option?.steps == null) continue;
                        foreach (var sub in option.steps)
                            if (sub != null && sub.kind == EDialogueStepKind.Action && ContainsLeave(sub.actions))
                                hasLeave = true;
                    }
                }

                if (isFarewell && !hasLeave)
                    report.Warn("对话内容", group.sourceRow,
                        $"告别组 {group.id} 里没有 Leave 事件——客人说完还是不会走，会一直占着那间客房；" +
                        "请在组末尾补一行「类型 = Action、文本 = Leave」");
                else if (!isFarewell && hasLeave)
                    report.Warn("对话内容", group.sourceRow,
                        $"对话组 {group.id} 配了 Leave，但它不属于【告别】（farewell）分类——" +
                        "别的状态下这条事件不会生效，运行时只会打一条 Warning");
            }
        }

        private static bool ContainsLeave(List<DialogueCall> actions)
        {
            if (actions == null) return false;
            foreach (var call in actions)
                if (call != null && string.Equals(call.func, "Leave", System.StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}

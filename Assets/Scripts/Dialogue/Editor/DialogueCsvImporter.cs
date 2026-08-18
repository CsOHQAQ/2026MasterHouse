using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// 对话导表（2026-08-14 重构）：Assets/Configs/对话组表.csv + 对话内容表.csv → DialogueTable.asset **整表重建**。
    ///
    /// 唯一数据源是 Excel/对话表.xlsx；CSV 由 Tools/导表/export_dialogue.py 生成，本类只负责 CSV → SO。
    /// **没有反向导出**：SO 是产物不是源，回写会造出第二个家（旧版那套 SO → CSV 已随本次重构删除）。
    ///
    /// 两张表的列（列名即契约，与导出脚本一一对应；「行号」由导出脚本追加，用于把报错指回 Excel）：
    ///   对话组表.csv ：对话组ID, 种族, 需求ID, 所属对话池, 进入条件, 备注, 行号
    ///   对话内容表.csv：对话组ID, 说话人, 立绘ID, 步骤, 选项, 句序, 类型, 文本, 条件, 行号
    ///
    /// 解析要点：
    ///   · 种族列**只接受单个 raceId**——见 ResolveRace 的注释，这是 2026-08-14 立绘 ID 化的硬前提
    ///   · 「立绘ID」对着 PortraitTable 校验；留空 = 沿用上一句（GVN 惯例，由 DialogueManager 在播放期承接）
    ///   · 「条件」「文本（Action 行）」写函数调用串，`;` 分隔多条；语法由 DialogueCallParser 解析，
    ///     函数名与参数个数对着 DialogueFuncs 的注册表校验——写错当场报出 Excel 行号
    ///   · 步骤/选项/句序三列全是数字，**行序没有语义**，可以在 Excel 里任意排序
    /// </summary>
    public static class DialogueCsvImporter
    {
        public const string GroupCsvPath = "Assets/Configs/对话组表.csv";
        public const string ContentCsvPath = "Assets/Configs/对话内容表.csv";
        public const string TableAssetPath = "Assets/Resources/OutGameUI/DialogueTable.asset";

        private const string AutoImportPrefKey = "MasterHouse.Dialogue.AutoImport";

        // ─── 菜单 ───────────────────────────────────────────────────────────

        [MenuItem("MasterHouse/对话系统/从 CSV 导入对话")]
        public static void ImportFromCsvMenu()
        {
            var report = Import();
            report.Dump();
            if (report.Errors == 0)
                EditorUtility.DisplayDialog("导表完成",
                    $"对话组 {report.GroupCount} 组 · 池挂载 {report.EntryCount} 条" +
                    (report.Warnings > 0 ? $"\n\n{report.Warnings} 条警告，详见 Console。" : "\n\n没有问题。"), "好");
            else
                EditorUtility.DisplayDialog("导表失败",
                    $"{report.Errors} 个错误，对话表**没有**被改写。\n\n" +
                    "错误明细在 Console 里，每条都带 Excel 的 sheet 与行号。", "好");
        }

        [MenuItem("MasterHouse/对话系统/自动导表（CSV 变化时）", true)]
        private static bool ToggleAutoImportValidate()
        {
            Menu.SetChecked("MasterHouse/对话系统/自动导表（CSV 变化时）", AutoImport);
            return true;
        }

        [MenuItem("MasterHouse/对话系统/自动导表（CSV 变化时）")]
        private static void ToggleAutoImport() => AutoImport = !AutoImport;

        /// <summary>对话模块两张表（对话 + 立绘）共用这一个自动导表开关，菜单也只有一处。</summary>
        internal static bool AutoImport
        {
            get => EditorPrefs.GetBool(AutoImportPrefKey, true);
            set => EditorPrefs.SetBool(AutoImportPrefKey, value);
        }

        private sealed class CsvPostprocessor : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(string[] imported, string[] deleted,
                string[] moved, string[] movedFrom)
            {
                if (!AutoImport) return;

                // 同一批里也有立绘表时**让路**：对话表要拿立绘表校验立绘ID，
                // 两个 postprocessor 谁先跑不保证；先跑对话就会报出一屏「立绘ID 不存在」的假错误。
                // 这种情况统一由 PortraitCsvImporter 导完立绘后串调对话导表。
                foreach (var path in imported)
                    if (path == PortraitCsvImporter.CsvPath)
                        return;

                foreach (var path in imported)
                    if (path == GroupCsvPath || path == ContentCsvPath)
                    {
                        // 延后一帧：导入回调里改资产会与当前这轮导入打架
                        EditorApplication.delayCall += () => Import().Dump();
                        return;
                    }
            }
        }

        // ─── 导入主流程 ─────────────────────────────────────────────────────

        /// <summary>
        /// 整表重建。**先全部解析并校验，一个错误都没有才落盘**——半张表比没有表更难查。
        /// </summary>
        public static DialogueReport Import()
        {
            var report = new DialogueReport();
            try
            {
                var groups = ParseContent(report);
                var entries = ParseGroups(report, groups);
                CrossValidate(report, groups, entries);

                report.GroupCount = groups.Count;
                report.EntryCount = entries.Count;
                if (report.Errors > 0) return report;

                var table = LoadOrCreateTable();
                table.groups = groups;
                table.entries = entries;
                table.InvalidateIndex();
                EditorUtility.SetDirty(table);
                AssetDatabase.SaveAssets();
                report.Applied = true;
            }
            catch (Exception e)
            {
                report.Error("导表", 0, $"导入过程抛异常：{e.Message}\n{e.StackTrace}");
            }
            return report;
        }

        private static DialogueTable LoadOrCreateTable()
        {
            var table = AssetDatabase.LoadAssetAtPath<DialogueTable>(TableAssetPath);
            if (table != null) return table;

            var dir = Path.GetDirectoryName(TableAssetPath)?.Replace('\\', '/');
            EnsureFolder(dir);
            table = ScriptableObject.CreateInstance<DialogueTable>();
            AssetDatabase.CreateAsset(table, TableAssetPath);
            Debug.Log("[对话导表] 新建对话整表：" + TableAssetPath);
            return table;
        }

        // ─── 第二页：对话内容 ───────────────────────────────────────────────

        private static List<DialogueGroup> ParseContent(DialogueReport report)
        {
            var result = new List<DialogueGroup>();
            var rows = ReadCsv(ContentCsvPath, report, "对话内容");
            if (rows.Count < 2) return result;

            var head = rows[0];
            int cId = Col(head, "对话组ID"), cSpeaker = Col(head, "说话人"), cPortrait = Col(head, "立绘ID");
            int cStep = Col(head, "步骤"), cOption = Col(head, "选项"), cSub = Col(head, "句序");
            int cKind = Col(head, "类型"), cText = Col(head, "文本"), cCond = Col(head, "条件");
            int cRow = Col(head, "行号");
            if (cId < 0 || cStep < 0 || cKind < 0)
            {
                report.Error("对话内容", 1, "缺少必需列（对话组ID / 步骤 / 类型）；请用最新的 Excel 模板重导");
                return result;
            }
            if (cPortrait < 0)
            {
                report.Error("对话内容", 1,
                    "缺少「立绘ID」列——表情枚举已于 2026-08-14 退役（美术要求的差分归不了类），" +
                    "请用最新的 Excel 模板重导；这一列填 Excel/立绘表.xlsx 里的立绘ID");
                return result;
            }

            // 立绘表可能还没建（首次引入本功能时）：那就只跳过 ID 存在性校验，其余照常导，
            // 免得「先有鸡还是先有蛋」把两张表一起卡死
            var portraits = PortraitCsvImporter.LoadTable();
            if (portraits == null)
                report.Warn("对话内容", 1,
                    $"找不到立绘索引表（{PortraitCsvImporter.TableAssetPath}），本次跳过「立绘ID 是否存在」的校验；" +
                    "请先编辑 Excel/立绘表.xlsx 并跑一次导表");

            // 先按 (组, 步骤, 选项, 句序) 收拢成三层结构，再按数字排序 —— 行序完全不参与解析
            var byGroup = new SortedDictionary<int, SortedDictionary<int, StepBucket>>();
            var firstRowOf = new Dictionary<int, int>();

            for (var i = 1; i < rows.Count; i++)
            {
                var row = rows[i];
                var excelRow = ParseInt(Get(row, cRow), i + 1);
                var rawId = Get(row, cId);
                if (string.IsNullOrWhiteSpace(rawId)) continue;
                if (!int.TryParse(rawId.Trim(), out var groupId))
                {
                    report.Error("对话内容", excelRow, $"「对话组ID」不是整数：{rawId}");
                    continue;
                }
                if (!int.TryParse(Get(row, cStep).Trim(), out var stepNo))
                {
                    report.Error("对话内容", excelRow, $"组 {groupId} 的「步骤」不是整数：{Get(row, cStep)}");
                    continue;
                }

                var optionNo = ParseOptional(Get(row, cOption));
                var subNo = ParseOptional(Get(row, cSub));
                var kindRaw = Get(row, cKind).Trim();
                if (!TryParseKind(kindRaw, out var kind))
                {
                    report.Error("对话内容", excelRow, $"组 {groupId} 第 {stepNo} 步的「类型」无法识别：{kindRaw}（只能是 Line / Action / Branch）");
                    continue;
                }

                if (!byGroup.TryGetValue(groupId, out var steps))
                {
                    steps = new SortedDictionary<int, StepBucket>();
                    byGroup[groupId] = steps;
                    firstRowOf[groupId] = excelRow;
                }
                if (!steps.TryGetValue(stepNo, out var bucket))
                {
                    bucket = new StepBucket();
                    steps[stepNo] = bucket;
                }

                var line = new DialogueLine { text = Get(row, cText) };
                if (!DialogueSpeakerText.TryParse(Get(row, cSpeaker), out var speaker))
                    report.Warn("对话内容", excelRow, $"「说话人」无法识别：{Get(row, cSpeaker)}，按 visitor 处理");
                line.speaker = speaker;

                // 立绘ID：留空 = 沿用上一句（在播放期承接，见 DialogueManager.CurrentPortraitId）。
                // 填了就必须真的存在——查不到只会表现成「立绘位空着」，运行时看不出是拼错还是没配。
                var portraitId = Get(row, cPortrait).Trim();
                if (portraitId.Length > 0)
                {
                    if (portraits != null && !portraits.Contains(portraitId))
                        report.Error("对话内容", excelRow,
                            $"「立绘ID」在立绘表里找不到：{portraitId}（可选值见 Excel/立绘表.xlsx，或对话表的「参考」页）");
                    if (kind != EDialogueStepKind.Line)
                        report.Warn("对话内容", excelRow,
                            $"这一行是 {kindRaw} 却填了「立绘ID」{portraitId}——只有台词行会换立绘，本格会被忽略");
                    // 玩家句照样显示立绘（2026-08-19 羊族定为玩家）：老板是有脸的角色，
                    // 这一条从「只有访客句显示立绘」放宽成「只有旁白不显示」。
                    else if (speaker == EDialogueSpeaker.Narration)
                        report.Warn("对话内容", excelRow,
                            $"说话人是旁白却填了「立绘ID」{portraitId}——" +
                            "旁白是环境描写、没有说话的人，不显示立绘，本格会被忽略");
                }
                line.portraitId = portraitId;

                // 「文本」列**只有 Action 行**才是调用串；Line 行是台词、Branch 行是选项文字，
                // 拿去当函数名解析会把每一句台词都报成「未知函数」
                var actions = kind == EDialogueStepKind.Action
                    ? ParseCalls(Get(row, cText), report, "对话内容", excelRow, false)
                    : new List<DialogueCall>();
                var conditions = ParseCalls(Get(row, cCond), report, "对话内容", excelRow, true);

                // ── 三种归位 ──
                if (optionNo < 0)
                {
                    // 主线行（选项/句序都空）
                    if (kind == EDialogueStepKind.Branch)
                    {
                        report.Error("对话内容", excelRow,
                            $"组 {groupId} 第 {stepNo} 步是 Branch 却没填「选项」列——每个选项各占一行，选项号从 1 开始");
                        continue;
                    }
                    if (subNo >= 0)
                    {
                        // 只填句序不填选项：多半是漏填了选项号。按主线行处理会把「句序」静默丢掉，
                        // 那是内容凭空消失——宁可报错
                        report.Error("对话内容", excelRow,
                            $"组 {groupId} 第 {stepNo} 步填了「句序」却没填「选项」——子句必须挂在某个选项下");
                        continue;
                    }
                    if (bucket.Main != null)
                    {
                        report.Error("对话内容", excelRow, $"组 {groupId} 的第 {stepNo} 步出现了两行主线内容（步骤号重复）");
                        continue;
                    }
                    bucket.Main = new DialogueStep { kind = kind, line = line, actions = actions, sourceRow = excelRow };
                    if (kind == EDialogueStepKind.Action && actions.Count == 0)
                        report.Error("对话内容", excelRow, $"组 {groupId} 第 {stepNo} 步是 Action，「文本」列必须写事件调用（如 Accept）");
                }
                else if (subNo < 0)
                {
                    // 选项行（填了选项、没填句序）
                    if (kind != EDialogueStepKind.Branch)
                    {
                        report.Error("对话内容", excelRow,
                            $"组 {groupId} 第 {stepNo} 步第 {optionNo} 项填了「选项」却不是 Branch 类型" +
                            "（选项本身写 Branch，选项后面的台词/事件要另填「句序」）");
                        continue;
                    }
                    // 子句行可能排在选项行前面（三列序号让行序不重要），那时已经建过占位桶——
                    // 填进去而不是当成重复
                    if (!bucket.Options.TryGetValue(optionNo, out var slot))
                    {
                        slot = new OptionBucket();
                        bucket.Options[optionNo] = slot;
                    }
                    else if (slot.Option != null)
                    {
                        report.Error("对话内容", excelRow, $"组 {groupId} 第 {stepNo} 步的选项号 {optionNo} 重复");
                        continue;
                    }
                    slot.Option = new DialogueOption
                    {
                        text = Get(row, cText), conditions = conditions, sourceRow = excelRow,
                    };
                    slot.Row = excelRow;
                }
                else
                {
                    // 子句行（选项 + 句序都填了）
                    if (kind == EDialogueStepKind.Branch)
                    {
                        report.Error("对话内容", excelRow,
                            $"组 {groupId} 第 {stepNo} 步第 {optionNo} 项的子句里出现了 Branch——**不支持嵌套分支**");
                        continue;
                    }
                    if (!bucket.Options.TryGetValue(optionNo, out var optionBucket))
                    {
                        optionBucket = new OptionBucket { Row = excelRow }; // 选项行可能排在子句后面，先占位
                        bucket.Options[optionNo] = optionBucket;
                    }
                    if (optionBucket.Subs.ContainsKey(subNo))
                    {
                        report.Error("对话内容", excelRow, $"组 {groupId} 第 {stepNo} 步第 {optionNo} 项的句序 {subNo} 重复");
                        continue;
                    }
                    if (kind == EDialogueStepKind.Action && actions.Count == 0)
                        report.Error("对话内容", excelRow,
                            $"组 {groupId} 第 {stepNo} 步第 {optionNo} 项第 {subNo} 句是 Action，「文本」列必须写事件调用");
                    optionBucket.Subs[subNo] = new DialogueSubStep
                    {
                        kind = kind, line = line, actions = actions, sourceRow = excelRow,
                    };
                }
            }

            // ── 组装 ──
            foreach (var pair in byGroup)
            {
                var group = new DialogueGroup { id = pair.Key, sourceRow = firstRowOf[pair.Key] };
                foreach (var stepPair in pair.Value)
                {
                    var bucket = stepPair.Value;
                    if (bucket.Options.Count > 0)
                    {
                        // 分支步的行号取第一个选项那一行——报错时策划跳过去正好落在这个分支上
                        var step = new DialogueStep
                        {
                            kind = EDialogueStepKind.Branch,
                            sourceRow = FirstRowOf(bucket.Options),
                        };
                        foreach (var optionPair in bucket.Options)
                        {
                            var optionBucket = optionPair.Value;
                            if (optionBucket.Option == null)
                            {
                                report.Error("对话内容", optionBucket.Row,
                                    $"组 {group.id} 第 {stepPair.Key} 步第 {optionPair.Key} 项只有子句、没有选项本身那一行" +
                                    "（选项行 = 填「选项」不填「句序」、类型 Branch）");
                                continue;
                            }
                            foreach (var sub in optionBucket.Subs) optionBucket.Option.steps.Add(sub.Value);
                            step.options.Add(optionBucket.Option);
                        }
                        if (bucket.Main != null)
                            report.Error("对话内容", group.sourceRow,
                                $"组 {group.id} 第 {stepPair.Key} 步同时有主线内容和分支选项——一个步骤号只能是其中一种");
                        group.steps.Add(step);
                    }
                    else if (bucket.Main != null)
                    {
                        group.steps.Add(bucket.Main);
                    }
                }
                result.Add(group);
            }
            return result;
        }

        private sealed class StepBucket
        {
            public DialogueStep Main;
            public readonly SortedDictionary<int, OptionBucket> Options = new SortedDictionary<int, OptionBucket>();
        }

        private sealed class OptionBucket
        {
            public DialogueOption Option;
            public int Row;
            public readonly SortedDictionary<int, DialogueSubStep> Subs = new SortedDictionary<int, DialogueSubStep>();
        }

        /// <summary>分支步的代表行号 = 选项号最小的那一项所在的 Excel 行。</summary>
        private static int FirstRowOf(SortedDictionary<int, OptionBucket> options)
        {
            foreach (var pair in options) return pair.Value.Row; // SortedDictionary，第一项就是最小选项号
            return 0;
        }

        // ─── 第一页：对话组 → 池 ────────────────────────────────────────────

        private static List<DialoguePoolEntry> ParseGroups(DialogueReport report, List<DialogueGroup> groups)
        {
            var result = new List<DialoguePoolEntry>();
            var rows = ReadCsv(GroupCsvPath, report, "对话组");
            if (rows.Count < 2) return result;

            var head = rows[0];
            int cId = Col(head, "对话组ID"), cRace = Col(head, "种族"), cNeed = Col(head, "需求ID");
            int cCategory = Col(head, "所属对话池"), cCond = Col(head, "进入条件"), cRow = Col(head, "行号");
            if (cId < 0 || cRace < 0 || cCategory < 0)
            {
                report.Error("对话组", 1, "缺少必需列（对话组ID / 种族 / 所属对话池）；请用最新的 Excel 模板重导");
                return result;
            }

            var allRaceIds = AllRaceIds();
            if (allRaceIds.Count == 0)
                report.Warn("对话组", 1, "工程里一个 VisitorRaceDef 都没有，「种族」列填什么都校验不了");
            var knownNeeds = AllNeedNames();

            for (var i = 1; i < rows.Count; i++)
            {
                var row = rows[i];
                var excelRow = ParseInt(Get(row, cRow), i + 1);
                var rawId = Get(row, cId);
                if (string.IsNullOrWhiteSpace(rawId)) continue;
                if (!int.TryParse(rawId.Trim(), out var groupId))
                {
                    report.Error("对话组", excelRow, $"「对话组ID」不是整数：{rawId}");
                    continue;
                }

                var categoryRaw = Get(row, cCategory);
                if (!DialogueCategoryText.TryParse(categoryRaw, out var category))
                {
                    report.Error("对话组", excelRow,
                        $"「所属对话池」无法识别：{categoryRaw}（可选：{string.Join(" / ", DialogueCategoryText.Keys)}）");
                    continue;
                }

                var needId = Get(row, cNeed).Trim();
                if (needId.Length > 0)
                {
                    if (!DialogueCategoryText.AllowsNeedId(category))
                    {
                        report.Error("对话组", excelRow,
                            $"分类「{DialogueCategoryText.NameOf(category)}」不该填需求ID" +
                            "（需求还没透露 / 已经办完了）；请清空这一格");
                        continue;
                    }
                    if (knownNeeds.Count > 0 && !knownNeeds.Contains(needId))
                        report.Error("对话组", excelRow, $"「需求ID」在工程里找不到对应的需求资产：{needId}");
                }
                else if (DialogueCategoryText.RequiresNeedId(category))
                {
                    report.Error("对话组", excelRow,
                        $"分类「{DialogueCategoryText.NameOf(category)}」**必须填需求ID**——一条需求配自己的一套说辞");
                    continue;
                }

                var conditions = ParseCalls(Get(row, cCond), report, "对话组", excelRow, true);

                var raceId = ResolveRace(Get(row, cRace), allRaceIds, report, excelRow);
                if (raceId == null) continue;

                result.Add(new DialoguePoolEntry
                {
                    groupId = groupId,
                    raceId = raceId,
                    needId = needId,
                    category = category,
                    conditions = conditions,
                    sourceRow = excelRow,
                });
            }
            return result;
        }

        /// <summary>
        /// 种族列：**只接受单个 raceId**。解析失败返回 null（调用方跳过该行）。
        ///
        /// 【2026-08-14：`通用` 与 `/` 多选一并作废】原先两者都会展开成多条 DialoguePoolEntry，
        /// 但它们指向的是**同一个 DialogueGroup、同一份 DialogueLine**——这在表情是枚举时没问题
        /// （枚举是种族无关的键，每个访客顶着自己那张脸），立绘改成具体 ID 之后就成了串脸：
        /// 一组写着 `fox_高兴` 的通用台词会让鸦族兔族猬族全都顶着狐狸的脸。
        ///
        /// 于是规则收紧成「一个对话组只属于一个种族」。代价是内容量随种族数线性增长
        /// （通用组要按种族各抄一份），换来的是每个种族本来就该有自己的性格与长相。
        /// </summary>
        private static string ResolveRace(string raw, List<string> all, DialogueReport report, int excelRow)
        {
            var text = (raw ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                report.Error("对话组", excelRow, "「种族」是空的（填一个 raceId）");
                return null;
            }
            if (text == "通用" || string.Equals(text, "all", StringComparison.OrdinalIgnoreCase))
            {
                report.Error("对话组", excelRow,
                    "「种族」不再支持「通用」——一个对话组只能属于一个种族，请给每个种族各写一行" +
                    $"（工程里现有：{string.Join(" / ", all)}）。" +
                    "原因：通用组的多个种族共用同一份台词与同一个立绘ID，会让所有种族顶着同一张脸");
                return null;
            }
            if (text.IndexOfAny(new[] { '/', '、', ',' }) >= 0)
            {
                report.Error("对话组", excelRow,
                    $"「种族」不再支持多选：{text}——一个对话组只能属于一个种族，请拆成多行" +
                    "（理由同「通用」：多个种族共用一份立绘ID 会串脸）");
                return null;
            }
            if (all.Count > 0 && !all.Contains(text))
            {
                report.Error("对话组", excelRow, $"「种族」{text} 在工程里找不到对应的 VisitorRaceDef.raceId");
                return null;
            }
            return text;
        }

        // ─── 跨表校验 ───────────────────────────────────────────────────────

        private static void CrossValidate(DialogueReport report, List<DialogueGroup> groups, List<DialoguePoolEntry> entries)
        {
            var groupIds = new HashSet<int>();
            foreach (var group in groups)
                if (!groupIds.Add(group.id))
                    report.Error("对话内容", group.sourceRow, $"对话组ID {group.id} 重复");

            var referenced = new HashSet<int>();
            // 「一个组只属于一个种族」的另一半：ResolveRace 堵住了单行多选，这里堵住多行分挂。
            // 少了这一条，`10001 fox` 和 `10001 crow` 两行照样能让两个种族共用一份立绘ID。
            var raceOfGroup = new Dictionary<int, string>();
            var rowOfGroup = new Dictionary<int, int>();
            foreach (var entry in entries)
            {
                referenced.Add(entry.groupId);
                if (!groupIds.Contains(entry.groupId))
                    report.Error("对话组", entry.sourceRow, $"引用了第二页里不存在的对话组 {entry.groupId}");

                if (!raceOfGroup.TryGetValue(entry.groupId, out var firstRace))
                {
                    raceOfGroup[entry.groupId] = entry.raceId;
                    rowOfGroup[entry.groupId] = entry.sourceRow;
                }
                else if (firstRace != entry.raceId)
                {
                    report.Error("对话组", entry.sourceRow,
                        $"对话组 {entry.groupId} 被挂给了两个种族（第 {rowOfGroup[entry.groupId]} 行是 {firstRace}，" +
                        $"这一行是 {entry.raceId}）——一个组只能属于一个种族，" +
                        "两个种族共用同一份台词就会共用同一个立绘ID。请把这一组复制成两组、各用各的 ID");
                }
            }
            foreach (var group in groups)
                if (!referenced.Contains(group.id))
                    report.Warn("对话内容", group.sourceRow,
                        $"对话组 {group.id} 没有被第一页挂到任何池上，永远抽不到");

            // 内容层校验（分支必须有无条件选项、闲聊组的形态、奖励事件的位置）交给校验器，
            // 这样「导表」与「随时校验全部资产」共用同一份规则
            DialogueAssetValidator.ValidateContent(groups, entries, report);
        }

        // ─── 调用串解析 ─────────────────────────────────────────────────────

        /// <summary>解析一格里的调用串（`;` 分隔）。isCondition 决定对着哪张注册表校验。</summary>
        private static List<DialogueCall> ParseCalls(string raw, DialogueReport report, string sheet, int excelRow,
            bool isCondition)
        {
            var result = new List<DialogueCall>();
            foreach (var piece in DialogueCallParser.SplitCalls(raw))
            {
                if (!DialogueCallParser.TryParse(piece, out var call, out var error))
                {
                    report.Error(sheet, excelRow, error);
                    continue;
                }
                if (!ValidateCall(call, isCondition, out var reason))
                {
                    report.Error(sheet, excelRow, reason);
                    continue;
                }
                result.Add(call);
            }
            return result;
        }

        /// <summary>对着 DialogueFuncs 的注册表校验函数名与参数个数（导表期就拦，不留到运行时）。</summary>
        public static bool ValidateCall(DialogueCall call, bool isCondition, out string reason)
        {
            reason = null;
            if (isCondition)
            {
                if (!DialogueFuncs.Conditions.TryGetValue(call.func, out var def))
                {
                    reason = $"未知的条件函数「{call.func}」；可用：{string.Join(" / ", SortedKeys(DialogueFuncs.Conditions.Keys))}";
                    return false;
                }
                if (call.args.Count < def.ArgCount)
                {
                    reason = $"条件 {call.func} 需要 {def.ArgCount} 个参数（{def.ArgsHint}），实际给了 {call.args.Count} 个";
                    return false;
                }
                return true;
            }

            if (!DialogueFuncs.Actions.TryGetValue(call.func, out var actionDef))
            {
                reason = $"未知的事件函数「{call.func}」；可用：{string.Join(" / ", SortedKeys(DialogueFuncs.Actions.Keys))}";
                return false;
            }
            // 事件的参数允许**少给**（如 CompleteNeed 不填档位 = 完美），但多给一定是写错了。
            // 注意别写成 Math.Max(1, ArgCount)——那会让零参事件的 `Accept(乱写)` 蒙混过关。
            if (call.args.Count > actionDef.ArgCount)
            {
                reason = actionDef.ArgCount == 0
                    ? $"事件 {call.func} 不接受参数，实际给了 {call.args.Count} 个"
                    : $"事件 {call.func} 最多接受 {actionDef.ArgCount} 个参数（{actionDef.ArgsHint}），实际给了 {call.args.Count} 个";
                return false;
            }
            return true;
        }

        private static List<string> SortedKeys(IEnumerable<string> keys)
        {
            var list = new List<string>(keys);
            list.Sort(StringComparer.Ordinal); // 报错文案要稳定，字典枚举顺序不稳（§11.2）
            return list;
        }

        // ─── 工程侧查询 ─────────────────────────────────────────────────────

        /// <summary>工程里全部 VisitorRaceDef 的 raceId（按 id 排序，稳定）。</summary>
        public static List<string> AllRaceIds()
        {
            var result = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:VisitorRaceDef"))
            {
                var race = AssetDatabase.LoadAssetAtPath<VisitorRaceDef>(AssetDatabase.GUIDToAssetPath(guid));
                if (race != null && !string.IsNullOrEmpty(race.raceId) && !result.Contains(race.raceId))
                    result.Add(race.raceId);
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        /// <summary>工程里全部 NeedDef 的资产名（第一页「需求ID」列填的就是它）。</summary>
        public static HashSet<string> AllNeedNames()
        {
            var result = new HashSet<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:NeedDef"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var name = Path.GetFileNameWithoutExtension(path);
                if (!string.IsNullOrEmpty(name)) result.Add(name);
            }
            return result;
        }

        // ─── CSV 读取 ───────────────────────────────────────────────────────

        /// <summary>读 CSV 成行数组；找不到文件时报错并返回空表。立绘导表也共用本方法。</summary>
        internal static List<string[]> ReadCsv(string path, DialogueReport report, string sheet,
            string missingMessage = null)
        {
            var result = new List<string[]>();
            var full = Path.GetFullPath(path);
            if (!File.Exists(full))
            {
                report.Error(sheet, 0, missingMessage ??
                    $"找不到 {path}——请编辑 Excel/对话表.xlsx 后运行 Tools/导表/export_config.bat");
                return result;
            }

            var text = File.ReadAllText(full, Encoding.UTF8);
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1); // 导出脚本写的是 UTF-8 BOM

            var lines = new List<string>();
            var current = new StringBuilder();
            var inQuote = false;
            foreach (var c in text)
            {
                if (c == '"') { inQuote = !inQuote; current.Append(c); }
                else if (!inQuote && c == '\n') { lines.Add(current.ToString().TrimEnd('\r')); current.Clear(); }
                else current.Append(c);
            }
            if (current.Length > 0) lines.Add(current.ToString());

            foreach (var line in lines)
                if (!string.IsNullOrWhiteSpace(line))
                    result.Add(ParseCsvLine(line));
            return result;
        }

        private static string[] ParseCsvLine(string line)
        {
            var fields = new List<string>();
            var i = 0;
            while (i <= line.Length)
            {
                if (i == line.Length) { fields.Add(string.Empty); break; }
                if (line[i] == '"')
                {
                    i++;
                    var sb = new StringBuilder();
                    while (i < line.Length)
                    {
                        if (line[i] == '"')
                        {
                            i++;
                            if (i < line.Length && line[i] == '"') { sb.Append('"'); i++; }
                            else break;
                        }
                        else { sb.Append(line[i]); i++; }
                    }
                    fields.Add(sb.ToString());
                    if (i < line.Length && line[i] == ',') i++;
                }
                else
                {
                    var comma = line.IndexOf(',', i);
                    if (comma < 0) { fields.Add(line.Substring(i).Trim()); break; }
                    fields.Add(line.Substring(i, comma - i).Trim());
                    i = comma + 1;
                }
            }
            return fields.ToArray();
        }

        // ─── 小工具 ─────────────────────────────────────────────────────────

        /// <summary>按列名取下标；没有这一列返回 -1。立绘导表也共用。</summary>
        internal static int Col(string[] head, string name) => Array.IndexOf(head, name);

        private static string Get(string[] row, int col) =>
            col >= 0 && col < row.Length ? row[col] : string.Empty;

        /// <summary>按下标取格；越界返回空串。立绘导表也共用。</summary>
        internal static string Cell(string[] row, int col) => Get(row, col);

        /// <summary>按下标取整数格；解析不了返回 fallback。立绘导表也共用。</summary>
        internal static int CellInt(string[] row, int col, int fallback) => ParseInt(Get(row, col), fallback);

        private static int ParseInt(string raw, int fallback) =>
            int.TryParse((raw ?? string.Empty).Trim(), out var value) ? value : fallback;

        /// <summary>可选数字列：空 = -1。</summary>
        private static int ParseOptional(string raw) =>
            int.TryParse((raw ?? string.Empty).Trim(), out var value) ? value : -1;

        private static bool TryParseKind(string raw, out EDialogueStepKind kind)
        {
            kind = EDialogueStepKind.Line;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            switch (raw.Trim().ToLowerInvariant())
            {
                case "line": case "台词": kind = EDialogueStepKind.Line; return true;
                case "action": case "事件": kind = EDialogueStepKind.Action; return true;
                case "branch": case "选项": case "分支": kind = EDialogueStepKind.Branch; return true;
                default: return false;
            }
        }

        /// <summary>递归建目录（AssetDatabase 口径）。立绘导表也共用。</summary>
        internal static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            var parent = path.Substring(0, path.LastIndexOf('/'));
            var leaf = path.Substring(path.LastIndexOf('/') + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }

    /// <summary>
    /// 导表 / 校验的结果收集器。**每条消息都带 Excel 的 sheet 名与行号**——
    /// 策划拿到「对话内容 第 37 行」能直接跳过去改，比「对话组 Group_xxx 第 3 步」有用得多
    /// （编辑器退役之后，SO 里的位置对策划已经没有意义了）。
    /// </summary>
    public sealed class DialogueReport
    {
        public readonly List<string> ErrorMessages = new List<string>();
        public readonly List<string> WarningMessages = new List<string>();

        /// <summary>报错前缀里的工作簿名——对话表与立绘表共用本类，各报各的文件。</summary>
        private readonly string workbook;

        public DialogueReport(string workbookName = "对话表.xlsx") => workbook = workbookName;

        public int GroupCount;
        public int EntryCount;

        /// <summary>是否真的改写了资产（有错误时不落盘）。</summary>
        public bool Applied;

        public int Errors => ErrorMessages.Count;
        public int Warnings => WarningMessages.Count;

        public void Error(string sheet, int excelRow, string message) =>
            ErrorMessages.Add(Format(sheet, excelRow, message));

        public void Warn(string sheet, int excelRow, string message) =>
            WarningMessages.Add(Format(sheet, excelRow, message));

        private string Format(string sheet, int excelRow, string message) =>
            excelRow > 0 ? $"{workbook}[{sheet}] 第 {excelRow} 行：{message}" : $"{workbook}[{sheet}]：{message}";

        /// <summary>把结果打进 Console。tag 决定日志前缀（对话导表 / 立绘导表 / 对话校验）。</summary>
        public void Dump(string tag = "对话导表")
        {
            foreach (var message in ErrorMessages) Debug.LogError($"[{tag}] " + message);
            foreach (var message in WarningMessages) Debug.LogWarning($"[{tag}] " + message);
            if (Errors > 0)
                Debug.LogError($"[{tag}] 失败：{Errors} 个错误、{Warnings} 条警告；**{workbook} 对应的资产没有被改写**（半张表比没有表更难查）");
            else if (Applied)
                Debug.Log($"[{tag}] 完成：" +
                          (GroupCount > 0 ? $"{GroupCount} 组对话、{EntryCount} 条池挂载" : $"{EntryCount} 条")
                          + (Warnings > 0 ? $"（{Warnings} 条警告）" : string.Empty));
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// 对话表导入/导出工具。
    /// CSV 格式（对话内容表.csv）：
    ///   对话组ID, 备注, 文件夹, 步骤, 类型, 说话人, 表情, 文本, 动作, 动作参数, 跳转, 跳转目标组, 选项条件
    ///   类型：台词 | 选项 | 事件
    ///   动作：接待|拒绝|完成需求|小游戏|货币|声望|日志
    ///   跳转：结束|继续|跳到组
    ///   选项条件：天数>=N / 货币>=N / 声望>=N / 种族:资产名 / 满意度>=档位 /
    ///             访客状态:枚举名 / 有空房 / 房间有家具 — 多个用;分隔
    /// CSV 格式（对话池表.csv）：
    ///   对话组ID, 文件夹, 种族, 触发分类, 权重, 进入条件
    ///   触发分类：firstMeeting|serviceStart|serviceCheck|rejected|
    ///             doneMismatch|donePlain|doneSatisfied|donePerfect|wanderChat
    /// </summary>
    public static class DialogueCsvImporter
    {
        private const string ContentCsvPath = "Assets/Configs/对话内容表.csv";
        private const string PoolCsvPath    = "Assets/Configs/对话池表.csv";
        private const string GroupBaseDir   = "Assets/GameData/Dialogue";

        private static readonly string[] AllRaces = { "crow", "fox", "hedgehog", "rabbit" };

        private static readonly string[] TriggerNames =
        {
            "firstMeeting", "serviceStart", "serviceCheck", "rejected",
            "doneMismatch", "donePlain", "doneSatisfied", "donePerfect", "wanderChat"
        };

        // ─── Asset Postprocessor ───────────────────────────────────────────

        private sealed class CsvPostprocessor : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(
                string[] importedAssets, string[] deletedAssets,
                string[] movedAssets, string[] movedFromAssetPaths)
            {
                bool needImport = false;
                foreach (var path in importedAssets)
                    if (path == ContentCsvPath || path == PoolCsvPath)
                        needImport = true;

                if (needImport) RunImport();
            }
        }

        // ─── Menu items ────────────────────────────────────────────────────

        [MenuItem("MasterHouse/对话系统/从 CSV 导入对话")]
        public static void ImportFromCsvMenu() => RunImport();

        [MenuItem("MasterHouse/对话系统/导出对话到 CSV（生成 Excel 底稿）")]
        public static void ExportToCsv()
        {
            ExportContentCsv();
            ExportPoolCsv();
            AssetDatabase.Refresh();
            Debug.Log("[对话导表] 导出完成 → 对话内容表.csv + 对话池表.csv");
            EditorUtility.DisplayDialog("导出完成",
                "已写入 Assets/Configs/ 下两张 CSV。\n\n" +
                "可用 Excel 打开编辑后，运行 Tools/导表/export_config.bat 回写。", "好");
        }

        // ─── Import main ───────────────────────────────────────────────────

        private static void RunImport()
        {
            try
            {
                var groups = File.Exists(ContentCsvPath) ? ImportGroupContent() : new Dictionary<string, string>();
                if (File.Exists(PoolCsvPath)) ImportPoolAssignments(groups);
                AssetDatabase.SaveAssets();
                Debug.Log("[对话导表] 导入完成");
            }
            catch (Exception e)
            {
                Debug.LogError($"[对话导表] 导入失败：{e.Message}\n{e.StackTrace}");
            }
        }

        // ─── 对话内容导入 ──────────────────────────────────────────────────

        private static Dictionary<string, string> ImportGroupContent()
        {
            var groupPaths = new Dictionary<string, string>();
            var rows = ReadCsv(ContentCsvPath);
            if (rows.Count < 2) return groupPaths;

            var h = rows[0];
            int iGroupId  = Col(h, "对话组ID");
            int iNote     = Col(h, "备注");
            int iFolder   = Col(h, "文件夹");
            int iStep     = Col(h, "步骤");
            int iKind     = Col(h, "类型");
            int iSpeaker  = Col(h, "说话人");
            int iEmotion  = Col(h, "表情");
            int iText     = Col(h, "文本");
            int iAction   = Col(h, "动作");
            int iActionP  = ColOpt(h, "动作参数");
            int iJump     = ColOpt(h, "跳转");
            int iJumpGrp  = ColOpt(h, "跳转目标组");
            int iCond     = ColOpt(h, "选项条件");

            var groupRows   = new Dictionary<string, List<string[]>>();
            var groupFolder = new Dictionary<string, string>();
            var groupNote   = new Dictionary<string, string>();

            for (int r = 1; r < rows.Count; r++)
            {
                var row = rows[r];
                string id = Get(row, iGroupId);
                if (string.IsNullOrEmpty(id)) continue;

                if (!groupRows.ContainsKey(id))
                {
                    groupRows[id]   = new List<string[]>();
                    groupFolder[id] = Fallback(Get(row, iFolder), "通用");
                    groupNote[id]   = Get(row, iNote);
                }
                else if (!string.IsNullOrEmpty(Get(row, iNote)))
                    groupNote[id] = Get(row, iNote);

                groupRows[id].Add(row);
            }

            foreach (var kv in groupRows)
            {
                string id     = kv.Key;
                string folder = groupFolder[id];
                string note   = groupNote[id];

                string assetDir  = $"{GroupBaseDir}/{folder}";
                string assetPath = $"{assetDir}/{id}.asset";

                if (!AssetDatabase.IsValidFolder(assetDir))
                    AssetDatabase.CreateFolder(
                        assetDir.Substring(0, assetDir.LastIndexOf('/')),
                        assetDir.Substring(assetDir.LastIndexOf('/') + 1));

                var so    = AssetDatabase.LoadAssetAtPath<DialogueGroupDef>(assetPath);
                bool isNew = so == null;
                if (isNew) so = ScriptableObject.CreateInstance<DialogueGroupDef>();

                so.id   = id;
                so.note = note;

                var stepGroups = new SortedDictionary<int, List<string[]>>();
                foreach (var row in kv.Value)
                {
                    if (!int.TryParse(Get(row, iStep), out int sn)) continue;
                    if (!stepGroups.ContainsKey(sn)) stepGroups[sn] = new List<string[]>();
                    stepGroups[sn].Add(row);
                }

                so.steps = new List<DialogueStep>();
                foreach (var sg in stepGroups)
                {
                    string firstKind = Get(sg.Value[0], iKind);

                    if (firstKind == "选项")
                    {
                        var step = new DialogueStep { kind = EDialogueStepKind.Branch };
                        step.options = new List<BranchOption>();
                        foreach (var optRow in sg.Value)
                        {
                            var opt = new BranchOption
                            {
                                text       = Get(optRow, iText),
                                conditions = ParseConditions(Get(optRow, iCond)),
                                actions    = ParseActions(Get(optRow, iAction), Get(optRow, iActionP)),
                                next       = ParseJump(Get(optRow, iJump)),
                                nextGroup  = null,
                            };
                            string jumpGroupId = Get(optRow, iJumpGrp);
                            if (opt.next == EBranchNext.JumpToGroup && !string.IsNullOrEmpty(jumpGroupId))
                            {
                                string jPath = FindGroupPath(jumpGroupId, groupPaths);
                                if (jPath != null)
                                    opt.nextGroup = AssetDatabase.LoadAssetAtPath<DialogueGroupDef>(jPath);
                            }
                            step.options.Add(opt);
                        }
                        so.steps.Add(step);
                    }
                    else if (firstKind == "事件")
                    {
                        var row  = sg.Value[0];
                        var step = new DialogueStep { kind = EDialogueStepKind.Action };
                        step.line    = new DialogueLine();
                        step.options = new List<BranchOption>();
                        step.actions = ParseActions(Get(row, iAction), Get(row, iActionP));
                        so.steps.Add(step);
                    }
                    else // 台词
                    {
                        var row  = sg.Value[0];
                        var step = new DialogueStep { kind = EDialogueStepKind.Line };
                        step.line = new DialogueLine
                        {
                            speaker = ParseSpeaker(Get(row, iSpeaker)),
                            emotion = ParseEmotion(Get(row, iEmotion)),
                            text    = Get(row, iText),
                        };
                        step.options = new List<BranchOption>();
                        step.actions = new List<IGameplayAction>();
                        so.steps.Add(step);
                    }
                }

                if (isNew) AssetDatabase.CreateAsset(so, assetPath);
                else       EditorUtility.SetDirty(so);

                groupPaths[id] = assetPath;
            }

            return groupPaths;
        }

        // ─── 对话池导入 ────────────────────────────────────────────────────

        private static void ImportPoolAssignments(Dictionary<string, string> groupPaths)
        {
            var rows = ReadCsv(PoolCsvPath);
            if (rows.Count < 2) return;

            var h = rows[0];
            int iGroupId  = Col(h, "对话组ID");
            int iFolder   = Col(h, "文件夹");
            int iRace     = Col(h, "种族");
            int iTrigger  = Col(h, "触发分类");
            int iWeight   = Col(h, "权重");
            int iCond     = ColOpt(h, "进入条件");

            var assign = new Dictionary<string, Dictionary<string, List<(string id, string folder, int w, string cond)>>>();
            foreach (var race in AllRaces)
                assign[race] = new Dictionary<string, List<(string, string, int, string)>>();

            for (int r = 1; r < rows.Count; r++)
            {
                var row    = rows[r];
                string gid = Get(row, iGroupId);
                if (string.IsNullOrEmpty(gid)) continue;

                string folder  = Fallback(Get(row, iFolder), "通用");
                string race    = Get(row, iRace);
                string trigger = Get(row, iTrigger);
                int.TryParse(Get(row, iWeight), out int weight);
                if (weight <= 0) weight = 1;
                string cond = Get(row, iCond);

                IEnumerable<string> targets = race == "通用"
                    ? (IEnumerable<string>)AllRaces : new[] { race };

                foreach (var r2 in targets)
                {
                    if (!assign.ContainsKey(r2)) continue;
                    if (!assign[r2].ContainsKey(trigger))
                        assign[r2][trigger] = new List<(string, string, int, string)>();
                    assign[r2][trigger].Add((gid, folder, weight, cond));
                }
            }

            foreach (var race in AllRaces)
            {
                string poolPath = $"{GroupBaseDir}/Pool_{race}.asset";
                var pool = AssetDatabase.LoadAssetAtPath<DialoguePoolDef>(poolPath);
                if (pool == null) { Debug.LogWarning($"[对话导表] Pool 不存在：{poolPath}"); continue; }

                var ra = assign[race];
                foreach (var trigger in TriggerNames)
                    ApplyTrigger(pool, trigger, ra, groupPaths);

                EditorUtility.SetDirty(pool);
            }
        }

        private static void ApplyTrigger(
            DialoguePoolDef pool, string trigger,
            Dictionary<string, List<(string id, string folder, int w, string cond)>> raceAssign,
            Dictionary<string, string> groupPaths)
        {
            if (!raceAssign.TryGetValue(trigger, out var entries)) return;

            var list = new List<DialogueGroupEntry>();
            foreach (var (gid, folder, w, cond) in entries)
            {
                string path = FindGroupPath(gid, groupPaths) ?? $"{GroupBaseDir}/{folder}/{gid}.asset";
                var grp = AssetDatabase.LoadAssetAtPath<DialogueGroupDef>(path);
                if (grp == null) { Debug.LogWarning($"[对话导表] 找不到对话组：{path}"); continue; }

                list.Add(new DialogueGroupEntry
                {
                    group      = grp,
                    weight     = w,
                    conditions = ParseConditions(cond),
                });
            }

            SetTriggerList(pool, trigger, list);
        }

        // ─── 导出（SO → CSV）──────────────────────────────────────────────

        private static void ExportContentCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("对话组ID,备注,文件夹,步骤,类型,说话人,表情,文本,动作,动作参数,跳转,跳转目标组,选项条件");

            var guids = AssetDatabase.FindAssets($"t:{nameof(DialogueGroupDef)}", new[] { GroupBaseDir });
            foreach (var guid in guids)
            {
                string path  = AssetDatabase.GUIDToAssetPath(guid);
                var group    = AssetDatabase.LoadAssetAtPath<DialogueGroupDef>(path);
                if (group == null) continue;

                string rel    = path.Replace($"{GroupBaseDir}/", "");
                string folder = rel.Contains("/") ? rel.Substring(0, rel.LastIndexOf('/')) : "通用";
                bool first    = true;

                for (int i = 0; i < group.steps.Count; i++)
                {
                    var step = group.steps[i];

                    if (step.kind == EDialogueStepKind.Line)
                    {
                        sb.AppendLine(CsvRow(
                            group.name, first ? group.note : "", first ? folder : "",
                            (i + 1).ToString(), "台词",
                            SpeakerStr(step.line.speaker),
                            step.line.speaker == EDialogueSpeaker.Visitor ? EmotionStr(step.line.emotion) : "",
                            step.line.text, "", "", "", "", ""));
                        first = false;
                    }
                    else if (step.kind == EDialogueStepKind.Action)
                    {
                        ActionToStr(step.actions, out string act, out string par);
                        sb.AppendLine(CsvRow(
                            group.name, first ? group.note : "", first ? folder : "",
                            (i + 1).ToString(), "事件",
                            "", "", "", act, par, "", "", ""));
                        first = false;
                    }
                    else if (step.kind == EDialogueStepKind.Branch)
                    {
                        foreach (var opt in step.options)
                        {
                            ActionToStr(opt.actions, out string act, out string par);
                            string jump = JumpStr(opt.next);
                            string jgrp = opt.next == EBranchNext.JumpToGroup && opt.nextGroup != null
                                ? opt.nextGroup.name : "";
                            string cond = ConditionsToStr(opt.conditions);
                            sb.AppendLine(CsvRow(
                                group.name, first ? group.note : "", first ? folder : "",
                                (i + 1).ToString(), "选项",
                                "", "", opt.text, act, par, jump, jgrp, cond));
                            first = false;
                        }
                    }
                }

                if (first)
                    sb.AppendLine(CsvRow(group.name, group.note, folder, "", "", "", "", "", "", "", "", "", ""));
            }

            Directory.CreateDirectory("Assets/Configs");
            File.WriteAllText(ContentCsvPath, sb.ToString(), Encoding.UTF8);
        }

        private static void ExportPoolCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("对话组ID,文件夹,种族,触发分类,权重,进入条件");

            foreach (var race in AllRaces)
            {
                string poolPath = $"{GroupBaseDir}/Pool_{race}.asset";
                var pool = AssetDatabase.LoadAssetAtPath<DialoguePoolDef>(poolPath);
                if (pool == null) continue;

                foreach (var trigger in TriggerNames)
                {
                    foreach (var entry in GetTriggerList(pool, trigger))
                    {
                        if (entry.group == null) continue;
                        string p      = AssetDatabase.GetAssetPath(entry.group);
                        string rel    = p.Replace($"{GroupBaseDir}/", "");
                        string folder = rel.Contains("/") ? rel.Substring(0, rel.LastIndexOf('/')) : "通用";
                        string cond   = ConditionsToStr(entry.conditions);
                        sb.AppendLine(CsvRow(entry.group.name, folder, race, trigger,
                            entry.weight.ToString(), cond));
                    }
                }
            }

            Directory.CreateDirectory("Assets/Configs");
            File.WriteAllText(PoolCsvPath, sb.ToString(), Encoding.UTF8);
        }

        // ─── Condition parsing ─────────────────────────────────────────────
        // 格式（;分隔）：
        //   天数>=N  货币>=N  声望>=N
        //   种族:VisitorRaceDef资产名  满意度>=不对味|一般|满意|完美
        //   访客状态:EVisitorState枚举名  有空房  房间有家具

        private static List<IGameplayCondition> ParseConditions(string raw)
        {
            var result = new List<IGameplayCondition>();
            if (string.IsNullOrWhiteSpace(raw)) return result;

            foreach (var part in raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var s = part.Trim();
                if (string.IsNullOrEmpty(s)) continue;

                if (s.StartsWith("天数>="))
                {
                    if (int.TryParse(s.Substring(4), out int v))
                        result.Add(new DayAtLeastCondition { day = v });
                }
                else if (s.StartsWith("货币>="))
                {
                    if (int.TryParse(s.Substring(4), out int v))
                        result.Add(new CurrencyAtLeastCondition { amount = v });
                }
                else if (s.StartsWith("声望>="))
                {
                    if (int.TryParse(s.Substring(4), out int v))
                        result.Add(new ReputationAtLeastCondition { amount = v });
                }
                else if (s.StartsWith("种族:"))
                {
                    var raceDef = FindAsset<VisitorRaceDef>(s.Substring(3));
                    if (raceDef != null)
                        result.Add(new VisitorRaceCondition { race = raceDef });
                    else
                        Debug.LogWarning($"[对话导表] 找不到 VisitorRaceDef：{s.Substring(3)}，条件跳过");
                }
                else if (s.StartsWith("满意度>="))
                {
                    result.Add(new SatisfactionAtLeastCondition { satisfaction = ParseSatisfaction(s.Substring(5)) });
                }
                else if (s.StartsWith("访客状态:"))
                {
                    if (Enum.TryParse(s.Substring(5), out EVisitorState st))
                        result.Add(new VisitorStateCondition { state = st });
                    else
                        Debug.LogWarning($"[对话导表] 无法解析 EVisitorState：{s.Substring(5)}，条件跳过");
                }
                else if (s == "有空房")
                {
                    result.Add(new HasFreeRoomCondition());
                }
                else if (s == "房间有家具")
                {
                    result.Add(new RoomHasAnyFurnitureCondition());
                }
            }
            return result;
        }

        private static string ConditionsToStr(List<IGameplayCondition> conds)
        {
            if (conds == null || conds.Count == 0) return "";
            var parts = new List<string>();
            foreach (var c in conds)
            {
                if      (c is DayAtLeastCondition d)           parts.Add($"天数>={d.day}");
                else if (c is CurrencyAtLeastCondition cu)     parts.Add($"货币>={cu.amount}");
                else if (c is ReputationAtLeastCondition r)    parts.Add($"声望>={r.amount}");
                else if (c is VisitorRaceCondition vr)
                    parts.Add(vr.race != null ? $"种族:{vr.race.name}" : "种族:?");
                else if (c is SatisfactionAtLeastCondition s)
                    parts.Add($"满意度>={SatisfactionStr(s.satisfaction)}");
                else if (c is VisitorStateCondition vs)
                    parts.Add($"访客状态:{vs.state}");
                else if (c is HasFreeRoomCondition)
                    parts.Add("有空房");
                else if (c is RoomHasAnyFurnitureCondition)
                    parts.Add("房间有家具");
            }
            return string.Join(";", parts);
        }

        // ─── Action parsing ────────────────────────────────────────────────
        // 动作 + 动作参数：
        //   接待/拒绝 → 无参数
        //   完成需求  → 参数为满意度档位（不对味/一般/满意/完美，默认完美）
        //   小游戏    → 无参数
        //   货币/声望 → ±整数
        //   日志      → 消息文本

        private static List<IGameplayAction> ParseActions(string act, string param)
        {
            var list = new List<IGameplayAction>();
            if (string.IsNullOrEmpty(act)) return list;

            switch (act)
            {
                case "接待":
                    list.Add(new AcceptVisitorAction()); break;
                case "拒绝":
                    list.Add(new RejectVisitorAction()); break;
                case "完成需求":
                    list.Add(new CompleteNeedAction
                    {
                        satisfaction = string.IsNullOrEmpty(param)
                            ? EServeSatisfaction.Perfect
                            : ParseSatisfaction(param)
                    }); break;
                case "小游戏":
                    list.Add(new StartMinigameAction()); break;
                case "货币":
                    int.TryParse(param, out int cv);
                    list.Add(new AddCurrencyAction { amount = cv }); break;
                case "声望":
                    int.TryParse(param, out int rv);
                    list.Add(new AddReputationAction { amount = rv }); break;
                case "日志":
                    list.Add(new LogAction { message = param }); break;
            }
            return list;
        }

        private static void ActionToStr(List<IGameplayAction> actions, out string act, out string par)
        {
            act = ""; par = "";
            if (actions == null || actions.Count == 0) return;

            var a = actions[0];
            if      (a is AcceptVisitorAction)                      act = "接待";
            else if (a is RejectVisitorAction)                      act = "拒绝";
            else if (a is CompleteNeedAction cn)
            {
                act = "完成需求";
                par = cn.satisfaction != EServeSatisfaction.Perfect ? SatisfactionStr(cn.satisfaction) : "";
            }
            else if (a is StartMinigameAction)                      act = "小游戏";
            else if (a is AddCurrencyAction ac)  { act = "货币";   par = ac.amount.ToString(); }
            else if (a is AddReputationAction ar){ act = "声望";   par = ar.amount.ToString(); }
            else if (a is LogAction la)          { act = "日志";   par = la.message; }
        }

        // ─── Helpers ───────────────────────────────────────────────────────

        private static EServeSatisfaction ParseSatisfaction(string s)
        {
            if (s == "一般")   return EServeSatisfaction.Plain;
            if (s == "满意")   return EServeSatisfaction.Satisfied;
            if (s == "完美")   return EServeSatisfaction.Perfect;
            return EServeSatisfaction.Mismatch;
        }

        private static string SatisfactionStr(EServeSatisfaction l)
        {
            if (l == EServeSatisfaction.Plain)      return "一般";
            if (l == EServeSatisfaction.Satisfied)  return "满意";
            if (l == EServeSatisfaction.Perfect)    return "完美";
            return "不对味";
        }

        private static EBranchNext ParseJump(string s)
        {
            if (s == "继续")   return EBranchNext.ContinueGroup;
            if (s == "跳到组") return EBranchNext.JumpToGroup;
            return EBranchNext.End;
        }

        private static string JumpStr(EBranchNext j)
        {
            if (j == EBranchNext.ContinueGroup) return "继续";
            if (j == EBranchNext.JumpToGroup)   return "跳到组";
            return "结束";
        }

        private static EDialogueSpeaker ParseSpeaker(string s)
        {
            if (s == "玩家") return EDialogueSpeaker.Player;
            if (s == "旁白") return EDialogueSpeaker.Narration;
            return EDialogueSpeaker.Visitor;
        }

        private static EDialogueEmotion ParseEmotion(string s)
        {
            if (s == "高兴") return EDialogueEmotion.Happy;
            if (s == "困惑") return EDialogueEmotion.Confused;
            if (s == "失望") return EDialogueEmotion.Sad;
            if (s == "惊讶") return EDialogueEmotion.Surprised;
            return EDialogueEmotion.Calm;
        }

        private static string SpeakerStr(EDialogueSpeaker s)
        {
            if (s == EDialogueSpeaker.Player)    return "玩家";
            if (s == EDialogueSpeaker.Narration) return "旁白";
            return "访客";
        }

        private static string EmotionStr(EDialogueEmotion e)
        {
            if (e == EDialogueEmotion.Happy)     return "高兴";
            if (e == EDialogueEmotion.Confused)  return "困惑";
            if (e == EDialogueEmotion.Sad)       return "失望";
            if (e == EDialogueEmotion.Surprised) return "惊讶";
            return "平静";
        }

        private static string FindGroupPath(string id, Dictionary<string, string> known)
        {
            if (known != null && known.TryGetValue(id, out string p)) return p;
            var guids = AssetDatabase.FindAssets(id, new[] { GroupBaseDir });
            foreach (var g in guids)
            {
                string ap = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileNameWithoutExtension(ap) == id) return ap;
            }
            return null;
        }

        private static T FindAsset<T>(string name) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(name)) return null;
            var guids = AssetDatabase.FindAssets($"t:{typeof(T).Name} {name}");
            foreach (var g in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileNameWithoutExtension(p) == name)
                    return AssetDatabase.LoadAssetAtPath<T>(p);
            }
            return null;
        }

        private static void SetTriggerList(DialoguePoolDef pool, string trigger, List<DialogueGroupEntry> list)
        {
            switch (trigger)
            {
                case "firstMeeting":  pool.firstMeeting  = list; break;
                case "serviceStart":  pool.serviceStart  = list; break;
                case "serviceCheck":  pool.serviceCheck  = list; break;
                case "rejected":      pool.rejected       = list; break;
                case "doneMismatch":  pool.doneMismatch   = list; break;
                case "donePlain":     pool.donePlain      = list; break;
                case "doneSatisfied": pool.doneSatisfied  = list; break;
                case "donePerfect":   pool.donePerfect    = list; break;
                case "wanderChat":    pool.wanderChat     = list; break;
            }
        }

        private static List<DialogueGroupEntry> GetTriggerList(DialoguePoolDef pool, string trigger)
        {
            switch (trigger)
            {
                case "firstMeeting":  return pool.firstMeeting  ?? new List<DialogueGroupEntry>();
                case "serviceStart":  return pool.serviceStart  ?? new List<DialogueGroupEntry>();
                case "serviceCheck":  return pool.serviceCheck  ?? new List<DialogueGroupEntry>();
                case "rejected":      return pool.rejected       ?? new List<DialogueGroupEntry>();
                case "doneMismatch":  return pool.doneMismatch   ?? new List<DialogueGroupEntry>();
                case "donePlain":     return pool.donePlain      ?? new List<DialogueGroupEntry>();
                case "doneSatisfied": return pool.doneSatisfied  ?? new List<DialogueGroupEntry>();
                case "donePerfect":   return pool.donePerfect    ?? new List<DialogueGroupEntry>();
                case "wanderChat":    return pool.wanderChat     ?? new List<DialogueGroupEntry>();
                default:              return new List<DialogueGroupEntry>();
            }
        }

        private static string CsvRow(params string[] fields)
        {
            var parts = new string[fields.Length];
            for (int i = 0; i < fields.Length; i++)
            {
                string v = fields[i] ?? "";
                if (v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
                    v = "\"" + v.Replace("\"", "\"\"") + "\"";
                parts[i] = v;
            }
            return string.Join(",", parts);
        }

        private static int Col(string[] h, string name)
        {
            int idx = Array.IndexOf(h, name);
            if (idx < 0) throw new Exception($"[对话导表] CSV 缺少列：{name}");
            return idx;
        }

        private static int ColOpt(string[] h, string name) => Array.IndexOf(h, name);

        private static string Get(string[] row, int col)
            => col >= 0 && col < row.Length ? row[col] : "";

        private static string Fallback(string val, string def)
            => string.IsNullOrWhiteSpace(val) ? def : val;

        private static List<string[]> ReadCsv(string path)
        {
            var result = new List<string[]>();
            string full = Path.GetFullPath(path);
            if (!File.Exists(full)) return result;

            string text = File.ReadAllText(full, Encoding.UTF8);
            var lines   = new List<string>();
            var cur     = new StringBuilder();
            bool inQ    = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"') { inQ = !inQ; cur.Append(c); }
                else if (!inQ && c == '\n') { lines.Add(cur.ToString().TrimEnd('\r')); cur.Clear(); }
                else cur.Append(c);
            }
            if (cur.Length > 0) lines.Add(cur.ToString());

            foreach (var line in lines)
                if (!string.IsNullOrWhiteSpace(line))
                    result.Add(ParseCsvLine(line));

            return result;
        }

        private static string[] ParseCsvLine(string line)
        {
            var fields = new List<string>();
            int i = 0;
            while (i <= line.Length)
            {
                if (i == line.Length) { fields.Add(""); break; }

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
                    int comma = line.IndexOf(',', i);
                    if (comma < 0) { fields.Add(line.Substring(i).Trim()); break; }
                    fields.Add(line.Substring(i, comma - i).Trim());
                    i = comma + 1;
                }
            }
            return fields.ToArray();
        }
    }
}

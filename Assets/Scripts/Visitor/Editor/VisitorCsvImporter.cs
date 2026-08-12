using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// 访客三表导表工具（流程与 FurnitureCsvImporter 一致）：
    /// 策划编辑 Excel/访客种族表.xlsx / 访客日程表.xlsx / 访客调参表.xlsx → 双击 Tools/导表/export_config.bat
    /// 导出 Assets/Configs/访客*.csv → 资产管线检测到变化即由 CsvPostprocessor **自动重建**对应 SO：
    ///   种族 → VisitorRaces/Race_&lt;raceId&gt;.asset（缺则新建，多余的资产保留并告警）
    ///   日程 → VisitorScheduleTable.asset（整表重建；§4.4 重排会改需求 roll，加内容请追加在 Excel 表尾）
    ///   调参/氛围 → VisitorTuningConfig.asset
    /// 引用列写法：需求权重「标签*权重[*必]」以 / 分隔（标签可写显示名或 id）；
    /// 立绘差分「表情=Resources路径」以 / 分隔（表情写中文名，如 平静/高兴）；对话池写资产名（如 Pool_fox）。
    /// </summary>
    public static class VisitorCsvImporter
    {
        private const string RacesDir = "Assets/Resources/OutGameUI/VisitorRaces";
        private const string ScheduleAssetPath = "Assets/Resources/OutGameUI/VisitorScheduleTable.asset";
        private const string TuningAssetPath = "Assets/Resources/OutGameUI/VisitorTuningConfig.asset";
        private const string AutoImportPrefKey = "MasterHouse.VisitorCsvAutoImport";
        private const string ConfigsDir = "Assets/Configs";
        private const string RaceCsvPath = ConfigsDir + "/访客种族表.csv";
        private const string ScheduleCsvPath = ConfigsDir + "/访客日程表.csv";
        private const string TuningCsvPath = ConfigsDir + "/访客调参表.csv";
        private const string AmbientCsvPath = ConfigsDir + "/访客氛围表.csv";

        /// <summary>ExportAll 写 CSV 会触发资产重导入，用此标记跳过随之而来的一次自动导入（内容本就来自资产）。</summary>
        private static bool suppressNextAutoImport;

        private static bool AutoImportEnabled
        {
            get => EditorPrefs.GetBool(AutoImportPrefKey, true);
            set => EditorPrefs.SetBool(AutoImportPrefKey, value);
        }

        [MenuItem("MasterHouse/访客系统/自动导表（CSV 变化时）")]
        private static void ToggleAutoImport()
        {
            AutoImportEnabled = !AutoImportEnabled;
            Debug.Log("[导表] 访客自动导表已" + (AutoImportEnabled ? "开启" : "关闭"));
        }

        [MenuItem("MasterHouse/访客系统/自动导表（CSV 变化时）", true)]
        private static bool ToggleAutoImportValidate()
        {
            Menu.SetChecked("MasterHouse/访客系统/自动导表（CSV 变化时）", AutoImportEnabled);
            return true;
        }

        /// <summary>Assets/Configs 下的访客 CSV 重新导入（bat 导出/外部覆盖/版本库更新）时自动触发导表。</summary>
        private sealed class CsvPostprocessor : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                foreach (var path in imported)
                {
                    if (path != RaceCsvPath && path != ScheduleCsvPath && path != TuningCsvPath && path != AmbientCsvPath)
                        continue;
                    if (suppressNextAutoImport) { suppressNextAutoImport = false; return; }
                    if (!AutoImportEnabled) return;
                    Debug.Log("[导表] 检测到访客配置表更新，自动导入…（可在菜单 MasterHouse → 访客系统 关闭）");
                    // 延迟到导入管线结束后执行，避免在 postprocess 回调里写资产
                    EditorApplication.delayCall += ImportAll;
                    return;
                }
            }
        }

        // ── 入口 ──

        [MenuItem("MasterHouse/访客系统/从 CSV 导入访客三表")]
        public static void ImportAll()
        {
            var races = ImportRaceCsv();
            var schedule = ImportScheduleCsv();
            var tuning = ImportTuningCsv();
            AssetDatabase.SaveAssets();
            Debug.Log($"[导表] 完成：种族 {races} 行、日程 {schedule} 行、调参 {tuning} 项 → VisitorRaces / ScheduleTable / TuningConfig");
        }

        [MenuItem("MasterHouse/访客系统/导出访客三表到 CSV")]
        public static void ExportAll()
        {
            Directory.CreateDirectory(ConfigsDir);
            suppressNextAutoImport = true; // 导出内容本就来自资产，跳过随之而来的自动回导
            ExportRaceCsv();
            ExportScheduleCsv();
            ExportTuningCsv();
            AssetDatabase.Refresh();
            Debug.Log($"[导表] 已导出到 {ConfigsDir}/访客种族表.csv、访客日程表.csv、访客调参表.csv、访客氛围表.csv");
        }

        // ── 导入：种族 ──

        private static int ImportRaceCsv()
        {
            var rows = ReadCsv(RaceCsvPath, out var col);
            var existing = LoadAll<VisitorRaceDef>();
            var tags = LoadAll<TagDef>();
            var pools = LoadAll<DialoguePoolDef>();
            var seen = new HashSet<string>();

            foreach (var row in rows)
            {
                var raceId = Cell(row, col, "种族id");
                if (string.IsNullOrEmpty(raceId))
                {
                    Debug.LogWarning("[导表] 访客种族表存在空 种族id 行，已跳过");
                    continue;
                }
                if (!seen.Add(raceId))
                {
                    Debug.LogWarning($"[导表] 种族 id 重复：{raceId}，后一行已跳过");
                    continue;
                }

                var race = existing.Find(r => r.raceId == raceId);
                if (race == null)
                {
                    race = ScriptableObject.CreateInstance<VisitorRaceDef>();
                    race.raceId = raceId;
                    Directory.CreateDirectory(RacesDir);
                    AssetDatabase.CreateAsset(race, $"{RacesDir}/Race_{raceId}.asset");
                    existing.Add(race);
                    Debug.Log($"[导表] 新建种族资产：Race_{raceId}.asset");
                }

                race.displayName = Cell(row, col, "显示名");
                race.waitTalkTimeoutTicks = Int(row, col, "等搭话超时tick", race.waitTalkTimeoutTicks);
                race.waitDeliverTimeoutTicks = Int(row, col, "等交货超时tick", race.waitDeliverTimeoutTicks);
                race.wanderMaxTicks = Int(row, col, "闲逛上限tick", race.wanderMaxTicks);
                race.stayOvernightPercent = Int(row, col, "跨天留宿概率%", race.stayOvernightPercent);
                race.needTagWeights = ParseNeedWeights(Cell(row, col, "需求权重"), tags, raceId);
                race.needCountMin = Int(row, col, "需求数下限", 1);
                race.needCountMax = Int(row, col, "需求数上限", race.needCountMin);
                race.portraits = ParsePortraits(Cell(row, col, "立绘差分"), raceId);
                race.sheetPath = Cell(row, col, "序列帧");
                race.dialoguePool = ResolveByAssetName(pools, Cell(row, col, "对话池"), raceId, "对话池");
                EditorUtility.SetDirty(race);
            }

            foreach (var race in existing)
                if (!seen.Contains(race.raceId))
                    Debug.LogWarning($"[导表] 种族资产「{race.raceId}」不在 CSV 里：资产保留未动（若要删除请手动删资产并同步日程表）");
            return seen.Count;
        }

        /// <summary>解析需求权重「标签*权重[*必]」以 / 分隔；标签可写显示名或 id。</summary>
        private static List<NeedTagWeight> ParseNeedWeights(string text, List<TagDef> tags, string raceId)
        {
            var result = new List<NeedTagWeight>();
            if (string.IsNullOrEmpty(text)) return result;
            foreach (var token in text.Split('/', '、'))
            {
                if (string.IsNullOrWhiteSpace(token)) continue;
                var parts = token.Trim().Split('*', '×');
                var tagName = parts[0].Trim();
                var tag = tags.Find(t => t.displayName == tagName || t.id == tagName);
                if (tag == null)
                {
                    Debug.LogWarning($"[导表] 种族「{raceId}」需求权重引用了不存在的标签「{tagName}」，该项已跳过" +
                                     "（标签写 TagDef 的显示名或 id）");
                    continue;
                }
                var weight = parts.Length > 1 && int.TryParse(parts[1].Trim(), out var w) ? w : 1;
                var required = false;
                for (var i = 2; i < parts.Length; i++)
                {
                    var flag = parts[i].Trim();
                    if (flag == "必" || flag == "必要" || flag.Equals("required", StringComparison.OrdinalIgnoreCase))
                        required = true;
                }
                result.Add(new NeedTagWeight { tag = tag, weight = weight, required = required });
            }
            return result;
        }

        /// <summary>解析立绘差分「表情=Resources路径」以 / 分隔；表情写中文名（平静/高兴/困惑/失望/惊讶）或枚举名。</summary>
        private static List<ExpressionPortrait> ParsePortraits(string text, string raceId)
        {
            var result = new List<ExpressionPortrait>();
            if (string.IsNullOrEmpty(text)) return result;
            foreach (var token in text.Split('/'))
            {
                if (string.IsNullOrWhiteSpace(token)) continue;
                var eq = token.IndexOf('=');
                if (eq <= 0)
                {
                    // 无「表情=」前缀的裸路径按平静（默认表情）处理
                    result.Add(new ExpressionPortrait { expression = EDialogueEmotion.Calm, portraitPath = token.Trim() });
                    continue;
                }
                var name = token.Substring(0, eq).Trim();
                var path = token.Substring(eq + 1).Trim();
                var emotion = EDialogueEmotion.Calm;
                var index = Array.IndexOf(DialogueEmotionText.Names, name);
                if (index >= 0) emotion = (EDialogueEmotion)index;
                else if (!Enum.TryParse(name, true, out emotion))
                {
                    Debug.LogWarning($"[导表] 种族「{raceId}」立绘差分表情「{name}」无法识别，按平静处理" +
                                     "（合法：平静/高兴/困惑/失望/惊讶）");
                    emotion = EDialogueEmotion.Calm;
                }
                result.Add(new ExpressionPortrait { expression = emotion, portraitPath = path });
            }
            return result;
        }

        // ── 导入：日程 ──

        private static int ImportScheduleCsv()
        {
            var rows = ReadCsv(ScheduleCsvPath, out var col);
            var table = LoadOrCreate<VisitorScheduleTable>(ScheduleAssetPath);
            var races = LoadAll<VisitorRaceDef>();
            var named = LoadAll<NamedVisitorDef>();
            table.entries.Clear();
            foreach (var row in rows)
            {
                var raceName = Cell(row, col, "种族id");
                var race = races.Find(r => r.raceId == raceName || r.displayName == raceName);
                if (race == null)
                {
                    Debug.LogWarning($"[导表] 日程表引用了不存在的种族「{raceName}」，该行已跳过");
                    continue;
                }
                table.entries.Add(new VisitorScheduleEntry
                {
                    day = Int(row, col, "天", 1),
                    appearMinute = Int(row, col, "出现时刻(分钟)", 9 * 60),
                    race = race,
                    namedOverride = ResolveByAssetName(named, Cell(row, col, "具名覆写"), raceName, "具名覆写"),
                });
            }
            EditorUtility.SetDirty(table);
            return table.entries.Count;
        }

        // ── 导入：调参 + 氛围 ──

        private static int ImportTuningCsv()
        {
            var rows = ReadCsv(TuningCsvPath, out var col);
            var config = LoadOrCreate<VisitorTuningConfig>(TuningAssetPath);
            var count = 0;
            foreach (var row in rows)
            {
                var key = Cell(row, col, "参数");
                if (string.IsNullOrEmpty(key)) continue;
                var value = Int(row, col, "值", int.MinValue);
                if (value == int.MinValue)
                {
                    Debug.LogWarning($"[导表] 访客调参「{key}」的值不是整数，已跳过");
                    continue;
                }
                count++;
                switch (key)
                {
                    case "openMinute": config.openMinute = value; break;
                    case "closeMinute": config.closeMinute = value; break;
                    case "bubbleIntervalTicks": config.bubbleIntervalTicks = value; break;
                    case "bubbleJitterTicks": config.bubbleJitterTicks = value; break;
                    case "bubbleHoldTicks": config.bubbleHoldTicks = value; break;
                    default:
                        Debug.LogWarning($"[导表] 访客调参存在未知参数「{key}」，已忽略（合法键见 VisitorTuningConfig 字段名）");
                        count--;
                        break;
                }
            }

            var ambientRows = ReadCsv(AmbientCsvPath, out var ambientCol);
            config.ambientVisitors.Clear();
            foreach (var row in ambientRows)
            {
                var id = Cell(row, ambientCol, "id");
                if (string.IsNullOrEmpty(id)) continue;
                config.ambientVisitors.Add(new AmbientVisitorDef
                {
                    id = id,
                    displayName = Cell(row, ambientCol, "显示名"),
                    sheetPath = Cell(row, ambientCol, "序列帧"),
                });
            }
            EditorUtility.SetDirty(config);
            return count;
        }

        // ── 导出（反向：从当前资产生成 CSV；xlsx 是编辑源，导出不会回写 xlsx）──

        private static void ExportRaceCsv()
        {
            var lines = new List<string>
            {
                Line("种族id", "显示名", "等搭话超时tick", "等交货超时tick", "闲逛上限tick", "跨天留宿概率%",
                    "需求权重", "需求数下限", "需求数上限", "立绘差分", "序列帧", "对话池"),
            };
            foreach (var race in LoadAll<VisitorRaceDef>())
            {
                var weights = new List<string>();
                foreach (var entry in race.needTagWeights)
                    if (entry != null && entry.tag != null)
                        weights.Add($"{entry.tag.displayName}*{entry.weight}" + (entry.required ? "*必" : ""));
                var portraits = new List<string>();
                foreach (var entry in race.portraits)
                    if (entry != null)
                        portraits.Add($"{DialogueEmotionText.NameOf(entry.expression)}={entry.portraitPath}");
                lines.Add(Line(race.raceId, race.displayName, race.waitTalkTimeoutTicks, race.waitDeliverTimeoutTicks,
                    race.wanderMaxTicks, race.stayOvernightPercent, string.Join("/", weights),
                    race.needCountMin, race.needCountMax, string.Join("/", portraits), race.sheetPath,
                    race.dialoguePool != null ? race.dialoguePool.name : ""));
            }
            WriteCsv(RaceCsvPath, lines);
        }

        private static void ExportScheduleCsv()
        {
            var lines = new List<string> { Line("天", "出现时刻(分钟)", "种族id", "具名覆写") };
            var table = AssetDatabase.LoadAssetAtPath<VisitorScheduleTable>(ScheduleAssetPath);
            if (table != null)
                foreach (var entry in table.entries)
                    lines.Add(Line(entry.day, entry.appearMinute,
                        entry.race != null ? entry.race.raceId : "",
                        entry.namedOverride != null ? entry.namedOverride.name : ""));
            WriteCsv(ScheduleCsvPath, lines);
        }

        private static void ExportTuningCsv()
        {
            var config = AssetDatabase.LoadAssetAtPath<VisitorTuningConfig>(TuningAssetPath);
            var lines = new List<string> { Line("参数", "值", "说明") };
            if (config != null)
            {
                lines.Add(Line("openMinute", config.openMinute, "开门时刻（当天分钟数）"));
                lines.Add(Line("closeMinute", config.closeMinute, "打烊时刻（当天分钟数）"));
                lines.Add(Line("bubbleIntervalTicks", config.bubbleIntervalTicks, "闲逛冒泡间隔（tick）"));
                lines.Add(Line("bubbleJitterTicks", config.bubbleJitterTicks, "冒泡间隔抖动（tick）"));
                lines.Add(Line("bubbleHoldTicks", config.bubbleHoldTicks, "气泡停留时长（tick）"));
            }
            WriteCsv(TuningCsvPath, lines);

            var ambient = new List<string> { Line("id", "显示名", "序列帧") };
            if (config != null)
                foreach (var entry in config.ambientVisitors)
                    ambient.Add(Line(entry.id, entry.displayName, entry.sheetPath));
            WriteCsv(AmbientCsvPath, ambient);
        }

        // ── 通用工具（与 FurnitureCsvImporter 同款）──

        private static List<T> LoadAll<T>() where T : ScriptableObject
        {
            var result = new List<T>();
            foreach (var guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) result.Add(asset);
            }
            return result;
        }

        /// <summary>按资产名解析引用（如对话池「Pool_fox」）；空值返回 null，找不到给警告。</summary>
        private static T ResolveByAssetName<T>(List<T> pool, string name, string owner, string field) where T : ScriptableObject
        {
            if (string.IsNullOrEmpty(name)) return null;
            var asset = pool.Find(p => p.name == name);
            if (asset == null)
                Debug.LogWarning($"[导表] 「{owner}」的{field}引用了不存在的资产「{name}」，已置空");
            return asset;
        }

        private static List<string[]> ReadCsv(string path, out Dictionary<string, int> columns)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"配置表缺失：{path}（可先用菜单「导出访客三表到 CSV」从当前资产生成）");
            var rows = new List<string[]>();
            columns = null;
            foreach (var rawLine in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(rawLine)) continue;
                var cells = ParseCsvLine(rawLine);
                if (columns == null)
                {
                    columns = new Dictionary<string, int>();
                    for (var i = 0; i < cells.Length; i++)
                        columns[cells[i].Trim()] = i; // 表头按列名索引，列顺序可自由调整
                    continue;
                }
                rows.Add(cells);
            }
            if (columns == null) throw new InvalidDataException($"配置表为空（无表头行）：{path}");
            return rows;
        }

        /// <summary>解析一行 CSV：支持双引号包裹（内含逗号/成对引号转义）。</summary>
        private static string[] ParseCsvLine(string line)
        {
            var cells = new List<string>();
            var value = new StringBuilder();
            var inQuotes = false;
            for (var i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { value.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else value.Append(ch);
                }
                else if (ch == '"') inQuotes = true;
                else if (ch == ',') { cells.Add(value.ToString()); value.Length = 0; }
                else value.Append(ch);
            }
            cells.Add(value.ToString());
            return cells.ToArray();
        }

        private static void WriteCsv(string path, List<string> lines)
        {
            // UTF-8 带 BOM：Excel 双击打开中文不乱码
            File.WriteAllText(path, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(true));
        }

        private static string Line(params object[] cells)
        {
            var encoded = new string[cells.Length];
            for (var i = 0; i < cells.Length; i++)
            {
                var text = cells[i]?.ToString() ?? string.Empty;
                encoded[i] = text.IndexOfAny(new[] { ',', '"', '\n' }) >= 0
                    ? "\"" + text.Replace("\"", "\"\"") + "\""
                    : text;
            }
            return string.Join(",", encoded);
        }

        private static string Cell(string[] row, Dictionary<string, int> col, string name)
        {
            if (!col.TryGetValue(name, out var index) || index >= row.Length) return string.Empty;
            return row[index].Trim();
        }

        private static int Int(string[] row, Dictionary<string, int> col, string name, int fallback)
        {
            var text = Cell(row, col, name);
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}

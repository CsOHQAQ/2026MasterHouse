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
    /// 引用列写法：种族的「默认立绘ID」写 Excel/立绘表.xlsx 里的立绘ID（原「立绘差分」那种
    /// 「表情=路径/表情=路径」的双层分隔串已随 2026-08-14 立绘 ID 化退役）；
    /// 日程的「需求」列写 NeedDef 资产名（如 Need_修理电路）。
    /// 种族表的「对话池」列已随 2026-08-14 对话资源重构退役——对话内容按 raceId 查 DialogueTable。
    /// 种族表的「跨天留宿概率%」列已于 2026-08-14 删除（消费方早已移除，见 VisitorRaceDef 的注释）。
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
                // 立绘ID 的存在性由立绘表那边负责（PortraitCsvImporter），这里只存字符串：
                // 导表顺序不保证，在这里查表会因为「立绘表还没导」报出假错误
                // 图鉴详情页内容（2026-08-19）：缺列时保持资产上的现值，不覆盖成空串
                race.aliasName = CellOrKeep(row, col, "别名", race.aliasName);
                race.title = CellOrKeep(row, col, "称号", race.title);
                race.stars = Int(row, col, "星级", race.stars);
                race.hobbies = CellOrKeep(row, col, "爱好", race.hobbies);
                race.intro = CellOrKeep(row, col, "介绍", race.intro);
                race.quote = CellOrKeep(row, col, "语录", race.quote);
                race.defaultPortraitId = Cell(row, col, "默认立绘ID");
                race.sheetPath = Cell(row, col, "序列帧");
                // 「对话池」列已随 2026-08-14 对话资源重构退役：对话内容按 raceId 查 DialogueTable，
                // 种族资产上不再挂引用。表里若还留着这一列会被静默忽略。
                EditorUtility.SetDirty(race);
            }

            foreach (var race in existing)
                if (!seen.Contains(race.raceId))
                    Debug.LogWarning($"[导表] 种族资产「{race.raceId}」不在 CSV 里：资产保留未动（若要删除请手动删资产并同步日程表）");
            return seen.Count;
        }

        // 立绘差分解析 ParsePortraits 已随 2026-08-14 立绘 ID 化删除：
        // 「表情=路径/表情=路径」这种双层分隔串正是 §16.6 明令禁止的无类型数据，
        // 现在种族表只存一个立绘ID，差分整体搬去 Excel/立绘表.xlsx。

        // ── 导入：日程 ──

        private static int ImportScheduleCsv()
        {
            var rows = ReadCsv(ScheduleCsvPath, out var col);
            var table = LoadOrCreate<VisitorScheduleTable>(ScheduleAssetPath);
            var races = LoadAll<VisitorRaceDef>();
            var named = LoadAll<NamedVisitorDef>();
            var needs = LoadAll<NeedDef>();
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
                    // 需求写 NeedDef 的资产名（同「对话池」列写 Pool_fox 的做法，§4.2）。
                    // 已知代价：改资产名即断引用且无法「查找引用」，这是 Excel 引用 SO 的固有问题，接受。
                    // 解析失败只打 Warning 不中断——空需求的后果由运行时的 LogError 指名行号报出
                    need = ResolveByAssetName(needs, Cell(row, col, "需求"), raceName, "需求"),
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
                if (key == "actorWorldScale")
                {
                    var scale = Float(row, col, "值", float.NaN);
                    if (float.IsNaN(scale))
                    {
                        Debug.LogWarning($"[导表] 访客调参「{key}」的值不是数字，已跳过");
                        continue;
                    }
                    config.actorWorldScale = Mathf.Clamp(scale, .2f, 1.2f);
                    count++;
                    continue;
                }
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
                    case "needPromptMinTicks": config.needPromptMinTicks = value; break;
                    case "needPromptMaxTicks": config.needPromptMaxTicks = value; break;
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
                Line("种族id", "显示名", "等搭话超时tick", "等交货超时tick", "闲逛上限tick",
                    "默认立绘ID", "序列帧"),
            };
            foreach (var race in LoadAll<VisitorRaceDef>())
                lines.Add(Line(race.raceId, race.displayName, race.waitTalkTimeoutTicks, race.waitDeliverTimeoutTicks,
                    race.wanderMaxTicks, race.defaultPortraitId, race.sheetPath));
            WriteCsv(RaceCsvPath, lines);
        }

        private static void ExportScheduleCsv()
        {
            var lines = new List<string> { Line("天", "出现时刻(分钟)", "种族id", "需求", "具名覆写") };
            var table = AssetDatabase.LoadAssetAtPath<VisitorScheduleTable>(ScheduleAssetPath);
            if (table != null)
                foreach (var entry in table.entries)
                    lines.Add(Line(entry.day, entry.appearMinute,
                        entry.race != null ? entry.race.raceId : "",
                        entry.need != null ? entry.need.name : "",
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
                lines.Add(Line("actorWorldScale", config.actorWorldScale.ToString("0.##", CultureInfo.InvariantCulture),
                    "访客演员基础世界缩放（叠加透视缩放前）"));
                lines.Add(Line("needPromptMinTicks", config.needPromptMinTicks, "入住后到开口示意的最短间隔（tick）"));
                lines.Add(Line("needPromptMaxTicks", config.needPromptMaxTicks, "入住后到开口示意的最长间隔（tick）"));
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

        /// <summary>
        /// **表里没有这一列**时保留资产现值（新增列还没进表，不至于把已有内容清掉）；
        /// 列已经存在就一律以表为准——**留空就是清空**。
        ///
        /// 2026-08-19 修：原来写成「空值也保留现值」，于是策划把单元格清掉、导表之后
        /// 资产里的旧内容还在，页面照旧显示（表里没配却有信息）。表是唯一真相，不能只进不出。
        /// </summary>
        private static string CellOrKeep(string[] row, Dictionary<string, int> col, string name, string current) =>
            col.ContainsKey(name) ? Cell(row, col, name) : current;

        private static int Int(string[] row, Dictionary<string, int> col, string name, int fallback)
        {
            var text = Cell(row, col, name);
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        private static float Float(string[] row, Dictionary<string, int> col, string name, float fallback)
        {
            var text = Cell(row, col, name);
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
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

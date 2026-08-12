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
    /// 音效表导表工具（流程与 FurnitureCsvImporter / VisitorCsvImporter 一致）：
    /// 策划编辑 Excel/音效表.xlsx → 双击 Tools/导表/export_config.bat 导出 Assets/Configs/音效表.csv →
    /// 资产管线检测到变化即自动整表重建 SfxTable.asset。
    /// 音效id 写 ESfx 枚举名（如 UiClick）或数字；剪辑写 Resources 相对路径（如 SoundEffect/1_Button_260812）
    /// 或 Assets/ 开头的完整路径；「说明」列仅供阅读，导入忽略。
    /// </summary>
    public static class SfxCsvImporter
    {
        private const string SfxAssetPath = "Assets/Resources/OutGameUI/SfxTable.asset";
        private const string AutoImportPrefKey = "MasterHouse.SfxCsvAutoImport";
        private const string ConfigsDir = "Assets/Configs";
        private const string SfxCsvPath = ConfigsDir + "/音效表.csv";

        /// <summary>ExportAll 写 CSV 会触发资产重导入，用此标记跳过随之而来的一次自动导入（内容本就来自资产）。</summary>
        private static bool suppressNextAutoImport;

        private static bool AutoImportEnabled
        {
            get => EditorPrefs.GetBool(AutoImportPrefKey, true);
            set => EditorPrefs.SetBool(AutoImportPrefKey, value);
        }

        [MenuItem("MasterHouse/音效系统/自动导表（CSV 变化时）")]
        private static void ToggleAutoImport()
        {
            AutoImportEnabled = !AutoImportEnabled;
            Debug.Log("[导表] 音效自动导表已" + (AutoImportEnabled ? "开启" : "关闭"));
        }

        [MenuItem("MasterHouse/音效系统/自动导表（CSV 变化时）", true)]
        private static bool ToggleAutoImportValidate()
        {
            Menu.SetChecked("MasterHouse/音效系统/自动导表（CSV 变化时）", AutoImportEnabled);
            return true;
        }

        /// <summary>Assets/Configs 下的音效 CSV 重新导入（bat 导出/外部覆盖/版本库更新）时自动触发导表。</summary>
        private sealed class CsvPostprocessor : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                foreach (var path in imported)
                {
                    if (path != SfxCsvPath) continue;
                    if (suppressNextAutoImport) { suppressNextAutoImport = false; return; }
                    if (!AutoImportEnabled) return;
                    Debug.Log("[导表] 检测到音效表更新，自动导入…（可在菜单 MasterHouse → 音效系统 关闭）");
                    EditorApplication.delayCall += ImportAll;
                    return;
                }
            }
        }

        // ── 入口 ──

        [MenuItem("MasterHouse/音效系统/从 CSV 导入音效表")]
        public static void ImportAll()
        {
            var rows = ReadCsv(SfxCsvPath, out var col);
            var table = LoadOrCreate<SfxTable>(SfxAssetPath);
            table.entries.Clear();
            var seen = new HashSet<ESfx>();
            foreach (var row in rows)
            {
                var idText = Cell(row, col, "音效id");
                if (string.IsNullOrEmpty(idText)) continue;
                if (!TryParseSfx(idText, out var id))
                {
                    Debug.LogWarning($"[导表] 音效表存在未知音效id「{idText}」，该行已跳过（合法值 = ESfx 枚举名或数字）");
                    continue;
                }
                if (id == ESfx.None)
                {
                    Debug.LogWarning("[导表] 音效表不应配置 None（显式静音占位），该行已跳过");
                    continue;
                }
                if (!seen.Add(id))
                {
                    Debug.LogWarning($"[导表] 音效id 重复：{id}，后一行已跳过");
                    continue;
                }
                table.entries.Add(new SfxEntry
                {
                    id = id,
                    clip = LoadClip(Cell(row, col, "剪辑")),
                    volume = Float(row, col, "音量", 1f),
                    minInterval = Float(row, col, "最短间隔秒", 0.05f),
                });
            }
            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();
            Debug.Log($"[导表] 完成：音效 {table.entries.Count} 行 → SfxTable");
        }

        [MenuItem("MasterHouse/音效系统/导出音效表到 CSV")]
        public static void ExportAll()
        {
            Directory.CreateDirectory(ConfigsDir);
            suppressNextAutoImport = true; // 导出内容本就来自资产，跳过随之而来的自动回导
            var lines = new List<string> { Line("音效id", "说明", "剪辑", "音量", "最短间隔秒") };
            var table = AssetDatabase.LoadAssetAtPath<SfxTable>(SfxAssetPath);
            if (table != null)
                foreach (var entry in table.entries)
                    lines.Add(Line(entry.id.ToString(), "", ClipPath(entry.clip),
                        entry.volume.ToString(CultureInfo.InvariantCulture),
                        entry.minInterval.ToString(CultureInfo.InvariantCulture)));
            // UTF-8 带 BOM：Excel 双击打开中文不乱码
            File.WriteAllText(SfxCsvPath, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(true));
            AssetDatabase.Refresh();
            Debug.Log($"[导表] 已导出到 {SfxCsvPath}");
        }

        // ── 字段解析 ──

        private static bool TryParseSfx(string text, out ESfx id)
        {
            if (Enum.TryParse(text, true, out id) && Enum.IsDefined(typeof(ESfx), id)) return true;
            if (int.TryParse(text, out var numeric) && Enum.IsDefined(typeof(ESfx), numeric))
            {
                id = (ESfx)numeric;
                return true;
            }
            return false;
        }

        /// <summary>剪辑列两种写法：①Assets/ 开头完整路径（含扩展名）；②Resources 相对路径（不带扩展名）。</summary>
        private static AudioClip LoadClip(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var clip = path.StartsWith("Assets/")
                ? AssetDatabase.LoadAssetAtPath<AudioClip>(path)
                : Resources.Load<AudioClip>(path);
            if (clip == null)
                Debug.LogWarning($"[导表] 音效剪辑未找到：{path}（该行 clip 置空，播放时会告警）");
            return clip;
        }

        private static string ClipPath(AudioClip clip)
        {
            if (clip == null) return string.Empty;
            var assetPath = AssetDatabase.GetAssetPath(clip);
            const string prefix = "Assets/Resources/";
            if (!assetPath.StartsWith(prefix)) return assetPath;
            var relative = assetPath.Substring(prefix.Length);
            var dot = relative.LastIndexOf('.');
            return dot > 0 ? relative.Substring(0, dot) : relative;
        }

        // ── 通用工具（与 FurnitureCsvImporter 同款）──

        private static List<string[]> ReadCsv(string path, out Dictionary<string, int> columns)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"配置表缺失：{path}（可先用菜单「导出音效表到 CSV」从当前资产生成）");
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
                        columns[cells[i].Trim()] = i;
                    continue;
                }
                rows.Add(cells);
            }
            if (columns == null) throw new InvalidDataException($"配置表为空（无表头行）：{path}");
            return rows;
        }

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

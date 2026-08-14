using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// 立绘导表（2026-08-14 立绘 ID 化）：Assets/Configs/立绘表.csv → PortraitTable.asset **整表重建**。
    ///
    /// 唯一数据源是 Excel/立绘表.xlsx；CSV 由 Tools/导表/export_portrait.py 生成，本类只负责 CSV → SO。
    /// 与对话表一样**没有反向导出**——SO 是产物不是源。
    ///
    /// 列（列名即契约，与导出脚本一一对应）：立绘ID, 资源路径, 备注, 行号
    ///
    /// 【导表顺序】导完立绘**直接串调对话导表**，不靠两个 AssetPostprocessor 赛跑：
    /// 对话表要拿本表校验「立绘ID 存不存在」，先后颠倒会让 Console 先刷一屏假错误
    /// （对话表引用了尚未导入的新立绘ID）。对应地，DialogueCsvImporter 的 postprocessor
    /// 发现同一批里也有立绘表 CSV 时会让路，由这里统一串起来。
    /// </summary>
    public static class PortraitCsvImporter
    {
        public const string CsvPath = "Assets/Configs/立绘表.csv";
        public const string TableAssetPath = "Assets/Resources/OutGameUI/PortraitTable.asset";

        private const string Workbook = "立绘表.xlsx";
        private const string Sheet = "立绘";

        // ─── 菜单 ───────────────────────────────────────────────────────────

        [MenuItem("MasterHouse/对话系统/从 CSV 导入立绘")]
        public static void ImportFromCsvMenu()
        {
            var report = Import();
            report.Dump("立绘导表");
            if (report.Errors == 0)
            {
                // 立绘变了，对话表的立绘ID 校验结果也可能跟着变——顺手重导一次，口径始终一致
                DialogueCsvImporter.Import().Dump();
                EditorUtility.DisplayDialog("导表完成",
                    $"立绘 {report.EntryCount} 条" +
                    (report.Warnings > 0 ? $"\n\n{report.Warnings} 条警告，详见 Console。" : "\n\n没有问题。") +
                    "\n\n已顺带重导对话表。", "好");
            }
            else
            {
                EditorUtility.DisplayDialog("导表失败",
                    $"{report.Errors} 个错误，立绘表**没有**被改写。\n\n" +
                    "错误明细在 Console 里，每条都带 Excel 行号。", "好");
            }
        }

        private sealed class CsvPostprocessor : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(string[] imported, string[] deleted,
                string[] moved, string[] movedFrom)
            {
                if (!DialogueCsvImporter.AutoImport) return;
                foreach (var path in imported)
                    if (path == CsvPath)
                    {
                        EditorApplication.delayCall += () =>
                        {
                            var report = Import();
                            report.Dump("立绘导表");
                            // 立绘没导成就别再往下走：拿旧表去校验对话只会报出一堆误导性错误
                            if (report.Errors == 0) DialogueCsvImporter.Import().Dump();
                        };
                        return;
                    }
            }
        }

        // ─── 导入主流程 ─────────────────────────────────────────────────────

        /// <summary>整表重建。与对话表同口径：**一个错误都没有才落盘**。</summary>
        public static DialogueReport Import()
        {
            var report = new DialogueReport(Workbook);
            try
            {
                var entries = Parse(report);
                report.EntryCount = entries.Count;
                if (report.Errors > 0) return report;

                var table = LoadOrCreate();
                table.entries = entries;
                table.InvalidateIndex();
                EditorUtility.SetDirty(table);
                AssetDatabase.SaveAssets();
                report.Applied = true;
            }
            catch (System.Exception e)
            {
                report.Error(Sheet, 0, $"导入过程抛异常：{e.Message}\n{e.StackTrace}");
            }
            return report;
        }

        private static List<PortraitEntry> Parse(DialogueReport report)
        {
            var result = new List<PortraitEntry>();
            var rows = DialogueCsvImporter.ReadCsv(CsvPath, report, Sheet,
                $"找不到 {CsvPath}——请编辑 Excel/{Workbook} 后运行 Tools/导表/export_config.bat");
            if (rows.Count < 2) return result;

            var head = rows[0];
            int cId = DialogueCsvImporter.Col(head, "立绘ID");
            int cPath = DialogueCsvImporter.Col(head, "资源路径");
            int cNote = DialogueCsvImporter.Col(head, "备注");
            int cRow = DialogueCsvImporter.Col(head, "行号");
            if (cId < 0 || cPath < 0)
            {
                report.Error(Sheet, 1, "缺少必需列（立绘ID / 资源路径）；请用最新的 Excel 模板重导");
                return result;
            }

            var seen = new Dictionary<string, int>();
            for (var i = 1; i < rows.Count; i++)
            {
                var row = rows[i];
                var excelRow = DialogueCsvImporter.CellInt(row, cRow, i + 1);
                var id = DialogueCsvImporter.Cell(row, cId).Trim();
                var path = DialogueCsvImporter.Cell(row, cPath).Trim();
                if (id.Length == 0 && path.Length == 0) continue; // 整行空白

                if (id.Length == 0)
                {
                    report.Error(Sheet, excelRow, $"「立绘ID」是空的（资源路径写着 {path}）");
                    continue;
                }
                if (seen.TryGetValue(id, out var firstRow))
                {
                    report.Error(Sheet, excelRow, $"立绘ID「{id}」重复，第 {firstRow} 行已经用过了");
                    continue;
                }
                seen[id] = excelRow;

                if (path.Length == 0)
                {
                    report.Error(Sheet, excelRow, $"立绘「{id}」没填资源路径");
                    continue;
                }

                // 素材还没进工程只给警告：美术流程里「先占 ID 后补图」是常态，
                // 拦下来会连累整张对话表导不出去。真到播放时取不到图，立绘位就是空的。
                if (Resources.Load<Texture2D>(path) == null)
                    report.Warn(Sheet, excelRow,
                        $"立绘「{id}」的资源路径找不到贴图：{path}" +
                        "（写法是 Resources 相对路径、不带扩展名，如 OutGameUI/Guests/fox；素材必须在某个 Resources 目录下）");

                result.Add(new PortraitEntry
                {
                    portraitId = id,
                    path = path,
                    note = cNote >= 0 ? DialogueCsvImporter.Cell(row, cNote) : string.Empty,
                    sourceRow = excelRow,
                });
            }
            return result;
        }

        private static PortraitTable LoadOrCreate()
        {
            var table = AssetDatabase.LoadAssetAtPath<PortraitTable>(TableAssetPath);
            if (table != null) return table;

            var dir = Path.GetDirectoryName(TableAssetPath)?.Replace('\\', '/');
            DialogueCsvImporter.EnsureFolder(dir);
            table = ScriptableObject.CreateInstance<PortraitTable>();
            AssetDatabase.CreateAsset(table, TableAssetPath);
            Debug.Log("[立绘导表] 新建立绘索引表：" + TableAssetPath);
            return table;
        }

        /// <summary>供对话导表交叉校验：当前工程里的立绘表（没有则返回 null）。</summary>
        public static PortraitTable LoadTable() =>
            AssetDatabase.LoadAssetAtPath<PortraitTable>(TableAssetPath);
    }
}

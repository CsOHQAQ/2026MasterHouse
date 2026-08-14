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
    /// 家具三表导表工具（流程仿 CatVsDog 的 export_config.bat）：
    /// 策划编辑 Excel/家具表.xlsx / 商店表.xlsx / 家具房间表.xlsx → 双击 Tools/导表/export_config.bat
    /// 导出 Assets/Configs/*.csv → CSV 在 Assets 内，资产管线检测到变化即由 CsvPostprocessor
    /// **自动整表重建**对应 SO（FurnitureTable / FurnitureRoomTable），Unity 开着关着都不需要额外步骤。
    /// 精灵图在表里写 Resources 相对路径（如 OutGameUI/Furniture/table），导入时解析回 Sprite 引用。
    /// 反向「导出到 CSV」用于从当前资产重新生成 CSV（注意：xlsx 是编辑源，导出不会回写 xlsx）。
    /// </summary>
    public static class FurnitureCsvImporter
    {
        private const string FurnitureAssetPath = "Assets/Resources/OutGameUI/FurnitureTable.asset";
        private const string StoreAssetPath = "Assets/Resources/OutGameUI/StoreTable.asset";
        private const string RoomAssetPath = "Assets/Resources/OutGameUI/FurnitureRoomTable.asset";
        private const string AutoImportPrefKey = "MasterHouse.FurnitureCsvAutoImport";
        /// <summary>CSV 落在 Assets 内（Assets/Configs/），资产管线才能自动感知变化。</summary>
        private const string ConfigsDir = "Assets/Configs";
        private const string FurnitureCsvPath = ConfigsDir + "/家具表.csv";
        private const string StoreCsvPath = ConfigsDir + "/商店表.csv";
        private const string StoreCategoryCsvPath = ConfigsDir + "/商店分类表.csv";
        private const string RoomCsvPath = ConfigsDir + "/家具房间表.csv";

        /// <summary>ExportAll 写 CSV 会触发资产重导入，用此标记跳过随之而来的一次自动导入（内容本就来自资产）。</summary>
        private static bool suppressNextAutoImport;

        private static bool AutoImportEnabled
        {
            get => EditorPrefs.GetBool(AutoImportPrefKey, true);
            set => EditorPrefs.SetBool(AutoImportPrefKey, value);
        }

        [MenuItem("MasterHouse/家具系统/自动导表（CSV 变化时）")]
        private static void ToggleAutoImport()
        {
            AutoImportEnabled = !AutoImportEnabled;
            Debug.Log("[导表] 自动导表已" + (AutoImportEnabled ? "开启" : "关闭"));
        }

        [MenuItem("MasterHouse/家具系统/自动导表（CSV 变化时）", true)]
        private static bool ToggleAutoImportValidate()
        {
            Menu.SetChecked("MasterHouse/家具系统/自动导表（CSV 变化时）", AutoImportEnabled);
            return true;
        }

        /// <summary>Assets/Configs 下的家具 CSV 重新导入（bat 导出/外部覆盖/版本库更新）时自动触发导表。</summary>
        private sealed class CsvPostprocessor : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                foreach (var path in imported)
                {
                    if (path != FurnitureCsvPath && path != StoreCsvPath && path != StoreCategoryCsvPath &&
                        path != RoomCsvPath) continue;
                    if (suppressNextAutoImport) { suppressNextAutoImport = false; return; }
                    if (!AutoImportEnabled) return;
                    Debug.Log("[导表] 检测到 Assets/Configs 配置表更新，自动导入…（可在菜单 MasterHouse → 家具系统 关闭）");
                    // 延迟到导入管线结束后执行，避免在 postprocess 回调里写资产
                    EditorApplication.delayCall += ImportAll;
                    return;
                }
            }
        }

        private static readonly string[] FurnitureHeader =
        {
            "id", "英文索引", "显示名", "分类", "描述", "表面类型", "可叠放", "占格列", "占格行", "显示宽", "显示高", "装饰分", "精灵图",
            "色值", "拿起音效", "放下音效",
            "桌面格启用", "桌面格列数", "桌面格宽", "桌面格高", "桌面格偏移X", "桌面高度",
        };

        /// <summary>商店表（售卖配置）：按 id 合回 FurnitureTable 对应条目（2026-08-13 从家具表拆出）。</summary>
        private static readonly string[] StoreHeader = { "id", "显示名", "价格", "解禁声望" };

        private static readonly string[] RoomHeader =
        {
            "记录类型", "房间id", "显示名", "场景宽", "场景高", "背景图", "景深模糊图", "失焦模糊图", "初始货币",
            "访客区左", "访客区下", "访客区右", "访客区上", "入口区左", "入口区下", "入口区右", "入口区上",
            "网格id", "表面类型", "列数", "行数", "格宽", "格高", "X", "Y", "远端宽度比",
            "家具id", "宿主家具id", "列", "行", "翻转",
        };

        // ── 入口 ──

        [MenuItem("MasterHouse/家具系统/从 CSV 导入家具三表")]
        public static void ImportAll()
        {
            var furniture = ImportFurnitureCsv();
            var store = ImportStoreCsv(furniture);
            var rooms = ImportRoomCsv();
            AssetDatabase.SaveAssets();
            Debug.Log($"[导表] 完成：家具 {furniture.entries.Count} 行、商店 {store} 行、房间记录 {rooms} 行 " +
                      "→ FurnitureTable / StoreTable / FurnitureRoomTable");
        }

        [MenuItem("MasterHouse/家具系统/导出家具三表到 CSV")]
        public static void ExportAll()
        {
            Directory.CreateDirectory(ConfigsDir);
            suppressNextAutoImport = true; // 导出内容本就来自资产，跳过随之而来的自动回导
            ExportFurnitureCsv();
            ExportStoreCsv();
            ExportRoomCsv();
            AssetDatabase.Refresh();
            Debug.Log($"[导表] 已导出到 {ConfigsDir}/家具表.csv、商店表.csv、家具房间表.csv");
        }

        // ── 导入 ──

        private static FurnitureTable ImportFurnitureCsv()
        {
            var rows = ReadCsv(FurnitureCsvPath, out var col);
            var table = LoadOrCreate<FurnitureTable>(FurnitureAssetPath);
            table.entries.Clear();
            foreach (var row in rows)
            {
                var entry = new FurnitureEntry
                {
                    id = Cell(row, col, "id"),
                    nameKey = Cell(row, col, "英文索引"),
                    displayName = Cell(row, col, "显示名"),
                    category = Cell(row, col, "分类"),
                    description = Cell(row, col, "描述"),
                    surfaces = ParseSurfaces(Cell(row, col, "表面类型")),
                    stackable = Bool(row, col, "可叠放"),
                    cols = Int(row, col, "占格列", 1),
                    rows = Int(row, col, "占格行", 1),
                    displayWidth = Float(row, col, "显示宽", 100f),
                    displayHeight = Float(row, col, "显示高", 100f),
                    decorationScore = Int(row, col, "装饰分", 0),
                    sprite = LoadSprite(Cell(row, col, "精灵图")),
                    swatchColor = ParseColor(Cell(row, col, "色值")),
                    pickupSound = LoadAudio(Cell(row, col, "拿起音效")),
                    putdownSound = LoadAudio(Cell(row, col, "放下音效")),
                    tableSurface = new FurnitureTableSurfaceConfig
                    {
                        enabled = Bool(row, col, "桌面格启用"),
                        cols = Int(row, col, "桌面格列数", 3),
                        cellWidth = Float(row, col, "桌面格宽", 64f),
                        cellHeight = Float(row, col, "桌面格高", 56f),
                        offsetX = Float(row, col, "桌面格偏移X", 50f),
                        surfaceHeight = Float(row, col, "桌面高度", 146f),
                    },
                };
                if (string.IsNullOrEmpty(entry.id))
                {
                    Debug.LogWarning("[导表] 家具表存在空 id 行，已跳过");
                    continue;
                }
                table.entries.Add(entry);
            }
            EditorUtility.SetDirty(table);
            return table;
        }

        /// <summary>
        /// 商店表 → StoreTable.asset（售卖配置独立成表，2026-08-13）。按 furnitureId 关联家具表：
        /// 引用不存在的家具打 Warning 并跳过；家具不在商店表里 = 非卖品（价格 0 / 解禁 0，见 StoreTable 注释）。
        /// </summary>
        private static int ImportStoreCsv(FurnitureTable furniture)
        {
            var rows = ReadCsv(StoreCsvPath, out var col);
            var table = LoadOrCreate<StoreTable>(StoreAssetPath);
            table.entries.Clear();
            var seen = new HashSet<string>();
            foreach (var row in rows)
            {
                var id = Cell(row, col, "id");
                if (string.IsNullOrEmpty(id)) continue;
                if (furniture != null && furniture.Find(id) == null)
                {
                    Debug.LogWarning($"[导表] 商店表的 id「{id}」在家具表里不存在，该行已跳过");
                    continue;
                }
                if (!seen.Add(id))
                {
                    Debug.LogWarning($"[导表] 商店表 id 重复：{id}，后一行已跳过");
                    continue;
                }
                table.entries.Add(new StoreEntry
                {
                    furnitureId = id,
                    price = Int(row, col, "价格", 0),
                    unlockReputation = Int(row, col, "解禁声望", 0),
                });
            }
            ImportStoreCategories(table);
            EditorUtility.SetDirty(table);
            return table.entries.Count;
        }

        /// <summary>商店分类表（大类名称 + 一行描述，设计稿 §1 策划配置）→ StoreTable.categories。缺文件保留现值。</summary>
        private static void ImportStoreCategories(StoreTable table)
        {
            if (!File.Exists(StoreCategoryCsvPath))
            {
                Debug.LogWarning($"[导表] 商店分类表缺失：{StoreCategoryCsvPath}，大类描述保留现值");
                return;
            }
            var rows = ReadCsv(StoreCategoryCsvPath, out var col);
            table.categories.Clear();
            foreach (var row in rows)
            {
                var name = Cell(row, col, "分类名");
                if (string.IsNullOrEmpty(name)) continue;
                table.categories.Add(new StoreCategoryEntry { name = name, desc = Cell(row, col, "描述") });
            }
        }

        private static int ImportRoomCsv()
        {
            var rows = ReadCsv(RoomCsvPath, out var col);
            var table = LoadOrCreate<FurnitureRoomTable>(RoomAssetPath);
            table.rooms.Clear();
            var count = 0;
            foreach (var row in rows)
            {
                var type = Cell(row, col, "记录类型");
                var roomId = Cell(row, col, "房间id");
                if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(roomId)) continue;
                count++;
                if (type == "房间")
                {
                    if (table.Find(roomId) != null)
                    {
                        Debug.LogWarning($"[导表] 房间 id 重复：{roomId}，后一行已跳过");
                        continue;
                    }
                    table.rooms.Add(new FurnitureRoomEntry
                    {
                        id = roomId,
                        displayName = Cell(row, col, "显示名"),
                        sceneWidth = Float(row, col, "场景宽", 1672f),
                        sceneHeight = Float(row, col, "场景高", 941f),
                        background = LoadSprite(Cell(row, col, "背景图")),
                        depthBlurOverlay = LoadSprite(Cell(row, col, "景深模糊图")),
                        focusBlurOverlay = LoadSprite(Cell(row, col, "失焦模糊图")),
                        startCredit = Int(row, col, "初始货币", 0),
                        // 访客活动区/入口区（归一化，左下原点）：按房间美术红框标定，缺列时用通用默认带
                        visitorWalkArea = Rect.MinMaxRect(
                            Float(row, col, "访客区左", .04f), Float(row, col, "访客区下", .03f),
                            Float(row, col, "访客区右", .96f), Float(row, col, "访客区上", .35f)),
                        visitorEntryArea = Rect.MinMaxRect(
                            Float(row, col, "入口区左", .08f), Float(row, col, "入口区下", .15f),
                            Float(row, col, "入口区右", .18f), Float(row, col, "入口区上", .33f)),
                    });
                    continue;
                }
                var room = table.Find(roomId);
                if (room == null)
                {
                    Debug.LogWarning($"[导表] 记录「{type}」引用了未定义的房间 {roomId}（房间行必须在其明细行之前），已跳过");
                    count--;
                    continue;
                }
                switch (type)
                {
                    case "网格":
                        room.grids.Add(new FurnitureGridConfig
                        {
                            id = Cell(row, col, "网格id"),
                            surface = ParseSurface(Cell(row, col, "表面类型")),
                            cols = Int(row, col, "列数", 1),
                            rows = Int(row, col, "行数", 1),
                            cellWidth = Float(row, col, "格宽", 60f),
                            cellHeight = Float(row, col, "格高", 60f),
                            x = Float(row, col, "X", 0f),
                            y = Float(row, col, "Y", 0f),
                            farWidthScale = Float(row, col, "远端宽度比", 1f),
                        });
                        break;
                    case "占用格":
                        room.blockedCells.Add(new FurnitureBlockedCellConfig
                        {
                            gridId = Cell(row, col, "网格id"),
                            col = Int(row, col, "列", 0),
                            row = Int(row, col, "行", 0),
                        });
                        break;
                    case "初始摆放":
                        room.initialPlacements.Add(new FurniturePlacementConfig
                        {
                            furnitureId = Cell(row, col, "家具id"),
                            gridId = Cell(row, col, "网格id"),
                            hostFurnitureId = Cell(row, col, "宿主家具id"),
                            col = Int(row, col, "列", 0),
                            row = Int(row, col, "行", 0),
                            flipped = Bool(row, col, "翻转"),
                        });
                        break;
                    default:
                        Debug.LogWarning($"[导表] 未知记录类型「{type}」，已跳过（合法：房间/网格/占用格/初始摆放）");
                        count--;
                        break;
                }
            }
            EditorUtility.SetDirty(table);
            return count;
        }

        // ── 导出 ──

        private static void ExportFurnitureCsv()
        {
            var table = AssetDatabase.LoadAssetAtPath<FurnitureTable>(FurnitureAssetPath);
            if (table == null) { Debug.LogError("[导表] 导出失败：FurnitureTable.asset 缺失"); return; }
            var lines = new List<string> { string.Join(",", FurnitureHeader) };
            foreach (var e in table.entries)
            {
                if (e == null) continue;
                lines.Add(Line(e.id, e.nameKey, e.displayName, e.category, e.description, SurfacesText(e.surfaces),
                    e.stackable ? "是" : "否", e.cols, e.rows,
                    e.displayWidth, e.displayHeight, e.decorationScore,
                    SpritePath(e.sprite), "#" + ColorUtility.ToHtmlStringRGB(e.swatchColor),
                    AudioPath(e.pickupSound), AudioPath(e.putdownSound),
                    e.tableSurface != null && e.tableSurface.enabled ? "是" : "否",
                    e.tableSurface?.cols ?? 3, e.tableSurface?.cellWidth ?? 64f, e.tableSurface?.cellHeight ?? 56f,
                    e.tableSurface?.offsetX ?? 50f, e.tableSurface?.surfaceHeight ?? 146f));
            }
            WriteCsv(FurnitureCsvPath, lines);
        }

        private static void ExportStoreCsv()
        {
            var store = AssetDatabase.LoadAssetAtPath<StoreTable>(StoreAssetPath);
            if (store == null) { Debug.LogError("[导表] 导出失败：StoreTable.asset 缺失"); return; }
            var furniture = AssetDatabase.LoadAssetAtPath<FurnitureTable>(FurnitureAssetPath);
            var lines = new List<string> { string.Join(",", StoreHeader) };
            foreach (var e in store.entries)
            {
                if (e == null) continue;
                // 显示名只为对照阅读，从家具表取
                var known = furniture != null ? furniture.Find(e.furnitureId) : null;
                lines.Add(Line(e.furnitureId, known != null ? known.displayName : string.Empty,
                    e.price, e.unlockReputation));
            }
            WriteCsv(StoreCsvPath, lines);

            var categories = new List<string> { Line("分类名", "描述") };
            foreach (var category in store.categories)
                if (category != null) categories.Add(Line(category.name, category.desc));
            WriteCsv(StoreCategoryCsvPath, categories);
        }

        private static void ExportRoomCsv()
        {
            var table = AssetDatabase.LoadAssetAtPath<FurnitureRoomTable>(RoomAssetPath);
            if (table == null) { Debug.LogError("[导表] 导出失败：FurnitureRoomTable.asset 缺失"); return; }
            var lines = new List<string> { string.Join(",", RoomHeader) };
            foreach (var room in table.rooms)
            {
                if (room == null) continue;
                var walk = room.visitorWalkArea;
                var entry = room.visitorEntryArea;
                lines.Add(Line("房间", room.id, room.displayName, room.sceneWidth, room.sceneHeight,
                    SpritePath(room.background), SpritePath(room.depthBlurOverlay), SpritePath(room.focusBlurOverlay),
                    room.startCredit, walk.xMin, walk.yMin, walk.xMax, walk.yMax,
                    entry.xMin, entry.yMin, entry.xMax, entry.yMax,
                    "", "", "", "", "", "", "", "", "", "", "", "", ""));
                foreach (var grid in room.grids)
                    lines.Add(Line("网格", room.id, "", "", "", "", "", "", "", "", "", "", "", "", "", "", "",
                        grid.id, SurfaceName(grid.surface), grid.cols, grid.rows,
                        grid.cellWidth, grid.cellHeight, grid.x, grid.y, grid.farWidthScale, "", "", "", "", ""));
                foreach (var cell in room.blockedCells)
                    lines.Add(Line("占用格", room.id, "", "", "", "", "", "", "", "", "", "", "", "", "", "", "",
                        cell.gridId, "", "", "", "", "", "", "", "", "", "", cell.col, cell.row, ""));
                foreach (var place in room.initialPlacements)
                    lines.Add(Line("初始摆放", room.id, "", "", "", "", "", "", "", "", "", "", "", "", "", "", "",
                        place.gridId, "", "", "", "", "", "", "", "", place.furnitureId, place.hostFurnitureId,
                        place.col, place.row, place.flipped ? "是" : "否"));
            }
            WriteCsv(RoomCsvPath, lines);
        }

        // ── CSV 基础设施 ──

        private static List<string[]> ReadCsv(string path, out Dictionary<string, int> columns)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"配置表缺失：{path}（可先用菜单「导出家具三表到 CSV」从当前资产生成）");
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
                var text = cells[i] is float f ? f.ToString(CultureInfo.InvariantCulture)
                    : cells[i]?.ToString() ?? string.Empty;
                encoded[i] = text.IndexOfAny(new[] { ',', '"', '\n' }) >= 0
                    ? "\"" + text.Replace("\"", "\"\"") + "\""
                    : text;
            }
            return string.Join(",", encoded);
        }

        // ── 字段解析 ──

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

        private static float Float(string[] row, Dictionary<string, int> col, string name, float fallback)
        {
            var text = Cell(row, col, name);
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        private static bool Bool(string[] row, Dictionary<string, int> col, string name)
        {
            var text = Cell(row, col, name);
            return text == "是" || text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>解析多选表面类型（如「地面/桌面」，分隔符支持 / 、 ,）；空值回落地面。</summary>
        private static List<FurnitureSurfaceType> ParseSurfaces(string text)
        {
            var result = new List<FurnitureSurfaceType>();
            if (!string.IsNullOrEmpty(text))
                foreach (var token in text.Split('/', '、', ',', '，'))
                {
                    if (string.IsNullOrWhiteSpace(token)) continue;
                    var surface = ParseSurface(token.Trim());
                    if (!result.Contains(surface)) result.Add(surface);
                }
            if (result.Count == 0) result.Add(FurnitureSurfaceType.Floor);
            return result;
        }

        private static string SurfacesText(List<FurnitureSurfaceType> surfaces)
        {
            if (surfaces == null || surfaces.Count == 0) return "地面";
            var names = new string[surfaces.Count];
            for (var i = 0; i < surfaces.Count; i++) names[i] = SurfaceName(surfaces[i]);
            return string.Join("/", names);
        }

        private static FurnitureSurfaceType ParseSurface(string text) => text switch
        {
            "地面" or "Floor" or "0" => FurnitureSurfaceType.Floor,
            "桌面" or "Table" or "1" => FurnitureSurfaceType.Table,
            "壁挂" or "Wall" or "2" => FurnitureSurfaceType.Wall,
            _ => LogSurfaceFallback(text),
        };

        private static FurnitureSurfaceType LogSurfaceFallback(string text)
        {
            if (!string.IsNullOrEmpty(text))
                Debug.LogWarning($"[导表] 未知表面类型「{text}」，按「地面」处理（合法：地面/桌面/壁挂）");
            return FurnitureSurfaceType.Floor;
        }

        private static string SurfaceName(FurnitureSurfaceType surface) => surface switch
        {
            FurnitureSurfaceType.Table => "桌面",
            FurnitureSurfaceType.Wall => "壁挂",
            _ => "地面",
        };

        /// <summary>
        /// 精灵图列两种写法：①以 Assets/ 开头的完整资产路径（含扩展名），如 Assets/PC ui/Scene/furniture/xxx.png；
        /// ②Resources 相对路径（不带扩展名），如 OutGameUI/Furniture/table。空 = 无引用。
        /// SO 持有的是 Sprite 引用，运行时不经路径加载，所以素材不必位于 Resources 下。
        /// </summary>
        private static Sprite LoadSprite(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var sprite = path.StartsWith("Assets/")
                ? AssetDatabase.LoadAssetAtPath<Sprite>(path)
                : Resources.Load<Sprite>(path);
            if (sprite == null && path.StartsWith("Assets/") && System.IO.File.Exists(path))
            {
                // 自愈：素材存在但没按 Sprite 类型导入（3D 模板默认 Default）→ 改导入设置重导后重试
                if (AssetImporter.GetAtPath(path) is TextureImporter importer && importer.textureType != TextureImporterType.Sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.SaveAndReimport();
                    sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                    if (sprite != null) Debug.Log($"[导表] 已把素材导入类型改为 Sprite：{path}");
                }
            }
            if (sprite == null)
                Debug.LogWarning($"[导表] 精灵图未找到或未按 Sprite 导入：{path}（该行 sprite 置空）");
            return sprite;
        }

        /// <summary>色值列（#RRGGBB）；空/非法回落白色。</summary>
        private static Color ParseColor(string hex)
        {
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var color)) return color;
            return Color.white;
        }

        /// <summary>音效列写法与图片列一致：Assets/ 完整路径或 Resources 相对路径（不带扩展名）；空 = 用全局默认。</summary>
        private static AudioClip LoadAudio(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var clip = path.StartsWith("Assets/")
                ? AssetDatabase.LoadAssetAtPath<AudioClip>(path)
                : Resources.Load<AudioClip>(path);
            if (clip == null)
                Debug.LogWarning($"[导表] 音效剪辑未找到：{path}（该列置空，回落全局默认音）");
            return clip;
        }

        private static string AudioPath(AudioClip clip)
        {
            if (clip == null) return string.Empty;
            var assetPath = AssetDatabase.GetAssetPath(clip);
            const string prefix = "Assets/Resources/";
            if (!assetPath.StartsWith(prefix)) return assetPath;
            var relative = assetPath.Substring(prefix.Length);
            var dot = relative.LastIndexOf('.');
            return dot > 0 ? relative.Substring(0, dot) : relative;
        }

        private static string SpritePath(Sprite sprite)
        {
            if (sprite == null) return string.Empty;
            var assetPath = AssetDatabase.GetAssetPath(sprite);
            const string prefix = "Assets/Resources/";
            if (!assetPath.StartsWith(prefix)) return assetPath; // Resources 外的素材：写完整资产路径
            var relative = assetPath.Substring(prefix.Length);
            var dot = relative.LastIndexOf('.');
            return dot > 0 ? relative.Substring(0, dot) : relative;
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

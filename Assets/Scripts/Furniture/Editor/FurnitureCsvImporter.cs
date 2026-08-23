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
    /// 家具四表导表工具（流程仿 CatVsDog 的 export_config.bat）：
    /// 策划编辑 Excel/家具族表.xlsx / 家具表.xlsx / 商店表.xlsx / 家具房间表.xlsx → 双击 Tools/导表/export_config.bat
    /// 导出 Assets/Configs/*.csv → CSV 在 Assets 内，资产管线检测到变化即由 CsvPostprocessor
    /// **自动整表重建**对应 SO（FurnitureFamilyTable / FurnitureTable / FurnitureRoomTable），
    /// Unity 开着关着都不需要额外步骤。
    /// 精灵图在表里写 Resources 相对路径（如 OutGameUI/Furniture/table），导入时解析回 Sprite 引用。
    /// 反向「导出到 CSV」用于从当前资产重新生成 CSV（注意：xlsx 是编辑源，导出不会回写 xlsx）。
    ///
    /// **族表 → 家具表的展开（家具族体系说明 §3.3）**：族级共有属性（分类/表面/占格/装饰分/音效/桌面格）
    /// 只在族表填一次，导入时按每行的「族id」查族表**展开填进** FurnitureEntry。因此
    /// <see cref="ImportFamilyCsv"/> 必须先于 <see cref="ImportFurnitureCsv"/> 执行——顺序由
    /// <see cref="ImportAll"/> 一处保证，不依赖两个 postprocessor 赛跑。
    /// </summary>
    public static class FurnitureCsvImporter
    {
        private const string FamilyAssetPath = "Assets/Resources/OutGameUI/FurnitureFamilyTable.asset";
        private const string FurnitureAssetPath = "Assets/Resources/OutGameUI/FurnitureTable.asset";
        private const string StoreAssetPath = "Assets/Resources/OutGameUI/StoreTable.asset";
        private const string RoomAssetPath = "Assets/Resources/OutGameUI/FurnitureRoomTable.asset";
        private const string AutoImportPrefKey = "MasterHouse.FurnitureCsvAutoImport";
        /// <summary>CSV 落在 Assets 内（Assets/Configs/），资产管线才能自动感知变化。</summary>
        private const string ConfigsDir = "Assets/Configs";
        private const string FamilyCsvPath = ConfigsDir + "/家具族表.csv";
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
                    if (path != FamilyCsvPath && path != FurnitureCsvPath && path != StoreCsvPath &&
                        path != StoreCategoryCsvPath && path != RoomCsvPath) continue;
                    if (suppressNextAutoImport) { suppressNextAutoImport = false; return; }
                    if (!AutoImportEnabled) return;
                    Debug.Log("[导表] 检测到 Assets/Configs 配置表更新，自动导入…（可在菜单 MasterHouse → 家具系统 关闭）");
                    // 延迟到导入管线结束后执行，避免在 postprocess 回调里写资产
                    EditorApplication.delayCall += ImportAll;
                    return;
                }
            }
        }

        /// <summary>家具族表（族级共有属性住这里，一行一个族）。</summary>
        private static readonly string[] FamilyHeader =
        {
            "族id", "族显示名", "分类", "描述", "表面类型", "可叠放", "占格列", "占格行", "装饰分", "拿起音效", "放下音效",
            "桌面格启用", "桌面格列数", "桌面格宽", "桌面格高", "桌面格偏移X", "桌面高度",
        };

        /// <summary>
        /// 家具表（只剩**逐变体不同**的列）：族级列已搬去族表，
        /// 占格因不同变体宽度可能不同，仍由家具表逐行保存；空值回退族表默认值。
        /// </summary>
        private static readonly string[] FurnitureHeader =
        {
            "id", "英文索引", "显示名", "族id", "显示宽", "显示高", "精灵图", "色值", "商店显示宽", "商店显示高", "商店列表图", "商店详情图", "占格列", "占格行",
        };

        /// <summary>商店表（售卖配置）：按 id 合回 FurnitureTable 对应条目（2026-08-13 从家具表拆出）。</summary>
        private static readonly string[] StoreHeader = { "id", "显示名", "价格", "解禁声望" };

        private static readonly string[] RoomHeader =
        {
            "记录类型", "房间id", "显示名", "场景宽", "场景高", "背景图", "景深模糊图", "失焦模糊图", "初始货币",
            "访客区左", "访客区下", "访客区右", "访客区上", "入口区左", "入口区下", "入口区右", "入口区上",
            "白天墙脚线", "夜间墙脚线", "夜间横向缩放", "夜间横向偏移",
            "网格id", "表面类型", "列数", "行数", "格宽", "格高", "X", "Y", "远端宽度比",
            "家具id", "宿主家具id", "列", "行", "翻转",
        };

        // ── 入口 ──

        [MenuItem("MasterHouse/家具系统/从 CSV 导入家具四表")]
        public static void ImportAll()
        {
            // 顺序有依赖：族表必须先解析，家具表逐行按「族id」查它展开族级属性（§3.3）
            var family = ImportFamilyCsv();
            var furniture = ImportFurnitureCsv(family);
            var store = ImportStoreCsv(furniture);
            var rooms = ImportRoomCsv();
            AssetDatabase.SaveAssets();
            Debug.Log($"[导表] 完成：家具族 {family.entries.Count} 行、家具 {furniture.entries.Count} 行、" +
                      $"商店 {store} 行、房间记录 {rooms} 行 " +
                      "→ FurnitureFamilyTable / FurnitureTable / StoreTable / FurnitureRoomTable");
        }

        [MenuItem("MasterHouse/家具系统/导出家具四表到 CSV")]
        public static void ExportAll()
        {
            Directory.CreateDirectory(ConfigsDir);
            suppressNextAutoImport = true; // 导出内容本就来自资产，跳过随之而来的自动回导
            ExportFamilyCsv();
            ExportFurnitureCsv();
            ExportStoreCsv();
            ExportRoomCsv();
            AssetDatabase.Refresh();
            Debug.Log($"[导表] 已导出到 {ConfigsDir}/家具族表.csv、家具表.csv、商店表.csv、家具房间表.csv");
        }

        // ── 导入 ──

        /// <summary>
        /// 家具族表 → FurnitureFamilyTable.asset。族级共有属性的唯一来源（§3.2）。
        /// 族 id / 族显示名是必填：缺了就没法被家具行引用、也没法在商城与收纳栏上屏，故按错误处理。
        /// </summary>
        private static FurnitureFamilyTable ImportFamilyCsv()
        {
            var rows = ReadCsv(FamilyCsvPath, out var col);
            var table = LoadOrCreate<FurnitureFamilyTable>(FamilyAssetPath);
            table.entries.Clear();
            var seen = new HashSet<string>();
            foreach (var row in rows)
            {
                var familyId = Cell(row, col, "族id");
                if (string.IsNullOrEmpty(familyId))
                {
                    Debug.LogError($"[导表] 家具族表{Where(row)}的「族id」是空的，该行已跳过");
                    continue;
                }
                if (!seen.Add(familyId))
                {
                    Debug.LogError($"[导表] 家具族表{Where(row)}的族 id 重复：{familyId}，后一行已跳过");
                    continue;
                }
                var displayName = Cell(row, col, "族显示名");
                if (string.IsNullOrEmpty(displayName))
                    Debug.LogError($"[导表] 家具族表{Where(row)}的族「{familyId}」没填族显示名，" +
                                   "商城卡片与收纳栏槽位将显示族 id");
                table.entries.Add(new FurnitureFamilyEntry
                {
                    familyId = familyId,
                    displayName = displayName,
                    category = Cell(row, col, "分类"),
                    description = Cell(row, col, "描述"),
                    surfaces = ParseSurfaces(Cell(row, col, "表面类型")),
                    stackable = Bool(row, col, "可叠放"),
                    cols = Int(row, col, "占格列", 1),
                    rows = Int(row, col, "占格行", 1),
                    decorationScore = Int(row, col, "装饰分", 0),
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
                });
            }
            EditorUtility.SetDirty(table);
            return table;
        }

        /// <summary>
        /// 家具表 → FurnitureTable.asset。**逐行按「族id」查族表，把 11 个族级字段展开填进每一行**（§3.3）。
        ///
        /// 引用了不存在的族 → <b>LogError 指名行号并跳过该行</b>：不静默用默认值，否则会得到一件
        /// 占格 1×1 的沙发，摆进房间才发现，极难查。族没有任何成员只打 Warning（可能是刚建还没配家具）。
        /// </summary>
        private static FurnitureTable ImportFurnitureCsv(FurnitureFamilyTable families)
        {
            var rows = ReadCsv(FurnitureCsvPath, out var col);
            var table = LoadOrCreate<FurnitureTable>(FurnitureAssetPath);
            table.entries.Clear();
            var usedFamilies = new HashSet<string>();
            foreach (var row in rows)
            {
                var id = Cell(row, col, "id");
                if (string.IsNullOrEmpty(id))
                {
                    Debug.LogWarning($"[导表] 家具表{Where(row)}缺 id，已跳过");
                    continue;
                }
                var familyId = Cell(row, col, "族id");
                var family = families != null ? families.Find(familyId) : null;
                if (family == null)
                {
                    Debug.LogError($"[导表] 家具表{Where(row)}的「{id}」引用了不存在的族 id：" +
                                   $"「{familyId}」——该行已跳过（请在 Excel/家具族表.xlsx 里补这个族）");
                    continue;
                }
                usedFamilies.Add(familyId);
                table.entries.Add(new FurnitureEntry
                {
                    // ── 变体特有：来自家具表本行 ──
                    id = id,
                    nameKey = Cell(row, col, "英文索引"),
                    displayName = Cell(row, col, "显示名"),
                    familyId = familyId,
                    displayWidth = Float(row, col, "显示宽", 100f),
                    displayHeight = Float(row, col, "显示高", 100f),
                    cols = Int(row, col, "占格列", family.cols),
                    rows = Int(row, col, "占格行", family.rows),
                    storeDisplayWidth = Float(row, col, "商店显示宽", 100f),
                    storeDisplayHeight = Float(row, col, "商店显示高", 100f),
                    sprite = LoadSprite(Cell(row, col, "精灵图")),
                    storeListSprite = LoadSprite(Cell(row, col, "商店列表图")),
                    storePreviewSprite = LoadSprite(Cell(row, col, "商店详情图")),
                    swatchColor = ParseColor(Cell(row, col, "色值")),
                    // ── 族级：从族表展开（改这些值请改族表，改这里会被下次导表覆盖）──
                    category = family.category,
                    description = family.description,
                    surfaces = new List<FurnitureSurfaceType>(family.surfaces ?? new List<FurnitureSurfaceType>()),
                    stackable = family.stackable,
                    decorationScore = family.decorationScore,
                    pickupSound = family.pickupSound,
                    putdownSound = family.putdownSound,
                    // 值类型语义：每行一份拷贝，避免整族共用同一个引用（改一个变体动全族）
                    tableSurface = CopyTableSurface(family.tableSurface),
                });
            }
            foreach (var family in families != null ? families.entries : new List<FurnitureFamilyEntry>())
                if (family != null && !string.IsNullOrEmpty(family.familyId) && !usedFamilies.Contains(family.familyId))
                    Debug.LogWarning($"[导表] 族「{family.familyId}」在家具表里没有任何成员" +
                                     "（刚建还没配家具就是正常的，不阻塞）");
            EditorUtility.SetDirty(table);
            return table;
        }

        private static FurnitureTableSurfaceConfig CopyTableSurface(FurnitureTableSurfaceConfig source)
        {
            if (source == null) return new FurnitureTableSurfaceConfig();
            return new FurnitureTableSurfaceConfig
            {
                enabled = source.enabled,
                cols = source.cols,
                cellWidth = source.cellWidth,
                cellHeight = source.cellHeight,
                offsetX = source.offsetX,
                surfaceHeight = source.surfaceHeight,
            };
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

        /// <summary>待解析的宿主引用：CSV 里填的是家具 id，落盘前要翻译成宿主的落位坐标（见 ResolveHostReferences）。</summary>
        private readonly struct PendingHost
        {
            public readonly FurnitureRoomEntry Room;
            public readonly FurniturePlacementConfig Placement;
            public readonly string HostFurnitureId;
            public readonly CsvRow Row;

            public PendingHost(FurnitureRoomEntry room, FurniturePlacementConfig placement, string hostFurnitureId, CsvRow row)
            {
                Room = room;
                Placement = placement;
                HostFurnitureId = hostFurnitureId;
                Row = row;
            }
        }

        private static int ImportRoomCsv()
        {
            var rows = ReadCsv(RoomCsvPath, out var col);
            var table = LoadOrCreate<FurnitureRoomTable>(RoomAssetPath);
            table.rooms.Clear();
            var pendingHosts = new List<PendingHost>();
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
                        // 昼夜几何校正（2026-08-18）：两张房间图的墙脚线高度不同，
                        // 网格按白天图标定，夜里按这两条线做分段线性映射，免得地面格爬到墙上
                        dayFloorLine = Float(row, col, "白天墙脚线", .8f),
                        nightFloorLine = Float(row, col, "夜间墙脚线", .8f),
                        nightWidthScale = Float(row, col, "夜间横向缩放", 1f),
                        nightShiftX = Float(row, col, "夜间横向偏移", 0f),
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
                        var placement = new FurniturePlacementConfig
                        {
                            furnitureId = Cell(row, col, "家具id"),
                            gridId = Cell(row, col, "网格id"),
                            col = Int(row, col, "列", 0),
                            row = Int(row, col, "行", 0),
                            flipped = Bool(row, col, "翻转"),
                        };
                        room.initialPlacements.Add(placement);
                        // 宿主用坐标存（§5.4），但 CSV 里填的是家具 id——攒起来等全表读完再解析：
                        // 宿主行可能排在被托管行**之后**，边读边查会漏
                        var hostFurnitureId = Cell(row, col, "宿主家具id");
                        if (!string.IsNullOrEmpty(hostFurnitureId))
                            pendingHosts.Add(new PendingHost(room, placement, hostFurnitureId, row));
                        break;
                    default:
                        Debug.LogWarning($"[导表] 未知记录类型「{type}」，已跳过（合法：房间/网格/占用格/初始摆放）");
                        count--;
                        break;
                }
            }
            ResolveHostReferences(pendingHosts);
            EditorUtility.SetDirty(table);
            return count;
        }

        /// <summary>
        /// 把「宿主家具id」翻译成宿主的落位坐标（家具库存说明 §5.4）。
        ///
        /// 资产里存坐标是因为家具可重复购买后同房间能摆多件同款，家具 id 不再唯一标识一件实例；
        /// 而 CSV 仍让策划填家具 id——填坐标反直觉，翻译交给导表器做。
        ///
        /// 同房间有多个同 id 候选时**报错并丢弃该行**（不静默取第一个）：那正是坐标要解决的歧义，
        /// 静默取第一个等于把 bug 从运行时挪到导表期。策划想在两张同款桌上放不同东西，换个配色即可。
        /// </summary>
        private static void ResolveHostReferences(List<PendingHost> pending)
        {
            foreach (var item in pending)
            {
                FurniturePlacementConfig host = null;
                var matches = 0;
                foreach (var candidate in item.Room.initialPlacements)
                {
                    // 宿主必须是基础家具（自己不在别人桌上），否则会出现「桌上的桌子」这种没有实现支撑的嵌套
                    if (candidate == null || candidate.IsOnHost || candidate.furnitureId != item.HostFurnitureId) continue;
                    matches++;
                    if (host == null) host = candidate;
                }
                if (matches == 0)
                {
                    Debug.LogError($"[导表] 家具房间表{Where(item.Row)}的「{item.Placement.furnitureId}」" +
                                   $"指定了宿主「{item.HostFurnitureId}」，但房间「{item.Room.id}」里没有摆放它——" +
                                   "该行已丢弃（宿主必须是同房间的一条基础摆放记录）");
                    item.Room.initialPlacements.Remove(item.Placement);
                    continue;
                }
                if (matches > 1)
                {
                    Debug.LogError($"[导表] 家具房间表{Where(item.Row)}的宿主「{item.HostFurnitureId}」在房间" +
                                   $"「{item.Room.id}」里摆了 {matches} 件，指不明是哪一件——该行已丢弃。" +
                                   "请把其中一件换成同族的别的配色，宿主才能唯一确定");
                    item.Room.initialPlacements.Remove(item.Placement);
                    continue;
                }
                item.Placement.hostGridId = host.gridId;
                item.Placement.hostCol = host.col;
                item.Placement.hostRow = host.row;
            }
        }

        // ── 导出 ──

        private static void ExportFamilyCsv()
        {
            var table = AssetDatabase.LoadAssetAtPath<FurnitureFamilyTable>(FamilyAssetPath);
            if (table == null) { Debug.LogError("[导表] 导出失败：FurnitureFamilyTable.asset 缺失"); return; }
            var lines = new List<string> { string.Join(",", FamilyHeader) };
            foreach (var f in table.entries)
            {
                if (f == null) continue;
                lines.Add(Line(f.familyId, f.displayName, f.category, f.description, SurfacesText(f.surfaces),
                    f.stackable ? "是" : "否", f.cols, f.rows, f.decorationScore,
                    AudioPath(f.pickupSound), AudioPath(f.putdownSound),
                    f.tableSurface != null && f.tableSurface.enabled ? "是" : "否",
                    f.tableSurface?.cols ?? 3, f.tableSurface?.cellWidth ?? 64f, f.tableSurface?.cellHeight ?? 56f,
                    f.tableSurface?.offsetX ?? 50f, f.tableSurface?.surfaceHeight ?? 146f));
            }
            WriteCsv(FamilyCsvPath, lines);
        }

        /// <summary>家具表只导出变体列——族级字段是导入时展开进来的产物，回写它们等于给数值开第二个家（§3.2）。</summary>
        private static void ExportFurnitureCsv()
        {
            var table = AssetDatabase.LoadAssetAtPath<FurnitureTable>(FurnitureAssetPath);
            if (table == null) { Debug.LogError("[导表] 导出失败：FurnitureTable.asset 缺失"); return; }
            var lines = new List<string> { string.Join(",", FurnitureHeader) };
            foreach (var e in table.entries)
            {
                if (e == null) continue;
                lines.Add(Line(e.id, e.nameKey, e.displayName, e.familyId,
                    e.displayWidth, e.displayHeight,
                    SpritePath(e.sprite), "#" + ColorUtility.ToHtmlStringRGB(e.swatchColor),
                    e.storeDisplayWidth, e.storeDisplayHeight,
                    SpritePath(e.storeListSprite), SpritePath(e.storePreviewSprite),
                    e.cols, e.rows));
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
                        place.gridId, "", "", "", "", "", "", "", "", place.furnitureId, HostFurnitureIdOf(room, place),
                        place.col, place.row, place.flipped ? "是" : "否"));
            }
            WriteCsv(RoomCsvPath, lines);
        }

        /// <summary>
        /// 导出侧的反向翻译：资产里存的是宿主坐标，CSV 里要写回家具 id（策划视角，见 ResolveHostReferences）。
        /// 找不到对应格子上的基础摆放说明资产被手改坏了，写空并 Warning——写个错的 id 会让下次导入静默出错。
        /// </summary>
        private static string HostFurnitureIdOf(FurnitureRoomEntry room, FurniturePlacementConfig place)
        {
            if (place == null || !place.IsOnHost) return string.Empty;
            foreach (var candidate in room.initialPlacements)
                if (candidate != null && candidate.OccupiesBaseCell(place.hostGridId, place.hostCol, place.hostRow))
                    return candidate.furnitureId;
            Debug.LogWarning($"[导表] 房间「{room.id}」的「{place.furnitureId}」指向的宿主格子 " +
                             $"({place.hostGridId} {place.hostCol},{place.hostRow}) 上没有基础家具，宿主列导出为空");
            return string.Empty;
        }

        // ── CSV 基础设施 ──

        /// <summary>
        /// CSV 的一行 + 它在文件里的行号。带行号是为了让「引用了不存在的族」这类报错**能指名行号**，
        /// 策划照着数字就能在 Excel 里定位（<see cref="Where"/> 负责把 CSV 行号换算成 Excel 行号）。
        /// </summary>
        private readonly struct CsvRow
        {
            public readonly string[] Cells;
            public readonly int Line;
            public CsvRow(string[] cells, int line) { Cells = cells; Line = line; }
        }

        /// <summary>
        /// 报错定位串。CSV 与 Excel 差一行：Excel 第 1 行是中文列名、第 2 行是字段名参考行
        /// （导出脚本会跳过它），所以 Excel 行号 = CSV 行号 + 1。
        /// </summary>
        private static string Where(CsvRow row) => $"第 {row.Line} 行（Excel 第 {row.Line + 1} 行）";

        private static List<CsvRow> ReadCsv(string path, out Dictionary<string, int> columns)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"配置表缺失：{path}（可先用菜单「导出家具四表到 CSV」从当前资产生成）");
            var rows = new List<CsvRow>();
            columns = null;
            var line = 0;
            foreach (var rawLine in File.ReadAllLines(path, Encoding.UTF8))
            {
                line++;
                if (string.IsNullOrWhiteSpace(rawLine)) continue;
                var cells = ParseCsvLine(rawLine);
                if (columns == null)
                {
                    columns = new Dictionary<string, int>();
                    for (var i = 0; i < cells.Length; i++)
                        columns[cells[i].Trim()] = i; // 表头按列名索引，列顺序可自由调整
                    continue;
                }
                rows.Add(new CsvRow(cells, line));
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

        private static string Cell(CsvRow row, Dictionary<string, int> col, string name)
        {
            if (!col.TryGetValue(name, out var index) || index >= row.Cells.Length) return string.Empty;
            return row.Cells[index].Trim();
        }

        private static int Int(CsvRow row, Dictionary<string, int> col, string name, int fallback)
        {
            var text = Cell(row, col, name);
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        private static float Float(CsvRow row, Dictionary<string, int> col, string name, float fallback)
        {
            var text = Cell(row, col, name);
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        private static bool Bool(CsvRow row, Dictionary<string, int> col, string name)
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

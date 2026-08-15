using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// 家具系统配置表生成器。默认菜单只补齐缺失资产，不覆盖手工调整；
    /// 「重建默认配置表」会覆盖全部行，需要弹窗确认。
    /// </summary>
    public static class FurnitureConfigSetupUtility
    {
        private const string ResourceDir = "Assets/Resources/OutGameUI";
        private const string SpriteDir = ResourceDir + "/Furniture";
        private const string FurnitureTablePath = ResourceDir + "/FurnitureTable.asset";
        private const string FamilyTablePath = ResourceDir + "/FurnitureFamilyTable.asset";
        private const string StoreTablePath = ResourceDir + "/StoreTable.asset";
        private const string RoomTablePath = ResourceDir + "/FurnitureRoomTable.asset";
        private const string EconomyConfigPath = ResourceDir + "/HouseEconomyConfig.asset";

        [MenuItem("MasterHouse/家具系统/创建配置表（补齐缺失）")]
        public static void CreateIfMissing()
        {
            var created = new List<string>();
            var furniture = AssetDatabase.LoadAssetAtPath<FurnitureTable>(FurnitureTablePath);
            if (furniture == null)
            {
                furniture = ScriptableObject.CreateInstance<FurnitureTable>();
                FillDefaultFurniture(furniture);
                AssetDatabase.CreateAsset(furniture, FurnitureTablePath);
                created.Add(FurnitureTablePath);
            }
            var families = AssetDatabase.LoadAssetAtPath<FurnitureFamilyTable>(FamilyTablePath);
            if (families == null)
            {
                families = ScriptableObject.CreateInstance<FurnitureFamilyTable>();
                FillDefaultFamilies(families, furniture);
                AssetDatabase.CreateAsset(families, FamilyTablePath);
                created.Add(FamilyTablePath);
            }
            var store = AssetDatabase.LoadAssetAtPath<StoreTable>(StoreTablePath);
            if (store == null)
            {
                store = ScriptableObject.CreateInstance<StoreTable>();
                FillDefaultStore(store);
                AssetDatabase.CreateAsset(store, StoreTablePath);
                created.Add(StoreTablePath);
            }
            var rooms = AssetDatabase.LoadAssetAtPath<FurnitureRoomTable>(RoomTablePath);
            if (rooms == null)
            {
                rooms = ScriptableObject.CreateInstance<FurnitureRoomTable>();
                FillDefaultRooms(rooms);
                AssetDatabase.CreateAsset(rooms, RoomTablePath);
                created.Add(RoomTablePath);
            }
            var economy = AssetDatabase.LoadAssetAtPath<EconomyConfig>(EconomyConfigPath);
            if (economy == null)
            {
                economy = ScriptableObject.CreateInstance<EconomyConfig>();
                AssetDatabase.CreateAsset(economy, EconomyConfigPath);
                created.Add(EconomyConfigPath);
            }
            AssetDatabase.SaveAssets();
            Debug.Log(created.Count > 0
                ? "[Furniture] 已创建配置表：" + string.Join("、", created)
                : "[Furniture] 配置表已存在，未做修改。若需恢复默认，请使用「重建默认配置表（覆盖）」。");
        }

        [MenuItem("MasterHouse/家具系统/重建默认配置表（覆盖）")]
        public static void RebuildDefaults()
        {
            if (!EditorUtility.DisplayDialog("重建默认配置表",
                    "将覆盖 FurnitureTable、FurnitureFamilyTable、StoreTable 与 FurnitureRoomTable 的全部行，手工调整会丢失。确认继续？",
                    "覆盖重建", "取消"))
                return;

            var furniture = AssetDatabase.LoadAssetAtPath<FurnitureTable>(FurnitureTablePath);
            if (furniture == null)
            {
                furniture = ScriptableObject.CreateInstance<FurnitureTable>();
                AssetDatabase.CreateAsset(furniture, FurnitureTablePath);
            }
            FillDefaultFurniture(furniture);
            EditorUtility.SetDirty(furniture);

            var families = AssetDatabase.LoadAssetAtPath<FurnitureFamilyTable>(FamilyTablePath);
            if (families == null)
            {
                families = ScriptableObject.CreateInstance<FurnitureFamilyTable>();
                AssetDatabase.CreateAsset(families, FamilyTablePath);
            }
            FillDefaultFamilies(families, furniture);
            EditorUtility.SetDirty(families);

            var store = AssetDatabase.LoadAssetAtPath<StoreTable>(StoreTablePath);
            if (store == null)
            {
                store = ScriptableObject.CreateInstance<StoreTable>();
                AssetDatabase.CreateAsset(store, StoreTablePath);
            }
            FillDefaultStore(store);
            EditorUtility.SetDirty(store);

            var rooms = AssetDatabase.LoadAssetAtPath<FurnitureRoomTable>(RoomTablePath);
            if (rooms == null)
            {
                rooms = ScriptableObject.CreateInstance<FurnitureRoomTable>();
                AssetDatabase.CreateAsset(rooms, RoomTablePath);
            }
            FillDefaultRooms(rooms);
            EditorUtility.SetDirty(rooms);

            var economy = AssetDatabase.LoadAssetAtPath<EconomyConfig>(EconomyConfigPath);
            if (economy == null)
            {
                economy = ScriptableObject.CreateInstance<EconomyConfig>();
                AssetDatabase.CreateAsset(economy, EconomyConfigPath);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[Furniture] 已重建默认配置表。");
        }

        private static Sprite LoadSprite(string fileName)
        {
            var path = $"{SpriteDir}/{fileName}.png";
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) Debug.LogWarning("[Furniture] 精灵缺失：" + path);
            return sprite;
        }

        private static FurnitureEntry Entry(string id, string name, FurnitureSurfaceType surface,
            int cols, int rows, float width, float height, int decorationScore,
            FurnitureTableSurfaceConfig table = null)
        {
            return new FurnitureEntry
            {
                id = id,
                nameKey = id, // 示例内容的英文索引即 id（table/pouf 等英文短名）
                displayName = name,
                // 示例内容没有换色变体，一件家具自成一族（族 id 即 id）。真实的 15 个族来自 Excel/家具族表.xlsx
                familyId = id,
                surfaces = new List<FurnitureSurfaceType> { surface },
                cols = cols,
                rows = rows,
                displayWidth = width,
                displayHeight = height,
                decorationScore = decorationScore,
                sprite = LoadSprite(id),
                tableSurface = table ?? new FurnitureTableSurfaceConfig(),
            };
        }

        private static StoreEntry Sale(string furnitureId, int price, int unlockReputation) =>
            new StoreEntry { furnitureId = furnitureId, price = price, unlockReputation = unlockReputation };

        private static void FillDefaultFurniture(FurnitureTable table)
        {
            table.entries = new List<FurnitureEntry>
            {
                // ── 起居室背景抠图切片 ──
                Entry("table", "圆木茶几", FurnitureSurfaceType.Floor, 4, 2, 282, 184, 40,
                    new FurnitureTableSurfaceConfig { enabled = true, cols = 3, cellWidth = 64, cellHeight = 56, offsetX = 50, surfaceHeight = 146 }),
                Entry("pouf", "黄绒蒲团", FurnitureSurfaceType.Floor, 3, 1, 180, 100, 15),
                Entry("vase", "白花花瓶", FurnitureSurfaceType.Table, 1, 1, 117, 186, 10),
                Entry("cups", "茶杯与书", FurnitureSurfaceType.Table, 1, 1, 116, 84, 8),
                Entry("lamp", "红罩台灯", FurnitureSurfaceType.Table, 1, 1, 84, 112, 12),
                Entry("picture", "山月挂画", FurnitureSurfaceType.Wall, 1, 2, 86, 118, 18),
                Entry("hangplant", "悬挂绿植", FurnitureSurfaceType.Wall, 2, 3, 118, 162, 20),
                Entry("bag", "帆布挂包", FurnitureSurfaceType.Wall, 1, 2, 82, 138, 10),
                // ── 叙事家具（Furniture 目录原有素材） ──
                Entry("whale-call", "鲸声电话亭", FurnitureSurfaceType.Floor, 4, 2, 250, 290, 80),
                Entry("moon-planter", "月亮花架", FurnitureSurfaceType.Floor, 2, 1, 130, 150, 45),
                Entry("dandelion-lamp", "蒲公英灯", FurnitureSurfaceType.Table, 1, 1, 80, 95, 30),
                Entry("wind-chimes", "兔耳风铃", FurnitureSurfaceType.Wall, 1, 2, 90, 130, 35),
                Entry("string-window", "琴弦窗户", FurnitureSurfaceType.Wall, 3, 3, 185, 160, 60),
            };
        }

        /// <summary>
        /// 家具族表默认内容：**从已填好的家具表反推**，一件家具一个族。
        /// 反推而不是再抄一份字面量，是为了让两张表天然一致——示例内容本来就没有换色变体，
        /// 族在这里只是个「一族一件」的退化形态，真实的 15 个族由 Excel/家具族表.xlsx 导入。
        /// </summary>
        private static void FillDefaultFamilies(FurnitureFamilyTable families, FurnitureTable furniture)
        {
            families.entries = new List<FurnitureFamilyEntry>();
            foreach (var entry in furniture.entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.familyId)) continue;
                families.entries.Add(new FurnitureFamilyEntry
                {
                    familyId = entry.familyId,
                    displayName = entry.displayName,
                    category = entry.category,
                    description = entry.description,
                    surfaces = new List<FurnitureSurfaceType>(entry.surfaces),
                    stackable = entry.stackable,
                    cols = entry.cols,
                    rows = entry.rows,
                    decorationScore = entry.decorationScore,
                    pickupSound = entry.pickupSound,
                    putdownSound = entry.putdownSound,
                    tableSurface = entry.tableSurface,
                });
            }
        }

        /// <summary>
        /// 商店表默认内容（2026-08-13 从家具表拆出）：价格=货币去处；解禁声望=声望的正反馈
        /// （初始声望 40，完成一次服务 +25）。**不列在这里的家具即非卖品**（等价价格 0，初始拥有）。
        /// </summary>
        private static void FillDefaultStore(StoreTable table)
        {
            table.entries = new List<StoreEntry>
            {
                Sale("lamp", 150, 60),
                Sale("bag", 300, 80),
                Sale("whale-call", 500, 200),
                Sale("moon-planter", 260, 120),
                Sale("dandelion-lamp", 180, 100),
                Sale("wind-chimes", 220, 90),
                Sale("string-window", 420, 160),
            };
        }

        private static void FillDefaultRooms(FurnitureRoomTable table)
        {
            var living = new FurnitureRoomEntry
            {
                id = "living",
                displayName = "起居室",
                sceneWidth = 1672,
                sceneHeight = 941,
                background = LoadSprite("room-living-clean"),
                depthBlurOverlay = LoadSprite("room-living-depthblur"),
                focusBlurOverlay = LoadSprite("room-living-blur"),
                startCredit = 2480,
                grids = new List<FurnitureGridConfig>
                {
                    new FurnitureGridConfig { id = "floor", surface = FurnitureSurfaceType.Floor, cols = 14, rows = 4, cellWidth = 60, cellHeight = 45, x = 400, y = 610 },
                    new FurnitureGridConfig { id = "wallL", surface = FurnitureSurfaceType.Wall, cols = 6, rows = 3, cellWidth = 60, cellHeight = 60, x = 90, y = 290 },
                    new FurnitureGridConfig { id = "wallR", surface = FurnitureSurfaceType.Wall, cols = 4, rows = 3, cellWidth = 60, cellHeight = 60, x = 1290, y = 260 },
                },
                initialPlacements = new List<FurniturePlacementConfig>
                {
                    new FurniturePlacementConfig { furnitureId = "table", gridId = "floor", col = 4, row = 2 },
                    new FurniturePlacementConfig { furnitureId = "pouf", gridId = "floor", col = 1, row = 2 },
                    new FurniturePlacementConfig { furnitureId = "picture", gridId = "wallL", col = 4, row = 0 },
                    new FurniturePlacementConfig { furnitureId = "hangplant", gridId = "wallR", col = 1, row = 0 },
                    // 桌面家具认宿主的**落位坐标**而不是家具 id（§5.4）：下面两件都放在上面那张 table 上
                    new FurniturePlacementConfig { furnitureId = "vase", hostGridId = "floor", hostCol = 4, hostRow = 2, col = 1, row = 0 },
                    new FurniturePlacementConfig { furnitureId = "cups", hostGridId = "floor", hostCol = 4, hostRow = 2, col = 2, row = 0 },
                },
            };
            // 场景占用格：沙发与人物（右半区）、看书的角色（左端）、落地灯与音箱（后排）
            for (var c = 8; c <= 13; c++)
                for (var r = 0; r <= 3; r++)
                    living.blockedCells.Add(new FurnitureBlockedCellConfig { gridId = "floor", col = c, row = r });
            living.blockedCells.Add(new FurnitureBlockedCellConfig { gridId = "floor", col = 0, row = 0 });
            living.blockedCells.Add(new FurnitureBlockedCellConfig { gridId = "floor", col = 1, row = 0 });
            living.blockedCells.Add(new FurnitureBlockedCellConfig { gridId = "floor", col = 0, row = 1 });
            living.blockedCells.Add(new FurnitureBlockedCellConfig { gridId = "floor", col = 1, row = 1 });
            living.blockedCells.Add(new FurnitureBlockedCellConfig { gridId = "floor", col = 3, row = 0 });
            living.blockedCells.Add(new FurnitureBlockedCellConfig { gridId = "floor", col = 4, row = 0 });

            table.rooms = new List<FurnitureRoomEntry> { living };
        }
    }
}

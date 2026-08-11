using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>桌面格配置：地面家具（如茶几）可携带一条桌面网格，供桌面家具摆放。</summary>
    [Serializable]
    public sealed class FurnitureTableSurfaceConfig
    {
        [Tooltip("是否提供桌面格")] public bool enabled;
        [Tooltip("桌面格列数")] public int cols = 3;
        [Tooltip("单元格宽（场景像素）")] public float cellWidth = 64f;
        [Tooltip("单元格高（场景像素）")] public float cellHeight = 56f;
        [Tooltip("桌面格相对家具左边缘的横向偏移（场景像素）")] public float offsetX = 50f;
        [Tooltip("桌面平面距家具底边的高度（场景像素）")] public float surfaceHeight = 146f;
    }

    /// <summary>家具配置表中的一行。</summary>
    [Serializable]
    public sealed class FurnitureEntry
    {
        [Tooltip("唯一 id，摆放与存档都用它引用")] public string id;
        [Tooltip("显示名称")] public string displayName;
        [Tooltip("商店分类（盆栽/摆件/桌椅/壁挂/灯具，商店页签用）")] public string category;
        [Tooltip("商店描述文案")] public string description;
        [Tooltip("可吸附的表面类型（可多选：如纸箱既可地面也可桌面；表格里用 / 分隔）")]
        public List<FurnitureSurfaceType> surfaces = new List<FurnitureSurfaceType> { FurnitureSurfaceType.Floor };
        [Tooltip("占格：列数")] public int cols = 1;
        [Tooltip("占格：行数")] public int rows = 1;
        [Tooltip("显示宽度（场景像素）")] public float displayWidth = 100f;
        [Tooltip("显示高度（场景像素）")] public float displayHeight = 100f;
        [Tooltip("购买价格（货币）；0 = 初始拥有")] public int price;
        [Tooltip("解禁所需声望；声望不足时在商城/收纳栏呈「？」")] public int unlockReputation;
        [Tooltip("摆放后对 House 装饰分的贡献")] public int decorationScore = 10;
        [Tooltip("家具精灵（Assets/Resources/OutGameUI/Furniture）")] public Sprite sprite;
        [Tooltip("桌面格配置（仅地面家具生效）")] public FurnitureTableSurfaceConfig tableSurface = new FurnitureTableSurfaceConfig();

        /// <summary>是否可吸附到指定表面类型的网格。</summary>
        public bool Supports(FurnitureSurfaceType surface) => surfaces != null && surfaces.Contains(surface);
    }

    /// <summary>
    /// 家具配置表（一张表，一行一件家具）。新增家具 = 往 Furniture 目录放一张精灵图 + 在这里加一行。
    /// </summary>
    [CreateAssetMenu(menuName = "MasterHouse/家具配置表", fileName = "FurnitureTable")]
    public sealed class FurnitureTable : ScriptableObject
    {
        public List<FurnitureEntry> entries = new List<FurnitureEntry>();

        public FurnitureEntry Find(string id)
        {
            for (var i = 0; i < entries.Count; i++)
                if (entries[i] != null && entries[i].id == id) return entries[i];
            return null;
        }
    }
}

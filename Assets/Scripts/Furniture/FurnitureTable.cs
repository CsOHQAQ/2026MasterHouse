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

    /// <summary>
    /// 家具配置表中的一行。
    ///
    /// **注意本类有两种字段**（家具族体系说明 §3.3）：
    ///   · 变体特有（id / 英文索引 / 显示名 / 显示宽高 / 占格 / 精灵图 / 色值 / 族id）——策划在家具表.xlsx 里逐行填；
    ///   · 族级共有（分类 / 描述 / 表面类型 / 可叠放 / 装饰分 / 音效 / 桌面格）——**策划不填**，
    ///     导表时由 <c>FurnitureCsvImporter</c> 按 <see cref="familyId"/> 查家具族表.xlsx 后**展开填入**。
    ///
    /// 展开而不是运行时查族表，是为了让商城/摆放/烘焙/装饰分/需求匹配等全部消费方零改动——
    /// 想改族级数值请改**族表**，改这里的字段会在下次导表时被覆盖。
    /// </summary>
    [Serializable]
    public sealed class FurnitureEntry
    {
        [Tooltip("唯一 id，摆放与存档都用它引用")] public string id;
        [Tooltip("英文索引（素材文件名里的英文名，检索/对照用，不上屏）")] public string nameKey;
        [Tooltip("显示名称（中文，界面上屏），带变体编号如「单人沙发·02」")] public string displayName;
        [Tooltip("所属族 id（家具族表的行）。商城/收纳栏按它折叠，条件类需求可按它匹配任意配色")]
        public string familyId;
        [Tooltip("【族级·导表展开】商店分类（盆栽/摆件/桌椅/壁挂/灯具，商店页签用）")] public string category;
        [Tooltip("【族级·导表展开】商店描述文案")] public string description;
        [Tooltip("【族级·导表展开】可吸附的表面类型（可多选：如纸箱既可地面也可桌面）")]
        public List<FurnitureSurfaceType> surfaces = new List<FurnitureSurfaceType> { FurnitureSurfaceType.Floor };
        [Tooltip("【族级·导表展开】可叠放（地毯类）：平铺在地面、不挡其他家具落格，渲染压在所有立式家具之下；同为可叠放的彼此仍互斥")]
        public bool stackable;
        [Tooltip("家具底部实际接地/接桌轮廓占用的格子列数；不按整张图片或显示宽度换算，家具表为空时回退族默认值")] public int cols = 1;
        [Tooltip("家具底部实际接地纵深占用的格子行数；桌面家具运行时固定为 1 行，壁挂家具按墙面可见轮廓，家具表为空时回退族默认值")] public int rows = 1;
        [Tooltip("实际显示宽度（场景像素）。与显示高度分别生效，用来校正素材自身比例")] public float displayWidth = 100f;
        [Tooltip("实际显示高度（场景像素）。与显示宽度分别生效，用来校正素材自身比例")] public float displayHeight = 100f;
        [Tooltip("商店预览宽度（独立虚拟画布单位）。只控制商品卡、详情与购买弹窗，不影响房间摆放")] public float storeDisplayWidth = 100f;
        [Tooltip("商店预览高度（独立虚拟画布单位）。只控制商品卡、详情与购买弹窗，不影响房间摆放")] public float storeDisplayHeight = 100f;
        [Tooltip("商店左侧商品列表专用图；空时回退摆放精灵")] public Sprite storeListSprite;
        [Tooltip("商店右侧详情展示专用图；空时回退列表图/摆放精灵")] public Sprite storePreviewSprite;
        // 售卖配置（价格 / 解禁声望）已于 2026-08-13 拆去 StoreTable，按 id 关联；读取走 EconomyManager
        [Tooltip("【族级·导表展开】摆放后对 House 装饰分的贡献")] public int decorationScore = 10;
        [Tooltip("家具精灵（Assets/Resources/OutGameUI/Furniture）")] public Sprite sprite;
        [Tooltip("色块颜色（商店选色块 tint；导表按素材平均色自动生成，策划可在表里改）")] public Color swatchColor = Color.white;
        [Tooltip("【族级·导表展开】专属拿起音效（空 = 用全局默认 FurniturePickup）")] public AudioClip pickupSound;
        [Tooltip("【族级·导表展开】专属放下音效（空 = 用全局默认 FurniturePlace）")] public AudioClip putdownSound;
        [Tooltip("【族级·导表展开】桌面格配置（仅地面家具生效）")] public FurnitureTableSurfaceConfig tableSurface = new FurnitureTableSurfaceConfig();

        /// <summary>是否可吸附到指定表面类型的网格。</summary>
        public bool Supports(FurnitureSurfaceType surface) => surfaces != null && surfaces.Contains(surface);
    }

    /// <summary>
    /// 家具在场景里的实际显示尺寸。
    /// 表里的显示宽高分别控制可见图形的两轴；家具素材的绘制比例不一定等于现实尺寸，
    /// 因此这里不能强制保持素材原始宽高比。
    /// </summary>
    public static class FurnitureDisplaySizing
    {
        public static Vector2 Resolve(FurnitureEntry entry)
        {
            if (entry == null) return Vector2.zero;
            return new Vector2(Mathf.Max(0f, entry.displayWidth), Mathf.Max(0f, entry.displayHeight));
        }

        /// <summary>
        /// 实际显示外框相对配置外框的缩放比。精确宽高模式下恒为 1；保留此入口供桌面格坐标统一使用。
        /// </summary>
        public static Vector2 FrameScale(FurnitureEntry entry)
        {
            if (entry == null) return Vector2.one;
            var display = Resolve(entry);
            return new Vector2(
                entry.displayWidth > 1e-4f ? display.x / entry.displayWidth : 1f,
                entry.displayHeight > 1e-4f ? display.y / entry.displayHeight : 1f);
        }

        /// <summary>
        /// 精灵实际图形区在**精灵局部空间**（原点 = 轴心）的包络：顶点包络，不含透明画布留白。
        /// 需要「家具真实脚底 / 中轴」而不只是尺寸时用它——素材普遍是 1024 大画布加留白，
        /// 用 <c>sprite.bounds</c> 拿到的底边在留白里，挂上去的东西会整体沉下去。
        /// </summary>
        public static Bounds TightBounds(Sprite sprite)
        {
            if (sprite == null) return new Bounds(Vector3.zero, Vector3.zero);
            var vertices = sprite.vertices;
            if (vertices == null || vertices.Length == 0) return sprite.bounds;
            Vector2 min = vertices[0], max = vertices[0];
            foreach (var vertex in vertices)
            {
                min = Vector2.Min(min, vertex);
                max = Vector2.Max(max, vertex);
            }
            var bounds = new Bounds();
            bounds.SetMinMax(min, max);
            return bounds;
        }

        /// <summary>精灵实际图形区的世界尺寸；使用顶点包络，不把透明画布留白算进家具。</summary>
        public static Vector2 TightSize(Sprite sprite)
        {
            return sprite == null ? Vector2.zero : (Vector2)TightBounds(sprite).size;
        }
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

using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// Hub 主楼世界坐标（2026-08-16 场景重做）：整张「主楼剖面图」即世界（归一化 (0~1)²），
    /// 五个可用区域是图中挖好的房间矩形——上四间是业务房间（0 起居室 = 三层左，1 卧室 = 三层右，
    /// 2 厨房 = 二层左，3 书房 = 二层右），底层通间是接待室（访客进场/排队/等分房都待在这里）。
    /// 区域矩形按图稿手工标定，对不齐时改 Regions 一处即可。
    /// 场景相机（HubSceneBinder）与访客舞台（OutGameVisitorStage）共用本换算，别各写一份。
    /// </summary>
    public static class HubWorldGrid
    {
        /// <summary>业务房间数（可住客、可摆家具的上四间）。</summary>
        public const int RoomCount = 4;

        /// <summary>接待室的区域下标：只做访客站位，不参与分房/家具业务。</summary>
        public const int Reception = 4;

        /// <summary>无效区域（世界点不落在任何房间里，如墙体/屋顶/天空）。</summary>
        public const int None = -1;

        /// <summary>
        /// 五个区域在主楼图中的归一化矩形（左下原点）。
        /// 默认值按 house-main 图稿标定；HubSceneWorld Prefab 就位后由 Configure 以 Prefab 锚点覆盖（Prefab 是布局真相）。
        /// </summary>
        private static Rect[] Regions =
        {
            new Rect(.2575f, .4604f, .2225f, .184f),  // 0 起居室（三层左·紫）
            new Rect(.4865f, .4604f, .2275f, .184f),  // 1 卧室（三层右·黄）
            new Rect(.27f,   .2978f, .21f,   .144f),  // 2 厨房（二层左·粉）
            new Rect(.4865f, .2978f, .2735f, .1555f), // 3 书房（二层右·蓝）
            new Rect(.1875f, .0444f, .5725f, .2445f), // 4 接待室（底层通间）
        };

        /// <summary>
        /// 各房间背景图的内容区（uvRect 口径）。房间图已由美术裁成纯内容（无黑框，2026-08-16 起），默认整图；
        /// 保留这层是为将来带边框素材留接缝。业务坐标（家具网格/活动区/入口区）以整图归一化标定，
        /// RoomToWorld/WorldToLocal 负责内容区换算，房间表与家具配置无需迁移。
        /// </summary>
        private static Rect[] ContentCrops =
        {
            Rect.MinMaxRect(0f, 0f, 1f, 1f), // 0 起居室
            Rect.MinMaxRect(0f, 0f, 1f, 1f), // 1 卧室
            Rect.MinMaxRect(0f, 0f, 1f, 1f), // 2 厨房
            Rect.MinMaxRect(0f, 0f, 1f, 1f), // 3 书房
            Rect.MinMaxRect(0f, 0f, 1f, 1f), // 4 接待室
        };

        /// <summary>
        /// 用 HubSceneWorld Prefab 的实际布局覆盖区域与裁切（HubSceneBinder 建层时调，2026-08-16 场景固化）：
        /// regions/crops 各 5 项（业务四间 + 接待室）。此后聚焦缩放、访客站位、家具热点全按 Prefab 手调结果走。
        /// </summary>
        public static void Configure(Rect[] regions, Rect[] crops)
        {
            if (regions != null && regions.Length == Regions.Length) Regions = regions;
            if (crops != null && crops.Length == ContentCrops.Length) ContentCrops = crops;
        }

        /// <summary>区域矩形（含接待室）。</summary>
        public static Rect RegionOf(int roomIndex) => Regions[Mathf.Clamp(roomIndex, 0, Regions.Length - 1)];

        /// <summary>房间背景图的内容区（uvRect 裁切用；已按区域宽高比修正，无拉伸）。</summary>
        public static Rect ContentCropOf(int roomIndex) => ContentCrops[Mathf.Clamp(roomIndex, 0, ContentCrops.Length - 1)];

        /// <summary>区域左下角（世界归一化坐标）。</summary>
        public static Vector2 CellOrigin(int roomIndex) => RegionOf(roomIndex).min;

        /// <summary>房间内归一化坐标（整图口径）→ 世界归一化坐标：先换到内容区分数，再铺进区域矩形。</summary>
        public static Vector2 RoomToWorld(int roomIndex, Vector2 local01)
        {
            var region = RegionOf(roomIndex);
            var crop = ContentCropOf(roomIndex);
            var inCrop = new Vector2((local01.x - crop.x) / crop.width, (local01.y - crop.y) / crop.height);
            return region.min + Vector2.Scale(inCrop, region.size);
        }

        /// <summary>世界归一化坐标落在哪个区域；都不命中返回 None（调用方自行兜底）。</summary>
        public static int RoomAt(Vector2 world01)
        {
            for (var i = 0; i < Regions.Length; i++)
                if (Regions[i].Contains(world01)) return i;
            return None;
        }

        /// <summary>世界归一化坐标 → 指定房间的整图归一化坐标（RoomToWorld 的逆；不钳制，越界值由调用方处理）。</summary>
        public static Vector2 WorldToLocal(int roomIndex, Vector2 world01)
        {
            var region = RegionOf(roomIndex);
            var crop = ContentCropOf(roomIndex);
            var inRegion = new Vector2((world01.x - region.x) / region.width, (world01.y - region.y) / region.height);
            return crop.min + Vector2.Scale(inRegion, crop.size);
        }

        /// <summary>聚焦某区域的目标缩放：区域宽度推满视口宽（区域比 16:9 扁，上下会留出主楼画面作环境）。</summary>
        public static float FocusZoom(int roomIndex) => 1f / RegionOf(roomIndex).width;
    }
}

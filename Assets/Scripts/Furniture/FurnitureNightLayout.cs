using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 昼夜网格校正（2026-08-18 反馈「夜里网格穿模」）。
    ///
    /// 昼夜两张房间图是分别绘制的，**墙脚线（后墙与地板的交界）高度不一样**——
    /// 实测起居室 0.794→0.802、卧室 0.832→0.893、厨房 0.785→0.783、书房 0.800→0.875。
    /// 网格是按白天图标定的，夜里直接沿用，地面格就会爬到墙上、壁挂格会掉到地板里。
    ///
    /// 这里不另存一套网格，而是把白天网格**按墙脚线分段线性映射**到夜间几何：
    /// 墙面段 [0, 墙脚线] 与地面段 [墙脚线, 图底] 各自等比缩放。于是
    /// 「第几行第几列」的语义完全不变——家具仍按网格相对位置摆，只是那个格子在夜里画到了新位置。
    /// 白天↔夜间之间按夜色权重插值，过渡期家具跟着背景一起挪，不会在某一帧突然跳。
    /// </summary>
    public static class FurnitureNightLayout
    {
        /// <summary>把白天标定的场景 y 映射到当前夜色权重下的 y（0 = 白天，1 = 夜间）。</summary>
        public static float MapY(FurnitureRoomEntry room, float y, float nightAlpha)
        {
            if (room == null || nightAlpha <= .001f) return y;
            var height = room.sceneHeight;
            if (height <= 1f) return y;
            var dayLine = Mathf.Clamp01(room.dayFloorLine) * height;
            var nightLine = Mathf.Clamp01(room.nightFloorLine) * height;
            if (Mathf.Abs(nightLine - dayLine) < .01f) return y;

            float mapped;
            if (y <= dayLine)
            {
                // 墙面段：图顶到墙脚线，按两条线的比例缩放
                mapped = dayLine > .01f ? y / dayLine * nightLine : y;
            }
            else
            {
                // 地面段：墙脚线到图底
                var dayDepth = height - dayLine;
                var nightDepth = height - nightLine;
                mapped = dayDepth > .01f ? nightLine + (y - dayLine) / dayDepth * nightDepth : y;
            }
            return Mathf.Lerp(y, mapped, Mathf.Clamp01(nightAlpha));
        }

        /// <summary>把白天标定的场景 x 映射到当前夜色权重下的 x（房间中线为缩放轴）。</summary>
        public static float MapX(FurnitureRoomEntry room, float x, float nightAlpha)
        {
            if (room == null || nightAlpha <= .001f) return x;
            var scale = room.nightWidthScale <= 0f ? 1f : room.nightWidthScale;
            if (Mathf.Abs(scale - 1f) < .0001f && Mathf.Abs(room.nightShiftX) < .01f) return x;
            var center = room.sceneWidth * .5f;
            var mapped = center + (x - center) * scale + room.nightShiftX;
            return Mathf.Lerp(x, mapped, Mathf.Clamp01(nightAlpha));
        }

        /// <summary>
        /// 按当前夜色权重生成一份校正过的网格配置（原配置不动，返回副本）。
        /// 格子的**行列数不变**，只有落到画面上的位置与格子尺寸跟着几何走。
        /// </summary>
        public static FurnitureGridConfig Adjust(FurnitureRoomEntry room, FurnitureGridConfig grid, float nightAlpha)
        {
            if (room == null || grid == null || nightAlpha <= .001f) return grid;
            var top = MapY(room, grid.y, nightAlpha);
            var bottom = MapY(room, grid.y + grid.rows * grid.cellHeight, nightAlpha);
            var left = MapX(room, grid.x, nightAlpha);
            var right = MapX(room, grid.x + grid.cols * grid.cellWidth, nightAlpha);
            if (Mathf.Abs(top - grid.y) < .01f && Mathf.Abs(bottom - (grid.y + grid.rows * grid.cellHeight)) < .01f &&
                Mathf.Abs(left - grid.x) < .01f) return grid;
            return new FurnitureGridConfig
            {
                id = grid.id,
                surface = grid.surface,
                cols = grid.cols,
                rows = grid.rows,
                cellWidth = grid.cols > 0 ? (right - left) / grid.cols : grid.cellWidth,
                cellHeight = grid.rows > 0 ? (bottom - top) / grid.rows : grid.cellHeight,
                x = left,
                y = top,
                farWidthScale = grid.farWidthScale,
            };
        }

        /// <summary>当前夜色权重（设置里关掉昼夜交替时恒为 0，与房间背景的夜间图同一条曲线）。</summary>
        public static float NightAlphaNow() => HouseDayLight.NightRoomAlphaNow();
    }
}

using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// Hub 四宫格世界坐标（2026-08-13）：四个房间平铺成 2×2 的连续世界，
    /// 世界归一化坐标 (0~1)²，每个房间占一个 0.5×0.5 的象限。
    /// 布局按阅读序：0 起居室=左上，1 卧室=右上，2 厨房=左下，3 书房=右下。
    /// 场景相机（HubSceneBinder）与访客舞台（OutGameVisitorStage）共用本换算，别各写一份。
    /// </summary>
    public static class HubWorldGrid
    {
        public const int RoomCount = 4;

        /// <summary>房间象限的左下角（世界归一化坐标）。</summary>
        public static Vector2 CellOrigin(int roomIndex)
        {
            var col = roomIndex % 2;
            var topRow = roomIndex < 2;
            return new Vector2(col * .5f, topRow ? .5f : 0f);
        }

        /// <summary>房间内归一化坐标 → 世界归一化坐标。</summary>
        public static Vector2 RoomToWorld(int roomIndex, Vector2 local01)
        {
            return CellOrigin(roomIndex) + local01 * .5f;
        }

        /// <summary>世界归一化坐标落在哪个房间。</summary>
        public static int RoomAt(Vector2 world01)
        {
            var col = world01.x >= .5f ? 1 : 0;
            return (world01.y >= .5f ? 0 : 2) + col;
        }

        /// <summary>世界归一化坐标 → 指定房间内的归一化坐标（不钳制，越界值由调用方处理）。</summary>
        public static Vector2 WorldToLocal(int roomIndex, Vector2 world01)
        {
            return (world01 - CellOrigin(roomIndex)) / .5f;
        }
    }
}

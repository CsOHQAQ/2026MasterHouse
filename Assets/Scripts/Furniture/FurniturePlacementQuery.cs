using System.Collections.Generic;

namespace MasterHouse
{
    /// <summary>
    /// 房间家具的只读查询封装（需求重做说明 §6.1）：条件类需求「所住房间里有没有那件家具」的唯一判据。
    ///
    /// 存在的理由是**别让业务层直接读 `FurnitureRoomController.sessions`**——那是 View 层的 static 字典（§11.4）。
    /// 业务只需要「这个房间里都摆了什么」，不需要知道摆放是会话态还是配置默认值。
    ///
    /// 三条实现要点（都是踩过的坑）：
    ///   ① 会话布局为 null 表示「该房间从未编辑过」，**必须回落到 room.initialPlacements**——
    ///      忘了回落就会漏判房间初始就摆着的家具（`FurnitureSceneComposer.Collect` 的 `?? initialPlacements` 同款）。
    ///   ② 桌面家具（hostFurnitureId 非空）与地面/壁挂家具混在同一个列表里，**两者都要算**——
    ///      客人不关心杯子是放在地上还是茶几上。
    ///   ③ roomIndex → roomId 走 FurnitureRoomTable.rooms[roomIndex].id，越界返回空。
    /// </summary>
    public static class FurniturePlacementQuery
    {
        /// <summary>该房间当前摆放的全部家具 id（含地面/壁挂/桌面家具）。房间不存在时返回空列表。</summary>
        public static IReadOnlyList<string> FurnitureIdsIn(int roomIndex)
        {
            var result = new List<string>();
            var placements = PlacementsOf(roomIndex);
            if (placements == null) return result;
            foreach (var placement in placements)
            {
                // 桌面家具与地面/壁挂家具一视同仁（要点②）
                if (placement == null || string.IsNullOrEmpty(placement.furnitureId)) continue;
                result.Add(placement.furnitureId);
            }
            return result;
        }

        /// <summary>
        /// 该房间是否摆放了列表中的任意一件（OR 语义）。列表为空/为 null 返回 false——
        /// 「没写要什么」不该被当成「什么都算数」，条件类需求配空家具列表是配置事故（校验器会报错）。
        /// </summary>
        public static bool RoomHasAny(int roomIndex, IReadOnlyList<string> furnitureIds)
        {
            if (furnitureIds == null || furnitureIds.Count == 0) return false;
            var placements = PlacementsOf(roomIndex);
            if (placements == null) return false;
            foreach (var placement in placements)
            {
                if (placement == null || string.IsNullOrEmpty(placement.furnitureId)) continue;
                for (var i = 0; i < furnitureIds.Count; i++)
                    if (placement.furnitureId == furnitureIds[i]) return true;
            }
            return false;
        }

        /// <summary>
        /// 取房间的当前摆放列表：优先会话布局，**null 时回落房间默认摆放**（要点①）。
        /// 房间表缺失或下标越界返回 null。
        /// </summary>
        private static List<FurniturePlacementConfig> PlacementsOf(int roomIndex)
        {
            var rooms = GameManager.Instance != null ? GameManager.Instance.FurnitureRoomTable : null;
            if (rooms == null || roomIndex < 0 || roomIndex >= rooms.rooms.Count) return null;
            var room = rooms.rooms[roomIndex];
            if (room == null || string.IsNullOrEmpty(room.id)) return null;
            return FurnitureRoomController.CaptureSessionPlacements(room.id) ?? room.initialPlacements;
        }
    }
}

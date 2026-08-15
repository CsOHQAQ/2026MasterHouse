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
    ///   ② 桌面家具（IsOnHost 为真）与地面/壁挂家具混在同一个列表里，**两者都要算**——
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
        /// 该房间当前摆放家具的装饰分总和（家具库存与交互重做说明 §6.1）：
        /// **完成服务**的客人离场时按它加成小费（加成额 = 本值 / EconomyConfig.decorScorePerTip）。
        ///
        /// 桌面家具同样计入（要点②）——客人不关心杯子放在地上还是茶几上。
        /// 房间不存在、从未编辑过且无默认摆放、或家具表缺失时返回 0（空房间就是 0 分，不是错误）。
        /// </summary>
        public static int DecorationScoreOf(int roomIndex)
        {
            var table = GameManager.Instance != null ? GameManager.Instance.FurnitureTable : null;
            var placements = PlacementsOf(roomIndex);
            if (table == null || placements == null) return 0;
            var sum = 0;
            foreach (var placement in placements)
            {
                if (placement == null || string.IsNullOrEmpty(placement.furnitureId)) continue;
                var entry = table.Find(placement.furnitureId);
                if (entry != null) sum += entry.decorationScore; // 全整数（§11.3）
            }
            return sum;
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
        /// 该房间是否摆放了这些**族**中任意一族的任意配色（家具族体系说明 §4.2）。
        ///
        /// 与 <see cref="RoomHasAny"/> 的区别只在粒度：那个要求「就要那张蓝的」，这个满足
        /// 「随便什么颜色的单人沙发都行」。空列表同样返回 false，理由见上。
        /// 摆放记的是具体家具 id，所以要反查家具表拿它的族——族级属性虽已展开进每一行，
        /// <c>familyId</c> 也在行上，一次 Find 就够。
        /// </summary>
        public static bool RoomHasAnyFamily(int roomIndex, IReadOnlyList<string> familyIds)
        {
            if (familyIds == null || familyIds.Count == 0) return false;
            var table = GameManager.Instance != null ? GameManager.Instance.FurnitureTable : null;
            var placements = PlacementsOf(roomIndex);
            if (table == null || placements == null) return false;
            foreach (var placement in placements)
            {
                if (placement == null || string.IsNullOrEmpty(placement.furnitureId)) continue;
                var entry = table.Find(placement.furnitureId);
                if (entry == null || string.IsNullOrEmpty(entry.familyId)) continue;
                for (var i = 0; i < familyIds.Count; i++)
                    if (entry.familyId == familyIds[i]) return true;
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

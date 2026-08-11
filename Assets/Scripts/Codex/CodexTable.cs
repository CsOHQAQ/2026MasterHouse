using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 图鉴内容表（§16.3 Codex 模块的 Model）：房间/设备/档案/成就/日记等纯展示内容。
    /// 【务必独占本文件】ScriptableObject 必须与文件同名，否则 Unity 不为其生成 MonoScript，
    /// 已创建的 .asset 会在域重载后丢失脚本引用（m_Script: {fileID: 0}）而损坏。条目类见 CodexDef.cs。
    /// 查询顺序 = 列表顺序，稳定可复现（§11.2）。
    /// </summary>
    [CreateAssetMenu(menuName = "MasterHouse/图鉴内容表", fileName = "CodexTable")]
    public sealed class CodexTable : ScriptableObject
    {
        [Tooltip("房间。列表顺序 = Hub 导航顺序（roomIndex）")]
        public List<RoomDef> rooms = new List<RoomDef>();

        [Tooltip("设备平铺列表，按 roomId 分组；房间内顺序 = 列表内相对顺序")]
        public List<DeviceDef> devices = new List<DeviceDef>();

        [Tooltip("档案条目，面板按 category 分 tab；tab 内顺序 = 列表内相对顺序")]
        public List<CodexEntryDef> archives = new List<CodexEntryDef>();

        public List<AchievementDef> achievements = new List<AchievementDef>();

        public List<JournalEntryDef> journalEntries = new List<JournalEntryDef>();

        public RoomDef FindRoom(string id)
        {
            foreach (var room in rooms)
                if (room != null && room.id == id) return room;
            return null;
        }

        /// <summary>把 roomId 房间的设备填入 result（先 Clear；传入复用列表避免每帧分配）。</summary>
        public void GetDevicesOfRoom(string roomId, List<DeviceDef> result)
        {
            result.Clear();
            foreach (var device in devices)
                if (device != null && device.roomId == roomId) result.Add(device);
        }

        public int CountDevicesOfRoom(string roomId)
        {
            var count = 0;
            foreach (var device in devices)
                if (device != null && device.roomId == roomId) count++;
            return count;
        }

        /// <summary>把 category 分类的档案填入 result（先 Clear）。</summary>
        public void GetArchives(ECodexArchiveCategory category, List<CodexEntryDef> result)
        {
            result.Clear();
            foreach (var entry in archives)
                if (entry != null && entry.category == category) result.Add(entry);
        }

        /// <summary>已拥有设备数量：Economy 装饰分构成项的统计源（§16.7 毒点①的 Def 资产统计）。</summary>
        public int CountOwnedDevices()
        {
            var count = 0;
            foreach (var device in devices)
                if (device != null && device.owned) count++;
            return count;
        }
    }
}

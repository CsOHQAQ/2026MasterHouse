using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>房间（Hub 导航/背景/设备分组的内容数据）。</summary>
    [Serializable]
    public sealed class RoomDef
    {
        [Tooltip("稳定键：存档 room 字段、DeviceDef.roomId、家具房间表都用它对齐")]
        public string id;
        public string displayName;
        [Tooltip("导航角标，如 HOME / REST")]
        public string code;
        [Tooltip("场景说明文案")]
        public string note;
        [Tooltip("Hub 背景图 Resources 路径，如 OutGameUI/house-hub-v2")]
        public string artPath;
    }

    /// <summary>设备（图鉴/设备面板内容）。</summary>
    [Serializable]
    public sealed class DeviceDef
    {
        [Tooltip("约定式引用 RoomDef.id，按房间分组展示")]
        public string roomId;
        public string displayName;
        [Tooltip("等级数值，UI 显示时格式化为 LV.{level}")]
        public int level;
        public string effect;
        [Tooltip("已拥有：现状无获取玩法，暂为内容态初始值；同时是 Economy 装饰分的设备数量统计源（§16.7）")]
        public bool owned;
    }

    /// <summary>档案面板的 tab 分类。</summary>
    public enum ECodexArchiveCategory
    {
        NarrativeFurniture = 0, // 叙事家具
        World = 1,              // 世界与角色
    }

    /// <summary>档案条目（叙事家具 / 世界与角色，两 tab 同结构）。</summary>
    [Serializable]
    public sealed class CodexEntryDef
    {
        public ECodexArchiveCategory category;
        [Tooltip("稳定键；面板对 id==\"map\" 有迷雾半径特判（UI 侧逻辑）")]
        public string id;
        public string displayName;
        [Tooltip("卡片抬头，如「回应家具」「场景概念」")]
        public string type;
        [Tooltip("归属/编号文案，如「洛恩」「HOME NODE」")]
        public string owner;
        [TextArea] public string note;
        [Tooltip("配图 Resources 路径")]
        public string imagePath;
    }

    /// <summary>成就内容（仅名称/条件；完成状态是运行时数据，不进 Def）。</summary>
    [Serializable]
    public sealed class AchievementDef
    {
        [Tooltip("稳定键，为将来成就系统的存档留")]
        public string id;
        public string displayName;
        [Tooltip("达成条件描述")]
        public string note;
    }

    /// <summary>日记内容。</summary>
    [Serializable]
    public sealed class JournalEntryDef
    {
        public string id;
        [Tooltip("展示文本（非真实日期，与游戏 Day 脱节是已知遗留）")]
        public string dateText;
        public string title;
        [TextArea] public string body;
    }

    // 注意：表类 CodexTable 必须独占同名文件 CodexTable.cs——
    // Unity 只为与文件同名的类生成 MonoScript，放在本文件里会导致 .asset 的脚本引用为空（资产损坏）。
}

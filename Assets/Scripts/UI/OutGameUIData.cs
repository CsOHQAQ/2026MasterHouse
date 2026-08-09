using System;
using System.Collections.Generic;

namespace MasterHouse
{
    [Serializable]
    internal sealed class OutGameSaveData
    {
        /// <summary>存档版本。2 = 含流通数值、家具所有权与摆放布局；3 = 含游戏时钟与访客到访状态；
        /// 旧档读入时新字段回落默认值。</summary>
        public int version;
        public int slot;
        public string room = "living";
        public bool[] served = new bool[4];
        public bool[] refused = new bool[4];
        /// <summary>v3：访客是否已到访过（到访后常驻屋内，跨天也在，直到服务完成/被拒绝）。</summary>
        public bool[] guestArrived = new bool[4];
        /// <summary>v3：游戏时钟（加速时间，与现实时间无关）。</summary>
        public int gameDay = 1;
        public float gameMinute = 8 * 60f;
        public int bgm = 64;
        public int sfx = 78;
        public string windowMode = "无边框";
        public string savedAt = "";
        public EconomySaveData economy = new EconomySaveData();
        /// <summary>是否保存过家具布局（区分「摆空了」与「从未编辑过（用房间默认摆放）」）。</summary>
        public bool hasFurnitureLayout;
        public List<FurniturePlacementConfig> furniturePlacements = new List<FurniturePlacementConfig>();
    }

    /// <summary>
    /// 旧局外 UI 的静态数据表残留。内容数据已全部 Def 化（§16.6）：访客/邻居 → VisitorTable，
    /// 房间/设备/档案/成就/日记 → CodexTable，时段文案 → HousePhaseText；本类只剩存档结构与过渡桥接，
    /// 随旧 UI 逐模块退役（存档结构待定 #9 统一存档时取代）。
    /// </summary>
    internal static class OutGameUIData
    {
        /// <summary>当前时段下标（过渡桥接：时段划分已归 HouseClock 模块整数判定，§16.4；本属性随旧 UI 退役删除）。</summary>
        public static int CurrentPhase => (int)GameManager.Instance.HouseClockManager.Data.CurrentPhase;
    }
}

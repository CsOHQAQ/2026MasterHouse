using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 局外时段（§16.4）：枚举值即旧 UI 的时段下标（0~5），旧文案数组按此索引，重写期间保持对应。
    /// </summary>
    public enum EHousePhase
    {
        Morning = 0,   // 早晨 07:00–09:00
        Forenoon = 1,  // 上午 09:00–12:00
        Noon = 2,      // 中午 12:00–14:00
        Afternoon = 3, // 下午 14:00–18:00
        Evening = 4,   // 晚上 18:00–22:00
        LateNight = 5, // 深夜 22:00–07:00
    }

    /// <summary>
    /// 时段展示文案。与 EHousePhase/划分边界同源同改（改边界必改本模块代码），故不进 SO（§16.6 决策，2026-08-09）。
    /// 下标 = (int)EHousePhase。
    /// </summary>
    public static class HousePhaseText
    {
        public static readonly string[] Names = { "早晨", "上午", "中午", "下午", "晚上", "深夜" };
        public static readonly string[] Ranges = { "07:00–09:00", "09:00–12:00", "12:00–14:00", "14:00–18:00", "18:00–22:00", "22:00–07:00" };
    }

    /// <summary>
    /// 局外游戏时钟运行时数据（§16.4）：唯一时间状态是 Day + 当天 tick 计数，禁止接触真实时间（§11.1）。
    /// 只能被 HouseClockManager 修改（§11.4）；时段/到访判定一律用整数属性比较，禁止 float 小时累加。
    /// </summary>
    public class HouseClockData
    {
        /// <summary>游戏内天数（从 1 开始）。</summary>
        public int Day = 1;

        /// <summary>当天已过的 tick 数（0 ~ DayTicks-1）。</summary>
        public int TickOfDay;

        /// <summary>每游戏分钟的 tick 数（局外时间倍率，待定 #18），来自 GameConfig 可配字段。</summary>
        public static int TicksPerMinute => Mathf.Max(1, GameConfig.Instance.HouseTicksPerGameMinute);

        /// <summary>一天的总 tick 数。</summary>
        public static int DayTicks => 24 * 60 * TicksPerMinute;

        /// <summary>当天已过的整分钟数（0~1439），整数比较的标准口径。</summary>
        public int MinuteOfDay => TickOfDay / TicksPerMinute;

        public int Hour => MinuteOfDay / 60;
        public int Minute => MinuteOfDay % 60;
        public string TimeText => $"{Hour:00}:{Minute:00}";

        /// <summary>只到小时的显示口径（2026-08-22 一轮测试改进 #6，仅 2.0 时间牌用；格式暂定「14时」，待后续对齐）。
        /// 别改 TimeText——日历面板等还有别的消费方要分钟。</summary>
        public string HourText => $"{Hour:00}时";

        /// <summary>当前时段：按当天分钟数整数比较划分（§16.4，禁止 float 小时判定）。</summary>
        public EHousePhase CurrentPhase
        {
            get
            {
                var m = MinuteOfDay;
                if (m >= 7 * 60 && m < 9 * 60) return EHousePhase.Morning;
                if (m >= 9 * 60 && m < 12 * 60) return EHousePhase.Forenoon;
                if (m >= 12 * 60 && m < 14 * 60) return EHousePhase.Noon;
                if (m >= 14 * 60 && m < 18 * 60) return EHousePhase.Afternoon;
                if (m >= 18 * 60 && m < 22 * 60) return EHousePhase.Evening;
                return EHousePhase.LateNight;
            }
        }

        /// <summary>过渡兼容：旧存档 v3 的 float 分钟字段口径；待定 #9 统一存档定案后改存 TickOfDay（§11.5）。</summary>
        public float MinuteOfDayF => TickOfDay / (float)TicksPerMinute;
    }
}

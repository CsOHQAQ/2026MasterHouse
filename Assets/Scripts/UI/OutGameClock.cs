using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 加速的游戏时钟（会话级单一数据源，随存档持久化，不读取现实时间）。
    /// 现实 1 秒 = 游戏 1 分钟（60× 加速，一整天 = 24 分钟现实时间）。
    /// 由 OutGameUI 在 Hub 期间驱动 Tick；标题页/开门过场不走时间。
    /// </summary>
    internal static class OutGameClock
    {
        /// <summary>加速倍率：现实每秒推进的游戏分钟数。</summary>
        public const float MinutesPerRealSecond = 1f;
        /// <summary>每天的起始时间（新一天从 08:00 开始）。</summary>
        public const int DayStartMinute = 8 * 60;

        public static int Day { get; private set; } = 1;
        /// <summary>当天已过的分钟数（0~1439，含小数）。</summary>
        public static float MinuteOfDay { get; private set; } = DayStartMinute;

        public static int Hour => Mathf.FloorToInt(MinuteOfDay / 60f) % 24;
        public static int Minute => Mathf.FloorToInt(MinuteOfDay % 60f);
        /// <summary>小数小时（8.5 = 08:30），供服务时间窗口比较。</summary>
        public static float HourF => MinuteOfDay / 60f;
        public static string TimeText => $"{Hour:00}:{Minute:00}";

        public static void Tick(float realDeltaSeconds)
        {
            MinuteOfDay += Mathf.Max(0f, realDeltaSeconds) * MinutesPerRealSecond;
            while (MinuteOfDay >= 24f * 60f)
            {
                MinuteOfDay -= 24f * 60f;
                Day++;
            }
        }

        /// <summary>跳到下一天早晨（周结算用）。</summary>
        public static void NextDay()
        {
            Day++;
            MinuteOfDay = DayStartMinute;
        }

        public static void Reset()
        {
            Day = 1;
            MinuteOfDay = DayStartMinute;
        }

        public static void Restore(int day, float minuteOfDay)
        {
            Day = Mathf.Max(1, day);
            MinuteOfDay = Mathf.Clamp(minuteOfDay, 0f, 24f * 60f - 1f);
        }
    }
}

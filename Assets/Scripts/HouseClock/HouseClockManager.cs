using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 局外游戏时钟逻辑（§16.4）：由 GameManager 的全局固定 tick 驱动，取代旧 OutGameClock 的帧驱动 float 分钟。
    /// 体验节奏沿用「现实 1 秒 = 游戏 1 分钟」，以 tick 表达：10 tick/秒 ÷ 每游戏分钟 10 tick（倍率见 GameConfig，待定 #18）。
    /// </summary>
    public class HouseClockManager
    {
        /// <summary>新一天的起始时间（08:00），以当天分钟数表达。</summary>
        public const int DayStartMinute = 8 * 60;

        public HouseClockData Data { get; } = new HouseClockData();

        /// <summary>
        /// 时钟闸门：时间只在 Hub 期间流动（标题页/开门过场暂停，家具模式继续走）。
        /// 由 UI 层按页面状态开合；全局 tick 照常到达，闸门关闭时不推进。
        /// </summary>
        public bool IsRunning { get; private set; }

        public void SetRunning(bool running) => IsRunning = running;

        /// <summary>每全局 tick 调用一次（GameManager 驱动），推进 1 tick 并处理跨天进位。</summary>
        public void Tick()
        {
            if (!IsRunning) return;
            Data.TickOfDay++;
            if (Data.TickOfDay >= HouseClockData.DayTicks)
            {
                Data.TickOfDay -= HouseClockData.DayTicks;
                Data.Day++;
            }
        }

        /// <summary>跳到下一天早晨（周结算用）。</summary>
        public void NextDay()
        {
            Data.Day++;
            Data.TickOfDay = DayStartMinute * HouseClockData.TicksPerMinute;
        }

        /// <summary>回到第 1 天早晨（新游戏/GM 重置）。</summary>
        public void ResetNew()
        {
            Data.Day = 1;
            Data.TickOfDay = DayStartMinute * HouseClockData.TicksPerMinute;
        }

        /// <summary>
        /// 过渡兼容：从旧存档 v3 的（天, float 分钟）恢复。
        /// 待定 #9 统一存档定案后改为 TickOfDay 全量序列化（§11.5）；离线时间是否补算随待定 #18 一并设计。
        /// </summary>
        public void RestoreFromMinutes(int day, float minuteOfDay)
        {
            Data.Day = Mathf.Max(1, day);
            var ticks = Mathf.RoundToInt(Mathf.Clamp(minuteOfDay, 0f, 24f * 60f - 1f) * HouseClockData.TicksPerMinute);
            Data.TickOfDay = Mathf.Clamp(ticks, 0, HouseClockData.DayTicks - 1);
        }
    }
}
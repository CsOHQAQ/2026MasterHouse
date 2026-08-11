using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 局外游戏时钟逻辑（§16.4）：由 GameManager 的全局固定 tick 驱动。
    /// 营业时段（开门/打烊时刻）配置在 VisitorTuningConfig（访客交付说明 §4.5），代码不留营业时间魔数——
    /// 旧 DayStartMinute 常量已迁入配置。
    /// 打烊闸门（§7）：到达打烊时刻后时钟停走（IsClosedForToday），一切 tick 业务统一冻结；
    /// 解冻由玩家【结束今天】显式触发（VisitorManager.EndDay → NextDay 跳次日开门时刻）。
    /// </summary>
    public class HouseClockManager
    {
        private readonly VisitorTuningConfig tuning;

        public HouseClockData Data { get; } = new HouseClockData();

        /// <summary>开门时刻（当天分钟数）。配置缺失时回落 8:00 并已在构造处报错。</summary>
        public int OpenMinute => tuning != null ? tuning.openMinute : 8 * 60;

        /// <summary>打烊时刻（当天分钟数）。配置缺失时回落 22:00。</summary>
        public int CloseMinute => tuning != null ? tuning.closeMinute : 22 * 60;

        /// <summary>
        /// 时钟闸门：时间只在 Hub 期间流动（标题页/开门过场暂停，家具模式继续走）。
        /// 由 UI 层按页面状态开合；全局 tick 照常到达，闸门关闭时不推进。
        /// </summary>
        public bool IsRunning { get; private set; }

        /// <summary>打烊闸门（§7）：当天时间已到打烊时刻，一切 tick 业务冻结，等玩家【结束今天】。整数比较（§11.3）。</summary>
        public bool IsClosedForToday => Data.TickOfDay >= CloseMinute * HouseClockData.TicksPerMinute;

        public HouseClockManager(VisitorTuningConfig tuning)
        {
            this.tuning = tuning;
        }

        public void SetRunning(bool running) => IsRunning = running;

        /// <summary>每全局 tick 调用一次（GameManager 驱动）。打烊后停走（跨天只经 NextDay，不再有 tick 进位）。</summary>
        public void Tick()
        {
            if (!IsRunning || IsClosedForToday) return;
            Data.TickOfDay++;
        }

        /// <summary>跳到下一天开门时刻（日结用，§7）。</summary>
        public void NextDay()
        {
            Data.Day++;
            Data.TickOfDay = OpenMinute * HouseClockData.TicksPerMinute;
        }

        /// <summary>回到第 1 天开门时刻（新游戏/GM 重置）。</summary>
        public void ResetNew()
        {
            Data.Day = 1;
            Data.TickOfDay = OpenMinute * HouseClockData.TicksPerMinute;
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

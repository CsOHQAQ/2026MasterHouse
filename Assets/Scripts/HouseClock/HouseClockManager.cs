using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 时钟停走的原因（对话设计说明 §8「闸门必须改成原因集合」）。
    /// 单一布尔顶不住多个持有者——后关的会把先关的覆盖掉，
    /// 典型事故是「关掉对话框顺手把打烊状态解冻了」。
    /// 打烊**不在这个枚举里**：它是当天时间的纯函数（IsClosedForToday），不需要谁来持有。
    /// </summary>
    public enum EClockStopReason
    {
        /// <summary>不在 Hub 页（标题页 / 开门过场 / 存档页……）。家具模式不退页，不算这条。</summary>
        OffHubPage = 0,

        /// <summary>模态对话框开启中（§8：局内产线停 tick、时钟停走、访客倒计时停表）。</summary>
        ModalDialogue = 1,
    }

    /// <summary>
    /// 局外游戏时钟逻辑（§16.4）：由 GameManager 的全局固定 tick 驱动。
    /// 营业时段（开门/打烊时刻）配置在 VisitorTuningConfig（访客交付说明 §4.5），代码不留营业时间魔数。
    /// 打烊闸门（§7）：到达打烊时刻后时钟停走（IsClosedForToday），一切 tick 业务统一冻结；
    /// 解冻由玩家【结束今天】显式触发（VisitorManager.EndDay → NextDay 跳次日开门时刻）。
    /// </summary>
    public class HouseClockManager
    {
        private readonly VisitorTuningConfig tuning;

        /// <summary>当前生效的停走原因位集合（按 EClockStopReason 取位）。</summary>
        private int stopReasons;

        public HouseClockData Data { get; } = new HouseClockData();

        /// <summary>开门时刻（当天分钟数）。配置缺失时回落 8:00 并已在构造处报错。</summary>
        public int OpenMinute => tuning != null ? tuning.openMinute : 8 * 60;

        /// <summary>打烊时刻（当天分钟数）。配置缺失时回落 22:00。</summary>
        public int CloseMinute => tuning != null ? tuning.closeMinute : 22 * 60;

        /// <summary>打烊闸门（§7）：当天时间已到打烊时刻，等玩家【结束今天】。整数比较（§11.3）。</summary>
        public bool IsClosedForToday => Data.TickOfDay >= CloseMinute * HouseClockData.TicksPerMinute;

        /// <summary>
        /// 时钟与局外业务是否推进：**没有任何停走原因且未打烊**。
        /// 驱动时钟推进与 VisitorManager.Tick（访客到访判定、超时、冒泡调度）。
        /// </summary>
        public bool IsRunning => stopReasons == 0 && !IsClosedForToday;

        /// <summary>
        /// 局内产线是否冻结（门控 LevelManager.TickAll）。
        /// **刻意不等于 !IsRunning**：局内测试场景没有 HubPage，OffHubPage 恒亮、IsRunning 恒 false，
        /// 若局内产线跟着 IsRunning 走就永远不会推进（待定 #19 联通前的隔离态）。
        /// 真正该冻结局内的只有两件事：打烊（堵死挂机刷资源，§16.4）与模态对话框（§8）。
        /// </summary>
        public bool IsWorldFrozen => IsClosedForToday || HasStopReason(EClockStopReason.ModalDialogue);

        public HouseClockManager(VisitorTuningConfig tuning)
        {
            this.tuning = tuning;
            // 初始不在 Hub 页（启动落在标题页），时间不流动
            SetStopReason(EClockStopReason.OffHubPage, true);
        }

        /// <summary>开合某一条停走原因。任一原因存在即关闸（§8）。</summary>
        public void SetStopReason(EClockStopReason reason, bool active)
        {
            var bit = 1 << (int)reason;
            if (active) stopReasons |= bit;
            else stopReasons &= ~bit;
        }

        public bool HasStopReason(EClockStopReason reason) => (stopReasons & (1 << (int)reason)) != 0;

        /// <summary>每全局 tick 调用一次（GameManager 驱动）。打烊后停走（跨天只经 NextDay，不再有 tick 进位）。</summary>
        public void Tick()
        {
            if (!IsRunning) return;
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
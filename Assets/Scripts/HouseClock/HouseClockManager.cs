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

        // 2 号位曾是 DeliveryPage（需求交付页面），已随 Item 链退役删除（需求重做说明 §9.1）。
        // 现在的验收走【服务中交谈】对话分支，闸门由 ModalDialogue 那条负责。
        // **新增原因请从 3 起**：本枚举的值是位集合的位号，复用 2 会在存档/日志里与历史数据撞车。

        /// <summary>小游戏页面开启中（小游戏说明 §3.4）：时钟停走、访客各类倒计时停表。
        /// 小游戏自己按 deltaTime 计时，与全局 tick 零关系（§3.3）。</summary>
        Minigame = 3,
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
        /// 「世界是否冻结」——不含页面闸门的那一档。
        ///
        /// **当前没有消费方**：它原本门控局内产线的 LevelManager.TickAll()，而局内节点玩法
        /// 已随小游戏框架落地退役（第 2 步）。刻意保留（落地访谈 D 项拍板）——
        /// 局内外正式联通（待定 #19）时还会有「不在 Hub 页但仍需推进」的东西需要这一档，
        /// 届时不必重新推导它为什么不能等于 !IsRunning。
        ///
        /// 它**刻意不等于 !IsRunning**：IsRunning 还含 OffHubPage，而没有 HubPage 的场景里
        /// 那一位恒亮，跟着它走就永远不推进。
        /// </summary>
        public bool IsWorldFrozen => IsClosedForToday ||
                                     HasStopReason(EClockStopReason.ModalDialogue) ||
                                     HasStopReason(EClockStopReason.Minigame);

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
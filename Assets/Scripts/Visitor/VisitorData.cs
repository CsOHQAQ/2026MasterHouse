using System;
using System.Collections.Generic;

namespace MasterHouse
{
    /// <summary>
    /// 当日结算累计（访客交付说明 §7）：惩罚/奖励在超时、拒绝、提交当时逐次结清，
    /// 本类只做同口径累计，日结面板**只展示不惩罚**、不重复扣。
    /// </summary>
    public sealed class VisitorDaySummary
    {
        /// <summary>各满意度档完成数（下标 = (int)EServeSatisfaction）。</summary>
        public readonly int[] ServedBySatisfaction = new int[4];

        /// <summary>拒绝口径合计：玩家拒绝 + 等搭话超时 + 等交货超时（同口径，§5）。</summary>
        public int RefusedCount;

        /// <summary>闲逛后自行离场数。</summary>
        public int WanderDepartCount;

        /// <summary>跨天留宿数（日结时 roll 中）。</summary>
        public int StayOvernightCount;

        /// <summary>服务奖励累计（完成需求的四档结算）。**不含离场小费**——那一笔进 <see cref="TipEarned"/>。</summary>
        public int CurrencyEarned;

        /// <summary>
        /// 离场小费累计（基础小费 + 装饰分加成）。与 <see cref="CurrencyEarned"/> 拆开是为了让日结面板
        /// 分两行显示——玩家看不出「装修给我多赚了多少」，这条循环就等于不存在（家具库存说明 §6.3）。
        /// </summary>
        public int TipEarned;

        public int ReputationEarned;

        public int ServedTotal
        {
            get
            {
                var total = 0;
                foreach (var count in ServedBySatisfaction) total += count;
                return total;
            }
        }

        public void Reset()
        {
            Array.Clear(ServedBySatisfaction, 0, ServedBySatisfaction.Length);
            RefusedCount = 0;
            WanderDepartCount = 0;
            StayOvernightCount = 0;
            CurrencyEarned = 0;
            TipEarned = 0;
            ReputationEarned = 0;
        }

        public VisitorDaySummary Clone()
        {
            var copy = new VisitorDaySummary
            {
                RefusedCount = RefusedCount,
                WanderDepartCount = WanderDepartCount,
                StayOvernightCount = StayOvernightCount,
                CurrencyEarned = CurrencyEarned,
                TipEarned = TipEarned,
                ReputationEarned = ReputationEarned,
            };
            Array.Copy(ServedBySatisfaction, copy.ServedBySatisfaction, ServedBySatisfaction.Length);
            return copy;
        }
    }

    /// <summary>
    /// 访客运行时数据（访客交付说明 §3）：当前在场实例集合 + 日程游标。
    /// 只能由 VisitorManager 修改（§11.4）。
    /// </summary>
    public class VisitorData
    {
        /// <summary>
        /// 派生种子的根（§6.1）。存档系统未落地期间（待定 #9）由启动层注入固定默认常量、GM 面板可改写；
        /// 存档接入后改为存档字段——过渡期也不存在无种子随机（§11.1）。
        /// </summary>
        public long RunSeed;

        /// <summary>
        /// 访客业务时间轴：营业中每全局 tick +1；标题冻结与打烊闸门期间停表（§7），
        /// 因此各实例的超时/冒泡计时天然「停表」，无需逐个暂停。
        /// </summary>
        public long BusinessTick;

        /// <summary>实例 id 计数器（稳定自增，随存档序列化，§11.5）。</summary>
        public int NextInstanceId = 1;

        /// <summary>日程游标：指向稳定排序后（day, 出现时刻, 下标）的下一条待消费条目（§4.4）。</summary>
        public int ScheduleCursor;

        // ScheduleExhaustedWarned（「日程已跑完」Warning 只打一次的标记）已于 2026-08-15 删除：
        // 那是占位处理，现由感谢试玩页取代（家具库存说明 §6.5），判据是 VisitorManager.IsFinalScheduledDay。

        /// <summary>当前在场实例，按 InstanceId 升序（生成顺序即 id 顺序，§11.2）。</summary>
        public readonly List<VisitorInstance> Instances = new List<VisitorInstance>();

        /// <summary>当日结算累计（日结面板展示源）。</summary>
        public readonly VisitorDaySummary Today = new VisitorDaySummary();
    }
}

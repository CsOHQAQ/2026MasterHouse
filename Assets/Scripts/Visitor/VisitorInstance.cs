using System.Collections.Generic;

namespace MasterHouse
{
    /// <summary>
    /// 访客生命周期状态（访客交付说明 §5）。到场前的「日程未到点」不建实例（由日程游标表达），
    /// 离场即从在场列表移除，Departed 仅作为移除瞬间的终态标记。
    /// </summary>
    public enum EVisitorState
    {
        /// <summary>前台等待接待：日程到点进场后站在前台，等玩家搭话/接待，超时按被拒绝口径离开。</summary>
        FrontDesk = 0,
        /// <summary>服务中：接待成功进入房间，等玩家提交物品；一次性、不可补交、不可重入。</summary>
        Serving = 1,
        /// <summary>闲逛：服务满意后在屋内游走冒泡，累计达种族上限自行离开；打烊时可 roll 跨天留宿。</summary>
        Wandering = 2,
        /// <summary>已离场（终态标记；实例同时从在场列表移除）。</summary>
        Departed = 3,
    }

    /// <summary>服务满意度四档（访客交付说明 §4.7）。</summary>
    public enum EServeSatisfaction
    {
        Mismatch = 0,  // 不对味：任一必要需求未命中
        Plain = 1,     // 一般：加分项命中比例低于阈值A
        Satisfied = 2, // 满意：加分项命中比例 ≥ 阈值A 且未全中
        Perfect = 3,   // 完美：加分项全命中（或需求里没有加分项）
    }

    /// <summary>满意度展示文案（下标 = (int)EServeSatisfaction）。</summary>
    public static class ServeSatisfactionText
    {
        public static readonly string[] Names = { "不对味", "一般", "满意", "完美" };

        public static string NameOf(EServeSatisfaction satisfaction) => Names[(int)satisfaction];
    }

    /// <summary>需求项（§4.6）：tag + 是否必要。</summary>
    public struct VisitorNeed
    {
        public TagDef Tag;
        public bool Required;
    }

    /// <summary>
    /// 运行时访客实例（访客交付说明 §4.6）。只能由 VisitorManager 修改（§11.4）。
    /// 所有计时以 VisitorData.BusinessTick 的整数比较进行（§11.3）。
    /// </summary>
    public sealed class VisitorInstance
    {
        /// <summary>稳定自增 id（遍历排序键，§11.2/11.3；随存档序列化）。</summary>
        public int InstanceId;

        public VisitorRaceDef Race;

        /// <summary>来源日程条目：第几天（重算派生种子用，§6.1）。</summary>
        public int ScheduleDay;

        /// <summary>来源日程条目：日程表 entries 的原始下标（重算派生种子用，§6.1）。</summary>
        public int ScheduleIndex;

        public EVisitorState State;

        /// <summary>进入当前状态时的业务 tick（算超时用，整数比较）。</summary>
        public long StateEnterTick;

        /// <summary>
        /// 当前所在房间（Hub 四宫格下标，0 = 起居室/门厅）。进场默认 0；
        /// 玩家在 Hub 场景把访客拖到其他房间时经 VisitorManager.MoveVisitorToRoom 修改（2026-08-13）。
        /// 纯位置信息，不参与超时/评分等业务判定。
        /// </summary>
        public int RoomIndex;

        /// <summary>需求项列表（roll 顺序即派生随机流顺序，天然稳定）。</summary>
        public readonly List<VisitorNeed> Needs = new List<VisitorNeed>();

        /// <summary>满意度（Submit 结算之后有效）。</summary>
        public EServeSatisfaction Satisfaction;

        /// <summary>提交的物品（结算展示与日志用）。</summary>
        public ItemDef SubmittedItem;

        /// <summary>下次闲逛冒泡的业务 tick（0 = 未排程）。</summary>
        public long NextBubbleTick;

        /// <summary>实例随机流：派生自 rollSeed（§6.1），需求 roll 之后继续用于冒泡抖动与跨天留宿 roll。</summary>
        public DeterministicRng Rng;

        public string DisplayName => Race != null ? Race.displayName : "访客";

        /// <summary>无外部注入时共用的默认短语组装器（无状态，线程无关）。</summary>
        private static readonly INeedPhraseBuilder DefaultPhraseBuilder = new DefaultNeedPhraseBuilder();

        /// <summary>
        /// 程序化需求句：任务卡等 UI 直接展示需求时用。
        ///
        /// 组装规则已于 2026-08-12 收进对话系统的 INeedPhraseBuilder（对话设计说明 §9），本方法只是薄壳。
        /// 与访客重做期的旧规则有两处不同，以 §9 为准：
        ///   ①「甜的、软的食物」而不是把每项平铺（形容词修饰中心名词，中心词取树最深的名词）；
        ///   ② **不再标注「（加分）」**——那是评分规则，写进台词等于给玩家漏答案。
        /// 台词里要说需求请用占位符 {需求}（§9），不要绕道调本方法。
        /// </summary>
        public string BuildNeedSentence(INeedPhraseBuilder builder = null)
        {
            var phrase = (builder ?? DefaultPhraseBuilder).Build(Needs);
            return string.IsNullOrEmpty(phrase) ? "我随便看看就好。" : $"我想要{phrase}。";
        }
    }
}

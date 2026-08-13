namespace MasterHouse
{
    /// <summary>
    /// 访客生命周期状态（访客交付说明 §5 + 需求重做说明 §4.4/§5.3）。到场前的「日程未到点」不建实例（由日程游标表达），
    /// 离场即从在场列表移除，Departed 仅作为移除瞬间的终态标记。
    ///
    /// **枚举值必须显式赋值且新增只能追加**——存档接缝 VisitorInstanceSaveData.state 存的是 (int)State，
    /// 改动已有值会静默错乱（§4.4）。
    /// </summary>
    public enum EVisitorState
    {
        /// <summary>前台等待接待：日程到点进场后站在起居室入口区排队，等玩家搭话/接待，超时按被拒绝口径离开。</summary>
        FrontDesk = 0,
        /// <summary>服务中：已入住某间客房，等需求被满足；房间被占，**服务中锁房**（不可拖走，§5.2）。</summary>
        Serving = 1,
        /// <summary>闲逛：服务完成后在自己房间游走冒泡，**房间仍被占**；累计达种族上限自行离开，打烊时可 roll 跨天留宿。</summary>
        Wandering = 2,
        /// <summary>已离场（终态标记；实例同时从在场列表移除）。</summary>
        Departed = 3,
        /// <summary>
        /// 等待分配房间：已接待、仍站在起居室入口区，等玩家把他拖进一间空客房（需求重做说明 §5.3）。
        /// **需求此时尚未透露**——【初次见面】只负责打招呼与接待/拒绝，进房后才播【开始等待服务】说出需求。
        /// 「先盲选房、进房后才说需求」是硬要求，别把需求提前泄给玩家。
        /// </summary>
        AwaitingRoom = 4,
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

    // 需求项 VisitorNeed（tag + 是否必要）已随 tag 需求体系退役（需求重做说明 §9.1）。
    // 现在一位访客只带一条 NeedDef，来自日程条目、零随机。

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
        /// **所住房间**（Hub 四宫格下标，0 = 起居室 = 大堂，不可分配为客房；1~3 为客房）。
        /// 进场默认 0；玩家把访客从「等待分配房间」拖进空客房时经 VisitorManager.MoveVisitorToRoom 落定。
        ///
        /// 2026-08-13 需求重做起，本字段从「纯位置信息」升级为**业务真相**：
        /// 条件类需求的判定依据就是「这个房间里有没有那件家具」（§5/§6），一房一客的占用校验也读它。
        /// </summary>
        public int RoomIndex;

        /// <summary>
        /// 本次拜访的需求（来自日程条目，零随机；需求重做说明 §4.2/§4.3）。
        /// 为空的日程条目在投放时就被 LogError 拦下，所以在场实例上这一格恒非空。
        /// </summary>
        public NeedDef Need;

        /// <summary>满意度（CompleteNeed 结算之后有效）。</summary>
        public EServeSatisfaction Satisfaction;

        /// <summary>下次闲逛冒泡的业务 tick（0 = 未排程）。</summary>
        public long NextBubbleTick;

        /// <summary>实例随机流：派生自 rollSeed（§6.1），需求 roll 之后继续用于冒泡抖动与跨天留宿 roll。</summary>
        public DeterministicRng Rng;

        public string DisplayName => Race != null ? Race.displayName : "访客";

        /// <summary>
        /// 需求句：任务卡等 UI 直接展示需求时用。
        ///
        /// 现在就是**直接取 NeedDef.description**——那句话由策划在需求资产里写死
        /// （需求重做说明 §4.3）。基于 tag 森林的程序化造句器 INeedPhraseBuilder 已随 Item 链退役（§9.1）：
        /// 需求不再是一组 tag，没有可组装的东西了。
        ///
        /// 薄壳保留是为了不动调用点。台词里要说需求请用占位符 {需求}，不要绕道调本方法。
        /// </summary>
        public string BuildNeedSentence() => Need != null ? Need.description : string.Empty;
    }
}

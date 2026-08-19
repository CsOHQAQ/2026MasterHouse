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
        /// <summary>
        /// 前台等待接待：日程到点进场后站在起居室入口区排队。**只有队首、且现在接待得了时可交互**。
        /// 等搭话超时则自己走了（不播对话、不扣声望）；打烊时由 EndDay 统一清场。
        /// </summary>
        FrontDesk = 0,
        /// <summary>
        /// 服务中：已入住某间客房，房间被占、**锁房不可拖走**。分两段——先安顿（NeedPromptTick 未到，
        /// 点他只有提示），到点开口示意后才能点开【需求对话】，服务超时也从示意那一刻起算。
        /// </summary>
        Serving = 1,
        /// <summary>
        /// 停留：需求了结（交付成功或超时）后在自己房间游走冒泡，**房间仍被占**；
        /// 累计达种族上限后转【待告别】（2026-08-20 起不再直接离场）。
        /// **跨天不 roll**，无条件保留到次日继续计时。
        /// </summary>
        Wandering = 2,
        /// <summary>已离场（终态标记；实例同时从在场列表移除）。</summary>
        Departed = 3,
        /// <summary>
        /// 等待分配房间：已接待、仍站在起居室入口区，等玩家把他拖进一间空客房。
        /// **需求此时尚未透露**——【初次见面】只负责打招呼与接待/拒绝，进房安顿完才说需求。
        /// 「先盲选房、安顿后才说需求」是硬要求，别把需求提前泄给玩家。
        ///
        /// 全场**最多同时 1 位**（CanAcceptGuest 串行化），所以他一定分得到房——
        /// 也因此这一态**不给拒绝出口**，且是唯一阻塞【结束今天】的状态。
        /// </summary>
        AwaitingRoom = 4,

        /// <summary>
        /// 待告别：停留时长到点，人还在自己房间里、**房间仍被占**，等玩家点他道个别（2026-08-20 定案）。
        /// 点击播【告别】对话，组末尾那条 `Action | Leave` 执行时才真的离场。
        ///
        /// **没有超时**（已定案可接受）：玩家一直不点，他就一直等在场上占着这间房——
        /// 这是自愿的代价，换来「客人不会在玩家没看见的时候悄悄消失」。
        /// 打烊也不清场（EndDay 只清前台），跟服务中/闲逛一样无条件跨天。
        ///
        /// **不可拖动、不可拒绝、不阻塞【结束今天】**：他不是玩家欠着的事，只是还没道别。
        /// </summary>
        AwaitingFarewell = 5,
    }

    /// <summary>
    /// 服务满意度四档，对应四个【需求反馈】对话分类（DialogueCategoryText.FeedbackOf）。
    ///
    /// **枚举名保持历史值不动**（存档接缝与 EconomyConfig 的字段名都按它对齐），
    /// 但语义已随 2026-08-14 重构更新，展示文案见下面的 Names：
    ///   Mismatch  → 失望：服务超时，需求没办到
    ///   Plain     → 一般：小游戏低分（条件类走不到）
    ///   Satisfied → 还行：小游戏中间分（条件类走不到）
    ///   Perfect   → 完美：条件类交付成功 / 小游戏满分
    /// </summary>
    public enum EServeSatisfaction
    {
        Mismatch = 0,
        Plain = 1,
        Satisfied = 2,
        Perfect = 3,
    }

    /// <summary>满意度展示文案（下标 = (int)EServeSatisfaction）。</summary>
    public static class ServeSatisfactionText
    {
        public static readonly string[] Names = { "失望", "一般", "还行", "完美" };

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

        /// <summary>
        /// 需求是否真的被满足过（`SettleNeedResult` 的 countAsServed 那一路才置位）。
        ///
        /// **不能用 Satisfaction 代替**：服务超时也会写 Satisfaction（失望档），
        /// 但那一路 countAsServed = false，不算完成服务。
        /// 离场小费的装饰分加成只给这一格为 true 的客人（家具库存说明 §6.1）。
        /// </summary>
        public bool NeedFulfilled;

        /// <summary>下次闲聊冒泡的业务 tick（0 = 未排程）。</summary>
        public long NextBubbleTick;

        /// <summary>
        /// 玩家是否已经跟他打过招呼（【初次见面】**正常播完**才置位；ESC 中断视为没播过）。
        /// 前台访客的二次点击据此改抽【等待接待】，不会把开场白重放一遍。
        ///
        /// **不进存档**（2026-08-14 定案）：对话侧整体只留接缝、不接存档（待定 #9），
        /// 这一格跟着一起等。
        /// </summary>
        public bool MetPlayer;

        /// <summary>
        /// 「已示意」的业务 tick：入住之后随机安顿一段时间才开口（VisitorTuningConfig.needPrompt*）。
        /// 0 = 还没排程（不该出现在 Serving 态上）。
        ///
        /// 这一格同时是**服务超时的起算点**——超时从他示意那一刻开始算，而不是从进屋开始算，
        /// 玩家不该为客人安顿的那段时间买单（2026-08-14 第 4/5 题定案）。
        /// </summary>
        public long NeedPromptTick;

        /// <summary>
        /// 实例随机流：派生自 rollSeed（§6.1），需求 roll 之后继续用于冒泡抖动。
        /// （原先还用于跨天留宿 roll，该 roll 已于 2026-08-14 删除，见 VisitorManager.EndDay 的注释。）
        /// </summary>
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

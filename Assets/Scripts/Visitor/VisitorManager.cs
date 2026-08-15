using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 访客业务逻辑（访客交付说明 §3/§5/§6/§7）：日程投放、五态状态机推进、两段超时、
    /// 接待/拒绝/提交结算、闲逛冒泡调度与日结，全部挂全局 tick、整数比较（§16.4/§11.3）。
    /// 表现层只读实例列表生成演员，不回写业务（§16.4 表现层豁免）。
    /// 对话事件经 Accept/Reject/CompleteNeed 驱动业务（§8 契约，状态不对时返回 false 而不是抛异常）。
    /// </summary>
    public class VisitorManager
    {
        /// <summary>存档未落地期间的固定默认 runSeed（§6.1，待定 #9）；GM 面板可改写，存档接入后改为存档字段。</summary>
        public const long DefaultRunSeed = 20260810;

        // ── 房间语义常量（需求重做说明 §5.1；禁止把这些数字散落成魔数）──

        /// <summary>起居室 = 大堂：大门所在地，访客到场在此入口区排队等接待。**不可分配为客房**。</summary>
        public const int LobbyRoomIndex = 0;

        /// <summary>可分配客房的下标区间 [FirstGuestRoomIndex, LastGuestRoomIndex]（卧室/厨房/书房），一房一客。</summary>
        public const int FirstGuestRoomIndex = 1;
        public const int LastGuestRoomIndex = 3;

        /// <summary>
        /// 前台同时容纳的访客上限（§5.4）。计数口径包含「前台等待接待」与「等待分配房间」两态——
        /// 两者都站在起居室入口区，视觉上就是同一条门口队伍（2026-08-13 访谈定案）。
        /// </summary>
        public const int FrontDeskCapacity = 2;

        private readonly VisitorScheduleTable schedule;
        private readonly VisitorTuningConfig tuning;
        private readonly HouseClockManager clock;
        private readonly EconomyManager economy;
        private readonly IDialogueService dialogue;

        /// <summary>按 (day, 出现时刻, 原始下标) 稳定排序后的日程（§4.4）；Index 指回 entries 原始下标（派生种子键，§6.1）。</summary>
        private readonly List<(int Day, int Minute, int Index)> sortedSchedule = new List<(int, int, int)>();

        // ── tick 内的收集缓冲 ──
        // 存在的理由是同一条：**遍历 Data.Instances 期间不做任何对外广播、不改集合**。
        // 广播是同步调用链，对话事件（Accept/Reject/CompleteNeed）会改在场列表。
        // departBuffer 有两个消费点（TickStates 与 EndDay），各自 Clear 后即用即弃、**不可重入**。

        /// <summary>离场。</summary>
        private readonly List<VisitorInstance> departBuffer = new List<VisitorInstance>();

        /// <summary>服务超时（走结算而不是直接离场）。</summary>
        private readonly List<VisitorInstance> timeoutBuffer = new List<VisitorInstance>();

        /// <summary>本 tick 刚开口示意的（循环外广播 InstanceChanged，好让访客卡亮提示）。</summary>
        private readonly List<VisitorInstance> promptedBuffer = new List<VisitorInstance>();

        /// <summary>本 tick 该冒闲聊气泡的。</summary>
        private readonly List<VisitorInstance> bubbleBuffer = new List<VisitorInstance>();

        public VisitorData Data { get; } = new VisitorData();

        // ── 粗粒度事件广播（§2.1：离散变化 Manager 广播，表现层/UI 订阅刷新）──

        /// <summary>新访客进场（前台等待接待）。</summary>
        public event Action<VisitorInstance> InstanceSpawned;

        /// <summary>实例状态推进（接待/提交等）。</summary>
        public event Action<VisitorInstance> InstanceChanged;

        /// <summary>实例离场（已从在场列表移除）。</summary>
        public event Action<VisitorInstance> InstanceDeparted;

        /// <summary>
        /// 对话分类被请求。对话本体的播放由 IDialogueService 实现方负责，
        /// 本事件只是给表现层的旁路通知（演员表情、日志、埋点）——**不要在订阅方里再播一次对话**。
        /// </summary>
        public event Action<VisitorInstance, EDialogueCategory> DialogueRequested;

        /// <summary>日结完成（携带当日累计快照，面板展示用；只展示不惩罚，§7）。</summary>
        public event Action<VisitorDaySummary> DayEnded;

        // PlayerCargoData 参数已随小游戏框架落地第 2 步删除：物品交付早在需求重做时就退役了，
        // 参数当时只为等 NodeSim 包一起清理而留着（需求重做说明 §9.2），现在那一包已经清完。
        public VisitorManager(VisitorScheduleTable schedule, VisitorTuningConfig tuning,
            HouseClockManager clock, EconomyManager economy, IDialogueService dialogue)
        {
            this.schedule = schedule;
            this.tuning = tuning;
            this.clock = clock;
            this.economy = economy;
            this.dialogue = dialogue;
            Data.RunSeed = DefaultRunSeed;
            BuildSortedSchedule();
        }

        private void BuildSortedSchedule()
        {
            sortedSchedule.Clear();
            if (schedule == null) return;
            for (var i = 0; i < schedule.entries.Count; i++)
            {
                var entry = schedule.entries[i];
                if (entry == null || entry.race == null)
                {
                    Debug.LogError($"[VisitorManager] 日程表第 {i} 行缺少种族引用，已跳过（§4.4）");
                    continue;
                }
                sortedSchedule.Add((entry.day, entry.appearMinute, i));
            }
            // (day, 出现时刻, 下标) 稳定排序（§4.4/§11.2）
            sortedSchedule.Sort((a, b) =>
            {
                if (a.Day != b.Day) return a.Day.CompareTo(b.Day);
                if (a.Minute != b.Minute) return a.Minute.CompareTo(b.Minute);
                return a.Index.CompareTo(b.Index);
            });
        }

        /// <summary>
        /// 每全局 tick 调用一次（GameManager，时钟推进之后）。
        /// 闸门关闭期间整体停表：业务 tick 不走，超时/冒泡计时天然冻结、不需要逐个实例暂停。
        /// 关闸的三种情形——不在 Hub 页、模态对话框开启（对话设计说明 §8）、打烊（§7）。
        /// </summary>
        public void Tick()
        {
            // IsRunning 自 2026-08-12 起已经把打烊与全部停走原因（不在 Hub 页 / 模态对话框）都算进去了，
            // 不必再单独判 IsClosedForToday
            if (!clock.IsRunning) return;
            Data.BusinessTick++;
            SpawnDueVisitors();
            TickStates();
        }

        // ── 日程投放（§4.4）──

        private void SpawnDueVisitors()
        {
            var day = clock.Data.Day;
            var minute = clock.Data.MinuteOfDay;
            while (Data.ScheduleCursor < sortedSchedule.Count)
            {
                var entry = sortedSchedule[Data.ScheduleCursor];
                if (entry.Day < day)
                {
                    // 待确认 #3（§5.4）：当天打烊时仍未投放的日程条目**默认作废并打 Warning**，不顺延到次日
                    // ——顺延会滚雪球，第二天开门先堆一屋子昨天的客人。
                    // 到这里的两种来路：出现时刻配在营业时段之外；或前台/客房一直没腾出来。
                    Debug.LogWarning($"[VisitorManager] 日程条目当天未投放已作废（day={entry.Day} minute={entry.Minute}），不顺延到次日；" +
                                     "请检查出现时刻是否在营业时段内，或当天前台/客房是否长期占满（§5.4）");
                    Data.ScheduleCursor++;
                    continue;
                }
                if (entry.Day > day || entry.Minute > minute) break;

                // 投放前置条件（§5.4）：前台没排满 且 至少有一间空客房。
                // 不满足时**游标不前进**，卡在这一条上等待——玩家一腾出前台/房间，下一位立刻进场。
                // 因此日程表的「出现时刻」语义退化为「**最早**出现时刻」。
                if (FrontDeskCount >= FrontDeskCapacity || !HasFreeRoom) break;

                Spawn(entry.Day, entry.Index);
                Data.ScheduleCursor++;
            }
            // 「日程跑完」原先在这里打一条 Warning 占位。2026-08-15 起改由**感谢试玩页**收尾
            //（家具库存说明 §6.5）：最后一天日结之后走整页路由离开 Hub，
            // OffHubPage 闸门生效 → tick 停 → 天然不再投放，不需要额外的开关。
        }

        /// <summary>
        /// 这一天是不是日程表的最后一天（demo 结局的判据，家具库存说明 §6.5）。
        ///
        /// 用「天」而不是「游标跑完」：游标可能因前台排满或客房住满卡在某条上不前进，
        /// 按天更可预期。**空日程表返回 false**——否则表缺失时第一天就弹结局。
        /// </summary>
        public bool IsFinalScheduledDay(int day) =>
            sortedSchedule.Count > 0 && day >= sortedSchedule[sortedSchedule.Count - 1].Day;

        private void Spawn(int scheduleDay, int scheduleIndex)
        {
            var entry = schedule.entries[scheduleIndex];
            if (entry.need == null)
            {
                // 需求必填（§4.2）：新模型下没有需求的访客进场也无事可做，直接拦在投放这一步。
                // 指名日程表的**原始行号**（不是排序后的次序），策划照着这个数去 Excel 里找那一行
                Debug.LogError($"[VisitorManager] 日程表第 {scheduleIndex} 行（day={scheduleDay} " +
                               $"race={(entry.race != null ? entry.race.raceId : "?")}）没有配置需求，已跳过投放；" +
                               "请在访客日程表.xlsx 的「需求」列填 NeedDef 的资产名（§4.2）");
                return;
            }
            var instance = new VisitorInstance
            {
                InstanceId = Data.NextInstanceId++,
                Race = entry.race,
                ScheduleDay = scheduleDay,
                ScheduleIndex = scheduleIndex,
                Need = entry.need, // 零随机：需求由策划在日程条目上配死（§4.2）
                State = EVisitorState.FrontDesk,
                RoomIndex = LobbyRoomIndex, // 到场先站大堂入口区排队，接待后才由玩家拖进客房
                StateEnterTick = Data.BusinessTick,
            };
            // 派生种子（§6.1）：每条日程独立推导随机流，不依赖调用顺序、无论何时重算都一致。
            // 需求已改为策划配死、不再 roll（§4.2），这条随机流现在只服务于闲逛冒泡抖动、
            // 跨天留宿 roll 与对话组选取——它们仍然要「读档不刷」
            var rollSeed = DeterministicRng.Hash(Data.RunSeed, scheduleDay, scheduleIndex);
            instance.Rng = new DeterministicRng(rollSeed);
            // 待确认（§4.4）：具名覆写 namedOverride 结构已建、运行时暂不消费（现阶段无剧情内容），entry.namedOverride 留待接通
            Data.Instances.Add(instance);
            InstanceSpawned?.Invoke(instance);
        }

        // ── 状态推进：两段超时 + 闲逛（§5）──

        private void TickStates()
        {
            departBuffer.Clear();
            timeoutBuffer.Clear();
            promptedBuffer.Clear();
            bubbleBuffer.Clear();
            foreach (var instance in Data.Instances) // 在场列表按 InstanceId 升序（§11.2）
            {
                var elapsed = Data.BusinessTick - instance.StateEnterTick;
                switch (instance.State)
                {
                    case EVisitorState.FrontDesk:
                        // 等搭话超时：**不播对话、不扣声望**，只是自己走了（2026-08-14 第 6 题定案）。
                        // 玩家从没跟他说过话，播【被拒绝】没有道理——那个分类也已随之整个删除。
                        if (elapsed >= instance.Race.waitTalkTimeoutTicks) departBuffer.Add(instance);
                        break;
                    // AwaitingRoom（等待分配房间）**刻意没有超时**：这一态只有「拖进空房」一条出口，
                    // 连拒绝都不给（接待时 CanAcceptGuest 已经保证有房，他一定分得到）。
                    // 它是唯一阻塞【结束今天】的状态——玩家想收工就必须先把人安顿好。
                    case EVisitorState.Serving:
                        // 安顿结束、开口示意的那一 tick 记一笔，循环外再广播。
                        // 用相等而不是 >=：BusinessTick 每 tick 加一，正好命中一次，不会连播。
                        if (Data.BusinessTick == instance.NeedPromptTick) promptedBuffer.Add(instance);
                        // 服务超时从**示意那一刻**起算，而不是从进屋起算：安顿的那段时间不该算玩家头上。
                        // 还没示意（NeedPromptTick 未到）时永远不会超时。
                        if (IsNeedPrompted(instance) &&
                            Data.BusinessTick - instance.NeedPromptTick >= instance.Race.waitDeliverTimeoutTicks)
                            timeoutBuffer.Add(instance);
                        break;
                    case EVisitorState.Wandering:
                        if (elapsed >= instance.Race.wanderMaxTicks) departBuffer.Add(instance);
                        else if (instance.NextBubbleTick > 0 && Data.BusinessTick >= instance.NextBubbleTick)
                            bubbleBuffer.Add(instance);
                        break;
                }
            }

            // **一切对外广播都在遍历之后**：InstanceChanged / RequestDialogue 的调用链是同步的，
            // 而对话事件（Accept/Reject/CompleteNeed）会改 Data.Instances——
            // 今天闲聊冒泡不会走到那些事件，但把广播留在循环里等于给后来人埋一个
            // InvalidOperationException。三个 buffer 的成本是零，规矩清楚。
            foreach (var instance in promptedBuffer) InstanceChanged?.Invoke(instance);
            foreach (var instance in bubbleBuffer)
            {
                RequestDialogue(instance, EDialogueCategory.SmallTalk);
                ScheduleNextBubble(instance);
            }
            // 服务超时：与「完成需求」同一条路，只是档位是失望——播【需求反馈·失望】后转停留，
            // **不扣声望**（2026-08-14 第 6 题定案）。客人不会当场拂袖而去，还会在屋里待一会儿。
            foreach (var instance in timeoutBuffer)
                SettleNeedResult(instance, EServeSatisfaction.Mismatch, countAsServed: false);

            foreach (var instance in departBuffer)
            {
                if (instance.State == EVisitorState.Wandering) Data.Today.WanderDepartCount++;
                else Data.Today.RefusedCount++; // 前台等太久自己走了：计一次流失，但不扣声望
                Depart(instance);
            }
        }

        /// <summary>访客是否已经「开口示意」（头顶提示亮起、可以点开需求对话）。</summary>
        public bool IsNeedPrompted(VisitorInstance instance) =>
            instance != null && instance.State == EVisitorState.Serving &&
            instance.NeedPromptTick > 0 && Data.BusinessTick >= instance.NeedPromptTick;

        /// <summary>
        /// 入住后排一个「开口示意」的时刻：先安顿一段随机时间才有话说。
        /// 走实例自己的确定性随机流（读档不刷），区间配在 VisitorTuningConfig。
        /// </summary>
        private void ScheduleNeedPrompt(VisitorInstance instance)
        {
            var min = tuning != null ? Mathf.Max(0, tuning.needPromptMinTicks) : 60;
            var max = tuning != null ? Mathf.Max(min, tuning.needPromptMaxTicks) : 180;
            var delay = min == max ? min : instance.Rng.Range(min, max + 1);
            instance.NeedPromptTick = Data.BusinessTick + delay;
        }

        private void ScheduleNextBubble(VisitorInstance instance)
        {
            var interval = tuning != null ? tuning.bubbleIntervalTicks : 120;
            var jitter = tuning != null ? tuning.bubbleJitterTicks : 0;
            if (jitter > 0) interval += instance.Rng.Range(-jitter, jitter + 1);
            instance.NextBubbleTick = Data.BusinessTick + Mathf.Max(1, interval);
        }

        // ── 对话 → 访客的三个业务动作（§8 契约：公开方法 + 合法性校验，状态不对返回 false）──

        /// <summary>
        /// 接待：前台等待中的访客转入「等待分配房间」（需求重做说明 §5.3）。
        ///
        /// **这里不进「服务中」、也不说需求**——「先盲选房、进房后才说需求」是硬要求：
        /// 玩家必须在不知道需求的情况下挑一间房，赌注才是真的。需求由 MoveVisitorToRoom 落房后才播。
        ///
        /// 接待不了时返回 false。正常情况下【初次见面】/【等待接待】的「接待」选项挂了
        /// CanAcceptGuest 条件会自动置灰，这里再拦一道是防御——策划漏配条件时宁可接待不生效，
        /// 也不能让访客卡在无房可住的中间态。
        /// </summary>
        public bool Accept(int instanceId)
        {
            var instance = Find(instanceId);
            if (instance == null || instance.State != EVisitorState.FrontDesk) return false;
            if (!CanAcceptGuest)
            {
                Debug.LogWarning($"[VisitorManager] 接待未生效（实例 {instanceId}）：客房住满，" +
                                 "或者已经有一位客人在等分房（前台是串行队列，上一位安顿好才轮到下一位）；" +
                                 "「接待」选项应当挂上 CanAcceptGuest 条件");
                return false;
            }
            SetState(instance, EVisitorState.AwaitingRoom);
            return true;
        }

        /// <summary>
        /// 拒绝：在「前台等待接待」与「服务中」两个状态可用。
        ///
        /// **「等待分配房间」刻意不可拒绝**（2026-08-14 第 7/8 题定案）：接待的那一刻
        /// CanAcceptGuest 已经保证了有一间空房留给他，玩家必须把他安顿好，不能反悔。
        /// 也正因如此这一态永远不会卡死——房间一定在那儿等着。
        ///
        /// **拒绝不扣声望**（2026-08-15，家具库存说明 §6.4）：原先分前台/服务中两档的惩罚已整体移除，
        /// 原 SettleRefusal 只剩计数 + 离场两句，故内联到此。客人照拿基础小费，
        /// 但拿不到服务奖励、声望与装饰分加成——正向激励差就是全部代价。
        /// </summary>
        public bool Reject(int instanceId)
        {
            var instance = Find(instanceId);
            if (instance == null || !CanReject(instance.State)) return false;
            Data.Today.RefusedCount++;
            Depart(instance);
            return true;
        }

        private static bool CanReject(EVisitorState state) =>
            state == EVisitorState.FrontDesk || state == EVisitorState.Serving;

        // ── 房间占用（需求重做说明 §5.2）──

        /// <summary>
        /// 房间是否已被占用：在场实例中有 Serving 或 Wandering 且 RoomIndex == 该房。
        /// **闲逛也占房**——服务完成后访客仍在自己房间游走，直到离场才释放。
        /// 起居室（大堂）与越界下标一律视为「不可分配」，即恒占用。
        /// </summary>
        public bool IsRoomOccupied(int roomIndex)
        {
            if (roomIndex < FirstGuestRoomIndex || roomIndex > LastGuestRoomIndex) return true;
            foreach (var instance in Data.Instances)
            {
                if (instance.RoomIndex != roomIndex) continue;
                if (instance.State == EVisitorState.Serving || instance.State == EVisitorState.Wandering) return true;
            }
            return false;
        }

        /// <summary>是否还有空客房（1~3 中至少一间未被占用）。**投放**的前置条件用它。</summary>
        public bool HasFreeRoom
        {
            get
            {
                for (var room = FirstGuestRoomIndex; room <= LastGuestRoomIndex; room++)
                    if (!IsRoomOccupied(room)) return true;
                return false;
            }
        }

        /// <summary>场上是否已经有人在等分房（最多一位，见 CanAcceptGuest）。</summary>
        public bool HasAwaitingRoomVisitor
        {
            get
            {
                foreach (var instance in Data.Instances)
                    if (instance.State == EVisitorState.AwaitingRoom)
                        return true;
                return false;
            }
        }

        /// <summary>
        /// 现在能不能接待新客人（2026-08-14 第 8/9 题定案）：**有空客房 且 没有别人正在等分房**。
        ///
        /// 「等分房」是一个串行环节——上一位没安顿好，下一位不开始排到。这条不变式换来两个好处：
        /// 待分房的那位一定分得到房（所以不需要拒绝出口，也就不会在打烊时卡死），
        /// 而且玩家不会同时欠着两个人的房间。
        ///
        /// **投放不受这条约束**（那用 HasFreeRoom）：门口本来就允许排队，前台额度 FrontDeskCapacity
        /// 管着上限；只是队首访客点开时「接待」是灰的，得先把待分房那位安顿了。
        /// </summary>
        public bool CanAcceptGuest => HasFreeRoom && !HasAwaitingRoomVisitor;

        /// <summary>
        /// 前台队首：在场的 FrontDesk 访客里 InstanceId 最小的那位（自增 id = 到场先后）。
        /// 严格 FIFO——**只有队首可交互**，排在后面的没有提示也点不动（2026-08-14 第 10 题定案·甲）。
        /// </summary>
        public VisitorInstance FrontDeskHead
        {
            get
            {
                VisitorInstance head = null;
                foreach (var instance in Data.Instances) // 列表按 InstanceId 升序（§11.2）
                    if (instance.State == EVisitorState.FrontDesk) { head = instance; break; }
                return head;
            }
        }

        /// <summary>
        /// 「现在点他为什么没有对话」——**判据只有这一处**，访客卡与 Hub 的 Toast 都读它，
        /// 免得两边各写一套 switch 然后慢慢漂开（曾经就漂过：卡上说「腾不出房间」，实际原因是别人在等分房）。
        /// </summary>
        public enum ENoTalkReason
        {
            /// <summary>点得动，有对话。</summary>
            None = 0,
            /// <summary>前台但不是队首：先招呼前面那位。</summary>
            NotFrontOfQueue,
            /// <summary>已经有一位在等分房：前台是串行队列。</summary>
            SomeoneAwaitingRoom,
            /// <summary>客房住满。</summary>
            NoFreeRoom,
            /// <summary>已接待、等玩家拖进空房（这一态没有对话）。</summary>
            AwaitingRoom,
            /// <summary>入住后还在安顿，没到开口示意的时刻。</summary>
            SettlingIn,
            /// <summary>停留中（服务已了结）。</summary>
            Wandering,
        }

        /// <summary>见 ENoTalkReason。返回 None 表示这一下点击应该有对话。</summary>
        public ENoTalkReason NoTalkReason(VisitorInstance instance)
        {
            if (instance == null) return ENoTalkReason.Wandering;
            switch (instance.State)
            {
                case EVisitorState.FrontDesk:
                    // 队首判定放在最前：不是队首时，房间够不够根本轮不到他关心
                    if (FrontDeskHead != instance) return ENoTalkReason.NotFrontOfQueue;
                    if (HasAwaitingRoomVisitor) return ENoTalkReason.SomeoneAwaitingRoom;
                    if (!HasFreeRoom) return ENoTalkReason.NoFreeRoom;
                    return ENoTalkReason.None;
                case EVisitorState.AwaitingRoom:
                    return ENoTalkReason.AwaitingRoom;
                case EVisitorState.Serving:
                    return IsNeedPrompted(instance) ? ENoTalkReason.None : ENoTalkReason.SettlingIn;
                default:
                    return ENoTalkReason.Wandering;
            }
        }

        /// <summary>这位访客现在点不点得动（HubPage 用它分派点击）。</summary>
        public bool CanInteract(VisitorInstance instance) => NoTalkReason(instance) == ENoTalkReason.None;

        /// <summary>头顶/卡上该不该亮「有话要说」的提示（表现层只读；与 CanInteract 同一判据）。</summary>
        public bool WantsAttention(VisitorInstance instance) => CanInteract(instance);

        /// <summary>
        /// 前台在场人数：「前台等待接待」+「等待分配房间」（§5.4）。
        /// 两者都站在起居室入口区，视觉上就是同一条门口队伍，所以共用 FrontDeskCapacity 这一个额度。
        /// </summary>
        public int FrontDeskCount
        {
            get
            {
                var count = 0;
                foreach (var instance in Data.Instances)
                    if (instance.State == EVisitorState.FrontDesk || instance.State == EVisitorState.AwaitingRoom)
                        count++;
                return count;
            }
        }

        /// <summary>
        /// 玩家把访客拖到某个房间后松手（Hub 四宫格，2026-08-13；裁决规则见需求重做说明 §5.2）。
        /// 与 Accept/Reject 同口径：公开方法 + 合法性校验，状态不对返回 false 而不是抛异常（§8）——
        /// 表现层的自动弹回机制会把演员送回业务房间。
        ///
        /// | 状态         | 放行条件                | 行为                                               |
        /// |--------------|-------------------------|----------------------------------------------------|
        /// | AwaitingRoom | 目标 ∈ 1..3 且该房空闲  | **分房**：转 Serving，随后随机安顿一段才开口示意    |
        /// | Wandering    | 目标 ∈ 1..3 且该房空闲  | 换房，纯位置变更，无业务影响                       |
        /// | FrontDesk    | 否                      | 前台访客在门口排队，不可搬走                       |
        /// | Serving      | 否                      | **服务中锁房**                                     |
        ///
        /// 服务中锁房是设计要点而不是保守：不锁的话条件类需求会退化成
        /// 「把客人搬去已经有那件家具的房间」，盲选房的赌注就不存在了。
        /// </summary>
        public bool MoveVisitorToRoom(int instanceId, int roomIndex)
        {
            var instance = Find(instanceId);
            if (instance == null) return false;
            if (instance.State != EVisitorState.AwaitingRoom && instance.State != EVisitorState.Wandering) return false;
            // 只能拖进客房：大堂不可分配，越界不接
            if (roomIndex < FirstGuestRoomIndex || roomIndex > LastGuestRoomIndex) return false;
            // 一房一客。原地放下（闲逛访客在自己房里挪了个位置）不算换房，直接放行
            if (instance.RoomIndex != roomIndex && IsRoomOccupied(roomIndex)) return false;

            if (instance.State == EVisitorState.Wandering)
            {
                if (instance.RoomIndex == roomIndex) return true;
                instance.RoomIndex = roomIndex;
                InstanceChanged?.Invoke(instance);
                return true;
            }

            // AwaitingRoom → 分配房间并开始服务。**先落房间再转状态**：
            // SetState 会广播 InstanceChanged，订阅方（访客卡、任务卡）读到的必须已经是新房间
            instance.RoomIndex = roomIndex;
            SetState(instance, EVisitorState.Serving);
            // 2026-08-14 起**进屋不再自动弹对话**（第 4/5 题定案）：先安顿一段随机时间，
            // 到点头顶亮提示，玩家点他才播【需求对话】说出需求。自动弹模态会在玩家逛商店 /
            // 摆家具时冷不丁盖上来（那两处时钟照走），而家具模式还禁着整个壳 Canvas——
            // 那会变成「看不见的对话框 + 关不掉的闸门」。
            ScheduleNeedPrompt(instance);
            return true;
        }

        /// <summary>
        /// 完成需求结算（需求重做说明 §6.3）：记账 → 请求【完成服务·档位】对话 → 转闲逛
        /// （「不对味」档直接离场，不进闲逛）。取代随 Item 链退役的 Submit。
        ///
        /// satisfaction 由调用方给：条件类是布尔判定、固定传 Perfect；
        /// 小游戏类将来由小游戏框架按分数定档（§7）。四档枚举与四个【完成服务·档位】对话组
        /// **正是为此保留**的，别因为条件类只用得到 Perfect 就把它们删了。
        ///
        /// 与 Accept/Reject 同口径：状态不对返回 false 而不是抛异常（§8 契约）。
        /// </summary>
        public bool CompleteNeed(int instanceId, EServeSatisfaction satisfaction)
        {
            var instance = Find(instanceId);
            if (instance == null || instance.State != EVisitorState.Serving) return false;
            SettleNeedResult(instance, satisfaction, countAsServed: true);
            return true;
        }

        /// <summary>
        /// 需求结算的统一出口：记账 → 播【需求反馈·档位】→ **一律转停留**。
        ///
        /// 两条来路走同一个形状（2026-08-14 第 6 题定案）：
        ///   玩家在【需求对话】里选「交付」→ CompleteNeed(完美 / 小游戏分数档)
        ///   服务超时                        → 这里传失望档、countAsServed = false
        ///
        /// 超时那条**不扣声望、也不计入当日服务数**，只是拿不到奖励；而且客人不会当场走人——
        /// 他还会在屋里停留一段（停留时长从转 Wandering 这一刻起算）再离开。
        /// 房间在停留期间**不释放**，直到真正离场才腾出来。
        /// </summary>
        private void SettleNeedResult(VisitorInstance instance, EServeSatisfaction satisfaction, bool countAsServed)
        {
            instance.Satisfaction = satisfaction;
            if (countAsServed)
            {
                // 只有这一路算「完成服务」：离场时的装饰分小费加成认这一格，超时那一路拿不到（§6.1）
                instance.NeedFulfilled = true;
                var (currency, reputation) = economy.CompleteGuestService(satisfaction);
                Data.Today.ServedBySatisfaction[(int)satisfaction]++;
                Data.Today.CurrencyEarned += currency;
                Data.Today.ReputationEarned += reputation;
            }
            RequestDialogue(instance, DialogueCategoryText.FeedbackOf(satisfaction));
            SetState(instance, EVisitorState.Wandering);
            ScheduleNextBubble(instance);
        }

        /// <summary>
        /// 玩家点击访客时的对话分派（2026-08-14 重构）：由本方法而不是 UI 决定说哪一类。
        /// 返回 false 表示「这一下点击不该有对话」，调用方自行给 Toast。
        ///
        ///   前台队首 + 现在接待得了 → 【初次见面】（首次）/【等待接待】（已打过招呼）
        ///   服务中 + 已开口示意     → 【需求对话】（说需求 + 交付/推迟/放弃分支）
        ///   其余                    → false
        /// </summary>
        public bool RequestTalk(int instanceId)
        {
            var instance = Find(instanceId);
            if (instance == null || !CanInteract(instance)) return false;
            // 返回值一路透传到 UI：内容缺失（分类空 / 条件全不满足 / 组里全是事件）时对话不会出现，
            // 那时得给玩家一句话，否则就是「点了、响了个音效、然后什么都没发生」
            return RequestDialogue(instance, instance.State == EVisitorState.Serving
                ? EDialogueCategory.NeedTalk
                : instance.MetPlayer ? EDialogueCategory.WaitingReception : EDialogueCategory.FirstMeeting);
        }

        /// <summary>
        /// 记下「已经打过招呼」（由 DialogueManager 在【初次见面】**正常播完**时回调）。
        /// ESC 中断不会走到这里——中断视为这段对话没发生，下次点他仍是初次见面。
        /// </summary>
        public void MarkMetPlayer(int instanceId)
        {
            var instance = Find(instanceId);
            if (instance != null) instance.MetPlayer = true;
        }

        // ── 日结（§7）──

        /// <summary>
        /// 场上是否还有未处理访客——**只剩「等待分配房间」这一种**（2026-08-14 第 11 题定案）。
        ///
        /// 打烊后时钟停走、所有超时一并停表，所以「阻塞」意味着玩家必须有办法亲手解开它：
        ///   前台等待 → 不阻塞：EndDay 里自动清场（从没答应过他们什么，店打烊了自然就散了）
        ///   服务中   → 不阻塞：原样跨天，次日续算延迟与超时，玩家可以选择今天办还是明天办
        ///   等待分房 → **阻塞**：他是你已经点头答应的客人，而 CanAcceptGuest 保证了一定有空房，
        ///              拖进去就解开了，不会死锁
        /// </summary>
        public bool HasBlockingVisitors
        {
            get
            {
                foreach (var instance in Data.Instances)
                    if (instance.State == EVisitorState.AwaitingRoom)
                        return true;
                return false;
            }
        }

        public bool CanEndDay => !HasBlockingVisitors;

        /// <summary>
        /// 结束今天（§7）：前台访客自动离场 → 生成当日结算快照 → 时钟跳次日开门时刻并解冻。
        /// 场上还有待分房访客时不可用（返回 null）。
        /// 日结只展示不惩罚——惩罚已在拒绝当时结清，这里不重复扣。
        ///
        /// 闲逛访客的「跨天留宿 roll」已于 2026-08-14 删除：服务中/待分房都无条件跨天了，
        /// 单给闲逛的掷一次骰子不一致；现在统一按停留时长走，到点自己离开。
        /// （因此失去消费方的 VisitorRaceDef.stayOvernightPercent 与种族表那一列已在同日的
        /// 立绘 ID 化那一轮里删除。日结快照里的 StayOvernightCount 是**实际留宿人数**，与它无关。）
        /// </summary>
        public VisitorDaySummary EndDay()
        {
            if (HasBlockingVisitors) return null;
            departBuffer.Clear();
            foreach (var instance in Data.Instances)
                if (instance.State == EVisitorState.FrontDesk)
                    departBuffer.Add(instance); // 打烊清场：门口没被接待的客人自己走了，不扣声望
            foreach (var instance in departBuffer)
            {
                Data.Today.RefusedCount++;
                Depart(instance);
            }
            var summary = Data.Today.Clone();
            clock.NextDay(); // Day+1，时间跳次日开门时刻（解冻打烊闸门）
            Data.Today.Reset();
            DayEnded?.Invoke(summary);
            return summary;
        }

        // ── 查询 ──

        public VisitorInstance Find(int instanceId)
        {
            foreach (var instance in Data.Instances)
                if (instance.InstanceId == instanceId)
                    return instance;
            return null;
        }

        /// <summary>当前在场访客数（HubPage「当前在场访客」语义，§10）。</summary>
        public int CountOnStage => Data.Instances.Count;

        /// <summary>GM：改写 runSeed（§6.1，存档未落地期间的调试入口）。只影响此后新投放访客的需求 roll。</summary>
        public void SetRunSeed(long seed) => Data.RunSeed = seed;

        /// <summary>GM 召唤计数：派生种子键与真实日程条目错开（10000 起），实例不参与存档恢复（debug 专用）。</summary>
        private int gmSpawnCount;

        /// <summary>
        /// GM：立即召唤一位访客到前台（忽略日程、营业时段与前台/房间上限，验证接待流程用）。
        /// race 为空时按日程表引用的种族轮换；need 为空时借用日程表里第一条配好的需求
        /// （GM 是调试入口，不该因为「没指定需求」就召唤不出人）。日程表两者都拿不到时返回 null。
        /// </summary>
        public VisitorInstance GmSpawnVisitor(VisitorRaceDef race = null, NeedDef need = null)
        {
            if (race == null)
            {
                var races = new List<VisitorRaceDef>();
                if (schedule != null)
                    foreach (var entry in schedule.entries)
                        if (entry != null && entry.race != null && !races.Contains(entry.race))
                            races.Add(entry.race);
                if (races.Count == 0) return null;
                race = races[gmSpawnCount % races.Count];
            }
            if (need == null && schedule != null)
                foreach (var entry in schedule.entries)
                    if (entry != null && entry.need != null) { need = entry.need; break; }
            if (need == null)
            {
                Debug.LogWarning("[VisitorManager] GM 召唤失败：日程表里没有任何配好需求的条目，" +
                                 "无法借用需求（请先在访客日程表的「需求」列填上 NeedDef 资产名）");
                return null;
            }
            var seedIndex = 10000 + gmSpawnCount++;
            var instance = new VisitorInstance
            {
                InstanceId = Data.NextInstanceId++,
                Race = race,
                ScheduleDay = clock.Data.Day,
                ScheduleIndex = seedIndex,
                Need = need,
                State = EVisitorState.FrontDesk,
                RoomIndex = LobbyRoomIndex,
                StateEnterTick = Data.BusinessTick,
            };
            instance.Rng = new DeterministicRng(DeterministicRng.Hash(Data.RunSeed, clock.Data.Day, seedIndex));
            Data.Instances.Add(instance);
            InstanceSpawned?.Invoke(instance);
            return instance;
        }

        /// <summary>清空全部访客状态（新游戏/GM 重置）。</summary>
        public void ResetNew()
        {
            Data.Instances.Clear();
            Data.BusinessTick = 0;
            Data.NextInstanceId = 1;
            Data.ScheduleCursor = 0;
            Data.Today.Reset();
        }

        // ── 内部结算 ──

        private void SetState(VisitorInstance instance, EVisitorState state)
        {
            instance.State = state;
            instance.StateEnterTick = Data.BusinessTick;
            InstanceChanged?.Invoke(instance);
        }

        /// <summary>
        /// 离场：留下小费 → 移出在场列表（房间随之释放）。
        ///
        /// **所有业务访客离场都给基础小费，包括被拒绝与超时流失的**（需求重做说明 §8）——
        /// 这是新模型下「不会陷入没钱死循环」的保证。
        /// **装饰分加成只给完成需求的客人**（家具库存说明 §6.1）：被拒绝的客人不会因为房间漂亮多给钱，
        /// 而且不这么限的话「装修好 + 全部拒绝」会是纯收益最优解。
        ///
        /// 取分口径 = **离场时所在的房间**，不为任何情况特判：前台等搭话超时 / 在前台被拒绝的
        /// RoomIndex 本来就是 0（大堂），所以大堂装修同样有回报，规则天然统一。
        ///
        /// ⚠ 家具模式开着时时钟照走（OpenFurnitureMode 不退页），所以这里可能在 active 房间被打开的状态下
        /// 经 DecorationScoreOf → CaptureSessionPlacements 触发一次 View 层的 SaveState()。
        /// 已知且刻意：那只写它自己的 sessions，无广播、不动 Data.Instances（同款先例见条件类需求的 RoomHasAny）。
        ///
        /// 氛围邻居（ambient）不在 Data.Instances 里，天然走不到这里。
        /// </summary>
        private void Depart(VisitorInstance instance)
        {
            var roomDecor = FurniturePlacementQuery.DecorationScoreOf(instance.RoomIndex);
            var tip = economy.GuestLeaveTip(roomDecor, instance.NeedFulfilled);
            Data.Today.TipEarned += tip; // 与服务奖励分开记账，日结面板分两行（§6.3）
            instance.State = EVisitorState.Departed;
            Data.Instances.Remove(instance);
            InstanceDeparted?.Invoke(instance);
        }

        /// <summary>
        /// 请求播放一段对话（§8）。fire-and-forget：不等播完、不接返回值——
        /// 模态对话框期间闸门关闭，业务时间本来就停着，「等对话播完」对访客状态机是免费的。
        /// </summary>
        private bool RequestDialogue(VisitorInstance instance, EDialogueCategory category)
        {
            var played = dialogue != null && dialogue.RequestVisitorDialogue(instance, category);
            DialogueRequested?.Invoke(instance, category);
            return played;
        }

        // ── 存档接缝占位（§16.5，待定 #9）：留 Capture/Restore 但无调用方，与 EconomyManager 现有做法一致 ──

        /// <summary>导出存档快照（无调用方，待定 #9）。需求不入档：恢复时按派生种子重算（§6.1）。</summary>
        public VisitorSaveData Capture()
        {
            var data = new VisitorSaveData
            {
                runSeed = Data.RunSeed,
                businessTick = Data.BusinessTick,
                nextInstanceId = Data.NextInstanceId,
                scheduleCursor = Data.ScheduleCursor,
            };
            foreach (var instance in Data.Instances)
            {
                data.instances.Add(new VisitorInstanceSaveData
                {
                    instanceId = instance.InstanceId,
                    scheduleDay = instance.ScheduleDay,
                    scheduleIndex = instance.ScheduleIndex,
                    state = (int)instance.State,
                    stateEnterTick = instance.StateEnterTick,
                    roomIndex = instance.RoomIndex,
                    satisfaction = (int)instance.Satisfaction,
                    needFulfilled = instance.NeedFulfilled,
                    nextBubbleTick = instance.NextBubbleTick,
                    rngState = instance.Rng.State,
                });
            }
            return data;
        }

        /// <summary>从存档快照恢复（无调用方，待定 #9）。data 为 null 时重置为初始状态。</summary>
        public void Restore(VisitorSaveData data)
        {
            ResetNew();
            if (data == null) return;
            Data.RunSeed = data.runSeed;
            Data.BusinessTick = data.businessTick;
            Data.NextInstanceId = data.nextInstanceId;
            Data.ScheduleCursor = data.scheduleCursor;
            foreach (var saved in data.instances)
            {
                if (saved.scheduleIndex < 0 || saved.scheduleIndex >= schedule.entries.Count) continue;
                var entry = schedule.entries[saved.scheduleIndex];
                if (entry == null || entry.race == null || entry.need == null) continue;
                var instance = new VisitorInstance
                {
                    InstanceId = saved.instanceId,
                    Race = entry.race,
                    ScheduleDay = saved.scheduleDay,
                    ScheduleIndex = saved.scheduleIndex,
                    Need = entry.need, // 需求不入档：与种族一样从日程条目重新取（零随机，§4.2）
                    State = (EVisitorState)saved.state,
                    StateEnterTick = saved.stateEnterTick,
                    RoomIndex = saved.roomIndex,
                    Satisfaction = (EServeSatisfaction)saved.satisfaction,
                    NeedFulfilled = saved.needFulfilled,
                    NextBubbleTick = saved.nextBubbleTick,
                };
                // 随机流按存档状态恢复（需求已不入档也不 roll，直接取自日程条目，§4.2）
                instance.Rng = new DeterministicRng(0) { State = saved.rngState };
                Data.Instances.Add(instance);
            }
        }
    }

    /// <summary>访客存档快照（存档接缝占位，无调用方，待定 #9）。</summary>
    [Serializable]
    public sealed class VisitorSaveData
    {
        public long runSeed;
        public long businessTick;
        public int nextInstanceId;
        public int scheduleCursor;
        public List<VisitorInstanceSaveData> instances = new List<VisitorInstanceSaveData>();
    }

    /// <summary>单个在场实例的存档快照（待定 #9）。</summary>
    [Serializable]
    public sealed class VisitorInstanceSaveData
    {
        public int instanceId;
        public int scheduleDay;
        public int scheduleIndex;
        public int state;
        public long stateEnterTick;
        public int roomIndex;
        public int satisfaction;
        /// <summary>需求是否真的被满足过（决定离场小费吃不吃装饰分加成）。satisfaction 代替不了它，见 VisitorInstance。</summary>
        public bool needFulfilled;
        public long nextBubbleTick;
        public ulong rngState;
    }
}

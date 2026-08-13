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

        /// <summary>超时/离场收集缓冲（tick 内复用，避免遍历中修改在场列表）。</summary>
        private readonly List<VisitorInstance> departBuffer = new List<VisitorInstance>();

        public VisitorData Data { get; } = new VisitorData();

        // ── 粗粒度事件广播（§2.1：离散变化 Manager 广播，表现层/UI 订阅刷新）──

        /// <summary>新访客进场（前台等待接待）。</summary>
        public event Action<VisitorInstance> InstanceSpawned;

        /// <summary>实例状态推进（接待/提交等）。</summary>
        public event Action<VisitorInstance> InstanceChanged;

        /// <summary>实例离场（已从在场列表移除）。</summary>
        public event Action<VisitorInstance> InstanceDeparted;

        /// <summary>
        /// 对话触发点被请求（§8 五触发点）。对话本体的播放由 IDialogueService 实现方负责，
        /// 本事件只是给表现层的旁路通知（演员表情、日志、埋点）——**不要在订阅方里再播一次对话**。
        /// 对话接缝改为 fire-and-forget 后不再有单句返回值（旧签名带 string 是占位实现时期的产物）。
        /// </summary>
        public event Action<VisitorInstance, EVisitorDialogueTrigger> DialogueRequested;

        /// <summary>日结完成（携带当日累计快照，面板展示用；只展示不惩罚，§7）。</summary>
        public event Action<VisitorDaySummary> DayEnded;

        /// <param name="cargo">
        /// 全局仓库。**已不再被消费**——物品交付随 Item 链退役（需求重做说明 §9.1）。
        /// 参数保留是因为 §9.2 要求 PlayerCargo 的构造与传参不动，等 NodeSim 包一起清理；
        /// 不再存成字段，免得留一个永远读不到的死引用。
        /// </param>
        public VisitorManager(VisitorScheduleTable schedule, VisitorTuningConfig tuning,
            HouseClockManager clock, EconomyManager economy, PlayerCargoData cargo, IDialogueService dialogue)
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
            // 待确认（§4.4）：日程跑完最后一天之后的行为——默认停止投放新访客并打一条 Warning，等策划补内容
            if (Data.ScheduleCursor >= sortedSchedule.Count && !Data.ScheduleExhaustedWarned &&
                (sortedSchedule.Count == 0 || day > sortedSchedule[sortedSchedule.Count - 1].Day))
            {
                Debug.LogWarning($"[VisitorManager] 日程表已全部消费（当前 DAY {day}），停止投放新访客；请在 VisitorScheduleTable 补充后续日程");
                Data.ScheduleExhaustedWarned = true;
            }
        }

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
            foreach (var instance in Data.Instances) // 在场列表按 InstanceId 升序（§11.2）
            {
                var elapsed = Data.BusinessTick - instance.StateEnterTick;
                switch (instance.State)
                {
                    case EVisitorState.FrontDesk:
                        if (elapsed >= instance.Race.waitTalkTimeoutTicks) departBuffer.Add(instance);
                        break;
                    // AwaitingRoom（等待分配房间）**刻意没有超时**：§5.3 的流程图上这一态只有「拖进空房」
                    // 与「拒绝」两条出口。它同时阻塞【结束今天】（§10），玩家想清场就必须显式拒绝，
                    // 不会出现「忘了分房 → 客人自己溜了 → 玩家不知道发生了什么」的静默流失。
                    case EVisitorState.Serving:
                        if (elapsed >= instance.Race.waitDeliverTimeoutTicks) departBuffer.Add(instance);
                        break;
                    case EVisitorState.Wandering:
                        if (elapsed >= instance.Race.wanderMaxTicks)
                        {
                            departBuffer.Add(instance);
                        }
                        else if (instance.NextBubbleTick > 0 && Data.BusinessTick >= instance.NextBubbleTick)
                        {
                            // 满意后闲逛（§8）：冒泡调度器定期请求一句闲逛台词
                            RequestDialogue(instance, EVisitorDialogueTrigger.WanderChat);
                            ScheduleNextBubble(instance);
                        }
                        break;
                }
            }
            foreach (var instance in departBuffer)
            {
                if (instance.State == EVisitorState.Wandering)
                {
                    Data.Today.WanderDepartCount++;
                    Depart(instance); // 闲逛到点自行离开，无惩罚
                }
                else
                {
                    // 两段超时都走【被拒绝】对话分类；声望按超时时所处状态取档（交付页落地说明 §5.2）
                    SettleRefusal(instance);
                }
            }
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
        /// 满房时返回 false。正常情况下【初次见面】的「接待」选项挂了 HasFreeRoomCondition 会自动置灰，
        /// 这里再拦一道是防御——策划漏配条件时宁可接待不生效，也不能让访客卡在无房可住的中间态。
        /// </summary>
        public bool Accept(int instanceId)
        {
            var instance = Find(instanceId);
            if (instance == null || instance.State != EVisitorState.FrontDesk) return false;
            if (!HasFreeRoom)
            {
                Debug.LogWarning($"[VisitorManager] 接待未生效：客房已住满（实例 {instanceId}）；" +
                                 "【初次见面】的「接待」选项应当挂上【访客/还有空客房】条件（§6.2）");
                return false;
            }
            SetState(instance, EVisitorState.AwaitingRoom);
            return true;
        }

        /// <summary>
        /// 拒绝：在「前台等待接待」「等待分配房间」「服务中」三个状态都可用
        /// （打烊后玩家必须能手动清场，§5 / §5.3）。
        /// 声望惩罚按当前状态分两档——已接待后反悔扣得更重（交付页落地说明 §5.2）。
        /// </summary>
        public bool Reject(int instanceId)
        {
            var instance = Find(instanceId);
            if (instance == null || !CanReject(instance.State)) return false;
            SettleRefusal(instance);
            return true;
        }

        private static bool CanReject(EVisitorState state) =>
            state == EVisitorState.FrontDesk || state == EVisitorState.AwaitingRoom || state == EVisitorState.Serving;

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

        /// <summary>是否还有空客房（1~3 中至少一间未被占用）。接待分支的条件与投放前置条件都用它。</summary>
        public bool HasFreeRoom
        {
            get
            {
                for (var room = FirstGuestRoomIndex; room <= LastGuestRoomIndex; room++)
                    if (!IsRoomOccupied(room)) return true;
                return false;
            }
        }

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
        /// | 状态         | 放行条件                | 行为                                             |
        /// |--------------|-------------------------|--------------------------------------------------|
        /// | AwaitingRoom | 目标 ∈ 1..3 且该房空闲  | **分房**：转 Serving + 播【开始等待服务】说出需求 |
        /// | Wandering    | 目标 ∈ 1..3 且该房空闲  | 换房，纯位置变更，无业务影响                     |
        /// | FrontDesk    | 否                      | 前台访客在门口排队，不可搬走                     |
        /// | Serving      | 否                      | **服务中锁房**                                   |
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
            // 到这一刻才说出需求（§5.3 硬要求：【初次见面】绝不能提前透露需求内容）
            RequestDialogue(instance, EVisitorDialogueTrigger.ServiceStart);
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
            instance.Satisfaction = satisfaction;
            var (currency, reputation) = economy.CompleteGuestService(satisfaction);
            Data.Today.ServedBySatisfaction[(int)satisfaction]++;
            Data.Today.CurrencyEarned += currency;
            Data.Today.ReputationEarned += reputation;
            RequestDialogue(instance, EVisitorDialogueTrigger.ServiceDone);
            if (satisfaction == EServeSatisfaction.Mismatch)
            {
                Depart(instance); // 不对味：直接离开，不进闲逛（§5）
            }
            else
            {
                // 转闲逛但**房间不释放**——访客在自己房间里游走，直到离场才腾出来（§5.2）
                SetState(instance, EVisitorState.Wandering);
                ScheduleNextBubble(instance);
            }
            return true;
        }

        /// <summary>
        /// 服务中再次交谈（需求重做说明 §6.4）：玩家点击「服务中」的访客时请求【服务中交谈】对话。
        /// 与 serviceStart（刚进屋说出需求）分开是刻意的——每次点击都重播完整需求对话体验很差；
        /// 条件类的验收分支就挂在这一类的对话组上。
        /// </summary>
        public bool RequestServiceCheck(int instanceId)
        {
            var instance = Find(instanceId);
            if (instance == null || instance.State != EVisitorState.Serving) return false;
            RequestDialogue(instance, EVisitorDialogueTrigger.ServiceCheck);
            return true;
        }

        /// <summary>
        /// 初次见面（§8）：玩家交互「前台等待接待」的访客时请求；状态不变，仍在前台。
        /// 返回是否真的发起了请求（访客不存在或不在前台时为 false）。
        /// </summary>
        public bool RequestFirstMeeting(int instanceId)
        {
            var instance = Find(instanceId);
            if (instance == null || instance.State != EVisitorState.FrontDesk) return false;
            RequestDialogue(instance, EVisitorDialogueTrigger.FirstMeeting);
            return true;
        }

        // ── 日结（§7）──

        /// <summary>
        /// 场上是否还有未处理访客（前台等待 / 等待分配房间 / 服务中）；闲逛中的不阻塞【结束今天】。
        /// 待确认 #5（§10）：AwaitingRoom **默认算**未处理——它没有超时，不阻塞的话
        /// 会跨天挂在门口，且玩家永远得不到「你还欠人家一间房」的提示。
        /// </summary>
        public bool HasBlockingVisitors
        {
            get
            {
                foreach (var instance in Data.Instances)
                    if (instance.State == EVisitorState.FrontDesk || instance.State == EVisitorState.AwaitingRoom ||
                        instance.State == EVisitorState.Serving)
                        return true;
                return false;
            }
        }

        public bool CanEndDay => !HasBlockingVisitors;

        /// <summary>
        /// 结束今天（§7）：闲逛访客按种族概率 roll 跨天留宿（其余离场）→ 生成当日结算快照 →
        /// 时钟跳次日开门时刻并解冻。场上有未处理访客时不可用（返回 null）。
        /// 日结只展示不惩罚——惩罚已在超时/拒绝当时结清，这里不重复扣。
        /// </summary>
        public VisitorDaySummary EndDay()
        {
            if (HasBlockingVisitors) return null;
            departBuffer.Clear();
            foreach (var instance in Data.Instances)
            {
                if (instance.State != EVisitorState.Wandering) continue;
                if (instance.Rng.Chance(instance.Race.stayOvernightPercent))
                    Data.Today.StayOvernightCount++; // 留宿：保留到次日继续闲逛
                else
                    departBuffer.Add(instance);
            }
            foreach (var instance in departBuffer)
            {
                Data.Today.WanderDepartCount++;
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
            Data.ScheduleExhaustedWarned = false;
            Data.Today.Reset();
        }

        // ── 内部结算 ──

        /// <summary>
        /// 拒绝口径结算（玩家拒绝 / 等搭话超时 / 等交货超时共用，§5）：扣声望 + 播【被拒绝】+ 离场。
        ///
        /// 惩罚**分两档**（交付页落地说明 §5.2）：接待后反悔比在前台谢客更失礼，扣得更多。
        /// 两段超时按「超时发生时所处状态」取档——语义一致（这位访客在哪个阶段被辜负），
        /// 且实现上就是下面这个三元，不必特判超时与手动拒绝。
        /// </summary>
        private void SettleRefusal(VisitorInstance instance)
        {
            // 「等待分配房间」算**已接待**档（与服务中同档）：EconomyConfig 里那两条的口径分别写着
            // 「在前台等待接待谢客」与「已接待后反悔」，而 AwaitingRoom 的客人已经被请进门了。
            // 需求重做说明没有明写这一态归哪档（待确认 #2 只说保留现状两档），此处按语义归档。
            var accepted = instance.State == EVisitorState.Serving || instance.State == EVisitorState.AwaitingRoom;
            if (accepted) economy.RefuseServingGuest();
            else economy.RefuseGuestService();
            Data.Today.RefusedCount++;
            Data.Today.ReputationLost += accepted
                ? economy.ServiceFailedReputationPenalty
                : economy.RefuseReputationPenalty;
            RequestDialogue(instance, EVisitorDialogueTrigger.Rejected);
            Depart(instance);
        }

        private void SetState(VisitorInstance instance, EVisitorState state)
        {
            instance.State = state;
            instance.StateEnterTick = Data.BusinessTick;
            InstanceChanged?.Invoke(instance);
        }

        /// <summary>
        /// 离场：留下基础金钱 → 移出在场列表（房间随之释放）。
        ///
        /// **所有业务访客离场都给钱，包括被拒绝与超时流失的**（需求重做说明 §8）——
        /// 这是新模型下「不会陷入没钱死循环」的保证。未满足需求只是拿不到
        /// CompleteGuestService 的按档奖励，不扣货币。
        /// 氛围邻居（ambient）不在 Data.Instances 里，天然走不到这里。
        /// </summary>
        private void Depart(VisitorInstance instance)
        {
            var tip = economy.GuestLeaveTip();
            Data.Today.CurrencyEarned += tip;
            instance.State = EVisitorState.Departed;
            Data.Instances.Remove(instance);
            InstanceDeparted?.Invoke(instance);
        }

        /// <summary>
        /// 请求播放一段对话（§8）。fire-and-forget：不等播完、不接返回值——
        /// 模态对话框期间闸门关闭，业务时间本来就停着，「等对话播完」对访客状态机是免费的。
        /// </summary>
        private void RequestDialogue(VisitorInstance instance, EVisitorDialogueTrigger trigger)
        {
            dialogue?.RequestVisitorDialogue(instance, trigger, instance.Satisfaction);
            DialogueRequested?.Invoke(instance, trigger);
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
        public long nextBubbleTick;
        public ulong rngState;
    }
}

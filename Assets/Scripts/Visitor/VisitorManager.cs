using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 访客业务逻辑（访客交付说明 §3/§5/§6/§7）：日程投放、五态状态机推进、两段超时、
    /// 接待/拒绝/提交结算、闲逛冒泡调度与日结，全部挂全局 tick、整数比较（§16.4/§11.3）。
    /// 表现层只读实例列表生成演员，不回写业务（§16.4 表现层豁免）。
    /// 对话事件经 Accept/Reject/Submit 驱动业务（§8 契约，状态不对时返回 false 而不是抛异常）。
    /// </summary>
    public class VisitorManager
    {
        /// <summary>存档未落地期间的固定默认 runSeed（§6.1，待定 #9）；GM 面板可改写，存档接入后改为存档字段。</summary>
        public const long DefaultRunSeed = 20260810;

        private readonly VisitorScheduleTable schedule;
        private readonly VisitorTuningConfig tuning;
        private readonly HouseClockManager clock;
        private readonly EconomyManager economy;
        private readonly PlayerCargoData cargo;
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

        public VisitorManager(VisitorScheduleTable schedule, VisitorTuningConfig tuning,
            HouseClockManager clock, EconomyManager economy, PlayerCargoData cargo, IDialogueService dialogue)
        {
            this.schedule = schedule;
            this.tuning = tuning;
            this.clock = clock;
            this.economy = economy;
            this.cargo = cargo;
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
                    // 该条目的日子已过去（如配置在打烊之后的时刻，从未来得及投放）——跳过并提示配置问题
                    Debug.LogWarning($"[VisitorManager] 日程条目已过期未投放（day={entry.Day} minute={entry.Minute}），已跳过；" +
                                     "请检查出现时刻是否配置在营业时段内");
                    Data.ScheduleCursor++;
                    continue;
                }
                if (entry.Day > day || entry.Minute > minute) break;
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
            var instance = new VisitorInstance
            {
                InstanceId = Data.NextInstanceId++,
                Race = entry.race,
                ScheduleDay = scheduleDay,
                ScheduleIndex = scheduleIndex,
                State = EVisitorState.FrontDesk,
                StateEnterTick = Data.BusinessTick,
            };
            // 派生种子（§6.1）：每条日程独立推导随机流，不依赖调用顺序、无论何时重算得到同一份需求
            var rollSeed = DeterministicRng.Hash(Data.RunSeed, scheduleDay, scheduleIndex);
            instance.Rng = new DeterministicRng(rollSeed);
            RollNeeds(instance);
            // 待确认（§4.4）：具名覆写 namedOverride 结构已建、运行时暂不消费（现阶段无剧情内容），entry.namedOverride 留待接通
            Data.Instances.Add(instance);
            InstanceSpawned?.Invoke(instance);
        }

        /// <summary>
        /// 需求 roll（§6.2）。待确认：抽到树的哪一层是难度旋钮本体、策划案未明确——
        /// 默认实现为「在权重表里随机抽，不做层级控制」；后续规则给出后只替换本方法。
        /// </summary>
        private void RollNeeds(VisitorInstance instance)
        {
            instance.Needs.Clear();
            var race = instance.Race;
            var pool = new List<NeedTagWeight>();
            foreach (var entry in race.needTagWeights)
                if (entry != null && entry.tag != null && entry.weight > 0)
                    pool.Add(entry);
            var min = Mathf.Max(0, Mathf.Min(race.needCountMin, race.needCountMax));
            var max = Mathf.Max(min, Mathf.Max(race.needCountMin, race.needCountMax));
            var count = Mathf.Min(instance.Rng.Range(min, max + 1), pool.Count);
            for (var picked = 0; picked < count; picked++)
            {
                var totalWeight = 0;
                foreach (var entry in pool) totalWeight += entry.weight;
                var roll = instance.Rng.Range(0, totalWeight);
                var chosen = pool.Count - 1;
                var accumulated = 0;
                for (var i = 0; i < pool.Count; i++)
                {
                    accumulated += pool[i].weight;
                    if (roll < accumulated) { chosen = i; break; }
                }
                instance.Needs.Add(new VisitorNeed { Tag = pool[chosen].tag, Required = pool[chosen].required });
                pool.RemoveAt(chosen); // 不重复抽同一 tag
            }
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

        /// <summary>接待：前台等待中的访客进入「服务中」，触发【开始等待服务】对话（含程序化需求句）。</summary>
        public bool Accept(int instanceId)
        {
            var instance = Find(instanceId);
            if (instance == null || instance.State != EVisitorState.FrontDesk) return false;
            SetState(instance, EVisitorState.Serving);
            RequestDialogue(instance, EVisitorDialogueTrigger.ServiceStart);
            return true;
        }

        /// <summary>
        /// 拒绝：在「前台等待接待」与「服务中」两个状态都可用（打烊后玩家必须能手动清场，§5）。
        /// 声望惩罚按当前状态分两档——服务中反悔扣得更重（交付页落地说明 §5.2）。
        /// </summary>
        public bool Reject(int instanceId)
        {
            var instance = Find(instanceId);
            if (instance == null ||
                (instance.State != EVisitorState.FrontDesk && instance.State != EVisitorState.Serving)) return false;
            SettleRefusal(instance);
            return true;
        }

        /// <summary>
        /// 玩家把访客拖到另一个房间（Hub 四宫格，2026-08-13）。与 Accept/Reject 同口径：
        /// 公开方法 + 合法性校验，状态不对返回 false 而不是抛异常（§8）。
        /// 纯位置变更，不影响超时/评分；任何在场状态都允许拖动（前台访客拖走也不改变其等待语义）。
        /// </summary>
        public bool MoveVisitorToRoom(int instanceId, int roomIndex)
        {
            // 房间数暂与 Hub 四宫格一致（CodexTable.rooms 前 4 间；地下仓库未解锁不算）
            if (roomIndex < 0 || roomIndex > 3) return false;
            var instance = Find(instanceId);
            if (instance == null) return false;
            if (instance.RoomIndex == roomIndex) return true;
            instance.RoomIndex = roomIndex;
            InstanceChanged?.Invoke(instance);
            return true;
        }

        /// <summary>
        /// 提交物品并结算（§5/§6.2）：服务一次性、不可补交——提交一次即定生死，交错了照样扣物品。
        /// 仓库无货返回 false（不存在的东西交不出去）；扣减发生在评分之前。
        /// </summary>
        public bool Submit(int instanceId, ItemDef item)
        {
            var instance = Find(instanceId);
            if (instance == null || instance.State != EVisitorState.Serving || item == null) return false;
            if (!cargo.TryConsume(item, 1)) return false;
            instance.SubmittedItem = item;
            instance.Satisfaction = Evaluate(instance, item);
            var (currency, reputation) = economy.CompleteGuestService(instance.Satisfaction);
            Data.Today.ServedBySatisfaction[(int)instance.Satisfaction]++;
            Data.Today.CurrencyEarned += currency;
            Data.Today.ReputationEarned += reputation;
            RequestDialogue(instance, EVisitorDialogueTrigger.ServiceDone);
            if (instance.Satisfaction == EServeSatisfaction.Mismatch)
            {
                Depart(instance); // 不对味：直接离开，不进闲逛（§5）
            }
            else
            {
                SetState(instance, EVisitorState.Wandering);
                ScheduleNextBubble(instance);
            }
            return true;
        }

        /// <summary>
        /// 试算满意度但不落地（交付页落地说明 §5.1）：不扣库存、不改状态、不请求对话，交付预览专用。
        /// 访客不存在 / 不在「服务中」/ 物品为空时返回「不对味」——交付页在这些情况下本来就不该显示预览。
        ///
        /// **与 Submit 共用同一个 Evaluate**，这是硬要求：两条路径的判定一旦分叉，
        /// 就会出现「预览显示完美、交出去变满意」，那是最难查的一类 bug。
        /// </summary>
        public EServeSatisfaction Preview(int instanceId, ItemDef item)
        {
            var instance = Find(instanceId);
            if (instance == null || instance.State != EVisitorState.Serving || item == null)
                return EServeSatisfaction.Mismatch;
            return Evaluate(instance, item);
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

        /// <summary>
        /// 评分（§6.2）：①任一必要需求未命中→不对味；②否则按加分项命中比例分档（阈值A 配在 EconomyConfig）；
        /// ③没有加分项（只有必要项）时直接判完美。命中 ⇔ 物品的某个 tag 等于需求 tag、或以它为祖先（§4.1）。
        ///
        /// 纯函数、无副作用——`Submit`（落地）与 `Preview`（试算）**共用这一份**，
        /// 交付预览显示的档位与确认交付后的实际结算档位由此逐字一致（交付页落地说明 §5.1）。
        /// </summary>
        private EServeSatisfaction Evaluate(VisitorInstance instance, ItemDef item)
        {
            var bonusTotal = 0;
            var bonusHit = 0;
            foreach (var need in instance.Needs)
            {
                var hit = false;
                foreach (var itemTag in item.tags)
                    if (itemTag != null && need.Tag.Covers(itemTag)) { hit = true; break; }
                if (need.Required)
                {
                    if (!hit) return EServeSatisfaction.Mismatch;
                }
                else
                {
                    bonusTotal++;
                    if (hit) bonusHit++;
                }
            }
            if (bonusTotal == 0 || bonusHit == bonusTotal) return EServeSatisfaction.Perfect;
            var percent = bonusHit * 100 / bonusTotal;
            return percent < economy.SatisfactionThresholdPercent ? EServeSatisfaction.Plain : EServeSatisfaction.Satisfied;
        }

        // ── 日结（§7）──

        /// <summary>场上是否还有未处理访客（前台等待/服务中）；闲逛中的不阻塞【结束今天】。</summary>
        public bool HasBlockingVisitors
        {
            get
            {
                foreach (var instance in Data.Instances)
                    if (instance.State == EVisitorState.FrontDesk || instance.State == EVisitorState.Serving)
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
        /// GM：立即召唤一位访客到前台（忽略日程与营业时段，验证接待流程用）。
        /// race 为空时按日程表引用的种族轮换；日程表没有任何种族时返回 null。
        /// </summary>
        public VisitorInstance GmSpawnVisitor(VisitorRaceDef race = null)
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
            var seedIndex = 10000 + gmSpawnCount++;
            var instance = new VisitorInstance
            {
                InstanceId = Data.NextInstanceId++,
                Race = race,
                ScheduleDay = clock.Data.Day,
                ScheduleIndex = seedIndex,
                State = EVisitorState.FrontDesk,
                StateEnterTick = Data.BusinessTick,
            };
            instance.Rng = new DeterministicRng(DeterministicRng.Hash(Data.RunSeed, clock.Data.Day, seedIndex));
            RollNeeds(instance);
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
            var serving = instance.State == EVisitorState.Serving;
            if (serving) economy.RefuseServingGuest();
            else economy.RefuseGuestService();
            Data.Today.RefusedCount++;
            Data.Today.ReputationLost += serving
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

        private void Depart(VisitorInstance instance)
        {
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
                if (entry == null || entry.race == null) continue;
                var instance = new VisitorInstance
                {
                    InstanceId = saved.instanceId,
                    Race = entry.race,
                    ScheduleDay = saved.scheduleDay,
                    ScheduleIndex = saved.scheduleIndex,
                    State = (EVisitorState)saved.state,
                    StateEnterTick = saved.stateEnterTick,
                    RoomIndex = saved.roomIndex,
                    Satisfaction = (EServeSatisfaction)saved.satisfaction,
                    NextBubbleTick = saved.nextBubbleTick,
                };
                // 需求按派生种子重算（读档刷需求的路子被堵死，§6.1），随后恢复实例随机流的后续状态
                instance.Rng = new DeterministicRng(DeterministicRng.Hash(Data.RunSeed, saved.scheduleDay, saved.scheduleIndex));
                RollNeeds(instance);
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

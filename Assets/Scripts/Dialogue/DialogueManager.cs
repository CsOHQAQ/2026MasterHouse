using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 对话业务逻辑（设计说明 §3/§5/§6）：选取、播放推进、事件执行与闲逛冒泡。
    /// 交付预览已随 Item 链退役（需求重做说明 §9.1）。
    /// 取代占位实现 DefDialogueService，实现访客侧的 IDialogueService 接缝。
    ///
    /// 播放推进**不进 tick**：模态对话框期间 tick 本来就停了（§8 闸门），
    /// 推进完全由玩家点击驱动。打字机是表现层计时，在 DialogueOverlay 里用 deltaTime（§5.1）。
    ///
    /// 关于 fire-and-forget（§8）：RequestVisitorDialogue 不返回任何东西、也没有回调——
    /// 访客侧调完就走，不需要等对话播完。协程状态无法序列化，与 §11 冲突，所以不用协程。
    /// </summary>
    public sealed class DialogueManager : IDialogueService
    {
        /// <summary>tuning 缺失时的 recent 环长兜底（§12 待确认默认值 3）。</summary>
        private const int FallbackRingLength = 3;

        private readonly DialogueTuningConfig tuning;

        // ── 两阶段初始化 ──
        // VisitorManager 的构造需要 IDialogueService，而本类又需要 VisitorManager——构造期存在循环依赖。
        // 解法是显式两阶段：GameManager 先造 DialogueManager，再造 VisitorManager，最后 Bind。
        // 不用「读 GameManager.Instance」的隐式解法：那会让逻辑层反向依赖场景单例，测试也没法替身。
        private VisitorManager visitors;
        private EconomyManager economy;
        private HouseClockManager clock;

        public DialogueData Data { get; } = new DialogueData();

        private DialogueRuntime runtime;

        /// <summary>待播队列（§见类注释）：对话事件会触发新的对话请求（接待 → 开始等待服务），
        /// 那时当前这段还没播完，排队等它播完再开，而不是打断。</summary>
        private readonly Queue<PendingRequest> pending = new Queue<PendingRequest>();

        /// <summary>选取用的复用缓冲（避免每次请求都分配）。</summary>
        private readonly List<DialogueGroupEntry> candidates = new List<DialogueGroupEntry>();
        private readonly List<DialogueGroupEntry> fresh = new List<DialogueGroupEntry>();

        /// <summary>对话框是否开着。与 runtime != null 不同——续播队列时 runtime 会短暂为 null 但框不关。</summary>
        private bool modalOpen;

        // ── 事件（§2.1：离散变化由 Manager 广播，View 订阅刷新）──

        /// <summary>模态对话框应当打开。</summary>
        public event Action PlaybackStarted;

        /// <summary>当前显示内容变了（换行 / 出选项 / 续播下一段），View 重绘。</summary>
        public event Action ContentChanged;

        /// <summary>模态对话框应当关闭（队列也空了）。</summary>
        public event Action PlaybackEnded;

        /// <summary>闲逛台词冒泡（场景气泡，不碰闸门、不走模态）。</summary>
        public event Action<VisitorInstance, string> BubbleRequested;

        public DialogueManager(DialogueTuningConfig tuning)
        {
            this.tuning = tuning;
            if (tuning == null)
                Debug.LogError("[对话] 调参配置缺失（Resources/OutGameUI/DialogueTuningConfig）：" +
                               "已按默认值运行（打字机 30 字/秒、recent 环长 3）。" +
                               "请执行菜单 MasterHouse → 配置中心 补齐资产");
        }

        /// <summary>第二阶段初始化（见字段区注释）。GameManager 在造完 VisitorManager 之后调用一次。</summary>
        /// <param name="cargo">
        /// 全局仓库。**已不再被消费**——GameplayContext.Cargo 随 Item 链退役删除（需求重做说明 §9.1）。
        /// 参数保留是因为 §9.2 要求 PlayerCargo 的构造与传参不动，等 NodeSim 包一起清理；
        /// 不再存成字段，免得留一个永远读不到的死引用。
        /// </param>
        public void Bind(VisitorManager visitors, EconomyManager economy, HouseClockManager clock, PlayerCargoData cargo)
        {
            this.visitors = visitors;
            this.economy = economy;
            this.clock = clock;
        }

        // ══════════ 对外查询（View 只读，§11.4）══════════

        public bool IsPlaying => runtime != null;

        /// <summary>当前正在说话的访客（离场后仍有效，见 GameplayContext.Visitor）。</summary>
        public VisitorInstance CurrentVisitor => runtime?.Context?.Visitor;

        /// <summary>当前台词；停在分支上或没在播时为 null。</summary>
        public DialogueLine CurrentLine
        {
            get
            {
                var step = runtime?.CurrentStep;
                return step != null && step.kind == EDialogueStepKind.Line ? step.line : null;
            }
        }

        /// <summary>当前台词的成文（已替换占位符，§9）。</summary>
        public string CurrentText
        {
            get
            {
                var line = CurrentLine;
                return line != null ? DialogueTextFormatter.Format(line.text, runtime.Context) : string.Empty;
            }
        }

        /// <summary>当前是否停在分支上（停在分支时点击不推进，必须选一个选项）。</summary>
        public bool IsAtBranch => runtime != null && runtime.IsAtBranch;

        /// <summary>当前分支的全部选项（含不满足条件的——它们置灰保留可见，§12）；不在分支上时为 null。</summary>
        public IReadOnlyList<BranchOption> CurrentOptions
        {
            get
            {
                var step = runtime?.CurrentStep;
                return step != null && step.kind == EDialogueStepKind.Branch ? step.options : null;
            }
        }

        /// <summary>选项是否可选（条件全通过）。View 用它决定置灰。</summary>
        public bool IsOptionEnabled(BranchOption option) =>
            option != null && ConditionsPass(option.conditions, runtime?.Context);

        /// <summary>选项文本的成文（已替换占位符）。</summary>
        public string FormatOptionText(BranchOption option) =>
            option != null ? DialogueTextFormatter.Format(option.text, runtime?.Context) : string.Empty;

        /// <summary>需求描述（§9 / 需求重做说明 §9.1）：现在就是 NeedDef.description，UI 与台词共用同一份文本。</summary>
        public string BuildNeedPhrase(VisitorInstance visitor) =>
            visitor != null ? visitor.BuildNeedSentence() : string.Empty;

        // ══════════ 访客 → 对话（IDialogueService，§7 五触发点）══════════

        public void RequestVisitorDialogue(VisitorInstance visitor, EVisitorDialogueTrigger trigger,
            EServeSatisfaction satisfaction)
        {
            if (visitor == null) return;

            // 闲逛走场景气泡，不开模态、不碰闸门（§8）
            if (trigger == EVisitorDialogueTrigger.WanderChat)
            {
                RequestBubble(visitor, satisfaction);
                return;
            }

            var request = new PendingRequest { Visitor = visitor, Trigger = trigger, Satisfaction = satisfaction };
            if (runtime != null)
            {
                pending.Enqueue(request);
                return;
            }
            if (TryBegin(request)) return;
            AfterPlayback(); // 这段没选出内容：继续消化队列，或收框
        }

        // ══════════ 播放推进（由 DialogueOverlay 调用）══════════

        /// <summary>玩家点击推进到下一步。停在分支上时无效（必须选）。</summary>
        public void Advance()
        {
            if (runtime == null || runtime.IsAtBranch) return;
            runtime.StepIndex++;
            Continue();
        }

        /// <summary>玩家选中分支选项。下标非法或选项不可选时无效。</summary>
        public void ChooseOption(int index)
        {
            if (runtime == null || !runtime.IsAtBranch) return;
            var options = runtime.CurrentStep.options;
            if (index < 0 || index >= options.Count) return;
            var option = options[index];
            if (option == null || !IsOptionEnabled(option)) return;

            ExecuteActions(option.actions);
            if (runtime == null) return; // 防御：事件若意外终止了播放

            switch (option.next)
            {
                case EBranchNext.ContinueGroup:
                    runtime.StepIndex++;
                    Continue();
                    break;
                case EBranchNext.JumpToGroup:
                    if (option.nextGroup == null)
                    {
                        Debug.LogError($"[对话] 选项「{option.text}」配了「跳到组」却没填目标组（组：{runtime.CurrentGroup.DisplayId}），按结束处理");
                        Finish();
                        break;
                    }
                    if (!runtime.JumpTo(option.nextGroup)) { Finish(); break; }
                    Continue();
                    break;
                default:
                    Finish();
                    break;
            }
        }

        /// <summary>
        /// 玩家中断（ESC / 关闭按钮），§5.2：
        /// **视为这段对话没有被播放**——不写 recent（下次仍可能抽到），业务不进入任何新状态。
        /// 已经执行过的事件不回滚（既成事实）；尚未执行到的只补执行 ExecuteOnInterrupt == true 的那些。
        ///
        /// 调用约定：由 DialogueOverlay.Close 调用，**调用时对话框实例已经自行销毁**
        /// （壳的退栈语义要求 Close 就得清理自己）。所以这里按 uiAlreadyClosed 收尾——
        /// 若队列里还压着下一段对话，会重新触发 PlaybackStarted 让 UI 拉起一个新的框。
        /// </summary>
        public void Interrupt()
        {
            if (runtime == null) return;

            var steps = runtime.CurrentGroup != null ? runtime.CurrentGroup.steps : null;
            if (steps != null)
            {
                // 只扫**当前组**的剩余步骤：跳转目标取决于玩家还没做的选择，
                // 硬猜会执行到玩家根本没选的分支上去。
                for (var i = Mathf.Max(0, runtime.StepIndex); i < steps.Count; i++)
                {
                    var step = steps[i];
                    if (step == null || step.kind != EDialogueStepKind.Action || step.actions == null) continue;
                    foreach (var action in step.actions)
                        if (action != null && action.ExecuteOnInterrupt)
                            SafeExecute(action);
                }
            }
            // 注意：不 MarkPlayed（中断视为没播过，下次仍可能抽到这一组）
            AfterPlayback(uiAlreadyClosed: true);
        }

        // ══════════ 生命周期 ══════════

        /// <summary>新游戏 / GM 重置：清空 recent 与队列，强制收框。</summary>
        public void ResetNew()
        {
            pending.Clear();
            runtime = null;
            Data.Reset();
            if (modalOpen)
            {
                modalOpen = false;
                SetModalGate(false);
                PlaybackEnded?.Invoke();
            }
        }

        /// <summary>存档恢复（无调用方，待定 #9）。对话组按 id 还原需要一张 id→资产表，
        /// 统一存档定案时由存档层提供；在那之前传 null，recent 环从空开始——
        /// 它只是防重复的润色，丢了不影响正确性。</summary>
        public void Restore(DialogueSaveData data) => Data.Restore(data, null);

        // ══════════ 内部：播放 ══════════

        private bool TryBegin(PendingRequest request)
        {
            var group = Select(request.Visitor, request.Trigger, request.Satisfaction, out var categoryKey);
            if (group == null) return false;

            runtime = new DialogueRuntime
            {
                Trigger = request.Trigger,
                Satisfaction = request.Satisfaction,
                CategoryKey = categoryKey,
                RootGroup = group,
                CurrentGroup = group,
                StepIndex = 0,
                Context = BuildContext(request.Visitor),
            };

            // 先把开头的 Action 步执行掉、推进到第一个「要玩家看的」步，**这期间不开框**——
            // 免得一个全是事件的组把对话框开一下又立刻关掉，闪屏。
            if (!AdvanceToDisplayable())
            {
                MarkPlayed(); // 事件都执行了，算正常播完
                runtime = null;
                return false;
            }

            if (!modalOpen)
            {
                modalOpen = true;
                SetModalGate(true);
                // 订阅方拉起对话框后会自行首刷，这里不再补 ContentChanged——
                // 否则打字机会被连打两次，第一句从头重放
                PlaybackStarted?.Invoke();
            }
            else
            {
                // 框已经开着（队列续播下一段），只通知重绘
                ContentChanged?.Invoke();
            }
            return true;
        }

        /// <summary>推进到下一个可显示步（Line 或有可选项的 Branch）；沿途执行 Action 步。走到组尾返回 false。</summary>
        private bool AdvanceToDisplayable()
        {
            while (true)
            {
                var step = runtime.CurrentStep;
                if (step == null) return false;

                switch (step.kind)
                {
                    case EDialogueStepKind.Line:
                        return true;

                    case EDialogueStepKind.Action:
                        ExecuteActions(step.actions);
                        if (runtime == null) return false;
                        runtime.StepIndex++;
                        break;

                    case EDialogueStepKind.Branch:
                        if (HasSelectableOption(step)) return true;
                        // 硬校验（§4.3）要求每个分支至少一个无条件选项，走到这里说明资产没过校验。
                        // 不能停在这——玩家会卡在一屏全灰的选项上，只能 ESC。
                        Debug.LogError($"[对话] 组「{runtime.CurrentGroup.DisplayId}」第 {runtime.StepIndex} 步的分支没有任何可选选项" +
                                       "（缺无条件选项，§4.3 硬校验）；已跳过该分支，请用资产校验器检查");
                        runtime.StepIndex++;
                        break;

                    default:
                        runtime.StepIndex++;
                        break;
                }
            }
        }

        private void Continue()
        {
            if (runtime == null) return;
            if (AdvanceToDisplayable())
            {
                ContentChanged?.Invoke();
                return;
            }
            Finish();
        }

        /// <summary>正常播完：记 recent，然后收尾。</summary>
        private void Finish()
        {
            MarkPlayed();
            AfterPlayback();
        }

        /// <summary>
        /// 播完/中断后的统一收尾：续播队列，队列空则关框。
        ///
        /// uiAlreadyClosed = 玩家按 ESC 的路径：对话框实例已经被自己销毁了。
        /// 这时必须先把 modalOpen 抹掉，否则续播下一段时会走「框还开着，只发 ContentChanged」的分支，
        /// 而那个框已经没了——结果是对话在后台继续播、玩家什么都看不到、闸门也解不开。
        /// </summary>
        private void AfterPlayback(bool uiAlreadyClosed = false)
        {
            runtime = null;
            if (uiAlreadyClosed) modalOpen = false;

            while (pending.Count > 0)
                if (TryBegin(pending.Dequeue()))
                    return;

            var wasOpen = modalOpen || uiAlreadyClosed;
            modalOpen = false;
            SetModalGate(false); // 无条件解闸：闸门是位集合，重复清除无害，但漏清会把时间永久冻住
            if (wasOpen) PlaybackEnded?.Invoke();
        }

        /// <summary>写 recent 环。**只有从池里抽中的那个组**写入——跳转到的组不是池成员，记进去会污染去重。</summary>
        private void MarkPlayed()
        {
            if (runtime == null || runtime.RootGroup == null || string.IsNullOrEmpty(runtime.CategoryKey)) return;
            Data.MarkPlayed(runtime.CategoryKey, runtime.RootGroup, RingLength);
        }

        // ══════════ 内部：选取（§6）══════════

        private DialogueGroupDef Select(VisitorInstance visitor, EVisitorDialogueTrigger trigger,
            EServeSatisfaction satisfaction, out string categoryKey)
        {
            categoryKey = null;
            var categoryName = DialoguePoolDef.CategoryName(trigger, satisfaction);

            var pool = PoolOf(visitor);
            if (pool == null)
            {
                Debug.LogError($"[对话] 种族「{RaceNameOf(visitor)}」没有配置对话池（VisitorRaceDef.dialoguePool），" +
                               $"「{categoryName}」跳过对话直接走后续流程（§4.5：缺台词不该卡死接待）");
                return null;
            }

            var entries = pool.GroupsFor(trigger, satisfaction);
            if (entries == null || entries.Count == 0)
            {
                Debug.LogError($"[对话] 种族「{RaceNameOf(visitor)}」的分类「{categoryName}」是空的，" +
                               "跳过对话直接走后续流程（§4.5）");
                return null;
            }

            var ctx = BuildContext(visitor);
            candidates.Clear();
            foreach (var entry in entries)
                if (entry != null && entry.IsUsable && ConditionsPass(entry.conditions, ctx))
                    candidates.Add(entry);

            if (candidates.Count == 0)
            {
                Debug.LogError($"[对话] 种族「{RaceNameOf(visitor)}」的分类「{categoryName}」里没有条件通过的对话组" +
                               "（组为空 / 权重 ≤0 / 条件全不满足），跳过对话（§4.5）");
                return null;
            }

            categoryKey = DialogueData.CategoryKey(RaceIdOf(visitor), trigger, satisfaction);

            fresh.Clear();
            foreach (var entry in candidates)
                if (!Data.WasRecentlyPlayed(categoryKey, entry.group))
                    fresh.Add(entry);
            if (fresh.Count == 0)
            {
                // 候选被 recent 排空 → 清环重筛，保证永远有话可说（§6）
                Data.ClearRecent(categoryKey);
                fresh.AddRange(candidates);
            }

            // 派生种子（§6）：Hash(runSeed, 访客实例Id, 触发分类, 本次请求序号)。
            // 无状态、可复现、读档不刷；请求序号让同一访客同一触发点的多次请求（闲逛冒泡）不至于永远同一句。
            var seed = DeterministicRng.Hash(RunSeed, visitor.InstanceId,
                CategoryIndex(trigger, satisfaction), Data.RequestSerial++);
            var rng = new DeterministicRng(seed);
            return WeightedPick(fresh, ref rng);
        }

        /// <summary>加权抽取。列表顺序来自资产（稳定），不涉及任何字典枚举（§11.2）。</summary>
        private static DialogueGroupDef WeightedPick(List<DialogueGroupEntry> pool, ref DeterministicRng rng)
        {
            var total = 0;
            foreach (var entry in pool) total += entry.weight;
            if (total <= 0) return pool[0].group;

            var roll = rng.Range(0, total);
            var accumulated = 0;
            foreach (var entry in pool)
            {
                accumulated += entry.weight;
                if (roll < accumulated) return entry.group;
            }
            return pool[pool.Count - 1].group;
        }

        /// <summary>分类的稳定整数编号（派生种子分量）：把满意度折进触发点，得到 §4.5 的八分类。</summary>
        private static int CategoryIndex(EVisitorDialogueTrigger trigger, EServeSatisfaction satisfaction)
        {
            var tier = trigger == EVisitorDialogueTrigger.ServiceDone ? (int)satisfaction : 0;
            return (int)trigger * 8 + tier;
        }

        // ══════════ 内部：闲逛冒泡 ══════════

        private void RequestBubble(VisitorInstance visitor, EServeSatisfaction satisfaction)
        {
            var group = Select(visitor, EVisitorDialogueTrigger.WanderChat, satisfaction, out var categoryKey);
            if (group == null) return;

            // 气泡只能显示一句，所以取组里的第一条 Line。
            // 闲逛组里放 Action / Branch 无处安放（气泡没有点击推进，也没有选项列），
            // 由资产校验器给警告——这里静默取第一句，不阻断表现。
            DialogueLine line = null;
            if (group.steps != null)
                foreach (var step in group.steps)
                    if (step != null && step.kind == EDialogueStepKind.Line && step.line != null)
                    {
                        line = step.line;
                        break;
                    }

            if (line == null)
            {
                Debug.LogError($"[对话] 闲逛对话组「{group.DisplayId}」里没有任何台词行，无法冒泡");
                return;
            }

            Data.MarkPlayed(categoryKey, group, RingLength);
            var ctx = BuildContext(visitor);
            BubbleRequested?.Invoke(visitor, DialogueTextFormatter.Format(line.text, ctx));
        }

        // ══════════ 内部：事件与条件 ══════════

        private void ExecuteActions(List<IGameplayAction> actions)
        {
            if (actions == null) return;
            foreach (var action in actions)
                if (action != null)
                    SafeExecute(action);
        }

        /// <summary>
        /// 执行单个事件并吞掉异常：内容驱动的系统里，一条配错的事件不该把整段对话打死在半路
        /// （那会连 ESC 都退不出去，因为播放状态卡在异常点上）。
        /// </summary>
        private void SafeExecute(IGameplayAction action)
        {
            try
            {
                action.Execute(runtime?.Context ?? BuildContext(null));
            }
            catch (Exception exception)
            {
                Debug.LogError($"[对话] 事件 {action.GetType().Name} 执行抛异常，已跳过该事件继续播放：\n{exception}");
            }
        }

        /// <summary>多个条件之间 AND；空列表 = 无条件通过（§4.2）。</summary>
        private static bool ConditionsPass(List<IGameplayCondition> conditions, GameplayContext ctx)
        {
            if (conditions == null || conditions.Count == 0) return true;
            foreach (var condition in conditions)
            {
                if (condition == null) continue; // 未选类型的空槽视为无条件，不拦
                try
                {
                    if (!condition.Evaluate(ctx)) return false;
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[对话] 条件 {condition.GetType().Name} 求值抛异常，按不通过处理：\n{exception}");
                    return false;
                }
            }
            return true;
        }

        private bool HasSelectableOption(DialogueStep step)
        {
            if (step.options == null) return false;
            foreach (var option in step.options)
                if (option != null && ConditionsPass(option.conditions, runtime.Context))
                    return true;
            return false;
        }

        // ══════════ 内部：小工具 ══════════

        private GameplayContext BuildContext(VisitorInstance visitor) => new GameplayContext
        {
            VisitorManager = visitors,
            Economy = economy,
            Clock = clock,
            Visitor = visitor,
        };

        private void SetModalGate(bool active) =>
            clock?.SetStopReason(EClockStopReason.ModalDialogue, active);

        private long RunSeed => visitors != null ? visitors.Data.RunSeed : 0L;

        private int RingLength => tuning != null ? tuning.RecentRingLengthSafe : FallbackRingLength;

        /// <summary>打字机速度（表现层读取，§5.1）。</summary>
        public float TypewriterCharsPerSecond =>
            tuning != null ? tuning.TypewriterCharsPerSecondSafe : 30f;

        private static DialoguePoolDef PoolOf(VisitorInstance visitor) =>
            visitor != null && visitor.Race != null ? visitor.Race.dialoguePool : null;

        private static string RaceIdOf(VisitorInstance visitor)
        {
            if (visitor == null || visitor.Race == null) return "?";
            return string.IsNullOrEmpty(visitor.Race.raceId) ? visitor.Race.name : visitor.Race.raceId;
        }

        private static string RaceNameOf(VisitorInstance visitor)
        {
            if (visitor == null || visitor.Race == null) return "（未知种族）";
            return string.IsNullOrEmpty(visitor.Race.displayName) ? visitor.Race.name : visitor.Race.displayName;
        }

        /// <summary>待播请求（见 pending 字段注释）。</summary>
        private struct PendingRequest
        {
            public VisitorInstance Visitor;
            public EVisitorDialogueTrigger Trigger;
            public EServeSatisfaction Satisfaction;
        }
    }
}

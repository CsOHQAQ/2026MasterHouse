using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 对话业务逻辑：选取、播放推进、事件执行与闲聊冒泡。实现访客侧的 IDialogueService 接缝。
    ///
    /// 播放推进**不进 tick**：模态对话框期间闸门关闭、tick 本来就停了，
    /// 推进完全由玩家点击驱动。打字机是表现层计时，在 DialogueOverlay 里用 deltaTime。
    ///
    /// 关于 fire-and-forget：RequestVisitorDialogue 不返回任何东西、也没有回调——
    /// 访客侧调完就走。协程状态无法序列化，与 §11 冲突，所以不用协程。
    ///
    /// 2026-08-14 对话资源重构：内容源从「散落的 DialogueGroupDef/DialoguePoolDef 资产」
    /// 换成一张 DialogueTable（由 Excel 导表整表重建）；触发点与满意度合并为一维的八分类；
    /// 事件与条件从 [SerializeReference] 多态类换成 DialogueFuncs 里的字典注册表（零反射）。
    /// </summary>
    public sealed class DialogueManager : IDialogueService
    {
        /// <summary>tuning 缺失时的 recent 环长兜底。</summary>
        private const int FallbackRingLength = 3;

        private readonly DialogueTuningConfig tuning;
        private readonly DialogueTable table;

        // ── 两阶段初始化 ──
        // VisitorManager 的构造需要 IDialogueService，而本类又需要 VisitorManager——构造期存在循环依赖。
        // 解法是显式两阶段：GameManager 先造 DialogueManager，再造 VisitorManager，最后 Bind。
        // 不用「读 GameManager.Instance」的隐式解法：那会让逻辑层反向依赖场景单例，测试也没法替身。
        private VisitorManager visitors;
        private EconomyManager economy;
        private HouseClockManager clock;

        public DialogueData Data { get; } = new DialogueData();

        private DialogueRuntime runtime;

        /// <summary>待播队列：对话事件会触发新的对话请求（接待 → 分房 → 需求对话），
        /// 那时当前这段还没播完，排队等它播完再开，而不是打断。</summary>
        private readonly Queue<PendingRequest> pending = new Queue<PendingRequest>();

        /// <summary>选取用的复用缓冲（避免每次请求都分配）。</summary>
        private readonly List<DialoguePoolEntry> candidates = new List<DialoguePoolEntry>();
        private readonly List<DialoguePoolEntry> fresh = new List<DialoguePoolEntry>();

        /// <summary>对话框是否开着。与 runtime != null 不同——续播队列时 runtime 会短暂为 null 但框不关。</summary>
        private bool modalOpen;

        // ── 事件（§2.1：离散变化由 Manager 广播，View 订阅刷新）──

        /// <summary>模态对话框应当打开。</summary>
        public event Action PlaybackStarted;

        /// <summary>当前显示内容变了（换行 / 出选项 / 续播下一段），View 重绘。</summary>
        public event Action ContentChanged;

        /// <summary>模态对话框应当关闭（队列也空了）。</summary>
        public event Action PlaybackEnded;

        /// <summary>闲聊台词冒泡（场景气泡，不碰闸门、不走模态）。</summary>
        public event Action<VisitorInstance, string> BubbleRequested;

        public DialogueManager(DialogueTuningConfig tuning, DialogueTable table)
        {
            this.tuning = tuning;
            this.table = table;
            if (tuning == null)
                Debug.LogError("[对话] 调参配置缺失（Resources/OutGameUI/DialogueTuningConfig）：" +
                               "已按默认值运行（打字机 30 字/秒、recent 环长 3）。" +
                               "请执行菜单 MasterHouse → 配置中心 补齐资产");
            if (table == null)
                Debug.LogError("[对话] 对话整表缺失（Resources/OutGameUI/DialogueTable）：全部对话都不会播。" +
                               "请编辑 Excel/对话表.xlsx 后运行 Tools/导表/export_config.bat，" +
                               "或在 Unity 里执行菜单 MasterHouse → 对话系统 → 从 CSV 导入对话");
        }

        /// <summary>第二阶段初始化（见字段区注释）。GameManager 在造完 VisitorManager 之后调用一次。</summary>
        public void Bind(VisitorManager visitors, EconomyManager economy, HouseClockManager clock)
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
                if (runtime == null) return null;
                if (runtime.OptionIndex >= 0)
                {
                    var sub = runtime.CurrentSubStep;
                    return sub != null && sub.kind == EDialogueStepKind.Line ? sub.line : null;
                }
                var step = runtime.MainStep;
                return step != null && step.kind == EDialogueStepKind.Line ? step.line : null;
            }
        }

        /// <summary>当前台词的成文（已替换占位符）。</summary>
        public string CurrentText
        {
            get
            {
                var line = CurrentLine;
                return line != null ? DialogueTextFormatter.Format(line.text, runtime.Context) : string.Empty;
            }
        }

        /// <summary>
        /// 当前该显示的立绘ID（已承接「留空 = 沿用上一句」，见 DialogueRuntime.CarriedPortraitId）。
        /// 没在播时为空串。表现层拿它去 PortraitTable 查图，查不到就不显示立绘。
        /// </summary>
        public string CurrentPortraitId => runtime != null ? runtime.CarriedPortraitId ?? string.Empty : string.Empty;

        // ── 玩家身份（2026-08-19）──
        // 「玩家」以前只是名字凸台上一个写死的「我」，谁在说话不关内容的事。现在羊族（Race_goat
        // 「嘻洋羊」）被定为玩家角色，老板的台词要挂她自己的名字与脸，这里才需要认人。
        //
        // 认人的判据有两条，**第二条是临时桥接**：
        //   ① 说话人 = player（对话表第二页「说话人」列写 player）——正规写法；
        //   ② 说话人 = visitor 但立绘ID 是玩家种族的（`goat_*`）——对话表现状就是这么写的：
        //      老板的台词全部挂在 visitor 上（她要露脸，而 §4.1 定的「玩家句不显示立绘」会把脸吃掉），
        //      于是名字凸台去取"当前访客"的名字，画面上就成了「猫的名字 + 羊的脸」。
        //      表还在策划手里编辑，这轮不动它，先在表现层把名字认回来。
        //      等表改成「说话人=player + 保留立绘」之后，②可以连同这段注释一起删掉。

        /// <summary>玩家所属种族（配在 DialogueTuningConfig.playerRace）；没配则为 null。</summary>
        private VisitorRaceDef PlayerRace => tuning != null ? tuning.playerRace : null;

        /// <summary>
        /// 玩家（旅馆老板）的显示名。取自玩家种族资产的显示名，**不在对话侧另存一份**。
        /// 没配玩家种族时回落旧口径「我」——那是 2026-08-19 之前名字凸台写死的那个字。
        /// </summary>
        public string PlayerDisplayName
        {
            get
            {
                var race = PlayerRace;
                return race != null && !string.IsNullOrEmpty(race.displayName) ? race.displayName : "我";
            }
        }

        /// <summary>
        /// 这张立绘是不是玩家的脸。按「种族id 或 种族id_差分」匹配立绘ID——
        /// 立绘表不带种族列（那张表刻意不区分角色），前缀是现有数据唯一的可用线索。
        /// 玩家种族没配时恒为 false，行为与本改动之前一致。
        /// </summary>
        private bool IsPlayerPortrait(string portraitId)
        {
            var race = PlayerRace;
            var raceId = race != null ? race.raceId : null;
            if (string.IsNullOrEmpty(raceId) || string.IsNullOrEmpty(portraitId)) return false;
            if (string.Equals(portraitId, raceId, StringComparison.OrdinalIgnoreCase)) return true;
            return portraitId.Length > raceId.Length &&
                   portraitId[raceId.Length] == '_' &&
                   portraitId.StartsWith(raceId, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>当前这句是不是玩家在说（两条判据见上方注释）。停在分支上或没在播时为 false。</summary>
        public bool IsCurrentLineFromPlayer
        {
            get
            {
                var line = CurrentLine;
                if (line == null) return false;
                if (line.speaker == EDialogueSpeaker.Player) return true;
                return line.speaker == EDialogueSpeaker.Visitor && IsPlayerPortrait(CurrentPortraitId);
            }
        }

        /// <summary>
        /// 名字凸台该显示的名字：旁白留空，玩家句显示玩家名，其余显示当前访客名。
        /// 访客缺失时回落「访客」——对话是访客说的却没有访客实例只可能是调用方组装上下文时漏了，
        /// 名字凸台不该因此空一格。
        /// </summary>
        public string CurrentSpeakerName
        {
            get
            {
                var line = CurrentLine;
                if (line == null || line.speaker == EDialogueSpeaker.Narration) return string.Empty;
                if (IsCurrentLineFromPlayer) return PlayerDisplayName;
                var visitor = CurrentVisitor;
                return visitor != null ? visitor.DisplayName : "访客";
            }
        }

        /// <summary>台词填了立绘ID 就换脸；留空则保持不变——承接只发生在这一处。</summary>
        private void CarryPortrait(DialogueLine line)
        {
            if (runtime == null || line == null || string.IsNullOrEmpty(line.portraitId)) return;
            runtime.CarriedPortraitId = line.portraitId;
        }

        /// <summary>当前是否停在分支上（停在分支时点击不推进，必须选一个选项）。</summary>
        public bool IsAtBranch => runtime != null && runtime.IsAtBranch;

        /// <summary>当前分支的全部选项（含不满足条件的——它们置灰保留可见）；不在分支上时为 null。</summary>
        public IReadOnlyList<DialogueOption> CurrentOptions
        {
            get
            {
                if (runtime == null || !runtime.IsAtBranch) return null;
                return runtime.MainStep.options;
            }
        }

        /// <summary>选项是否可选（条件全通过）。View 用它决定置灰。</summary>
        public bool IsOptionEnabled(DialogueOption option) =>
            option != null && DialogueFuncs.EvaluateAll(option.conditions, runtime?.Context);

        /// <summary>交付判据的条件函数名（#12 独占规则的键）。交付选项换条件写法时要同步这里。</summary>
        private const string DeliverConditionFunc = "RoomHasNeedFurniture";

        /// <summary>
        /// 选项是否可见（2026-08-22 一轮测试改进 #12）：分支里存在「条件含 RoomHasNeedFurniture
        /// 且当前满足」的交付选项时，仅显示这些选项、其余整体隐藏——家具都摆好了还把
        /// 「还没准备好」之类的选项亮着，只会引导玩家走弯路。
        ///
        /// 运行时固定规则、零改表：以条件函数为键，天然只作用于条件类需求对话；
        /// 交付条件不满足时一切照旧（交付选项置灰保留可见），
        /// 校验器「每分支至少一个无条件选项」的防卡死硬规则不受影响。
        /// </summary>
        public bool IsOptionVisible(DialogueOption option)
        {
            if (option == null) return false;
            if (runtime == null || !runtime.IsAtBranch) return true;
            if (IsSatisfiedDeliverOption(option)) return true;
            var options = runtime.MainStep.options;
            if (options == null) return true;
            for (var i = 0; i < options.Count; i++)
                if (IsSatisfiedDeliverOption(options[i]))
                    return false; // 有可交付的选项在场：其余选项整体让位
            return true;
        }

        /// <summary>是否为「条件含交付判据且条件当前全部满足」的交付选项。</summary>
        private bool IsSatisfiedDeliverOption(DialogueOption option)
        {
            if (option == null || option.conditions == null) return false;
            var hasDeliverCondition = false;
            for (var i = 0; i < option.conditions.Count; i++)
            {
                var call = option.conditions[i];
                if (call == null || call.func != DeliverConditionFunc) continue;
                hasDeliverCondition = true;
                break;
            }
            return hasDeliverCondition && IsOptionEnabled(option);
        }

        /// <summary>选项文本的成文（已替换占位符）。</summary>
        public string FormatOptionText(DialogueOption option) =>
            option != null ? DialogueTextFormatter.Format(option.text, runtime?.Context) : string.Empty;

        /// <summary>需求描述：就是 NeedDef.description，UI 与台词共用同一份文本。</summary>
        public string BuildNeedPhrase(VisitorInstance visitor) =>
            visitor != null ? visitor.BuildNeedSentence() : string.Empty;

        /// <summary>打字机速度（表现层读取）。</summary>
        public float TypewriterCharsPerSecond =>
            tuning != null ? tuning.TypewriterCharsPerSecondSafe : 30f;

        // ══════════ 访客 → 对话（IDialogueService）══════════

        public bool RequestVisitorDialogue(VisitorInstance visitor, EDialogueCategory category)
        {
            if (visitor == null) return false;

            // 闲聊走场景气泡，不开模态、不碰闸门
            if (category == EDialogueCategory.SmallTalk) return RequestBubble(visitor);

            var request = new PendingRequest { Visitor = visitor, Category = category };
            if (runtime != null)
            {
                // 排队也算「有内容」：当前这段播完就轮到它
                pending.Enqueue(request);
                return true;
            }
            if (TryBegin(request)) return true;
            AfterPlayback(); // 这段没选出内容：继续消化队列，或收框
            return false;
        }

        // ══════════ 播放推进（由 DialogueOverlay 调用）══════════

        /// <summary>玩家点击推进到下一步。停在分支上时无效（必须选）。</summary>
        public void Advance()
        {
            if (runtime == null || runtime.IsAtBranch) return;
            if (runtime.OptionIndex >= 0) runtime.SubIndex++;
            else runtime.StepIndex++;
            Continue();
        }

        /// <summary>玩家选中分支选项。下标非法或选项不可选时无效。</summary>
        public void ChooseOption(int index)
        {
            if (runtime == null || !runtime.IsAtBranch) return;
            var options = runtime.MainStep.options;
            if (index < 0 || index >= options.Count) return;
            var option = options[index];
            if (option == null || !IsOptionEnabled(option)) return;
            if (!IsOptionVisible(option)) return; // #12：被独占规则藏起来的选项不可选（防御，View 本就不给它绑点击）

            // 位置切进子句，随后的 Continue 会执行掉子句开头的事件并停在第一句台词上；
            // 子句里没有台词（纯事件选项）时自动汇合回主线下一步。
            runtime.EnterOption(index);
            Continue();
        }

        /// <summary>
        /// 玩家中断（ESC / 关闭按钮）：**视为这段对话没有被播放**——不写 recent（下次仍可能抽到），
        /// 业务不进入任何新状态。已经执行过的事件不回滚（既成事实）；尚未执行到的**一律丢弃**。
        ///
        /// 「未执行事件按开关补执行」那套（ExecuteOnInterrupt）已于 2026-08-14 重构删除：
        /// 八个事件里从来没有一个用过它，而「必须发生的后续推进由状态机自己走完」才是根本
        /// （§5.3 铁律①）。将来真需要再加是纯增量。
        ///
        /// 调用约定：由 DialogueOverlay.Close 调用，**调用时对话框实例已经自行销毁**。
        /// 所以这里按 uiAlreadyClosed 收尾——若队列里还压着下一段，会重新触发 PlaybackStarted 拉起新框。
        /// </summary>
        public void Interrupt()
        {
            if (runtime == null) return;
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

        /// <summary>存档恢复（无调用方，待定 #9）。recent 环存的是组 id，不需要任何解析表。</summary>
        public void Restore(DialogueSaveData data) => Data.Restore(data);

        // ══════════ 内部：播放 ══════════

        private bool TryBegin(PendingRequest request)
        {
            var group = Select(request.Visitor, request.Category, out var categoryKey);
            if (group == null) return false;

            runtime = new DialogueRuntime
            {
                Category = request.Category,
                CategoryKey = categoryKey,
                Group = group,
                StepIndex = 0,
                OptionIndex = -1,
                SubIndex = 0,
                Context = BuildContext(request.Visitor),
                // 首句若没填立绘ID，顶的就是这位访客种族的默认脸
                CarriedPortraitId = request.Visitor != null && request.Visitor.Race != null
                    ? request.Visitor.Race.defaultPortraitId
                    : string.Empty,
            };

            // 先把开头的事件步执行掉、推进到第一个「要玩家看的」步，**这期间不开框**——
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

        /// <summary>
        /// 推进到下一个可显示位置（台词，或有可选项的分支）；沿途执行事件步。
        /// 走到组尾返回 false。
        /// </summary>
        private bool AdvanceToDisplayable()
        {
            // 步数上限兜底：正常内容走不到这里，配置异常时也不该把主线程转死
            for (var guard = 0; guard < 4096; guard++)
            {
                if (runtime == null) return false;

                // ① 在某个选项的子句里
                if (runtime.OptionIndex >= 0)
                {
                    var option = runtime.CurrentOption;
                    if (option == null || option.steps == null || runtime.SubIndex >= option.steps.Count)
                    {
                        runtime.LeaveOption(); // 子句播完 → 汇合到主线的下一步
                        continue;
                    }
                    var sub = option.steps[runtime.SubIndex];
                    if (sub == null) { runtime.SubIndex++; continue; }
                    if (sub.kind == EDialogueStepKind.Line) { CarryPortrait(sub.line); return true; }
                    DialogueFuncs.ExecuteAll(sub.actions, runtime.Context);
                    if (runtime == null) return false; // 防御：事件若意外终止了播放
                    runtime.SubIndex++;
                    continue;
                }

                // ② 在主线上
                var step = runtime.MainStep;
                if (step == null) return false;
                switch (step.kind)
                {
                    case EDialogueStepKind.Line:
                        CarryPortrait(step.line);
                        return true;

                    case EDialogueStepKind.Action:
                        DialogueFuncs.ExecuteAll(step.actions, runtime.Context);
                        if (runtime == null) return false;
                        runtime.StepIndex++;
                        break;

                    case EDialogueStepKind.Branch:
                        if (HasSelectableOption(step)) return true;
                        // 硬校验要求每个分支至少一个无条件选项，走到这里说明资产没过校验。
                        // 不能停在这——玩家会卡在一屏全灰的选项上，只能 ESC。
                        Debug.LogError($"[对话] 组 {runtime.Group.DisplayId} 第 {runtime.StepIndex + 1} 步的分支" +
                                       "没有任何可选选项（缺无条件选项）；已跳过该分支，请用资产校验器检查对话表");
                        runtime.StepIndex++;
                        break;

                    default:
                        runtime.StepIndex++;
                        break;
                }
            }
            Debug.LogError("[对话] 推进步数超过上限，疑似对话表结构异常，已强制结束本次播放");
            return false;
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

        /// <summary>正常播完：记 recent、通知访客侧，然后收尾。</summary>
        private void Finish()
        {
            MarkPlayed();
            // 「初次见面」正常播完才算真的见过面（ESC 中断视为没播过，下次仍播初次见面）。
            // 之后再点这位前台访客抽的是【等待接待】，不会把开场白重放一遍。
            if (runtime != null && runtime.Category == EDialogueCategory.FirstMeeting)
                visitors?.MarkMetPlayer(runtime.Context.VisitorInstanceId);
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

        private void MarkPlayed()
        {
            if (runtime == null || runtime.Group == null || string.IsNullOrEmpty(runtime.CategoryKey)) return;
            Data.MarkPlayed(runtime.CategoryKey, runtime.Group.id, RingLength);
        }

        // ══════════ 内部：选取 ══════════

        /// <summary>
        /// 选出一个对话组。规则：
        ///   候选 = 表[种族][分类] → 需求筛选（专属优先）→ 条件筛选 → 排除 recent
        ///   候选被 recent 排空 → 清环重筛（保证永远有话可说）
        ///   种子 = Hash(runSeed, 访客实例Id, 分类, 本次请求序号)，等权随机
        ///
        /// **需求筛选是「专属优先」而不是「混着抽」**：策划特地为某条需求写了台词，就该说那个，
        /// 不该有一半概率被通用句盖过去。没有专属组时才回落到「需求ID 留空」的通用组。
        /// </summary>
        private DialogueGroup Select(VisitorInstance visitor, EDialogueCategory category, out string categoryKey)
        {
            categoryKey = null;
            var categoryName = DialogueCategoryText.NameOf(category);
            var raceName = RaceNameOf(visitor);

            if (table == null) return null;

            var raceId = RaceIdOf(visitor);
            var entries = table.EntriesOf(raceId, category);
            if (entries.Count == 0)
            {
                Debug.LogError($"[对话] 种族「{raceName}」的分类「{categoryName}」在对话表里一条都没有，" +
                               "跳过对话直接走后续流程；请在 Excel/对话表.xlsx 第一页补一行");
                return null;
            }

            var ctx = BuildContext(visitor);
            var needId = visitor != null && visitor.Need != null ? visitor.Need.name : string.Empty;

            // ① 专属（需求ID 匹配）
            candidates.Clear();
            foreach (var entry in entries)
                if (entry != null && !string.IsNullOrEmpty(entry.needId) && entry.needId == needId &&
                    DialogueFuncs.EvaluateAll(entry.conditions, ctx))
                    candidates.Add(entry);

            // ② 没有专属才用通用（需求ID 留空）
            if (candidates.Count == 0)
                foreach (var entry in entries)
                    if (entry != null && string.IsNullOrEmpty(entry.needId) &&
                        DialogueFuncs.EvaluateAll(entry.conditions, ctx))
                        candidates.Add(entry);

            if (candidates.Count == 0)
            {
                Debug.LogError($"[对话] 种族「{raceName}」的分类「{categoryName}」没有可用候选" +
                               $"（需求「{(string.IsNullOrEmpty(needId) ? "无" : needId)}」没有专属组，" +
                               "通用组也不存在或条件全不满足），跳过对话");
                return null;
            }

            categoryKey = DialogueData.CategoryKey(raceId, category);

            fresh.Clear();
            foreach (var entry in candidates)
                if (!Data.WasRecentlyPlayed(categoryKey, entry.groupId))
                    fresh.Add(entry);
            if (fresh.Count == 0)
            {
                // 候选被 recent 排空 → 清环重筛，保证永远有话可说
                Data.ClearRecent(categoryKey);
                fresh.AddRange(candidates);
            }

            // 派生种子：无状态、可复现、读档不刷；请求序号让同一访客同一分类的多次请求
            // （闲聊冒泡、反复点访客）不至于永远同一句。
            var instanceId = visitor != null ? visitor.InstanceId : 0;
            var seed = DeterministicRng.Hash(RunSeed, instanceId, (int)category, Data.RequestSerial++);
            var rng = new DeterministicRng(seed);
            var picked = fresh[rng.Range(0, fresh.Count)]; // 权重列已删除，候选内等权

            var group = table.GroupOf(picked.groupId);
            if (group == null)
                Debug.LogError($"[对话] 对话表第一页引用了不存在的对话组 {picked.groupId}" +
                               $"（Excel 第 {picked.sourceRow} 行），跳过对话");
            return group;
        }

        // ══════════ 内部：闲聊冒泡 ══════════

        private bool RequestBubble(VisitorInstance visitor)
        {
            var group = Select(visitor, EDialogueCategory.SmallTalk, out var categoryKey);
            if (group == null) return false;

            // 气泡只能显示一句，所以取组里的第一条台词。
            // 闲聊组里放事件或分支无处安放（气泡没有点击推进，也没有选项列），
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
                Debug.LogError($"[对话] 闲聊对话组 {group.DisplayId} 里没有任何台词行，无法冒泡");
                return false;
            }

            Data.MarkPlayed(categoryKey, group.id, RingLength);
            var ctx = BuildContext(visitor);
            BubbleRequested?.Invoke(visitor, DialogueTextFormatter.Format(line.text, ctx));
            return true;
        }

        /// <summary>
        /// 表现层拉框失败时的兜底（DialogueOverlay.Open 的两个 return 出口）。
        ///
        /// 没有这一条会**永久冻住时间**：TryBegin 里已经 `SetModalGate(true)` 并广播了 PlaybackStarted，
        /// 而框没开出来 ⇒ 没有 overlay 实例、没人会调 Close ⇒ ModalDialogue 这条停走原因再也清不掉。
        /// </summary>
        public void AbortForMissingUi()
        {
            if (runtime == null && !modalOpen) return;
            Debug.LogError("[对话] 对话层没能打开（Prefab 缺失或结构不对），本次播放已放弃并解闸；" +
                           "修复 Resources/OutGameUI/Prefabs/DialogueView 之后重新点访客");
            pending.Clear();
            runtime = null;
            modalOpen = false;
            SetModalGate(false);
        }

        // ══════════ 内部：小工具 ══════════

        private bool HasSelectableOption(DialogueStep step)
        {
            if (step.options == null) return false;
            foreach (var option in step.options)
                if (option != null && DialogueFuncs.EvaluateAll(option.conditions, runtime.Context))
                    return true;
            return false;
        }

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
            public EDialogueCategory Category;
        }
    }
}

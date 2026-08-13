using System;
using UnityEngine;

namespace MasterHouse
{
    // ══════════════════════════════════════════════════════════════════════════
    //  对话事件子类集合（设计说明 §4.2）
    //
    //  ⚠️ 本文件里每一个类的**改名或改命名空间都必须挂 [MovedFrom]**
    //     （using UnityEngine.Scripting.APIUpdating; [MovedFrom("旧全名")]），
    //     否则策划已配的数据会被 Unity 静默清空且不报错——
    //     [SerializeReference] 按「程序集 + 类型全名」寻址，找不到就丢。
    //     这是本方案唯一的高危操作。
    //
    //  这些是普通 class，不是 Unity 对象，**不受「必须独占同名文件」的约束**，
    //  按分类合并在本文件即可（§4.2）。
    //
    //  铁律（§5.3）：
    //    ① 只做一次性状态转换与结算，**绝不承担必须发生的后续推进**。
    //       拒绝事件只置状态，离场由访客状态机在 tick 里自己走完——
    //       把「离场」挂在对话末尾的话，玩家一按 ESC 访客就永远卡在场上。
    //    ② 奖励类事件只允许放在对话组末尾或分支选项上（中途给奖励 + ESC = 反复领取）。
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 接待访客：前台等待中的访客进入「等待分配房间」（需求重做说明 §5.3 → VisitorManager.Accept）。
    ///
    /// 语义已于 2026-08-13 从「进入服务中」改为「进入等待分配房间」——**接待这一步不说需求**。
    /// 需求要等玩家把客人拖进一间空客房、进屋之后才由 MoveVisitorToRoom 播【开始等待服务】说出来
    /// （「先盲选房、进房后才说需求」是硬要求）。类名没动，所以不需要 [MovedFrom]。
    ///
    /// 满房时 Accept 返回 false。给这个事件所在的选项挂上【访客/还有空客房】条件，
    /// 满房时选项会自动置灰，玩家不会点到一个无效选项（§6.2）。
    /// </summary>
    [Serializable, SubclassLabel("访客/接待（进入等待分配房间）")]
    public sealed class AcceptVisitorAction : GameplayActionBase
    {
        public override void Execute(GameplayContext ctx)
        {
            if (ctx?.VisitorManager == null)
            {
                Debug.LogWarning("[对话事件] 接待：上下文缺 VisitorManager，已跳过");
                return;
            }
            // 合法性校验在 VisitorManager 内，状态不对返回 false 而不是抛异常（§7 契约）
            if (!ctx.VisitorManager.Accept(ctx.VisitorInstanceId))
                Debug.LogWarning($"[对话事件] 接待未生效：实例 {ctx.VisitorInstanceId} 不在「前台等待接待」状态，或客房已住满");
        }
    }

    /// <summary>
    /// 拒绝访客：按拒绝口径结算声望并让访客离场（§7 → VisitorManager.Reject）。
    /// 前台等待 / 等待分配房间 / 服务中三个状态都可用（打烊后玩家必须能手动清场，需求重做说明 §5.3）。
    /// 声望分两档：已接待过的（待分房、服务中）比在前台谢客扣得更多。
    /// </summary>
    [Serializable, SubclassLabel("访客/拒绝")]
    public sealed class RejectVisitorAction : GameplayActionBase
    {
        public override void Execute(GameplayContext ctx)
        {
            if (ctx?.VisitorManager == null)
            {
                Debug.LogWarning("[对话事件] 拒绝：上下文缺 VisitorManager，已跳过");
                return;
            }
            if (!ctx.VisitorManager.Reject(ctx.VisitorInstanceId))
                Debug.LogWarning($"[对话事件] 拒绝未生效：实例 {ctx.VisitorInstanceId} 不在可拒绝状态");
        }
    }

    /// <summary>
    /// 完成需求结算（需求重做说明 §6.2/§6.3 → VisitorManager.CompleteNeed）：
    /// 记账 → 播【完成服务·档位】→ 转闲逛。取代了随 Item 链退役的「提交物品」。
    ///
    /// **条件类固定判「完美」**（用户定案 §6.3）：条件类是布尔判定，没有中间档可分。
    /// 小游戏类将来由小游戏框架按分数调 CompleteNeed 传对应档位，不走这个事件。
    ///
    /// 属奖励类事件（IRewardAction），受 §5.3 铁律②约束：**只允许放在对话组末尾或分支选项上**。
    /// 放中途 + 玩家 ESC = 反复领取；校验器会对放错位置给警告。
    ///
    /// 配套条件：把【访客/所住房间有需求家具】挂在同一个选项上，否则玩家不满足需求也能点。
    /// </summary>
    [Serializable, SubclassLabel("访客/完成需求结算")]
    public sealed class CompleteNeedAction : GameplayActionBase, IRewardAction
    {
        [Tooltip("结算档位。条件类需求固定用「完美」；其余档位留给小游戏类按分数定档（§6.3）")]
        public EServeSatisfaction satisfaction = EServeSatisfaction.Perfect;

        public override void Execute(GameplayContext ctx)
        {
            if (ctx?.VisitorManager == null)
            {
                Debug.LogWarning("[对话事件] 完成需求结算：上下文缺 VisitorManager，已跳过");
                return;
            }
            if (!ctx.VisitorManager.CompleteNeed(ctx.VisitorInstanceId, satisfaction))
                Debug.LogWarning($"[对话事件] 完成需求结算未生效：实例 {ctx.VisitorInstanceId} 不在「服务中」");
        }
    }

    /// <summary>
    /// 开始小游戏（需求重做说明 §6.2/§7，小游戏说明 §3.7）。
    ///
    /// **只登记意图、不当场打开页面**——这不是偷懒，是退栈顺序要求的：
    /// 本事件在 DialogueManager.ChooseOption 内执行，此时对话层还在栈顶且仍在播；
    /// 当场 PushOverlay 的话，紧接着的 PlaybackEnded → CloseFromPlaybackEnded → PopOverlay
    /// 弹掉的会是**小游戏层**而不是对话层。真正打开由 HubPage 在播放结束后调 ConsumePending。
    ///
    /// 取不到小游戏时保持报错 + Toast、**不改变任何业务状态**（对话铁律 1）：
    /// 访客仍在「服务中」，玩家可以再点开重试，策划补完资产即可生效。
    /// </summary>
    [Serializable, SubclassLabel("访客/开始小游戏")]
    public sealed class StartMinigameAction : GameplayActionBase
    {
        public override void Execute(GameplayContext ctx)
        {
            var visitor = ctx?.Visitor;
            var who = visitor != null ? visitor.DisplayName : "访客";

            var need = visitor != null ? visitor.Need as MinigameNeedDef : null;
            if (need == null)
            {
                Debug.LogWarning($"[对话事件] 开始小游戏：{who} 的需求不是小游戏类" +
                                 $"（当前 {(visitor?.Need != null ? visitor.Need.DisplayId : "无需求")}），" +
                                 $"本次什么都没发生。请检查这条对话组是否挂错了触发分类");
                Toast("这位客人要的不是小游戏");
                return;
            }

            if (need.minigame == null)
            {
                Debug.LogError($"[对话事件] 开始小游戏：需求「{need.DisplayId}」没有配 minigame 引用，无法开局。" +
                               $"请在需求编辑器里给它指一个 MinigameDef", need);
                Toast("这条需求还没配小游戏");
                return;
            }

            MinigameOverlay.Request(need.minigame, visitor.InstanceId, need.DisplayId);
        }

        private static void Toast(string message)
        {
            // 不用 ?. ——Unity 的「已销毁但引用非 null」要靠重载的 == 才判得出来
            var ui = HouseUIManager.Instance;
            if (ui != null) ui.ShowToast(message);
        }
    }

    // 提交指定物品 SubmitItemAction 已随 Item 链退役（需求重做说明 §9.1）：
    // 访客不再交付物品，验收改走【访客/完成需求结算】。

    /// <summary>
    /// 增减货币（§5.3 铁律②：只放在对话组末尾或分支选项上）。
    /// 走 EconomyManager.AddCurrency 而非 GmAddCurrency——后者是调试后门，不是玩法收支。
    /// </summary>
    [Serializable, SubclassLabel("经济/增减货币")]
    public sealed class AddCurrencyAction : GameplayActionBase, IRewardAction
    {
        [Tooltip("正数为给予，负数为扣除；结果下限为 0")]
        public int amount;

        public override void Execute(GameplayContext ctx)
        {
            if (ctx?.Economy == null)
            {
                Debug.LogWarning("[对话事件] 增减货币：上下文缺 EconomyManager，已跳过");
                return;
            }
            ctx.Economy.AddCurrency(amount);
        }
    }

    /// <summary>增减声望（§5.3 铁律②：只放在对话组末尾或分支选项上）。声望变化会实时影响家具解禁状态。</summary>
    [Serializable, SubclassLabel("经济/增减声望")]
    public sealed class AddReputationAction : GameplayActionBase, IRewardAction
    {
        [Tooltip("正数为给予，负数为扣除；结果下限为 0")]
        public int amount;

        public override void Execute(GameplayContext ctx)
        {
            if (ctx?.Economy == null)
            {
                Debug.LogWarning("[对话事件] 增减声望：上下文缺 EconomyManager，已跳过");
                return;
            }
            ctx.Economy.AddReputation(amount);
        }
    }

    // 发放物品 GrantItemAction 已随 Item 链退役（§9.1）：局外侧没有物品的消费出口了，
    // 发出去也只能躺在仓库里。伴手礼这类奖励暂用【经济/增减货币】表达。

    /// <summary>
    /// 往 Console 打一条日志。给策划自查分支走向用（「这条分支到底进没进来」），不影响任何业务。
    /// 也是验收清单里「ESC 中途退出：未执行事件按 flag 决定」最省事的观察手段。
    /// </summary>
    [Serializable, SubclassLabel("调试/打印日志")]
    public sealed class LogAction : GameplayActionBase
    {
        [TextArea(1, 3)] public string message;

        public override void Execute(GameplayContext ctx)
        {
            var who = ctx?.Visitor != null ? ctx.Visitor.DisplayName : "（无访客）";
            Debug.Log($"[对话调试] {who}：{message}");
        }
    }
}

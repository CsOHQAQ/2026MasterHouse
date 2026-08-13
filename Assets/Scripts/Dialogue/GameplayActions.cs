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
    /// 开始小游戏（需求重做说明 §6.2/§7）——**本包占位，不改变任何业务状态**。
    ///
    /// 小游戏框架尚未设计（另开专题）。这里刻意只打日志 + 提示，
    /// **不为它预建接口、注册表或任何抽象**（§15.3「不预设抽象、不建没有调用方的接缝」）：
    /// 等框架定案时，把这个 Execute 的实现换掉即可，策划已配的对话数据一行不用动。
    ///
    /// 过渡期后果是明示的：小游戏类需求的访客只能走「拒绝」或「等交货超时」离场，验收时不算 bug（§7）。
    /// </summary>
    [Serializable, SubclassLabel("访客/开始小游戏（尚未接入）")]
    public sealed class StartMinigameAction : GameplayActionBase
    {
        public override void Execute(GameplayContext ctx)
        {
            var who = ctx?.Visitor != null ? ctx.Visitor.DisplayName : "访客";
            Debug.LogWarning($"[对话事件] 开始小游戏：小游戏框架尚未接入（{who}），本次什么都没发生（§7 明示的过渡态）");
            // 不用 ?. ——Unity 的「已销毁但引用非 null」要靠重载的 == 才判得出来
            var ui = HouseUIManager.Instance;
            if (ui != null) ui.ShowToast("小游戏尚未接入");
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

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
    /// 接待访客：前台等待中的访客进入「服务中」（设计说明 §7 → VisitorManager.Accept）。
    /// 注意 Accept 内部会请求【开始等待服务】对话——那条请求会被 DialogueManager 排进待播队列，
    /// 等当前这段对话播完再开，不会打断正在进行的播放。
    /// </summary>
    [Serializable, SubclassLabel("访客/接待（进入服务中）")]
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
                Debug.LogWarning($"[对话事件] 接待未生效：实例 {ctx.VisitorInstanceId} 不在「前台等待接待」状态");
        }
    }

    /// <summary>
    /// 拒绝访客：按拒绝口径结算声望并让访客离场（§7 → VisitorManager.Reject）。
    /// 前台等待与服务中两个状态都可用（打烊后玩家必须能手动清场，访客交付说明 §5）。
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
    /// 提交指定物品并结算（§7 → VisitorManager.Submit）。
    /// 服务一次性、不可补交——提交一次即定生死，交错了照样扣物品（访客交付说明 §5）。
    /// 物品在此处由策划写死；玩家自选物品的交付走「需求交付页面」，那是另一份落地文档的范围（§7 外部依赖）。
    /// </summary>
    [Serializable, SubclassLabel("访客/提交指定物品")]
    public sealed class SubmitItemAction : GameplayActionBase
    {
        [Tooltip("要提交的物品；仓库无货时提交失败（不存在的东西交不出去）")]
        public ItemDef item;

        public override void Execute(GameplayContext ctx)
        {
            if (ctx?.VisitorManager == null)
            {
                Debug.LogWarning("[对话事件] 提交物品：上下文缺 VisitorManager，已跳过");
                return;
            }
            if (item == null)
            {
                Debug.LogError("[对话事件] 提交物品：没有配置物品（该事件未填 item），已跳过");
                return;
            }
            if (!ctx.VisitorManager.Submit(ctx.VisitorInstanceId, item))
                Debug.LogWarning($"[对话事件] 提交未生效：实例 {ctx.VisitorInstanceId} 不在「服务中」，或仓库里没有「{item.DisplayName}」");
        }
    }

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

    /// <summary>
    /// 发放物品到全局仓库（PlayerCargoData）。访客的伴手礼、剧情奖励等用它。
    /// 同样受铁律②约束：只放在对话组末尾或分支选项上。
    /// </summary>
    [Serializable, SubclassLabel("经济/发放物品")]
    public sealed class GrantItemAction : GameplayActionBase, IRewardAction
    {
        public ItemDef item;

        [Tooltip("数量（≤0 视为配置错误，跳过并报错）")]
        public int count = 1;

        public override void Execute(GameplayContext ctx)
        {
            if (ctx?.Cargo == null)
            {
                Debug.LogWarning("[对话事件] 发放物品：上下文缺 PlayerCargo，已跳过");
                return;
            }
            if (item == null || count <= 0)
            {
                Debug.LogError($"[对话事件] 发放物品：配置无效（item={(item != null ? item.DisplayName : "空")} count={count}），已跳过");
                return;
            }
            ctx.Cargo.Add(item, count);
        }
    }

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

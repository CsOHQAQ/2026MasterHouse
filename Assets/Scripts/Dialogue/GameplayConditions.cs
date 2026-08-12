using System;
using UnityEngine;

namespace MasterHouse
{
    // ══════════════════════════════════════════════════════════════════════════
    //  对话条件子类集合（设计说明 §4.2）
    //
    //  ⚠️ 与事件同样受 [MovedFrom] 改名约束——改名不挂特性 = 静默清空策划配置。
    //
    //  所有 Evaluate 必须是**纯查询、无副作用**：条件会在筛选候选对话组、
    //  渲染选项置灰等场合被反复调用，带副作用会导致同一 tick 内多次结算。
    //
    //  多个条件之间默认 AND。需要 OR/NOT 时再加组合子类，现在不做（§12 明确不做）。
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>访客处于指定生命周期状态。用于「只在服务中才出现的选项」这类判定。</summary>
    [Serializable, SubclassLabel("访客/处于指定状态")]
    public sealed class VisitorStateCondition : IGameplayCondition
    {
        public EVisitorState state = EVisitorState.FrontDesk;

        public bool Evaluate(GameplayContext ctx)
        {
            // 注意用实例上的 State 而不是「是否还在在场列表」——被拒绝的访客播对话时已离场（见 GameplayContext.Visitor）
            return ctx?.Visitor != null && ctx.Visitor.State == state;
        }
    }

    /// <summary>访客是指定种族。用于让多个种族共用一个对话组、组内再按种族分岔。</summary>
    [Serializable, SubclassLabel("访客/是指定种族")]
    public sealed class VisitorRaceCondition : IGameplayCondition
    {
        public VisitorRaceDef race;

        public bool Evaluate(GameplayContext ctx)
        {
            if (race == null)
            {
                Debug.LogWarning("[对话条件] 是指定种族：没有配置种族，判定为不通过");
                return false;
            }
            return ctx?.Visitor != null && ctx.Visitor.Race == race;
        }
    }

    /// <summary>本次服务的满意度达到指定档位或更高（Mismatch &lt; Plain &lt; Satisfied &lt; Perfect）。仅提交结算之后有意义。</summary>
    [Serializable, SubclassLabel("访客/满意度不低于")]
    public sealed class SatisfactionAtLeastCondition : IGameplayCondition
    {
        public EServeSatisfaction satisfaction = EServeSatisfaction.Satisfied;

        public bool Evaluate(GameplayContext ctx) =>
            ctx?.Visitor != null && ctx.Visitor.Satisfaction >= satisfaction;
    }

    /// <summary>访客的需求里包含指定 tag（命中判定含祖先关系，见 TagDef.Covers）。用于「猜中需求」类的特殊台词。</summary>
    [Serializable, SubclassLabel("访客/需求包含标签")]
    public sealed class VisitorNeedsTagCondition : IGameplayCondition
    {
        public TagDef tag;

        [Tooltip("只看必要需求项（不勾则必要项与加分项都算）")]
        public bool requiredOnly;

        public bool Evaluate(GameplayContext ctx)
        {
            if (tag == null)
            {
                Debug.LogWarning("[对话条件] 需求包含标签：没有配置标签，判定为不通过");
                return false;
            }
            if (ctx?.Visitor == null) return false;
            foreach (var need in ctx.Visitor.Needs)
            {
                if (requiredOnly && !need.Required) continue;
                if (need.Tag == tag) return true;
            }
            return false;
        }
    }

    /// <summary>全局仓库里某物品的存量不低于指定数量。用于「有货才给的提交选项」。</summary>
    [Serializable, SubclassLabel("仓库/持有物品不少于")]
    public sealed class HasItemCondition : IGameplayCondition
    {
        public ItemDef item;
        public int count = 1;

        public bool Evaluate(GameplayContext ctx)
        {
            if (item == null)
            {
                Debug.LogWarning("[对话条件] 持有物品不少于：没有配置物品，判定为不通过");
                return false;
            }
            return ctx?.Cargo != null && ctx.Cargo.Get(item) >= count;
        }
    }

    /// <summary>货币不低于指定值。</summary>
    [Serializable, SubclassLabel("经济/货币不少于")]
    public sealed class CurrencyAtLeastCondition : IGameplayCondition
    {
        public int amount;

        public bool Evaluate(GameplayContext ctx) =>
            ctx?.Economy != null && ctx.Economy.Data.Currency >= amount;
    }

    /// <summary>声望不低于指定值。</summary>
    [Serializable, SubclassLabel("经济/声望不少于")]
    public sealed class ReputationAtLeastCondition : IGameplayCondition
    {
        public int amount;

        public bool Evaluate(GameplayContext ctx) =>
            ctx?.Economy != null && ctx.Economy.Data.Reputation >= amount;
    }

    /// <summary>当前是第 N 天或更晚。用于「开业几天后才解锁的寒暄」。</summary>
    [Serializable, SubclassLabel("时钟/天数不少于")]
    public sealed class DayAtLeastCondition : IGameplayCondition
    {
        public int day = 1;

        public bool Evaluate(GameplayContext ctx) =>
            ctx?.Clock != null && ctx.Clock.Data.Day >= day;
    }
}

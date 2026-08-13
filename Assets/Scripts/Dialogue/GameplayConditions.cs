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

    /// <summary>
    /// 访客所住房间里摆着他要的家具之一（需求重做说明 §6.2）——**条件类需求的验收判据**。
    ///
    /// 把它挂在【服务中交谈】对话组的验收选项上：房间里没有那件家具时选项置灰，
    /// 玩家去买/去搬来任意一件之后选项即可选，选中触发【访客/完成需求结算】。
    ///
    /// 判定走 FurniturePlacementQuery（§6.1），所以「新买的 / 从别的房间搬来的 / 房间初始就摆着的 /
    /// 摆在桌面上的」一律算数——客人只关心屋里有没有，不关心它怎么来的。
    /// </summary>
    [Serializable, SubclassLabel("访客/所住房间有需求家具")]
    public sealed class RoomHasAnyFurnitureCondition : IGameplayCondition
    {
        public bool Evaluate(GameplayContext ctx)
        {
            var visitor = ctx?.Visitor;
            // 不是条件类需求时返回 false：小游戏类的验收走小游戏分数，不该被这条误判成通过
            if (visitor == null || !(visitor.Need is ConditionNeedDef need)) return false;
            return FurniturePlacementQuery.RoomHasAny(visitor.RoomIndex, need.furnitureIds);
        }
    }

    /// <summary>
    /// 还有空客房可以分配（需求重做说明 §6.2）。挂在【初次见面】的「接待」选项上——
    /// 三间客房住满时该选项自动置灰，玩家不会点到一个必然失败的接待。
    /// </summary>
    [Serializable, SubclassLabel("访客/还有空客房")]
    public sealed class HasFreeRoomCondition : IGameplayCondition
    {
        public bool Evaluate(GameplayContext ctx) =>
            ctx?.VisitorManager != null && ctx.VisitorManager.HasFreeRoom;
    }

    // 需求包含标签 VisitorNeedsTagCondition 与 持有物品不少于 HasItemCondition
    // 已随 Item 链与 tag 需求体系退役（需求重做说明 §9.1）：
    // 需求现在是一条 NeedDef 而不是一组 tag，仓库也不再是访客服务的消费出口。
    // 「这位客人要的是不是某类东西」将来若要判定，加一条读 ctx.Visitor.Need 的条件即可。

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

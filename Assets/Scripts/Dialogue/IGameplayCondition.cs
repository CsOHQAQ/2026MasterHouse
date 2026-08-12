namespace MasterHouse
{
    /// <summary>
    /// 对话条件（设计说明 §4.2）：用于分支选项的可选性判定，以及对话组进入候选池的筛选。
    ///
    /// - 同一处的**多个条件之间默认 AND**。需要 OR/NOT 时再加 OrCondition/NotCondition 组合子类，
    ///   现在不做（§12 明确不做）。
    /// - 条件为空 = 无条件通过。分支的硬校验：**每个 Branch 至少要有一个无条件选项**，
    ///   否则条件全不满足时对话卡死（§4.3，编辑器与资产校验器都要拦）。
    /// - 与 IGameplayAction 同样受 [MovedFrom] 改名约束——改名不挂特性 = 静默清空策划配置。
    ///
    /// Evaluate 必须是**纯查询**：不得修改任何业务状态。条件会在筛选候选、渲染选项置灰等
    /// 场合被反复调用，带副作用会导致同一 tick 内多次结算。
    /// </summary>
    public interface IGameplayCondition
    {
        bool Evaluate(GameplayContext ctx);
    }
}

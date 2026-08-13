namespace MasterHouse
{
    /// <summary>
    /// 事件与条件的执行上下文（设计说明 §4.2）：持各 Manager 引用 + 当前访客实例。
    /// 字段可空——非访客场景（将来的成就/任务/家具解禁触发）留空即可，消费方自行判空。
    /// 由 DialogueManager 在每次播放前组装，播放期间只读。
    /// </summary>
    public sealed class GameplayContext
    {
        public VisitorManager VisitorManager;
        public EconomyManager Economy;
        public HouseClockManager Clock;
        // Cargo（全局仓库）与 PreviewItem（交付框里那件）已随 Item 链退役删除（需求重做说明 §9.1）：
        // 访客不再交付物品，两者都没有消费方了。

        /// <summary>
        /// 当前访客实例；非访客场景为 null。
        /// **持实例引用而不是只持 id**：被拒绝与「不对味」的访客在对话播放之前就已离场
        /// （SettleRefusal/Submit 内先请求对话再 Depart，Depart 会把实例移出在场列表），
        /// 此时 VisitorManager.Find(id) 已经返回 null，但台词仍要能说出它的名字、读它的需求。
        /// 需要「访客还在场吗」这种判定时用 IsOnStage，别拿 Visitor != null 当在场判据。
        /// </summary>
        public VisitorInstance Visitor;

        /// <summary>当前访客实例 id；无访客时为 -1。</summary>
        public int VisitorInstanceId => Visitor != null ? Visitor.InstanceId : -1;

        /// <summary>访客是否仍在在场列表里（离场后为 false，见 Visitor 字段注释）。</summary>
        public bool IsVisitorOnStage =>
            Visitor != null && VisitorManager != null && VisitorManager.Find(Visitor.InstanceId) != null;
    }
}

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
        public PlayerCargoData Cargo;

        /// <summary>
        /// 当前访客实例；非访客场景为 null。
        /// **持实例引用而不是只持 id**：被拒绝与「不对味」的访客在对话播放之前就已离场
        /// （SettleRefusal/Submit 内先请求对话再 Depart，Depart 会把实例移出在场列表），
        /// 此时 VisitorManager.Find(id) 已经返回 null，但台词仍要能说出它的名字、读它的需求。
        /// 需要「访客还在场吗」这种判定时用 IsOnStage，别拿 Visitor != null 当在场判据。
        /// </summary>
        public VisitorInstance Visitor;

        /// <summary>
        /// 「玩家正拿着的那件东西」：交付预览期间指交付框里的候选物品，其余场合为 null。
        ///
        /// 存在的理由：`{物品名}` 平时取 `Visitor.SubmittedItem`，而那一格**只有真正交付之后才有值**
        /// （VisitorManager.Submit）。预览发生在交付之前，没有这一格的话，
        /// 「这{物品名}……好像不太对」会渲染成「这这个……好像不太对」——
        /// 而引用玩家手上这件东西恰恰是预览单句最该说的内容（交付页落地说明，2026-08-12 访谈）。
        /// </summary>
        public ItemDef PreviewItem;

        /// <summary>当前访客实例 id；无访客时为 -1。</summary>
        public int VisitorInstanceId => Visitor != null ? Visitor.InstanceId : -1;

        /// <summary>访客是否仍在在场列表里（离场后为 false，见 Visitor 字段注释）。</summary>
        public bool IsVisitorOnStage =>
            Visitor != null && VisitorManager != null && VisitorManager.Find(Visitor.InstanceId) != null;
    }
}

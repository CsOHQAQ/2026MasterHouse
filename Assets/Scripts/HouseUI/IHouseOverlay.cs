namespace MasterHouse
{
    /// <summary>
    /// 叠加层（系统面板/对话层）：压在当前页面之上的可退栈单元，ESC 先弹栈再问页面。
    /// 3.5c 面板栈迁移时投入使用；接口先行以固定壳的 ESC 语义。
    /// </summary>
    public interface IHouseOverlay
    {
        /// <summary>关闭并清理自身视图（由壳在弹栈时调用）。</summary>
        void Close();

        /// <summary>
        /// ESC 语义扩展（2026-08-14）：返回 true 表示本层自己消费了这次 ESC——
        /// 比如先关掉层内的子弹窗（商店获得弹窗），壳这次就不弹栈。默认不消费。
        /// </summary>
        bool ConsumeEscape() => false;
    }
}

namespace MasterHouse
{
    /// <summary>对话接缝默认实现（§16.9）：台词直接来自内容表（VisitorDef.transactionLine，§16.6）。</summary>
    public sealed class DefDialogueService : IDialogueService
    {
        public string GetVisitorLine(VisitorDef visitor) =>
            visitor != null ? visitor.transactionLine : string.Empty;
    }
}
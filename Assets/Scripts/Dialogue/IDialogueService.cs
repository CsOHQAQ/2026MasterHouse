namespace MasterHouse
{
    /// <summary>
    /// 对话接缝（§16.9，待定 #17）：「取对话内容、走对话流程」的最小接口。
    /// 现状 = 访客事务单句台词；自研对话系统进场时替换实现（对话树/分支/演出等），
    /// 不动访客业务（VisitorManager）与对话层 UI（DialogueOverlay）。
    /// 功能要求未定前禁止扩接口（§16.6「只为已有内容建结构」同样适用于接口）。
    /// </summary>
    public interface IDialogueService
    {
        /// <summary>取访客事务对话内容（当前为单句台词）。</summary>
        string GetVisitorLine(VisitorDef visitor);
    }
}

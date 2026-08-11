namespace MasterHouse
{
    /// <summary>访客 → 对话的五个触发点（访客交付说明 §8）。</summary>
    public enum EVisitorDialogueTrigger
    {
        /// <summary>初次见面：进入「前台等待接待」后，玩家交互时。</summary>
        FirstMeeting = 0,
        /// <summary>开始等待服务：接待成功、进入「服务中」时（含程序化需求句）。</summary>
        ServiceStart = 1,
        /// <summary>被拒绝：玩家拒绝 / 等搭话超时 / 等交货超时（三者同口径）。</summary>
        Rejected = 2,
        /// <summary>完成服务：提交结算后，附带 EServeSatisfaction 作为筛选键。</summary>
        ServiceDone = 3,
        /// <summary>满意后闲逛：闲逛期间由冒泡调度器定期请求。</summary>
        WanderChat = 4,
    }

    /// <summary>
    /// 对话接缝（§16.9，待定 #17）：访客侧在状态转换点经此请求播放对话（访客交付说明 §8 的五个触发点）。
    /// 两侧各出一半、签名以对话系统交付文档为准——本接口只固定契约形状，当前默认实现仅返回单句文本，
    /// 由 UI（Toast/气泡/debug 对话层）临时展示；对话系统进场时替换实现并接管播放流程，
    /// 对话事件对业务的驱动走 VisitorManager 的公开方法（Accept/Reject/Submit）。
    /// </summary>
    public interface IDialogueService
    {
        /// <summary>请求一段访客对话（当前为单句台词）；satisfaction 仅 ServiceDone 触发点有意义。</summary>
        string RequestVisitorLine(VisitorInstance visitor, EVisitorDialogueTrigger trigger, EServeSatisfaction satisfaction);
    }
}

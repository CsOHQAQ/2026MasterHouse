namespace MasterHouse
{
    /// <summary>访客 → 对话的五个触发点（访客交付说明 §8 / 对话设计说明 §7）。</summary>
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
    /// 对话接缝（架构设计 §16.9）：访客侧在状态转换点经此请求播放对话。
    /// 保留接口而不让 VisitorManager 直接依赖 DialogueManager，是为了让访客模块不反向依赖对话模块——
    /// 两者的构造还存在循环（VisitorManager 需要本接口，DialogueManager 需要 VisitorManager），
    /// 靠 DialogueManager.Bind 的两阶段初始化解开。
    ///
    /// **fire-and-forget**（设计说明 §8）：没有返回值、没有回调。
    /// 模态对话框开启期间营业闸门关闭（局内产线停 tick、时钟停走、访客倒计时停表），
    /// 所以「等对话播完」对业务是免费的，访客侧调完就走。
    /// 协程状态无法序列化，与 §11 冲突，所以不用协程等待。
    ///
    /// 演进记录：初版是 GetVisitorLine(VisitorDef) 取单句；访客系统重做（2026-08-11）升级为本五触发点契约、
    /// 返回 string 由占位实现 DefDialogueService 供 debug 层展示；对话系统落地（2026-08-12）
    /// 改为 void 并由 DialogueManager 实现，DefDialogueService 退役。
    /// </summary>
    public interface IDialogueService
    {
        /// <summary>
        /// 请求播放一段访客对话。satisfaction 仅 ServiceDone 触发点有意义，其余触发点忽略。
        /// 内容缺失（没配对话池 / 分类为空）时打 Error 但**不阻塞业务**——缺台词不该卡死接待（§4.5）。
        /// </summary>
        void RequestVisitorDialogue(VisitorInstance visitor, EVisitorDialogueTrigger trigger,
            EServeSatisfaction satisfaction);
    }
}

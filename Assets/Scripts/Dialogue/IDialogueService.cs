namespace MasterHouse
{
    /// <summary>
    /// 对话接缝（架构设计 §16.9）：访客侧在状态转换点经此请求播放对话。
    /// 保留接口而不让 VisitorManager 直接依赖 DialogueManager，是为了让访客模块不反向依赖对话模块——
    /// 两者的构造还存在循环（VisitorManager 需要本接口，DialogueManager 需要 VisitorManager），
    /// 靠 DialogueManager.Bind 的两阶段初始化解开。
    ///
    /// **fire-and-forget**：没有返回值、没有回调。模态对话框开启期间营业闸门关闭
    /// （时钟停走、访客各类倒计时停表），所以「等对话播完」对业务是免费的，访客侧调完就走。
    /// 协程状态无法序列化，与 §11 冲突，所以不用协程等待。
    ///
    /// 演进记录：初版 GetVisitorLine(VisitorDef) 取单句 → 访客系统重做升级为五触发点契约 →
    /// 对话系统落地改为 void 由 DialogueManager 实现 → 2026-08-14 对话资源重构，
    /// 触发点枚举 EVisitorDialogueTrigger 与满意度筛选键合并为一维的 EDialogueCategory（八分类）。
    /// </summary>
    public interface IDialogueService
    {
        /// <summary>
        /// 请求播放一段访客对话。
        /// 内容缺失（种族没配 / 分类为空 / 条件全不满足）时打 Error 但**不阻塞业务**——
        /// 缺台词不该卡死接待，业务照常往下走。
        /// </summary>
        void RequestVisitorDialogue(VisitorInstance visitor, EDialogueCategory category);
    }
}

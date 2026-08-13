using System;

namespace MasterHouse
{
    /// <summary>
    /// 小游戏与宿主之间的**全部**契约（小游戏说明 §3.2）。
    ///
    /// 依赖方向是这套框架的立身之本（§3.1）：
    /// <code>
    ///   Visitor / Dialogue      ←─ 不认识小游戏
    ///         │ StartMinigameAction（对话分支事件）
    ///         ▼
    ///   MinigameOverlay（宿主）  ─→ 认识 IMinigame 与 MinigameDef
    ///         │ Launch(level, onFinish, onAbort)
    ///         ▼
    ///   具体小游戏（Circuit…）   ←─ **不认识任何 Manager**
    /// </code>
    ///
    /// 具体小游戏只认识两样东西：本接口，和自己的关卡类型。
    /// 它不引用 GameManager / VisitorManager / EconomyManager / HouseClockManager / 家具 / 经济中的任何一个
    /// ——这条是硬约束，改完全文检索一遍 Minigame/ 目录。
    ///
    /// 时间也自治（§3.3）：小游戏期间营业闸门是关的，跟着全局 tick 走会被一起冻住。
    /// 所以小游戏整体属 View 层豁免区，允许 Time.deltaTime、允许无种子 Random。
    /// **唯一的例外是关卡抽取**（§3.5），那个必须确定性，且由宿主负责、轮不到小游戏操心。
    /// </summary>
    public interface IMinigame
    {
        /// <summary>
        /// 启动一局。level 由宿主抽好，实现方按自己的关卡类型强转。
        /// <para>结束时**必须调且只调一次** onFinish 或 onAbort：</para>
        /// <list type="bullet">
        /// <item>onFinish(score)：score 为 0~100 的 int，各小游戏自己定义怎么算。宿主据此定满意度档位并结算。</item>
        /// <item>onAbort()：玩家中途退出，**不结算**——访客保持「服务中」，可以再点开重玩。</item>
        /// </list>
        /// </summary>
        void Launch(MinigameLevelDef level, Action<int> onFinish, Action onAbort);
    }
}

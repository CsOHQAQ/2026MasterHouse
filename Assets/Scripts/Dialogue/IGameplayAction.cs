using System;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 对话事件（设计说明 §4.2）：由对话组的 Action 步骤或分支选项触发的一次性业务动作。
    ///
    /// 具体子类是普通 class + [Serializable]，靠 [SerializeReference] 多态序列化，因此
    /// **不受「MonoBehaviour/ScriptableObject 必须独占同名文件」那条约束**（那条只针对 Unity 对象），
    /// 可按分类合并进少数几个文件（见 GameplayActions.cs）。
    ///
    /// ⚠️ 高危：子类**改名或改命名空间必须挂 [MovedFrom]**（UnityEngine.Scripting.APIUpdating），
    ///    否则策划已配的数据会被 Unity 静默清空——[SerializeReference] 按「程序集 + 类型全名」寻址，
    ///    找不到就丢，且不报错。这是本方案唯一的高危操作。
    ///
    /// 两条铁律（§5.3）：
    ///   ① 事件只做**一次性的状态转换与结算**，绝不承担「必须发生的后续推进」。
    ///      例如拒绝事件只把访客状态置为已拒绝，离场由访客状态机在 tick 里自己走完；
    ///      绝不能把「离场」挂在对话末尾——玩家一按 ESC 访客就永远卡在场上。
    ///      ExecuteOnInterrupt 是补丁，状态机自洽才是根本。
    ///   ② 奖励类事件只允许放在**对话组末尾或分支选项**上。中途给奖励 + 玩家 ESC = 反复领取。
    ///      （编辑器给提示性校验，放错位置警告但不阻断。）
    /// </summary>
    public interface IGameplayAction
    {
        /// <summary>
        /// 玩家 ESC 中断对话时，尚未执行到的本事件是否仍要补执行（§5.2）。
        /// 已经执行过的事件一律不回滚（既成事实）。
        /// </summary>
        bool ExecuteOnInterrupt { get; }

        void Execute(GameplayContext ctx);
    }

    /// <summary>
    /// 奖励类事件的标记接口（无成员）。
    /// 存在的唯一理由是让资产校验器能识别「这是发东西的事件」，从而执行 §5.3 铁律②的位置检查：
    /// **奖励只允许放在对话组末尾或分支选项上**——中途给奖励 + 玩家 ESC = 反复领取。
    /// 新增会给玩家好处的事件时记得挂上它，否则校验器查不出摆放事故。
    /// </summary>
    public interface IRewardAction
    {
    }

    /// <summary>
    /// 事件基类：收纳所有子类共用的「中断补执行」开关。
    /// 做成序列化字段而非各子类硬编码常量，是因为同一个事件**放在组末尾和放在中途语义不同**——
    /// 该由策划按摆放位置逐个决定，而不是由类型一刀切。
    /// </summary>
    [Serializable]
    public abstract class GameplayActionBase : IGameplayAction
    {
        [SerializeField]
        [Tooltip("玩家 ESC 中断对话时，若本事件尚未执行到，是否仍要补执行（§5.2）。\n" +
                 "默认关——中断的语义是「这段对话没有发生过」。\n" +
                 "只在「不补执行会让业务卡死」时才勾；正确做法优先是让状态机自洽。")]
        private bool executeOnInterrupt;

        public bool ExecuteOnInterrupt => executeOnInterrupt;

        public abstract void Execute(GameplayContext ctx);
    }
}

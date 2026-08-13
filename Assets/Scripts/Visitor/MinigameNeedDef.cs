using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 小游戏类需求（访客需求重做说明 §4.1/§7）：玩一局小游戏，按分数定满意度。
    ///
    /// **本包只建结构**：小游戏框架尚未设计（另开专题），所以这里除了基类的 description 之外一个字段没有，
    /// 入口事件 StartMinigameAction 也只是 LogWarning + Toast 提示未接入、不改变任何业务状态。
    /// 小游戏引用与「分数 → 满意度」的阈值配置等小游戏框架定案后再补
    /// ——**不要**为它预建接口、注册表、Def 基类或任何抽象（§15.3「不预设抽象、不建没有调用方的接缝」）。
    ///
    /// 过渡期后果是明示的：小游戏类需求的访客只能走「拒绝」或「等交货超时」离场，验收时不算 bug（§7）。
    /// 四档 EServeSatisfaction 与四个【完成服务·档位】对话组全部保留不动，就是留给它的（§6.3）。
    ///
    /// 【务必独占本文件】ScriptableObject 必须与文件同名，否则 .asset 的脚本引用会损坏（见 RETRO）。
    /// </summary>
    [CreateAssetMenu(menuName = "MasterHouse/访客需求·小游戏类", fileName = "Need_")]
    public sealed class MinigameNeedDef : NeedDef
    {
        public override ENeedType NeedType => ENeedType.Minigame;
    }
}

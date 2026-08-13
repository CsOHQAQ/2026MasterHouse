using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 小游戏类需求（访客需求重做说明 §4.1/§7）：玩一局小游戏，按分数定满意度。
    ///
    /// 需求重做那一包只建了空结构、入口事件是占位；**小游戏框架落地第 3 步已兑现**：
    /// 这里补上 minigame 引用，StartMinigameAction 接通 MinigameOverlay。
    /// 「分数 → 满意度」的四档阈值不在这里配——它属于小游戏本身而不是某一条需求，
    /// 所以放在 MinigameDef 上（小游戏说明 §3.6），换难度就换一个 MinigameDef。
    ///
    /// 【务必独占本文件】ScriptableObject 必须与文件同名，否则 .asset 的脚本引用会损坏（见 RETRO）。
    /// </summary>
    [CreateAssetMenu(menuName = "MasterHouse/访客需求·小游戏类", fileName = "Need_")]
    public sealed class MinigameNeedDef : NeedDef
    {
        public override ENeedType NeedType => ENeedType.Minigame;

        [Tooltip("要玩哪个小游戏。关卡由它的关卡池按「runSeed + 访客实例 + 本需求」抽（§3.5），" +
                 "同一位访客反复进出恒定抽到同一张")]
        public MinigameDef minigame;
    }
}

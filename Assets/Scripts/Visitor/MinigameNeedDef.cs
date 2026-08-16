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
    /// **关卡则相反，是逐条需求的事**（2026-08-16 加 level 字段）：修理电路的关卡是手工设计的题面，
    /// 哪条需求打哪一关必须配死，不能交给关卡池随机；而制作咖啡的关卡只是一组手感参数，随机无所谓。
    /// 所以 level 是**可选**的——填了就打它，留空才回落 §8.4 的确定性抽取。
    ///
    /// 【务必独占本文件】ScriptableObject 必须与文件同名，否则 .asset 的脚本引用会损坏（见 RETRO）。
    /// </summary>
    [CreateAssetMenu(menuName = "MasterHouse/访客需求·小游戏类", fileName = "Need_")]
    public sealed class MinigameNeedDef : NeedDef
    {
        public override ENeedType NeedType => ENeedType.Minigame;

        [Tooltip("要玩哪个小游戏")]
        public MinigameDef minigame;

        [Tooltip("指定关卡（可选，2026-08-16 加）。留空 = 从上面这个小游戏的关卡池按" +
                 "「runSeed + 访客实例 + 本需求」确定性抽取（§8.4），同一位访客反复进出恒定抽到同一张。\n" +
                 "修理电路的关卡是手工设计的题面，一般在这里逐条点名；" +
                 "制作咖啡的关卡只是一组手感参数，留空随机即可。\n" +
                 "点名的关卡**不要求**在关卡池里，直接拖资产即可。")]
        public MinigameLevelDef level;
    }
}

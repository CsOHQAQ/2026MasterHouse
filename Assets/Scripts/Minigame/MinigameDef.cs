using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 一个小游戏的定义（小游戏说明 §3.6）：挂哪个 Prefab、有哪些关卡、分数怎么定档。
    ///
    /// **难度不做单独抽象**：要区分难度就多做一个 MinigameDef（同一个 prefab、不同关卡池）。
    /// 这是用资产模拟档位，符合架构 §15.3「不预设抽象」。
    ///
    /// **关卡池从「唯一来源」降级为「兜底来源」**（2026-08-16）：需求侧可以点名关卡
    /// （<see cref="MinigameNeedDef"/>.level），点了就打那一张、连池都不查。
    /// 修理电路那种手工设计题面的小游戏因此可以把池留空，一关挂一条需求。
    ///
    /// 【务必独占本文件】ScriptableObject 必须与文件同名，否则 .asset 的脚本引用会损坏（见 RETRO）。
    /// </summary>
    [CreateAssetMenu(menuName = "MasterHouse/小游戏定义", fileName = "Minigame_")]
    public sealed class MinigameDef : ScriptableObject
    {
        [Tooltip("稳定键（日志/存档/编辑器索引用）")]
        public string minigameId;

        [Tooltip("显示名，如「修理电路」")]
        public string displayName;

        [Tooltip("小游戏 Prefab，根节点须挂实现 IMinigame 的 MonoBehaviour。\n" +
                 "**强类型引用而不是 Resources 路径字符串**（待确认 #2）：拖拽即可、改名不断、Unity 自动处理依赖")]
        public GameObject prefab;

        [Tooltip("关卡池：**需求没有点名关卡时**的兜底来源（2026-08-16 起降级，见类注释）。\n" +
                 "同一位访客反复进出恒定抽到同一张（§3.5），「重开」是磨同一关而不是刷关卡。\n" +
                 "关卡是手工设计题面的（修理电路）由需求逐条点名，这里可以留空；" +
                 "关卡只是一组手感参数的（制作咖啡）留在池里随机即可")]
        public List<MinigameLevelDef> levels = new List<MinigameLevelDef>();

        [Header("分数 → 满意度（升序阈值，含下界；待确认 #1，策划实测调）")]
        [Tooltip("≥ 此值为「一般」，低于则「不对味」")]
        public int plainMin = 1;

        [Tooltip("≥ 此值为「满意」")]
        public int satisfiedMin = 60;

        [Tooltip("≥ 此值为「完美」")]
        public int perfectMin = 100;

        /// <summary>日志用标识：优先 minigameId，未填时回落资产名。</summary>
        public string DisplayId => string.IsNullOrEmpty(minigameId) ? name : minigameId;

        /// <summary>展示名：未填时回落资产名。</summary>
        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;

        /// <summary>
        /// 分数定档（§3.4）。阈值含下界、从高往低比，配反了也不会漏档。
        /// </summary>
        public EServeSatisfaction Evaluate(int score)
        {
            if (score >= perfectMin) return EServeSatisfaction.Perfect;
            if (score >= satisfiedMin) return EServeSatisfaction.Satisfied;
            if (score >= plainMin) return EServeSatisfaction.Plain;
            return EServeSatisfaction.Mismatch;
        }

        /// <summary>
        /// 抽关卡（§3.5）：<c>池[ Hash(runSeed, 访客实例Id, 需求Id) % 池长度 ]</c>。
        /// **只在需求没点名关卡时走这条路**（2026-08-16），调用方是 MinigameOverlay.Open。
        ///
        /// 同一位访客反复进出**恒定抽到同一张**——否则理性玩家会反复退出直到抽到最好做的那张。
        /// 与项目现有的对话组选取是同一套派生种子思路，读档也不刷。
        /// 池为空返回 null（调用方报错，不静默开一局空关卡）。
        /// </summary>
        public MinigameLevelDef PickLevel(long runSeed, int visitorInstanceId, string needId)
        {
            if (levels == null || levels.Count == 0) return null;

            var hash = DeterministicRng.Hash(runSeed, visitorInstanceId, DeterministicRng.HashString(needId));
            // 取模前转无符号：Hash 返回的 long 可能为负，直接取模会得到负下标
            var index = (int)((ulong)hash % (ulong)levels.Count);
            return levels[index];
        }
    }
}

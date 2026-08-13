using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 「修理电路」的一关（小游戏说明 §4）。继承 MinigameLevelDef 以进关卡池（§3.2）。
    ///
    /// 题面 = 画布形状 + 预置的电源与电池；玩家能做的只有画导线与摆中转件。
    /// 难度靠两个预算旋钮调：导线总格数 MaxLinkCells、各中转件的 BuildableNodes.MaxCount（§4.3）。
    ///
    /// 类名保留不改（§5.5 待确认 #5）：目录已经在 Minigame/Circuit/ 下，叫 LevelDef 不歧义。
    /// </summary>
    [CreateAssetMenu(fileName = "关卡", menuName = "MasterHouse/修理电路关卡", order = 30)]
    public class LevelDef : MinigameLevelDef
    {
        [Tooltip("画布形状——内联 GridGroup，可以是非矩形")]
        public GridGroup Canvas = new GridGroup();

        [Tooltip("预置节点：电源与电池靠它预置，是不可移动不可删除的题面")]
        public List<PresetNodeEntry> PresetNodes = new List<PresetNodeEntry>();

        [Tooltip("本关可摆的中转件与各自数量上限：难度的主要调节旋钮之一（§4.3）")]
        public List<BuildableNodeEntry> BuildableNodes = new List<BuildableNodeEntry>();

        [Tooltip("导线总格数上限，0 = 不限（§4.3）。口径 = Σ 每条线的途径格数；" +
                 "描格时预算耗尽即停住不延伸，与撞墙同一手感；删线退还")]
        public int MaxLinkCells;
    }

    [Serializable]
    public class PresetNodeEntry
    {
        public NodeDef Node;

        [Tooltip("放置格：画布坐标")]
        public Vector2Int Cell;

        public bool CanMove;
        public bool CanDelete;
    }

    [Serializable]
    public class BuildableNodeEntry
    {
        public NodeDef Node;
        public int MaxCount = 1;
    }
}

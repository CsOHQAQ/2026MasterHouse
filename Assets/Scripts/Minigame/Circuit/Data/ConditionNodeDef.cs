using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>条件节点的一条需求：窗口内收到的量达标即视为该条满足。</summary>
    [Serializable]
    public class ConditionEntry
    {
        [Tooltip("需求物资（水、电这类持续供给的资源）")]
        public ItemDef Item;

        [Tooltip("滑动窗口内需累计收到的量")]
        public int RequiredAmount = 1;

        [Tooltip("滑动窗口长度（tick）：只统计最近这么多 tick 内的到货")]
        public int WindowTicks = 100;
    }

    /// <summary>
    /// 条件型节点（§7）：检查上游供给速率是否达标，是「家具是否修好」的判据。
    /// 收到的物资即刻蒸发——无暂存、无限吸收，因此上游永不背压（有意设计）。
    /// 这是全游戏物资守恒的第二个明示例外（第一个是 §6.2 类型不兼容清空）。
    /// 不可被玩家删除、不可由玩家摆放，只能由策划在 LevelDef.PresetNodes 中预置。
    /// </summary>
    [CreateAssetMenu(fileName = "条件节点", menuName = "MasterHouse/节点/条件型", order = 24)]
    public class ConditionNodeDef : NodeDef
    {
        public override ENodeType NodeType => ENodeType.Condition;

        [Tooltip("需求列表：多条之间为「全部满足」才算本节点达标；留空 = 恒达标")]
        public List<ConditionEntry> Conditions = new List<ConditionEntry>();
    }
}

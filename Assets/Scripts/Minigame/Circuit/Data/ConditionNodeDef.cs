using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>电池的一条点亮条件：收到的电量与 RequiredAmount 的关系（小游戏说明 §4.2）。</summary>
    [Serializable]
    public class ConditionEntry
    {
        [Tooltip("要求收到的电量")]
        public int RequiredAmount = 1;

        [Tooltip("收多了算不算数：\n" +
                 "  勾选 = 收到量 ≥ 要求即点亮（玩法是「尽量多连」）\n" +
                 "  不勾 = 必须刚好等于，多了也不亮（玩法是「精确分配」，核心难度旋钮）")]
        public bool AllowExcess;
    }

    /// <summary>
    /// 电池（小游戏说明 §4.2）：可配多个输入 Pin，收到的电量 = 各输入 Pin 上链接携带电量之和。
    /// 多输入口让玩家不必靠合流器就能凑数——合流器因此是省线的优化件，不是通关必需品。
    ///
    /// Conditions 保留为列表（落地访谈 C 项拍板）：多条之间为「全部满足」才点亮，
    /// 实际关卡配一条即可；留列表是给将来"多种电"之类的扩展留位。
    /// 电池是题面，只能由策划在 LevelDef.PresetNodes 中预置，玩家不可摆放/删除。
    /// </summary>
    [CreateAssetMenu(fileName = "电池", menuName = "MasterHouse/节点/电池", order = 24)]
    public class ConditionNodeDef : NodeDef
    {
        public override ENodeType NodeType => ENodeType.Condition;

        [Tooltip("点亮条件：多条之间为「全部满足」；留空 = 恒点亮")]
        public List<ConditionEntry> Conditions = new List<ConditionEntry>();
    }
}

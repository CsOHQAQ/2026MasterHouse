using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 节点运行时数据（§10）。只能由 Manager 修改；View 只读（§2）。
    /// </summary>
    public class NodeData
    {
        /// <summary>稳定自增 Id，一切节点遍历的排序键（§11.2）。随存档序列化。</summary>
        public readonly long NodeId;

        public readonly NodeDef Def;

        /// <summary>放置的全局格坐标（节点形状原点）。</summary>
        public Vector2Int Origin;

        /// <summary>预置节点约束（§8.1）；玩家自建节点均为 true。</summary>
        public bool CanMove = true;
        public bool CanDelete = true;

        /// <summary>非法临时态标记（§4.3）：冻结不参与模拟；存在时禁止存档。</summary>
        public bool IsIllegal;

        /// <summary>输入暂存——仅加工型使用，其余类型为 null。</summary>
        public readonly ItemStorage InputStorage;

        /// <summary>输出暂存——资源/加工的产出；中转型的内部暂存（容量待定 #6）。仓库型为 null（漏斗 §7）。</summary>
        public readonly ItemStorage OutputStorage;

        /// <summary>条件型：各条需求的滑动窗口状态；其余类型为 null。</summary>
        public readonly ConditionState ConditionState;

        /// <summary>资源型：生产计时（tick）。</summary>
        public int ProductionCounter;

        /// <summary>加工型：当前是否有一批配方在加工。</summary>
        public bool RecipeInProgress;

        /// <summary>加工型：当前批次已推进的 tick 数。</summary>
        public int RecipeProgressTicks;

        /// <summary>按节点内索引排列（稳定顺序）。</summary>
        public readonly List<PinData> Pins = new List<PinData>();

        public NodeData(long nodeId, NodeDef def, Vector2Int origin, bool canMove, bool canDelete)
        {
            NodeId = nodeId;
            Def = def;
            Origin = origin;
            CanMove = canMove;
            CanDelete = canDelete;

            switch (def.NodeType)
            {
                case ENodeType.Resource:
                    OutputStorage = new ItemStorage(((ResourceNodeDef)def).StorageCap);
                    break;
                case ENodeType.Processor:
                    var processorDef = (ProcessorNodeDef)def;
                    InputStorage = new ItemStorage(processorDef.InputStorageCapPerItem);
                    OutputStorage = new ItemStorage(processorDef.OutputStorageCapPerItem);
                    break;
                case ENodeType.Storage:
                    // 仓库是漏斗不是容器（§7）：无暂存，投递直接进 PlayerCargo
                    break;
                case ENodeType.Transit:
                    // 待定 #6：中转暂存容量暂按小容量
                    OutputStorage = new ItemStorage(((TransitNodeDef)def).StorageCapPerItem);
                    break;
                case ENodeType.Condition:
                    // 无暂存：收到即蒸发，只记窗口到货
                    ConditionState = new ConditionState((ConditionNodeDef)def);
                    break;
            }

            for (int i = 0; i < def.Pins.Count; i++)
                Pins.Add(new PinData(this, i));
        }

        /// <summary>Pin 的外侧接线格（全局坐标）：链接端点落在这一格上。</summary>
        public Vector2Int GetPinPortCell(int pinIndex)
        {
            var layout = Def.Pins[pinIndex];
            return Origin + layout.LocalCell + Direction4.ToOffset(layout.Facing);
        }
    }
}
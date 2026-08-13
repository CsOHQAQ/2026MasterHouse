using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 节点运行时数据。只能由 Manager 与 CircuitSolver 修改；View 只读。
    /// </summary>
    public class NodeData
    {
        /// <summary>稳定自增 Id，一切节点遍历的排序键。</summary>
        public readonly long NodeId;

        public readonly NodeDef Def;

        /// <summary>放置的画布格坐标（节点形状原点）。</summary>
        public Vector2Int Origin;

        /// <summary>预置节点约束；玩家自摆的中转件均为 true。</summary>
        public bool CanMove = true;
        public bool CanDelete = true;

        /// <summary>按节点内索引排列（稳定顺序）。</summary>
        public readonly List<PinData> Pins = new List<PinData>();

        // ── 以下两个字段由 CircuitSolver 每次求解重写，供 View 读取（电池专用） ──

        /// <summary>电池：各输入 Pin 收到的电量之和。</summary>
        public int ReceivedPower;

        /// <summary>电池：本轮是否点亮。非电池节点恒 false。</summary>
        public bool IsLit;

        public NodeData(long nodeId, NodeDef def, Vector2Int origin, bool canMove, bool canDelete)
        {
            NodeId = nodeId;
            Def = def;
            Origin = origin;
            CanMove = canMove;
            CanDelete = canDelete;

            for (int i = 0; i < def.Pins.Count; i++)
                Pins.Add(new PinData(this, i));
        }

        /// <summary>Pin 的外侧接线格（画布坐标）：链接端点落在这一格上。</summary>
        public Vector2Int GetPinPortCell(int pinIndex)
        {
            var layout = Def.Pins[pinIndex];
            return Origin + layout.LocalCell + Direction4.ToOffset(layout.Facing);
        }
    }
}

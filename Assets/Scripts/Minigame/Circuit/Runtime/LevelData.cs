using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>格子占用者：节点或链接，二者其一非空。</summary>
    public class GridOccupant
    {
        public readonly NodeData Node;
        public readonly LinkData Link;

        public GridOccupant(NodeData node)
        {
            Node = node;
        }

        public GridOccupant(LinkData link)
        {
            Link = link;
        }
    }

    /// <summary>
    /// 一局「修理电路」的运行时数据。只能由 Manager 修改。
    ///
    /// **不存档**（小游戏说明 §2）：每次打开都由 LevelManager.BuildLevel 重新造一份，
    /// 放弃即销毁、再进重置。所以这里没有 tick 计数、没有常驻标记、没有任何跨局状态。
    /// </summary>
    public class LevelData
    {
        public readonly LevelDef Def;

        /// <summary>按 NodeId 升序维护（创建即追加，NodeId 自增，天然有序）。</summary>
        public readonly List<NodeData> Nodes = new List<NodeData>();

        /// <summary>按 LinkId 升序维护。</summary>
        public readonly List<LinkData> Links = new List<LinkData>();

        public long NextNodeId;
        public long NextLinkId;

        /// <summary>占用索引：画布格坐标 → 占用者（节点与导线共用）。仅做键查询，禁止枚举遍历（§11.2）。</summary>
        private readonly Dictionary<Vector2Int, GridOccupant> occupancy =
            new Dictionary<Vector2Int, GridOccupant>();

        /// <summary>画布格集合，构造时由 Canvas 展开。仅做成员查询，禁止枚举遍历（§11.2）。</summary>
        private readonly HashSet<Vector2Int> canvasCells = new HashSet<Vector2Int>();

        public LevelData(LevelDef def)
        {
            Def = def;
            foreach (var cell in def.Canvas.CellsAt(Vector2Int.zero))
                canvasCells.Add(cell);
        }

        public bool IsInCanvas(Vector2Int cell) => canvasCells.Contains(cell);

        public bool IsOccupied(Vector2Int cell) => occupancy.ContainsKey(cell);

        public GridOccupant GetOccupant(Vector2Int cell)
        {
            occupancy.TryGetValue(cell, out var occupant);
            return occupant;
        }

        // ── 导线预算（§4.3）──

        /// <summary>已用导线格数：Σ 每条线的途径格数。删线后自然回落，预算即退还。</summary>
        public int UsedLinkCells
        {
            get
            {
                int total = 0;
                for (int i = 0; i < Links.Count; i++)
                    total += Links[i].PathCells.Count;
                return total;
            }
        }

        /// <summary>导线格数上限；0 = 不限。</summary>
        public int LinkCellBudget => Def.MaxLinkCells;

        /// <summary>还能再画几格；不限时返回 int.MaxValue。</summary>
        public int RemainingLinkCells =>
            LinkCellBudget <= 0 ? int.MaxValue : Mathf.Max(0, LinkCellBudget - UsedLinkCells);

        // ── 占用登记：仅供 Manager 调用 ──

        public void OccupyNode(NodeData node)
        {
            foreach (var cell in node.Def.Shape.CellsAt(node.Origin))
                occupancy[cell] = new GridOccupant(node);
        }

        public void ReleaseNode(NodeData node)
        {
            foreach (var cell in node.Def.Shape.CellsAt(node.Origin))
                occupancy.Remove(cell);
        }

        public void OccupyLink(LinkData link)
        {
            foreach (var cell in link.PathCells)
                occupancy[cell] = new GridOccupant(link);
        }

        public void ReleaseLink(LinkData link)
        {
            foreach (var cell in link.PathCells)
                occupancy.Remove(cell);
        }
    }
}

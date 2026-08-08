using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>格子占用者：节点或链接，二者其一非空（§10 占用索引的值）。</summary>
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
    /// 小关运行时数据（§10）。只能由 Manager 修改。
    /// 存档 = 数据类全量序列化（§11.5，存档系统本身待定 #9）。
    /// </summary>
    public class LevelData
    {
        public readonly LevelDef Def;

        /// <summary>本关逻辑 tick 计数（§3.1）。随存档序列化（§11.5）。</summary>
        public long TickCount;

        /// <summary>按 NodeId 升序维护（创建即追加，NodeId 自增，天然有序 §11.2）。</summary>
        public readonly List<NodeData> Nodes = new List<NodeData>();

        /// <summary>按 LinkId 升序维护（§11.2）。</summary>
        public readonly List<LinkData> Links = new List<LinkData>();

        /// <summary>Id 计数器，随存档序列化（LinkId 计数器为 §11.5 明确必含项）。</summary>
        public long NextNodeId;
        public long NextLinkId;

        public bool IsLoaded;

        // 待定 #7：Unload 期间的稳态净产出表，结构随算法定案，先占位
        // public List<ItemStack> SteadyStateNetOutputPerTick;

        /// <summary>占用索引（§10）：全局格坐标 → 占用者（节点与连线共用）。仅做键查询，禁止枚举遍历（§11.2）。</summary>
        private readonly Dictionary<Vector2Int, GridOccupant> occupancy =
            new Dictionary<Vector2Int, GridOccupant>();

        /// <summary>画布格集合，Load 时由 Canvas 展开。仅做成员查询，禁止枚举遍历（§11.2）。</summary>
        private readonly HashSet<Vector2Int> canvasCells = new HashSet<Vector2Int>();

        public LevelData(LevelDef def)
        {
            Def = def;
            foreach (var cell in def.Canvas.CellsAt(def.WorldOrigin))
                canvasCells.Add(cell);
        }

        public bool IsInCanvas(Vector2Int cell) => canvasCells.Contains(cell);

        public bool IsOccupied(Vector2Int cell) => occupancy.ContainsKey(cell);

        public GridOccupant GetOccupant(Vector2Int cell)
        {
            occupancy.TryGetValue(cell, out var occupant);
            return occupant;
        }

        // ── 占用登记：仅供 Manager 调用（§2）──

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

        /// <summary>
        /// 是否存在非法态对象（非法临时态节点 / 断线链接）。
        /// 存在时禁止存档（§4.3、§11.6）；UI 提示交互待定 #14。
        /// </summary>
        public bool HasIllegalObjects()
        {
            foreach (var node in Nodes)
                if (node.IsIllegal)
                    return true;
            foreach (var link in Links)
                if (link.State == ELinkState.Broken)
                    return true;
            return false;
        }
    }
}
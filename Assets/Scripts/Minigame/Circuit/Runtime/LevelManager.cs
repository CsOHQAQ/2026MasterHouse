using System;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 一局「修理电路」的构建与节点增删改。
    ///
    /// **不再有 tick**：三阶段流水线、家具效果产出、条件节点滑动窗口全部随物资链删除，
    /// 供电改由 CircuitSolver 一次求解（§4.2）。也**不再有常驻关卡列表**：
    /// 小游戏无存档、放弃即重置（§2/§4.5），每次打开都 BuildLevel 造一份新的，关掉就丢。
    ///
    /// 每个改动布局的公开方法都在收尾时重算一次供电再广播事件——
    /// 订阅方读到的一定是算好的结果，不必自己记得调 Solve。
    /// </summary>
    public class LevelManager
    {
        private readonly LinkManager linkManager;

        /// <summary>玩家摆下中转件。</summary>
        public event Action<LevelData, NodeData> OnNodePlaced;

        /// <summary>中转件删除完成（附着导线已先行删除并各自广播 OnLinkDeleted）。</summary>
        public event Action<LevelData, NodeData> OnNodeRemoved;

        /// <summary>中转件移动完成（附着导线已删除）。</summary>
        public event Action<LevelData, NodeData> OnNodeMoved;

        public LevelManager(LinkManager linkManager)
        {
            this.linkManager = linkManager;
        }

        // ───────────────── 开局 ─────────────────

        /// <summary>
        /// 按 LevelDef 造一局：铺预置的电源与电池（题面），算一次初始供电。
        /// 每次打开小游戏调一次，返回的对象用完即弃。
        /// </summary>
        public LevelData BuildLevel(LevelDef def)
        {
            var level = new LevelData(def);

            foreach (var preset in def.PresetNodes)
            {
                if (preset.Node == null) continue;
                if (!CanPlaceNode(level, preset.Node, preset.Cell))
                {
                    Debug.LogError($"[修理电路] 关卡 {def.name} 的预置节点 {preset.Node.name} " +
                                   $"在 {preset.Cell} 放置非法（越界或与其他预置件重叠），已跳过");
                    continue;
                }
                PlaceNodeCore(level, preset.Node, preset.Cell, preset.CanMove, preset.CanDelete);
            }

            CircuitSolver.Solve(level);
            return level;
        }

        // ───────────────── 摆放资格 ─────────────────

        /// <summary>合法性：每个占格 ∈ 画布 ∧ 未被占用。逐格查询，不假设矩形。</summary>
        public bool CanPlaceNode(LevelData level, NodeDef def, Vector2Int origin)
        {
            foreach (var cell in def.Shape.CellsAt(origin))
                if (!level.IsInCanvas(cell) || level.IsOccupied(cell))
                    return false;
            return true;
        }

        /// <summary>
        /// 移动落点的合法性：与 CanPlaceNode 同口径，但**忽略两类占用**——
        /// 本节点自己现在占的格，以及挂在本节点上、即将随移动一并删除的导线所占的格。
        /// 不忽略的话，把件往旁边挪一格会被自己的线挡住。
        /// </summary>
        public bool CanMoveNodeTo(LevelData level, NodeData node, Vector2Int newOrigin)
        {
            foreach (var cell in node.Def.Shape.CellsAt(newOrigin))
            {
                if (!level.IsInCanvas(cell)) return false;
                var occupant = level.GetOccupant(cell);
                if (occupant == null) continue;
                if (occupant.Node == node) continue;
                if (occupant.Link != null && IsAttachedTo(occupant.Link, node)) continue;
                return false;
            }
            return true;
        }

        private static bool IsAttachedTo(LinkData link, NodeData node) =>
            link.FromPin.Owner == node || link.ToPin.Owner == node;

        /// <summary>本局同类节点计数（BuildableNodes 上限校验用）。</summary>
        public int CountNodesOf(LevelData level, NodeDef def)
        {
            int count = 0;
            foreach (var node in level.Nodes)
                if (node.Def == def)
                    count++;
            return count;
        }

        /// <summary>
        /// 建造资格（§4.3）：只有中转件可由玩家摆放，且受本关 BuildableNodes 的数量上限约束。
        /// 电源与电池是题面，按类型硬拦。
        /// </summary>
        public bool CanBuild(LevelData level, NodeDef def)
        {
            if (def == null || def.NodeType != ENodeType.Transit) return false;

            foreach (var entry in level.Def.BuildableNodes)
                if (entry.Node == def)
                    return CountNodesOf(level, def) < entry.MaxCount;
            return false; // 不在本关可建列表中
        }

        /// <summary>本关某种中转件还能摆几个（UI 的「中转件 2/3」用）。</summary>
        public int RemainingBuildCount(LevelData level, NodeDef def)
        {
            foreach (var entry in level.Def.BuildableNodes)
                if (entry.Node == def)
                    return Mathf.Max(0, entry.MaxCount - CountNodesOf(level, def));
            return 0;
        }

        // ───────────────── 增删改 ─────────────────

        public NodeData PlaceNode(LevelData level, NodeDef def, Vector2Int origin,
            bool canMove = true, bool canDelete = true)
        {
            var node = PlaceNodeCore(level, def, origin, canMove, canDelete);
            CircuitSolver.Solve(level);
            OnNodePlaced?.Invoke(level, node);
            return node;
        }

        /// <summary>纯修改不广播不求解：开局铺预置节点走这里（BuildLevel 收尾统一算一次）。</summary>
        private static NodeData PlaceNodeCore(LevelData level, NodeDef def, Vector2Int origin,
            bool canMove, bool canDelete)
        {
            var node = new NodeData(level.NextNodeId++, def, origin, canMove, canDelete);
            level.Nodes.Add(node);
            level.OccupyNode(node);
            return node;
        }

        /// <summary>删除中转件，附着导线一并删除（预算随之退还）。电源/电池按类型硬拦。</summary>
        public bool RemoveNode(LevelData level, NodeData node)
        {
            if (node == null) return false;
            if (node.Def.NodeType != ENodeType.Transit) return false; // 题面不可删
            if (!node.CanDelete) return false;

            linkManager.DeleteLinksOf(level, node);
            level.ReleaseNode(node);
            level.Nodes.Remove(node);

            CircuitSolver.Solve(level);
            OnNodeRemoved?.Invoke(level, node);
            return true;
        }

        /// <summary>
        /// 移动中转件。**附着导线直接删除并退还预算**（落地访谈拍板）——
        /// 不保留"断线态"：小游戏可无限重画，留个半死不活的对象既要渲染又要解释，不划算。
        /// 落点非法时整个操作不发生（返回 false）；交互层的幽灵预览已经把绿/红显示给玩家了。
        /// </summary>
        public bool MoveNode(LevelData level, NodeData node, Vector2Int newOrigin)
        {
            if (node == null) return false;
            if (node.Def.NodeType != ENodeType.Transit) return false; // 题面不可移动
            if (!node.CanMove) return false;
            if (node.Origin == newOrigin) return true;
            if (!CanMoveNodeTo(level, node, newOrigin)) return false;

            linkManager.DeleteLinksOf(level, node);
            level.ReleaseNode(node);
            node.Origin = newOrigin;
            level.OccupyNode(node);

            CircuitSolver.Solve(level);
            OnNodeMoved?.Invoke(level, node);
            return true;
        }
    }
}

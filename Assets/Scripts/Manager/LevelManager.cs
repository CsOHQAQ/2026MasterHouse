using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 小关生命周期与 tick 调度（§9）：
    /// 初始化预置节点、Load/Unload、稳态结算（待定 #7）；
    /// 按 §3.2 的固定三阶段推进聚焦小关的 tick；节点放置/移动/删除。
    /// </summary>
    public class LevelManager
    {
        private readonly LinkManager linkManager;
        private readonly PlayerCargoData playerCargo;

        /// <summary>已加载小关，按加载顺序推进 tick（顺序稳定）。</summary>
        private readonly List<LevelData> loadedLevels = new List<LevelData>();

        public IReadOnlyList<LevelData> LoadedLevels => loadedLevels;

        public LevelManager(LinkManager linkManager, PlayerCargoData playerCargo)
        {
            this.linkManager = linkManager;
            this.playerCargo = playerCargo;
        }

        // ───────────────── 生命周期（§8.4）─────────────────

        public LevelData LoadLevel(LevelDef def)
        {
            var level = new LevelData(def);

            // 按 LevelDef 初始化预置节点（资源点、中转节点靠它预置 §8.1）
            foreach (var preset in def.PresetNodes)
            {
                if (preset.Node == null) continue;
                var origin = def.WorldOrigin + preset.Cell;
                if (!CanPlaceNode(level, preset.Node, origin))
                {
                    Debug.LogError($"关卡 {def.name} 预置节点 {preset.Node.name} 在 {origin} 放置非法，已跳过");
                    continue;
                }
                PlaceNode(level, preset.Node, origin, preset.CanMove, preset.CanDelete);
            }

            // 待定 #9：存档恢复（存档系统未讨论），此处占位
            // 待定 #7：若曾 Unload，按稳态净产出表 × Δtick 一次性补账（§3.4），此处占位

            level.IsLoaded = true;
            loadedLevels.Add(level);
            return level;
        }

        public void UnloadLevel(LevelData level)
        {
            // 待定 #7：稳态吞吐分析（整条链瓶颈产率）→ 生成每 tick 净产出表，此处占位
            level.IsLoaded = false;
            loadedLevels.Remove(level);
            // View 与运行时数据的释放由 View 层响应（绑定机制待定 #10）
        }

        // ───────────────── tick 推进 ─────────────────

        /// <summary>由 GameManager 每个固定 tick 调用一次。</summary>
        public void TickAll()
        {
            for (int i = 0; i < loadedLevels.Count; i++)
                TickLevel(loadedLevels[i]);
        }

        /// <summary>tick 流水线：全局三阶段，顺序固定（§3.2）。</summary>
        private void TickLevel(LevelData level)
        {
            level.TickCount++;
            linkManager.TickDeliverPhase(level, playerCargo); // ① 投递：本 tick 送达的料当 tick 进配方
            TickNodePhase(level);                             // ② 节点：生产 / 配方推进
            linkManager.TickPickupPhase(level);               // ③ 取货：本 tick 新产出当 tick 被拉走
        }

        /// <summary>② 节点阶段：按 NodeId 稳定顺序遍历（§11.2）。</summary>
        private void TickNodePhase(LevelData level)
        {
            foreach (var node in level.Nodes)
            {
                if (node.IsIllegal) continue; // 非法临时态冻结（§4.3）
                switch (node.Def.NodeType)
                {
                    case ENodeType.Resource:
                        TickResourceNode(node);
                        break;
                    case ENodeType.Processor:
                        TickProcessorNode(node);
                        break;
                    // Storage：漏斗无内部行为（投递阶段直接入 PlayerCargo §7）
                    // Transit：无配方转运，无内部行为
                }
            }
        }

        private static void TickResourceNode(NodeData node)
        {
            var def = (ResourceNodeDef)node.Def;
            if (def.OutputItem == null) return;

            // 自身暂存满则停产（§7）——满时计时不推进，背压的最上游终点（§6.4）
            if (node.OutputStorage.GetFreeSpace(def.OutputItem) <= 0) return;

            node.ProductionCounter++;
            if (node.ProductionCounter < def.TicksPerProduction) return;

            node.ProductionCounter = 0;
            // 空位不足一次产量时截断（收多少产多少）
            node.OutputStorage.Add(def.OutputItem, def.AmountPerProduction);
        }

        private static void TickProcessorNode(NodeData node)
        {
            var def = (ProcessorNodeDef)node.Def;
            var recipe = def.Recipe; // 待定 #3：先按「策划配单条配方」实现
            if (recipe == null) return;

            if (!node.RecipeInProgress)
            {
                // 输入暂存齐一批料才开工
                if (!node.InputStorage.HasAll(recipe.Inputs)) return;
                node.InputStorage.ConsumeAll(recipe.Inputs);
                node.RecipeInProgress = true;
                node.RecipeProgressTicks = 0;
            }

            if (node.RecipeProgressTicks < recipe.WorkTicks)
            {
                node.RecipeProgressTicks++;
                if (node.RecipeProgressTicks < recipe.WorkTicks) return;
            }

            // 批次完成：产出须全部放得下才入库，否则停在完成态每 tick 重试（输出侧背压）
            if (!node.OutputStorage.CanAddAll(recipe.Outputs)) return;
            node.OutputStorage.AddAll(recipe.Outputs);
            node.RecipeInProgress = false;
            node.RecipeProgressTicks = 0;
        }

        // ───────────────── 节点放置 / 移动 / 删除 ─────────────────

        /// <summary>合法性：每个占格 ∈ 画布 ∧ 未被占用（§4.2）。逐格查询，不假设矩形（§4.1）。</summary>
        public bool CanPlaceNode(LevelData level, NodeDef def, Vector2Int origin)
        {
            foreach (var cell in def.Shape.CellsAt(origin))
                if (!level.IsInCanvas(cell) || level.IsOccupied(cell))
                    return false;
            return true;
        }

        /// <summary>本关同类节点计数（BuildableNodes 上限校验用）。</summary>
        public int CountNodesOf(LevelData level, NodeDef def)
        {
            int count = 0;
            foreach (var node in level.Nodes)
                if (node.Def == def)
                    count++;
            return count;
        }

        /// <summary>建造资格：v1 建造免费 + BuildableNodes 数量上限（§8.3；成本经济待定 #12）。</summary>
        public bool CanBuild(LevelData level, NodeDef def)
        {
            foreach (var entry in level.Def.BuildableNodes)
                if (entry.Node == def)
                    return CountNodesOf(level, def) < entry.MaxCount;
            return false; // 不在本关可建列表中
        }

        public NodeData PlaceNode(LevelData level, NodeDef def, Vector2Int origin,
            bool canMove = true, bool canDelete = true)
        {
            var node = new NodeData(level.NextNodeId++, def, origin, canMove, canDelete);
            level.Nodes.Add(node);
            level.OccupyNode(node);
            return node;
        }

        /// <summary>删除节点，附着链接一并删除。</summary>
        public bool RemoveNode(LevelData level, NodeData node)
        {
            if (!node.CanDelete) return false;

            // 先收集再删，避免遍历中修改 Pin.Links；按 LinkId 升序（Pin.Links 有序，逐 Pin 合并仍稳定）
            var attached = new List<LinkData>();
            foreach (var pin in node.Pins)
                foreach (var link in pin.Links)
                    if (!attached.Contains(link))
                        attached.Add(link);
            foreach (var link in attached)
                linkManager.DeleteLink(level, link);

            if (!node.IsIllegal)
                level.ReleaseNode(node);
            level.Nodes.Remove(node);
            return true;
        }

        /// <summary>
        /// 移动节点（§4.3 非法临时态）：
        /// 附着链接一律进入断线态（端点已位移，走线失效；无自动重寻路，修线是玩法 §5）；
        /// 新位置冲突时只冻结本节点，其余对象照常模拟。
        /// </summary>
        public void MoveNode(LevelData level, NodeData node, Vector2Int newOrigin)
        {
            if (!node.CanMove) return;

            foreach (var pin in node.Pins)
                foreach (var link in pin.Links)
                    linkManager.BreakLink(link);

            if (!node.IsIllegal)
                level.ReleaseNode(node);
            node.Origin = newOrigin;

            if (CanPlaceNode(level, node.Def, newOrigin))
            {
                node.IsIllegal = false;
                level.OccupyNode(node);
            }
            else
            {
                // 非法临时态：冻结自身、不写占用索引；存在期间禁止存档（§4.3）。UI 提示待定 #14
                node.IsIllegal = true;
            }
        }
    }
}
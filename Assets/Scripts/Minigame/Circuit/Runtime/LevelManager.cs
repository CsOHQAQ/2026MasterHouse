using System;
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

        /// <summary>
        /// 常驻关卡数据（按创建顺序，顺序稳定 §11.2）。
        /// 玩家离开局内后**数据不释放**——当前没有存档（待定 #9），丢弃 = 玩家布局全没；
        /// 统一存档接入后可改为「卸载 + 从存档恢复」。
        /// </summary>
        private readonly List<LevelData> levels = new List<LevelData>();

        public IReadOnlyList<LevelData> Levels => levels;

        /// <summary>
        /// 玩家正在打开的关卡，同一时刻至多一个；**只有它推进节点模拟**。
        /// 其余常驻关卡完全静止（家具效果产出除外，见 TickAll）。
        /// </summary>
        public LevelData ActiveLevel { get; private set; }

        // ── 结构变化广播（§2.1）：Manager 完成修改后触发，携带数据对象引用。
        //    只覆盖玩家操作产生的离散结构变化；连续量（暂存/进度/链接状态）由 View 每帧轮询。
        //    读档/Load 时 View 仍以全量重建为真相源，广播只是运行中的增量优化 ──

        /// <summary>玩家打开关卡（进入局内）：数据已就位，View 应全量重建。</summary>
        public event Action<LevelData> OnLevelOpened;

        /// <summary>玩家关闭关卡（退出局内）：数据仍常驻，View 只释放表现物。</summary>
        public event Action<LevelData> OnLevelClosed;

        /// <summary>玩家放置节点完成。</summary>
        public event Action<LevelData, NodeData> OnNodePlaced;

        /// <summary>节点删除完成（附着链接已先行删除并各自广播 OnLinkDeleted）。</summary>
        public event Action<LevelData, NodeData> OnNodeRemoved;

        /// <summary>节点移动完成（含落点冲突进入非法临时态的情况，View 读 IsIllegal 呈现）。</summary>
        public event Action<LevelData, NodeData> OnNodeMoved;

        public LevelManager(LinkManager linkManager, PlayerCargoData playerCargo)
        {
            this.linkManager = linkManager;
            this.playerCargo = playerCargo;
        }

        // ───────────────── 生命周期（§8.4）─────────────────

        /// <summary>
        /// 取回常驻关卡数据，不存在则按 LevelDef 创建（预置节点就位、算一次生效）。
        /// 正式流程里由「玩家在房间摆下一件带关卡的家具」触发；测试场景由调试面板驱动。
        /// </summary>
        public LevelData EnsureLevel(LevelDef def)
        {
            var existing = FindLevel(def);
            if (existing != null) return existing;

            var level = new LevelData(def);

            // 按 LevelDef 初始化预置节点（资源点、中转、条件节点靠它预置 §8.1）
            foreach (var preset in def.PresetNodes)
            {
                if (preset.Node == null) continue;
                var origin = def.WorldOrigin + preset.Cell;
                if (!CanPlaceNode(level, preset.Node, origin))
                {
                    Debug.LogError($"关卡 {def.name} 预置节点 {preset.Node.name} 在 {origin} 放置非法，已跳过");
                    continue;
                }
                PlaceNodeCore(level, preset.Node, origin, preset.CanMove, preset.CanDelete);
            }

            // 待定 #9：存档恢复（存档系统未讨论），此处占位

            level.IsLoaded = true;
            UpdateEffective(level); // 有条件节点则初值为「未生效」——家具尚未修好
            levels.Add(level);
            return level;
        }

        /// <summary>玩家进入局内：该关成为唯一推进节点模拟的关卡。</summary>
        public LevelData OpenLevel(LevelDef def)
        {
            var level = EnsureLevel(def);
            if (ActiveLevel == level) return level;

            if (ActiveLevel != null) CloseLevel();
            ActiveLevel = level;
            OnLevelOpened?.Invoke(level);
            return level;
        }

        /// <summary>
        /// 玩家退出局内：数据原样常驻，只是不再推进节点模拟。
        /// 家具效果产出照常继续（生效状态锁存在离开的那一刻）。
        /// </summary>
        public void CloseLevel()
        {
            var level = ActiveLevel;
            if (level == null) return;
            ActiveLevel = null;
            // View 释放表现物：响应本广播（§2.1）
            OnLevelClosed?.Invoke(level);
        }

        /// <summary>
        /// 丢弃常驻数据（调试面板热重载：全量丢弃运行时状态，再从 LevelDef 重建）。
        /// 正式玩法没有这条路径——布局是玩家资产，只有存档能替代它。
        /// </summary>
        public void DiscardLevel(LevelDef def)
        {
            var level = FindLevel(def);
            if (level == null) return;
            if (ActiveLevel == level) CloseLevel();
            level.IsLoaded = false;
            levels.Remove(level);
        }

        /// <summary>按 Def 查常驻数据；未创建过返回 null。</summary>
        public LevelData FindLevel(LevelDef def)
        {
            for (int i = 0; i < levels.Count; i++)
                if (levels[i].Def == def)
                    return levels[i];
            return null;
        }

        // ───────────────── tick 推进 ─────────────────

        /// <summary>
        /// 由 GameManager 每个固定 tick 调用一次，分两段：
        /// ① **只推进玩家打开中的关卡**的节点模拟——未打开的关卡完全不更新
        ///    （该规则取代了原 §3.4「后台小关稳态结算」，待定 #7 随之作废）；
        /// ② **所有常驻关卡**推进家具效果产出——节点不更新 ≠ 家具不生效。
        /// 顺序固定：先模拟后产出，本 tick 刚修好的家具当 tick 就开始产出。
        /// </summary>
        public void TickAll()
        {
            if (ActiveLevel != null)
                TickLevel(ActiveLevel);

            for (int i = 0; i < levels.Count; i++)
                TickFurnitureOutput(levels[i]);
        }

        /// <summary>tick 流水线：全局三阶段，顺序固定（§3.2）。</summary>
        private void TickLevel(LevelData level)
        {
            level.TickCount++;
            linkManager.TickDeliverPhase(level, playerCargo); // ① 投递：本 tick 送达的料当 tick 进配方
            TickNodePhase(level);                             // ② 节点：生产 / 配方推进 / 条件窗口
            linkManager.TickPickupPhase(level);               // ③ 取货：本 tick 新产出当 tick 被拉走
            UpdateEffective(level);                           // 条件汇总：家具是否修好
        }

        /// <summary>
        /// 关卡（= 家具）是否生效：没有条件节点则恒生效；有则**全部**条件节点达标。
        /// 非法临时态的条件节点算不达标（它被冻结，窗口不推进）。
        /// 只在关卡被打开时调用——玩家不在局内时结果保持不变（锁存）。
        /// </summary>
        private static void UpdateEffective(LevelData level)
        {
            bool hasCondition = false;
            bool allSatisfied = true;
            foreach (var node in level.Nodes) // 按 NodeId 稳定顺序（§11.2）
            {
                if (node.ConditionState == null) continue;
                hasCondition = true;
                if (node.IsIllegal || !node.ConditionState.Satisfied)
                {
                    allSatisfied = false;
                    break;
                }
            }
            level.IsEffective = !hasCondition || allSatisfied;
        }

        /// <summary>
        /// 家具效果产出：生效的关卡按 LevelDef.Outputs 持续产出到玩家仓库。
        /// 计时器走**全局 tick**（不是 level.TickCount，后者只在关卡打开时推进），
        /// 因此玩家不在局内时家具照常产出；打烊时随全局停 tick 一起停（§16.4）。
        /// </summary>
        private void TickFurnitureOutput(LevelData level)
        {
            if (!level.IsEffective) return;

            var outputs = level.Def.Outputs;
            if (outputs == null) return;

            int count = Math.Min(outputs.Count, level.OutputCounters.Length);
            for (int i = 0; i < count; i++)
            {
                var entry = outputs[i];
                if (entry == null || entry.Item == null || entry.Amount <= 0) continue;

                int period = Math.Max(1, entry.TicksPerOutput);
                level.OutputCounters[i]++;
                if (level.OutputCounters[i] < period) continue;

                level.OutputCounters[i] = 0;
                playerCargo.Add(entry.Item, entry.Amount);
            }
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
                    case ENodeType.Condition:
                        // 推进滑动窗口（投递阶段已记本 tick 到货），重算达标
                        node.ConditionState.Advance(level.TickCount);
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
            // 条件节点是关卡的生效判据，只能由策划预置；按类型强制，自由模式也不放行
            if (def.NodeType == ENodeType.Condition) return false;

            foreach (var entry in level.Def.BuildableNodes)
                if (entry.Node == def)
                    return CountNodesOf(level, def) < entry.MaxCount;
            return false; // 不在本关可建列表中
        }

        public NodeData PlaceNode(LevelData level, NodeDef def, Vector2Int origin,
            bool canMove = true, bool canDelete = true)
        {
            var node = PlaceNodeCore(level, def, origin, canMove, canDelete);
            OnNodePlaced?.Invoke(level, node);
            return node;
        }

        /// <summary>纯修改不广播：Load 初始化预置节点走这里（§2.1 全量重建是真相源，逐节点广播多余）。</summary>
        private static NodeData PlaceNodeCore(LevelData level, NodeDef def, Vector2Int origin,
            bool canMove, bool canDelete)
        {
            var node = new NodeData(level.NextNodeId++, def, origin, canMove, canDelete);
            level.Nodes.Add(node);
            level.OccupyNode(node);
            return node;
        }

        /// <summary>
        /// 删除节点，附着链接一并删除。
        /// CanDelete 资格校验属 Controller 层（与 CanBuild 同型；自由模式只绕过 Controller 层校验——权限模型）。
        /// </summary>
        public bool RemoveNode(LevelData level, NodeData node)
        {
            // 条件节点不可删除：关卡靠它判定生效，删掉等于关卡失去判据。
            // 按类型强制（不只是 CanDelete 字段），自由模式也不放行。
            if (node.Def.NodeType == ENodeType.Condition) return false;

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
            OnNodeRemoved?.Invoke(level, node);
            return true;
        }

        /// <summary>
        /// 移动节点（§4.3 非法临时态）：
        /// 附着链接一律进入断线态（端点已位移，走线失效；无自动重寻路，修线是玩法 §5）；
        /// 新位置冲突时只冻结本节点，其余对象照常模拟。
        /// CanMove 资格校验属 Controller 层（与 CanBuild 同型；自由模式只绕过 Controller 层校验——权限模型）。
        /// </summary>
        public void MoveNode(LevelData level, NodeData node, Vector2Int newOrigin)
        {
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

            OnNodeMoved?.Invoke(level, node);
        }
    }
}
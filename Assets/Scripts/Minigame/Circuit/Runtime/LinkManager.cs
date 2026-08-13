using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 链接逻辑服务（§9）。无状态：链接数据归 LevelData，本类只做运算（待定 #8 的建议方案）。
    /// 职责：tick 流水线的投递/取货阶段（§3.2 ①③）、仲裁（§3.3）、链接创建校验与删除。
    /// </summary>
    public class LinkManager
    {
        // ── 结构变化广播（§2.1）：Manager 完成修改后触发，携带数据对象引用。
        //    BreakLink / SetLinkTypeInvalid 及阻塞等只是 State 字段变化，不广播，View 每帧轮询配色 ──

        /// <summary>链接创建完成（占用索引与中转 Pin 同步已就位）。</summary>
        public event Action<LevelData, LinkData> OnLinkCreated;

        /// <summary>链接删除完成。</summary>
        public event Action<LevelData, LinkData> OnLinkDeleted;

        // ───────────────── ① 投递阶段 ─────────────────

        /// <summary>
        /// 所有「在途计时已到期」的持货链接，按目标 Pin 上的优先级 + 轮询顺序尝试交付（§3.2）。
        /// 遍历顺序：节点按 NodeId、Pin 按节点内索引、链接按 LinkId——全部稳定键（§11.2）。
        /// </summary>
        public void TickDeliverPhase(LevelData level, PlayerCargoData playerCargo)
        {
            foreach (var node in level.Nodes)
            {
                if (node.IsIllegal) continue; // 非法临时态冻结（§4.3）
                foreach (var pin in node.Pins)
                {
                    if (pin.RuntimeDirection != EPinDirection.Input) continue;
                    DeliverToPin(node, pin, playerCargo, level.TickCount);
                }
            }
        }

        /// <summary>tick 参数供条件节点记到货窗口（§条件节点）；其余类型用不到。</summary>
        private static void DeliverToPin(NodeData node, PinData pin, PlayerCargoData playerCargo, long tick)
        {
            // 推进在途计时，并收集「到期持货」的链接（新到期 + 既有阻塞）
            List<LinkData> ready = null;
            foreach (var link in pin.Links)
            {
                if (link.State == ELinkState.InTransit)
                {
                    link.TransitCounter++;
                    if (link.TransitCounter < link.TransitTicks) continue;
                }
                else if (link.State != ELinkState.Blocked)
                {
                    continue;
                }
                (ready ?? (ready = new List<LinkData>())).Add(link);
            }
            if (ready == null) return;

            SortForArbitration(ready, pin.RoundRobinPointer);

            bool servedAny = false;
            long lastServed = -1;
            foreach (var link in ready)
            {
                // 输入侧语义（§6.2）：有多少空位收多少，收不下的留在槽中
                int space = GetDeliverSpace(node, link.ItemType);
                int give = Mathf.Min(link.SlotCount, space);
                if (give > 0)
                {
                    Deposit(node, link.ItemType, give, playerCargo, tick);
                    link.SlotCount -= give;
                    servedAny = true;
                    lastServed = link.LinkId;
                }

                if (link.SlotCount <= 0)
                {
                    // 交付完毕：槽清空，下个节拍自动恢复取货（§6.4）
                    link.SlotItem = null;
                    link.SlotCount = 0;
                    link.State = ELinkState.Idle;
                }
                else
                {
                    // 余量原地持有，每 tick 重试投递（§6.4）；View 表现为脉冲停在目标门口
                    link.State = ELinkState.Blocked;
                }
            }
            if (servedAny)
                pin.RoundRobinPointer = lastServed;
        }

        /// <summary>目标节点对该物资当前可接收的空位。</summary>
        private static int GetDeliverSpace(NodeData node, ItemDef item)
        {
            switch (node.Def.NodeType)
            {
                case ENodeType.Processor:
                    return node.InputStorage.GetFreeSpace(item);
                case ENodeType.Storage:
                    return int.MaxValue; // 仓库是漏斗，无上限（§7）；白名单在建线时校验
                case ENodeType.Transit:
                    return node.OutputStorage.GetFreeSpace(item); // 容量待定 #6
                case ENodeType.Condition:
                    return int.MaxValue; // 无暂存、无限吸收：上游永不背压（有意设计）
                default:
                    return 0; // 资源型不应有输入 Pin
            }
        }

        private static void Deposit(NodeData node, ItemDef item, int count,
            PlayerCargoData playerCargo, long tick)
        {
            switch (node.Def.NodeType)
            {
                case ENodeType.Processor:
                    node.InputStorage.Add(item, count);
                    break;
                case ENodeType.Storage:
                    playerCargo.Add(item, count); // 收到即从本关经济中消失（§7 漏斗；结算待定 #1/#15）
                    break;
                case ENodeType.Transit:
                    node.OutputStorage.Add(item, count);
                    break;
                case ENodeType.Condition:
                    // 收到即蒸发，只记入窗口（守恒的第二个明示例外）
                    node.ConditionState.Record(item, count, tick);
                    break;
            }
        }

        // ───────────────── ③ 取货阶段 ─────────────────

        /// <summary>
        /// 所有「槽位为空且到达节拍」的链接，按源 Pin 上的优先级 + 轮询顺序向源节点请求物资（§3.2）。
        /// 槽位非空 → 不取新货：由状态机保证（只有 Idle 参与，§6.4 规则 2）。
        /// </summary>
        public void TickPickupPhase(LevelData level)
        {
            foreach (var node in level.Nodes)
            {
                if (node.IsIllegal) continue;
                foreach (var pin in node.Pins)
                {
                    if (pin.RuntimeDirection != EPinDirection.Output) continue;
                    PickupFromPin(node, pin);
                }
            }
        }

        private static void PickupFromPin(NodeData node, PinData pin)
        {
            List<LinkData> ready = null;
            foreach (var link in pin.Links)
            {
                if (link.State != ELinkState.Idle) continue;
                // 节拍推进；到位后若取不到货，保持到位、每 tick 重试
                if (link.BeatCounter < link.BeatTicks) link.BeatCounter++;
                if (link.BeatCounter < link.BeatTicks) continue;
                if (link.ItemType == null) continue;
                (ready ?? (ready = new List<LinkData>())).Add(link);
            }
            if (ready == null) return;

            SortForArbitration(ready, pin.RoundRobinPointer);

            bool servedAny = false;
            long lastServed = -1;
            foreach (var link in ready)
            {
                // 输出侧语义（§6.2）：请求量受源 Pin 最大速率限制；剩余不足时仅输出剩余量
                int request = Mathf.Max(1, pin.Def.MaxRate);
                int taken = TakeFromSource(node, link.ItemType, request);
                if (taken <= 0) continue; // 源头空，本 tick 空手

                link.SlotItem = link.ItemType;
                link.SlotCount = taken;
                link.State = ELinkState.InTransit;
                link.TransitCounter = 0;
                link.BeatCounter = 0;
                servedAny = true;
                lastServed = link.LinkId;
            }
            if (servedAny)
                pin.RoundRobinPointer = lastServed;
        }

        private static int TakeFromSource(NodeData node, ItemDef item, int amount)
        {
            switch (node.Def.NodeType)
            {
                case ENodeType.Resource:
                case ENodeType.Processor:
                case ENodeType.Transit:
                    return node.OutputStorage.Remove(item, amount);
                default:
                    return 0; // 仓库 v1 只进不出（待定 #15）
            }
        }

        // ───────────────── 仲裁（§3.3）─────────────────

        /// <summary>
        /// 先按优先级分层（数值大者先），同优先级内轮询：按 LinkId 升序、从「指针之后」轮转。
        /// v1 简化：轮询指针整个 Pin 共用一个（不分层各存）。
        /// 比较器是全序（LinkId 唯一），排序结果确定（§11.2）。
        /// </summary>
        private static void SortForArbitration(List<LinkData> links, long pointer)
        {
            links.Sort((a, b) =>
            {
                if (a.Priority != b.Priority)
                    return b.Priority.CompareTo(a.Priority);
                bool aAfter = a.LinkId > pointer;
                bool bAfter = b.LinkId > pointer;
                if (aAfter != bAfter)
                    return aAfter ? -1 : 1;
                return a.LinkId.CompareTo(b.LinkId);
            });
        }

        // ───────────────── 链接创建 / 删除 ─────────────────

        /// <summary>
        /// 创建链接（§6.2 强类型自动推导）。
        /// <para><paramref name="path"/> = 玩家手绘的途径格（§5 手动描格），本层会**完整复核**其合法性——
        /// 不信任 Controller 传入的数据（§11.4）；传 null 时退回一次性 A*（仅调试面板「重新布线」使用）。</para>
        /// pinA/pinB 不分先后，方向按两端 Pin 的运行时方向解析；
        /// 若解析出的 from→to 与 path 首尾相反，path 会被反转后存入。失败返回 null 并给出原因。
        /// </summary>
        public LinkData TryCreateLink(LevelData level, PinData pinA, PinData pinB, out string failReason,
            IReadOnlyList<Vector2Int> path = null)
        {
            failReason = null;
            if (pinA == null || pinB == null || pinA == pinB)
            {
                failReason = "无效的 Pin";
                return null;
            }
            if (pinA.Owner == pinB.Owner)
            {
                failReason = "不能连接同一节点上的两个 Pin";
                return null;
            }

            // 方向解析：需要一端输出、一端输入；中转未同步 Pin（None）随对端确定（§6.3）
            PinData fromPin, toPin;
            if (pinA.RuntimeDirection == EPinDirection.Output && pinB.RuntimeDirection != EPinDirection.Output)
            {
                fromPin = pinA;
                toPin = pinB;
            }
            else if (pinB.RuntimeDirection == EPinDirection.Output && pinA.RuntimeDirection != EPinDirection.Output)
            {
                fromPin = pinB;
                toPin = pinA;
            }
            else if (pinA.RuntimeDirection == EPinDirection.Input && pinB.RuntimeDirection == EPinDirection.None)
            {
                fromPin = pinB; // 未同步中转 Pin 充当输出侧
                toPin = pinA;
            }
            else if (pinB.RuntimeDirection == EPinDirection.Input && pinA.RuntimeDirection == EPinDirection.None)
            {
                fromPin = pinA;
                toPin = pinB;
            }
            else
            {
                // 含两端均未同步（中转-中转直连成链）的情况：类型传播未定案，待定 #2，先拒绝
                failReason = "两端 Pin 方向不构成一输出一输入";
                return null;
            }

            // 类型推导（§6.2）：两端已配类型必须一致；未同步中转 Pin 采纳对端类型
            var fromType = fromPin.RuntimeItemType;
            var toType = toPin.RuntimeItemType;
            if (fromType != null && toType != null && fromType != toType)
            {
                // 创建时直接拒绝；「类型失效」状态只用于运行中被动变得不兼容的链接（§6.5）
                failReason = "两端 Pin 物资类型不兼容";
                return null;
            }
            var item = fromType != null ? fromType : toType;
            if (item == null)
            {
                failReason = "无法推导传输物资类型";
                return null;
            }

            // 仓库白名单校验（§7）
            if (toPin.Owner.Def is StorageNodeDef storageDef &&
                storageDef.Whitelist.Count > 0 && !storageDef.Whitelist.Contains(item))
            {
                failReason = "仓库白名单不收该物资";
                return null;
            }

            // 中转配对 Pin 同步预检（§6.3；边角待定 #2：按「后连接的一端报不兼容」占位）
            if (!CanSyncTransitPin(fromPin, item) || !CanSyncTransitPin(toPin, item))
            {
                failReason = "中转配对 Pin 类型冲突（待定 #2 占位规则）";
                return null;
            }

            var start = fromPin.Owner.GetPinPortCell(fromPin.IndexInNode);
            var goal = toPin.Owner.GetPinPortCell(toPin.IndexInNode);

            List<Vector2Int> pathCells;
            if (path == null)
            {
                // A* 只剩调试面板「重新布线」这一条调用路径（§5：正式玩法一律玩家手绘）
                pathCells = FindPath(level, start, goal);
                if (pathCells == null)
                {
                    failReason = "找不到合法走线";
                    return null;
                }
            }
            else
            {
                pathCells = ValidatePlayerPath(level, path, start, goal, out failReason);
                if (pathCells == null) return null;
            }

            var config = GameConfig.Instance;
            var link = new LinkData(level.NextLinkId++, fromPin, toPin)
            {
                ItemType = item,
                PathCells = pathCells,
                // 待定 #4：节拍先用 GameConfig 全局默认；在途时长见 ComputeTransitTicks
                BeatTicks = config != null ? config.DefaultLinkBeatTicks : 10,
                TransitTicks = ComputeTransitTicks(config, pathCells),
            };

            level.Links.Add(link);          // 追加即按 LinkId 升序（§11.2）
            fromPin.Links.Add(link);
            toPin.Links.Add(link);
            level.OccupyLink(link);         // 连线占据途径的每一格（§4.2）

            ApplyTransitPinSync(fromPin, item, EPinDirection.Output);
            ApplyTransitPinSync(toPin, item, EPinDirection.Input);

            OnLinkCreated?.Invoke(level, link);
            return link;
        }

        /// <summary>
        /// 复核玩家手绘路径并规整方向（§5）：
        /// 每格 ∈ 画布 ∧ 未被占用、相邻格 4 向连续、整条不自交，
        /// 且首尾格分别落在两端 Pin 的外侧接线格上。
        /// 玩家可能从输入侧往输出侧画，此时整条反转后返回（存储一律 from→to）。
        /// </summary>
        private static List<Vector2Int> ValidatePlayerPath(LevelData level, IReadOnlyList<Vector2Int> path,
            Vector2Int start, Vector2Int goal, out string failReason)
        {
            failReason = null;
            if (path == null || path.Count == 0)
            {
                failReason = "走线为空";
                return null;
            }

            var first = path[0];
            var last = path[path.Count - 1];
            bool reversed;
            if (first == start && last == goal) reversed = false;
            else if (first == goal && last == start) reversed = true;
            else
            {
                failReason = "走线两端没有接在 Pin 的接口格上";
                return null;
            }

            var seen = new HashSet<Vector2Int>(); // 仅成员查询（§11.2）
            for (int i = 0; i < path.Count; i++)
            {
                var cell = path[i];
                if (!seen.Add(cell))
                {
                    failReason = "走线与自身重叠";
                    return null;
                }
                if (!IsCellFreeForLink(level, cell))
                {
                    failReason = "走线经过的格子越界或已被占用";
                    return null;
                }
                if (i > 0 && Mathf.Abs(cell.x - path[i - 1].x) + Mathf.Abs(cell.y - path[i - 1].y) != 1)
                {
                    failReason = "走线不是 4 向连续的折线";
                    return null;
                }
            }

            var result = new List<Vector2Int>(path);
            if (reversed) result.Reverse();
            return result;
        }

        /// <summary>
        /// 在途时长（待定 #4）：默认用 GameConfig 全局固定值；
        /// 实验开关开启时按线长计算（途径格数 × 每格 tick 数，含两端格）。
        /// 仅创建时赋值一次，运行时不重算——切换开关后重载关卡刷新全部链接。
        /// </summary>
        private static int ComputeTransitTicks(GameConfig config, List<Vector2Int> path)
        {
            if (config == null) return 10;
            return config.TransitTicksByLength
                ? Mathf.Max(1, path.Count * config.TransitTicksPerCell)
                : config.DefaultLinkTransitTicks;
        }

        /// <summary>删除链接。槽中在途物资随之消失（玩家主动操作；其余场景全游戏守恒 §6.4）。</summary>
        public void DeleteLink(LevelData level, LinkData link)
        {
            level.ReleaseLink(link);
            link.FromPin.Links.Remove(link);
            link.ToPin.Links.Remove(link);
            level.Links.Remove(link);

            ClearTransitSyncIfIdle(link.FromPin);
            ClearTransitSyncIfIdle(link.ToPin);

            OnLinkDeleted?.Invoke(level, link);
        }

        /// <summary>走线非法（棋盘变化/端点被移动）→ 断线：全部停止，槽中货物原地保留，等玩家修线（§6.5）。</summary>
        public void BreakLink(LinkData link)
        {
            if (link.State == ELinkState.Broken || link.State == ELinkState.TypeInvalid) return;
            link.State = ELinkState.Broken;
        }

        /// <summary>
        /// 类型失效（§6.2/§6.5）：清空槽中全部在途物资（有意蒸发，玩家应有心理预期）并停止工作。
        /// 触发场景：中转同步传播使既有链接两端类型变得不兼容（传播规则待定 #2，调用点留待其定案）。
        /// </summary>
        public void SetLinkTypeInvalid(LinkData link)
        {
            link.SlotItem = null;
            link.SlotCount = 0;
            link.State = ELinkState.TypeInvalid;
        }

        // ───────────────── 中转配对 Pin 同步（§6.3）─────────────────

        /// <summary>同步预检：目标类型与该 Pin 及其配对 Pin 已生效的类型是否冲突。</summary>
        private static bool CanSyncTransitPin(PinData pin, ItemDef item)
        {
            if (pin.Owner.Def.NodeType != ENodeType.Transit) return true;
            if (pin.RuntimeItemType != null && pin.RuntimeItemType != item) return false;
            var pair = pin.PairedPin;
            if (pair != null && pair.RuntimeItemType != null && pair.RuntimeItemType != item) return false;
            return true;
        }

        /// <summary>
        /// 应用同步：该 Pin 采纳连接方向与物资类型，配对 Pin 同步同类型、相反方向——
        /// 直到与最初的上游断开连接为止（§6.3）。
        /// 待定 #2：配对两端同时接上游、中转链/环的传播未定案，当前只做单跳同步。
        /// </summary>
        private static void ApplyTransitPinSync(PinData pin, ItemDef item, EPinDirection direction)
        {
            if (pin.Owner.Def.NodeType != ENodeType.Transit) return;
            if (pin.Def.ItemType != null) return; // 预配类型的 Pin 不参与运行时同步

            pin.RuntimeItemType = item;
            pin.RuntimeDirection = direction;

            var pair = pin.PairedPin;
            if (pair != null && pair.Def.ItemType == null)
            {
                pair.RuntimeItemType = item;
                if (pair.RuntimeDirection == EPinDirection.None)
                    pair.RuntimeDirection = direction == EPinDirection.Input
                        ? EPinDirection.Output
                        : EPinDirection.Input;
            }
        }

        /// <summary>断开后解除同步（§6.3）。v1：未预配的中转 Pin 在自身与配对 Pin 都无链接时还原（待定 #2）。</summary>
        private static void ClearTransitSyncIfIdle(PinData pin)
        {
            if (pin.Owner.Def.NodeType != ENodeType.Transit) return;
            if (pin.Def.ItemType != null) return;

            var pair = pin.PairedPin;
            if (pin.Links.Count > 0 || (pair != null && pair.Links.Count > 0)) return;

            pin.RuntimeItemType = null;
            pin.RuntimeDirection = pin.Def.Direction;
            if (pair != null && pair.Def.ItemType == null)
            {
                pair.RuntimeItemType = null;
                pair.RuntimeDirection = pair.Def.Direction;
            }
        }

        // ───────────────── A* 布线（§5）─────────────────

        private struct PathNode
        {
            public Vector2Int Cell;
            public int G;
            public int F;
            public int Order; // 入队序号，平手时先入队者优先（确定性 tie-break）

            public PathNode(Vector2Int cell, int g, int f, int order)
            {
                Cell = cell;
                G = g;
                F = f;
                Order = order;
            }
        }

        /// <summary>
        /// 创建时一次性 A* 自动布线：4 向直角折线；
        /// 途径格必须 ∈ 画布 ∧ 不被任何节点/连线占用（§4.2）。
        /// 找不到返回 null。邻居固定「上右下左」顺序 + 稳定 tie-break，结果确定。
        /// </summary>
        public static List<Vector2Int> FindPath(LevelData level, Vector2Int start, Vector2Int goal)
        {
            if (!IsCellFreeForLink(level, start) || !IsCellFreeForLink(level, goal)) return null;
            if (start == goal) return new List<Vector2Int> { start };

            var open = new List<PathNode>(); // 画布规模小，直接线性取最小
            var gScore = new Dictionary<Vector2Int, int>(); // 仅键查询（§11.2）
            var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
            var closed = new HashSet<Vector2Int>(); // 仅成员查询
            int order = 0;

            open.Add(new PathNode(start, 0, Heuristic(start, goal), order++));
            gScore[start] = 0;

            while (open.Count > 0)
            {
                int best = 0;
                for (int i = 1; i < open.Count; i++)
                    if (open[i].F < open[best].F ||
                        (open[i].F == open[best].F && open[i].Order < open[best].Order))
                        best = i;
                var current = open[best];
                open.RemoveAt(best);

                if (current.Cell == goal)
                    return ReconstructPath(cameFrom, start, goal);
                if (!closed.Add(current.Cell)) continue;

                foreach (var offset in Direction4.Offsets)
                {
                    var next = current.Cell + offset;
                    if (closed.Contains(next) || !IsCellFreeForLink(level, next)) continue;
                    int g = current.G + 1;
                    if (gScore.TryGetValue(next, out var oldG) && oldG <= g) continue;
                    gScore[next] = g;
                    cameFrom[next] = current.Cell;
                    open.Add(new PathNode(next, g, g + Heuristic(next, goal), order++));
                }
            }
            return null;
        }

        /// <summary>连线途径格合法性（§4.2）：∈ 画布 ∧ ∉ 节点占格 ∧ ∉ 其他连线途径格。</summary>
        private static bool IsCellFreeForLink(LevelData level, Vector2Int cell)
        {
            return level.IsInCanvas(cell) && !level.IsOccupied(cell);
        }

        private static int Heuristic(Vector2Int a, Vector2Int b)
        {
            return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
        }

        private static List<Vector2Int> ReconstructPath(
            Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int start, Vector2Int goal)
        {
            var path = new List<Vector2Int> { goal };
            var cell = goal;
            while (cell != start)
            {
                cell = cameFrom[cell];
                path.Add(cell);
            }
            path.Reverse();
            return path;
        }
    }
}
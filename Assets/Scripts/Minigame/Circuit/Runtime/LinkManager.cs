using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 导线逻辑服务。无状态：链接数据归 LevelData，本类只做运算。
    /// 职责只剩建线校验与删线——tick 流水线（投递/取货/仲裁/背压）已随物资链整体删除，
    /// 供电改由 CircuitSolver 一次求解。
    /// </summary>
    public class LinkManager
    {
        /// <summary>链接创建完成（占用索引与中转方向同步已就位）。</summary>
        public event Action<LevelData, LinkData> OnLinkCreated;

        /// <summary>链接删除完成。</summary>
        public event Action<LevelData, LinkData> OnLinkDeleted;

        // ───────────────── 建线 ─────────────────

        /// <summary>
        /// 创建一条导线。<paramref name="path"/> = 玩家手绘的途径格，本层**完整复核**其合法性——
        /// 不信任 Controller 传入的数据（§11.4）。
        /// pinA/pinB 不分先后，方向按两端 Pin 的运行时方向解析；
        /// 若解析出的 from→to 与 path 首尾相反，path 会被反转后存入。失败返回 null 并给出原因。
        /// </summary>
        public LinkData TryCreateLink(LevelData level, PinData pinA, PinData pinB, out string failReason,
            IReadOnlyList<Vector2Int> path)
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

            // 一个 Pin 至多接一条线（§4.2 修订版）：这是硬规则，不是约定
            if (pinA.Link != null || pinB.Link != null)
            {
                failReason = "这个接口已经接了线";
                return null;
            }

            if (!ResolveDirection(pinA, pinB, out var fromPin, out var toPin, out failReason))
                return null;

            // 成环会让"输出 = 输入之和"变成自指方程，纯函数求解器无解——建线时就拦掉
            if (CircuitSolver.WouldCreateCycle(fromPin, toPin))
            {
                failReason = "这样接会形成回路";
                return null;
            }

            var start = fromPin.Owner.GetPinPortCell(fromPin.IndexInNode);
            var goal = toPin.Owner.GetPinPortCell(toPin.IndexInNode);

            var pathCells = ValidatePlayerPath(level, path, start, goal, out failReason);
            if (pathCells == null) return null;

            // 导线预算（§8.3）：**这里是唯一的闸门**。2026-08-16 起描格不再按预算停住——玩家可以画超，
            // 超出的部分在棋盘上画成红色、顶栏数字同步标红，松手提交时才在这里被拒（失败原因会显示给玩家）
            int budget = level.LinkCellBudget;
            if (budget > 0 && level.UsedLinkCells + pathCells.Count > budget)
            {
                failReason = $"导线预算不够（还剩 {level.RemainingLinkCells} 格，这条要 {pathCells.Count} 格）";
                return null;
            }

            var link = new LinkData(level.NextLinkId++, fromPin, toPin) { PathCells = pathCells };

            level.Links.Add(link);      // 追加即按 LinkId 升序（§11.2）
            fromPin.Link = link;
            toPin.Link = link;
            level.OccupyLink(link);     // 导线占据途径的每一格

            ApplyTransitDirectionSync(fromPin, EPinDirection.Output);
            ApplyTransitDirectionSync(toPin, EPinDirection.Input);

            // 先算后播：订阅方读到的一定是算好的供电结果
            CircuitSolver.Solve(level);
            OnLinkCreated?.Invoke(level, link);
            return link;
        }

        /// <summary>
        /// 方向解析：需要一端输出、一端输入。
        /// 十字件的 Pin 出厂方向为 None，随对端确定（§4.7）；两端都是 None 时无法定向，拒绝。
        /// </summary>
        private static bool ResolveDirection(PinData pinA, PinData pinB,
            out PinData fromPin, out PinData toPin, out string failReason)
        {
            failReason = null;
            var a = pinA.RuntimeDirection;
            var b = pinB.RuntimeDirection;

            if (a == EPinDirection.Output && b != EPinDirection.Output)
            {
                fromPin = pinA;
                toPin = pinB;
                return true;
            }
            if (b == EPinDirection.Output && a != EPinDirection.Output)
            {
                fromPin = pinB;
                toPin = pinA;
                return true;
            }
            if (a == EPinDirection.Input && b == EPinDirection.None)
            {
                fromPin = pinB; // 未定向的中转 Pin 充当输出侧
                toPin = pinA;
                return true;
            }
            if (b == EPinDirection.Input && a == EPinDirection.None)
            {
                fromPin = pinA;
                toPin = pinB;
                return true;
            }

            fromPin = null;
            toPin = null;
            // 含两端均未定向（十字件直连十字件）的情况：谁进谁出无从判断，拒绝
            failReason = a == EPinDirection.None && b == EPinDirection.None
                ? "两端都是未定向的中转口，无法判断电流方向"
                : "两端 Pin 方向不构成一输出一输入";
            return false;
        }

        /// <summary>
        /// 复核玩家手绘路径并规整方向：
        /// 每格 ∈ 画布 ∧ 未被占用、相邻格 4 向连续、整条不自交，
        /// 且首尾格分别落在两端 Pin 的外侧接线格上。
        /// 玩家可能从输入侧往输出侧画，此时整条反转后返回（存储一律 from→to）。
        ///
        /// 【本方法在本次重构中一行未改】——它是描格玩法最容易写错的一块。
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

        /// <summary>导线途径格合法性：∈ 画布 ∧ ∉ 节点占格 ∧ ∉ 其他导线途径格。</summary>
        public static bool IsCellFreeForLink(LevelData level, Vector2Int cell)
        {
            return level.IsInCanvas(cell) && !level.IsOccupied(cell);
        }

        // ───────────────── 删线 ─────────────────

        /// <summary>删除一条线。占的格子与导线预算随之退还。</summary>
        public void DeleteLink(LevelData level, LinkData link)
        {
            if (link == null) return;

            level.ReleaseLink(link);
            if (link.FromPin.Link == link) link.FromPin.Link = null;
            if (link.ToPin.Link == link) link.ToPin.Link = null;
            level.Links.Remove(link);

            ClearTransitDirectionSyncIfIdle(link.FromPin);
            ClearTransitDirectionSyncIfIdle(link.ToPin);

            CircuitSolver.Solve(level);
            OnLinkDeleted?.Invoke(level, link);
        }

        /// <summary>删除挂在该节点上的全部导线（移动/删除中转件时调用）。</summary>
        public void DeleteLinksOf(LevelData level, NodeData node)
        {
            // 先收集再删，避免遍历中改动 Pin.Link
            List<LinkData> attached = null;
            foreach (var pin in node.Pins)
                if (pin.Link != null)
                    (attached ?? (attached = new List<LinkData>())).Add(pin.Link);
            if (attached == null) return;

            // 同一条线不会挂在同一节点的两个 Pin 上（建线时已拦同节点自连），无需去重
            foreach (var link in attached)
                DeleteLink(level, link);
        }

        // ───────────────── 中转件方向同步（§4.7）─────────────────

        /// <summary>
        /// 十字件出厂时 Pin 方向为 None，第一条线接上才定向：
        /// 本 Pin 采纳连接方向，**同组的其余 Pin 取反向**（1 进 1 出的组因此变成直通）。
        /// 分流/合流的方向由策划配死，不参与同步。
        /// </summary>
        private static void ApplyTransitDirectionSync(PinData pin, EPinDirection direction)
        {
            if (pin.Owner.Def.NodeType != ENodeType.Transit) return;
            if (pin.Def.Direction != EPinDirection.None) return; // 预配方向的 Pin 不参与
            if (pin.Group < 0) return;

            pin.RuntimeDirection = direction;

            var opposite = direction == EPinDirection.Input
                ? EPinDirection.Output
                : EPinDirection.Input;
            foreach (var peer in pin.Owner.Pins)
            {
                if (peer == pin || peer.Group != pin.Group) continue;
                if (peer.Def.Direction != EPinDirection.None) continue;
                if (peer.RuntimeDirection == EPinDirection.None)
                    peer.RuntimeDirection = opposite;
            }
        }

        /// <summary>断线后解除同步：整组都没有线了才还原为未定向。</summary>
        private static void ClearTransitDirectionSyncIfIdle(PinData pin)
        {
            if (pin.Owner.Def.NodeType != ENodeType.Transit) return;
            if (pin.Def.Direction != EPinDirection.None) return;
            if (pin.Group < 0) return;

            foreach (var peer in pin.Owner.Pins)
                if (peer.Group == pin.Group && peer.Link != null)
                    return; // 组内还有线，保持当前定向

            foreach (var peer in pin.Owner.Pins)
                if (peer.Group == pin.Group && peer.Def.Direction == EPinDirection.None)
                    peer.RuntimeDirection = EPinDirection.None;
        }
    }
}

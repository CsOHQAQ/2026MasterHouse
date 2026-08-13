using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 供电求解（小游戏说明 §4.2 修订版）。**纯函数、无状态**：
    /// 输出完全由 LevelData 的当前布局决定，没有 tick、没有时序、没有累积量。
    /// 玩家每次增删线 / 摆件 / 移件后调一次 Solve 即可。
    ///
    /// 模型只有三条规则：
    ///   ① 电源的每个输出 Pin 送出 PinDef.MaxRate 的电；一个 Pin 至多接一条线。
    ///   ② 中转件按 PinDef.PinGroup 分组，**每个输出口 = floor(组内输入之和 / 组内输出口总数)**。
    ///      十字件（1 进 1 出）退化为直通，合流（N 进 1 出）得求和，分流（1 进 N 出）得平分。
    ///      分母是**输出口总数**而非已接线数：没接线的口那一份直接浪费，整除余数同样蒸发。
    ///   ③ 电池汇总各输入 Pin 收到的电量，与 RequiredAmount 比较，多出部分看 AllowExcess。
    ///
    /// 图无环由 LinkManager 在建线时保证（见 WouldCreateCycle）；本类仍留一道递归深度防御，
    /// 万一有环漏网也只是该支路判 0，不会爆栈。
    /// </summary>
    public static class CircuitSolver
    {
        /// <summary>递归深度上限（防御用）。正常关卡的链路长度远达不到。</summary>
        private const int MaxDepth = 512;

        // ═════════ 求解 ═════════

        /// <summary>重算全场供电：写入各 LinkData.Power、各电池的 ReceivedPower 与 IsLit。</summary>
        public static void Solve(LevelData level)
        {
            if (level == null) return;

            // ① 清空上一轮的求解缓存与结果
            foreach (var node in level.Nodes)
            {
                node.ReceivedPower = 0;
                node.IsLit = false;
                foreach (var pin in node.Pins)
                {
                    pin.OutPower = 0;
                    pin.OutPowerResolved = false;
                }
            }
            foreach (var link in level.Links)
                link.Power = 0;

            // ② 每条线携带的电量 = 其源 Pin 的输出量（记忆化递归，按 LinkId 稳定顺序）
            foreach (var link in level.Links)
                link.Power = ResolveOutPower(link.FromPin, 0);

            // ③ 电池汇总与点亮判定（按 NodeId 稳定顺序）
            foreach (var node in level.Nodes)
            {
                if (node.Def.NodeType != ENodeType.Condition) continue;
                int received = 0;
                foreach (var pin in node.Pins)
                {
                    if (pin.RuntimeDirection != EPinDirection.Input || pin.Link == null) continue;
                    received += pin.Link.Power;
                }
                node.ReceivedPower = received;
                node.IsLit = Judge(((ConditionNodeDef)node.Def).Conditions, received);
            }
        }

        /// <summary>
        /// 本 Pin 作为输出口送出多少电。结果记忆化在 PinData 上，同一轮 Solve 内只算一次。
        /// </summary>
        private static int ResolveOutPower(PinData pin, int depth)
        {
            if (pin == null) return 0;
            if (pin.OutPowerResolved) return pin.OutPower;

            if (depth > MaxDepth)
            {
                // 走到这里说明成环检测漏了。判 0 并报错，而不是让求解器爆栈
                Debug.LogError($"[修理电路] 供电求解递归超过 {MaxDepth} 层，疑似链路成环，" +
                               $"该支路按 0 电处理（节点 {pin.Owner.Def.name} 的第 {pin.IndexInNode} 号 Pin）");
                pin.OutPowerResolved = true;
                pin.OutPower = 0;
                return 0;
            }

            // 先置位再递归：万一有环，环上各 Pin 只会各算一次并读到 0，天然收敛为「环内不供电」
            pin.OutPowerResolved = true;

            int result = 0;
            switch (pin.Owner.Def.NodeType)
            {
                case ENodeType.Resource:
                    // 电源：本 Pin 的额定输出。多个输出 Pin 各供各的，节点没有总闸
                    result = Mathf.Max(0, pin.Def.MaxRate);
                    break;

                case ENodeType.Transit:
                    result = ResolveTransitOutput(pin, depth);
                    break;

                // Condition（电池）没有输出口，恒 0
            }

            pin.OutPower = result;
            return result;
        }

        /// <summary>中转件分组公式：floor(组内输入之和 / 组内输出口总数)。</summary>
        private static int ResolveTransitOutput(PinData pin, int depth)
        {
            int group = pin.Group;
            if (group < 0) return 0; // 未分组的中转 Pin 不导电（编辑器校验会报错）

            int inputSum = 0;
            int outputCount = 0;
            foreach (var peer in pin.Owner.Pins) // 按节点内索引，稳定顺序
            {
                if (peer.Group != group) continue;
                if (peer.RuntimeDirection == EPinDirection.Output)
                {
                    // 分母按**口总数**算，不管这个口有没有接线（§4.7 落地拍板）
                    outputCount++;
                }
                else if (peer.RuntimeDirection == EPinDirection.Input && peer.Link != null)
                {
                    inputSum += ResolveOutPower(peer.Link.FromPin, depth + 1);
                }
            }

            if (outputCount <= 0) return 0;
            return inputSum / outputCount; // 整数除法即向下取整，余数蒸发
        }

        /// <summary>点亮判定：多条之间「全部满足」；留空 = 恒点亮。</summary>
        private static bool Judge(List<ConditionEntry> conditions, int received)
        {
            if (conditions == null || conditions.Count == 0) return true;
            foreach (var entry in conditions)
            {
                if (entry == null) continue;
                if (received < entry.RequiredAmount) return false;
                if (received > entry.RequiredAmount && !entry.AllowExcess) return false;
            }
            return true;
        }

        // ═════════ 计分（§4.4）═════════

        /// <summary>点亮的电池数。</summary>
        public static int CountLit(LevelData level)
        {
            int lit = 0;
            foreach (var node in level.Nodes)
                if (node.Def.NodeType == ENodeType.Condition && node.IsLit)
                    lit++;
            return lit;
        }

        /// <summary>电池总数。</summary>
        public static int CountBatteries(LevelData level)
        {
            int total = 0;
            foreach (var node in level.Nodes)
                if (node.Def.NodeType == ENodeType.Condition)
                    total++;
            return total;
        }

        /// <summary>
        /// 本局得分（0~100）：点亮的电池数 / 电池总数 × 100，四舍五入。
        /// 按节点个数算，不按需求量加权、不给部分分——「点亮」是二元的。
        /// 用整数运算做四舍五入，不碰 float。
        /// </summary>
        public static int Score(LevelData level)
        {
            int total = CountBatteries(level);
            if (total <= 0) return 0; // 无电池的关卡是配置错误，编辑器校验会报错
            return (CountLit(level) * 100 + total / 2) / total;
        }

        // ═════════ 成环检测（供 LinkManager 建线时调用）═════════

        /// <summary>图顶点：中转件按 (节点, 分组) 拆开——十字件的两个分组互不连通，
        /// 按节点粒度检测会把「两条线各走一组的正常交叉」误判成环，那正好废掉十字件唯一的用途。</summary>
        private readonly struct Vertex : IEquatable<Vertex>
        {
            public readonly NodeData Node;
            public readonly int Group;

            public Vertex(NodeData node, int group)
            {
                Node = node;
                Group = group;
            }

            public bool Equals(Vertex other) => Node == other.Node && Group == other.Group;
            public override bool Equals(object obj) => obj is Vertex other && Equals(other);
            public override int GetHashCode() =>
                (Node != null ? Node.NodeId.GetHashCode() : 0) * 397 ^ Group;
        }

        /// <summary>
        /// 拟建链接 fromPin(输出侧) → toPin(输入侧) 是否会形成回路。
        /// 判据：从 toPin 所在顶点出发沿下游能否走回 fromPin 所在顶点。
        ///
        /// 只有中转件能被"穿过"——电源没有输入口、电池没有输出口，都进不了环。
        /// 因此源端不是中转件时直接判否。
        /// </summary>
        public static bool WouldCreateCycle(PinData fromPin, PinData toPin)
        {
            if (fromPin == null || toPin == null) return false;
            if (fromPin.Owner.Def.NodeType != ENodeType.Transit) return false;

            var target = new Vertex(fromPin.Owner, fromPin.Group);
            var stack = new Stack<Vertex>();
            var seen = new HashSet<Vertex>(); // 仅成员查询，不枚举（§11.2）

            stack.Push(new Vertex(toPin.Owner, toPin.Group));
            while (stack.Count > 0)
            {
                var vertex = stack.Pop();
                if (vertex.Equals(target)) return true;
                if (!seen.Add(vertex)) continue;

                var node = vertex.Node;
                if (node.Def.NodeType != ENodeType.Transit || vertex.Group < 0) continue;

                foreach (var pin in node.Pins)
                {
                    if (pin.Group != vertex.Group) continue;
                    if (pin.RuntimeDirection != EPinDirection.Output || pin.Link == null) continue;
                    var downstream = pin.Link.ToPin;
                    stack.Push(new Vertex(downstream.Owner, downstream.Group));
                }
            }
            return false;
        }
    }
}

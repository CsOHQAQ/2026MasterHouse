using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// 节点编辑器的数据操作与校验工具（仅编辑器）。
    /// 对 NodeDef 的所有修改统一走这里，保证 Undo 记录与各节点类型的 Pin 规则一致：
    /// - 电源：Pin 方向固定为输出，各 Pin 的 MaxRate 就是它供出的电量；
    /// - 电池：Pin 方向固定为输入，收到的电量按各输入口求和；
    /// - 中转件：Pin 按 PinGroup 分组，方向由策划配（十字件留「同步」，分流合流配死进出）。
    /// </summary>
    public static class NodeDefEditUtil
    {
        // ==================== 形状 ====================

        /// <summary>在形状中添加一个格子（已存在则忽略）。</summary>
        public static void PaintCell(NodeDef def, Vector2Int cell)
        {
            if (def.Shape.ContainsDelta(cell)) return;
            Undo.RecordObject(def, "绘制形状格");
            def.Shape.Grids.Add(new GridData { DeltaPosition = cell, Type = EGridType.Default });
            EditorUtility.SetDirty(def);
        }

        /// <summary>从形状中擦除一个格子（不存在则忽略）。</summary>
        public static void EraseCell(NodeDef def, Vector2Int cell)
        {
            int idx = def.Shape.Grids.FindIndex(g => g.DeltaPosition == cell);
            if (idx < 0) return;
            Undo.RecordObject(def, "擦除形状格");
            def.Shape.Grids.RemoveAt(idx);
            EditorUtility.SetDirty(def);
        }

        /// <summary>
        /// 归一化：把形状平移到以最左下格为 (0,0)（x、y 分别取最小值），
        /// 所有 Pin 的本地格随同平移。保存前必须调用。
        /// </summary>
        public static void Normalize(NodeDef def)
        {
            var grids = def.Shape.Grids;
            if (grids.Count == 0) return;

            int minX = int.MaxValue, minY = int.MaxValue;
            foreach (var g in grids)
            {
                minX = Mathf.Min(minX, g.DeltaPosition.x);
                minY = Mathf.Min(minY, g.DeltaPosition.y);
            }
            if (minX == 0 && minY == 0) return;

            Undo.RecordObject(def, "归一化形状原点");
            var offset = new Vector2Int(-minX, -minY);
            for (int i = 0; i < grids.Count; i++)
            {
                var g = grids[i];
                g.DeltaPosition += offset;
                grids[i] = g;
            }
            foreach (var p in def.Pins)
                p.LocalCell += offset;
            EditorUtility.SetDirty(def);
        }

        /// <summary>节点类型的中文短名（各编辑器的列表/标签共用）。</summary>
        public static string TypeName(NodeDef def)
        {
            switch (def)
            {
                case ResourceNodeDef _: return "电源";
                case TransitNodeDef _: return "中转";
                case ConditionNodeDef _: return "电池";
                default: return "未知";
            }
        }

        // ==================== Pin 规则 ====================

        /// <summary>三种类型现在都可自由增删 Pin：电源多口供电、电池多口收电、中转件靠分组组织。</summary>
        public static bool AllowFreePinEdit(NodeDef def) => def != null;

        /// <summary>
        /// 按节点类型返回 Pin 的固定方向。
        /// 中转件返回 null——它的方向由策划逐 Pin 配：十字件留「同步」(None)，
        /// 分流器配 1 入 N 出、合流器配 N 入 1 出（§4.7）。
        /// </summary>
        public static EPinDirection? ForcedDirection(NodeDef def)
        {
            if (def is ResourceNodeDef) return EPinDirection.Output;
            if (def is ConditionNodeDef) return EPinDirection.Input;
            return null;
        }

        /// <summary>添加一个 Pin。方向按类型固定（电源=输出、电池=输入），中转件默认「同步」并归入 0 组。</summary>
        public static void AddPin(NodeDef def)
        {
            if (!AllowFreePinEdit(def)) return;
            Undo.RecordObject(def, "添加 Pin");
            var forced = ForcedDirection(def);
            var layout = new PinLayout
            {
                Pin = new PinDef
                {
                    Direction = forced ?? EPinDirection.None,
                    MaxRate = 1,
                    PinGroup = def is TransitNodeDef ? 0 : -1,
                },
                LocalCell = Vector2Int.zero,
                // 输入口默认朝左接线，输出口朝右
                Facing = forced == EPinDirection.Input ? EDirection4.Left : EDirection4.Right,
            };
            AutoFacing(def, layout);
            def.Pins.Add(layout);
            EditorUtility.SetDirty(def);
        }

        /// <summary>
        /// 中转件：一次性添加一整个分组（§4.7）。
        /// 十字件 = 两次调用 (1,1)；分流器 = 一次 (1,N)；合流器 = 一次 (N,1)。
        /// 十字件那种 1 进 1 出的组允许留「同步」方向，交给运行时定向——
        /// 所以 inputs==1 && outputs==1 时不写死方向。
        /// </summary>
        public static void AddTransitGroup(TransitNodeDef def, int inputs, int outputs)
        {
            inputs = Mathf.Max(1, inputs);
            outputs = Mathf.Max(1, outputs);
            Undo.RecordObject(def, "添加中转分组");

            int group = NextFreeGroup(def);
            bool bidirectional = inputs == 1 && outputs == 1; // 十字件：方向留给运行时

            for (int i = 0; i < inputs; i++)
                def.Pins.Add(NewGroupPin(group, bidirectional ? EPinDirection.None : EPinDirection.Input,
                    EDirection4.Left));
            for (int i = 0; i < outputs; i++)
                def.Pins.Add(NewGroupPin(group, bidirectional ? EPinDirection.None : EPinDirection.Output,
                    EDirection4.Right));

            EditorUtility.SetDirty(def);
        }

        static PinLayout NewGroupPin(int group, EPinDirection direction, EDirection4 facing) =>
            new PinLayout
            {
                Pin = new PinDef { Direction = direction, MaxRate = 1, PinGroup = group },
                LocalCell = Vector2Int.zero,
                Facing = facing,
            };

        /// <summary>当前未被占用的最小分组号。</summary>
        public static int NextFreeGroup(NodeDef def)
        {
            int max = -1;
            foreach (var p in def.Pins)
                if (p?.Pin != null)
                    max = Mathf.Max(max, p.Pin.PinGroup);
            return max + 1;
        }

        /// <summary>
        /// 删除 Pin。分组号与 Pin 下标已经解耦（不再是"互指的配对索引"），
        /// 所以删一个 Pin 不必连带删伙伴、也不用重映射任何索引——直接移除即可。
        /// </summary>
        public static void RemovePin(NodeDef def, int index)
        {
            if (index < 0 || index >= def.Pins.Count) return;
            Undo.RecordObject(def, "删除 Pin");
            def.Pins.RemoveAt(index);
            EditorUtility.SetDirty(def);
        }

        /// <summary>
        /// 按类型规则一键修正所有 Pin 的方向字段。
        /// 只对方向被类型定死的电源（输出）与电池（输入）生效；中转件的方向由策划逐 Pin 配，本方法直接返回。
        /// </summary>
        public static void FixPinDirections(NodeDef def)
        {
            var forced = ForcedDirection(def);
            if (forced == null) return;
            Undo.RecordObject(def, "修正 Pin 方向");
            foreach (var p in def.Pins)
                p.Pin.Direction = forced.Value;
            EditorUtility.SetDirty(def);
        }

        // ==================== Pin 摆放 ====================

        /// <summary>把 Pin 摆到指定格，并在朝向被形状挡住时自动转向外侧。</summary>
        public static void PlacePin(NodeDef def, int index, Vector2Int cell)
        {
            if (index < 0 || index >= def.Pins.Count) return;
            Undo.RecordObject(def, "摆放 Pin");
            def.Pins[index].LocalCell = cell;
            AutoFacing(def, def.Pins[index]);
            EditorUtility.SetDirty(def);
        }

        /// <summary>按 上→右→下→左 顺序切换 Pin 朝向。</summary>
        public static void CycleFacing(NodeDef def, int index)
        {
            if (index < 0 || index >= def.Pins.Count) return;
            Undo.RecordObject(def, "切换 Pin 朝向");
            def.Pins[index].Facing = (EDirection4)(((int)def.Pins[index].Facing + 1) % 4);
            EditorUtility.SetDirty(def);
        }

        /// <summary>若当前朝向的相邻格仍在形状内（连线无法接入），自动转到第一个朝向形状外的方向。</summary>
        static void AutoFacing(NodeDef def, PinLayout layout)
        {
            if (!def.Shape.ContainsDelta(layout.LocalCell + Direction4.ToOffset(layout.Facing))) return;
            for (int d = 0; d < 4; d++)
            {
                if (!def.Shape.ContainsDelta(layout.LocalCell + Direction4.Offsets[d]))
                {
                    layout.Facing = (EDirection4)d;
                    return;
                }
            }
        }

        // ==================== 校验 ====================

        public static string DirName(EPinDirection dir)
        {
            switch (dir)
            {
                case EPinDirection.Input: return "输入";
                case EPinDirection.Output: return "输出";
                default: return "同步"; // None：方向随第一条接上的线确定（§4.7 十字件）
            }
        }

        /// <summary>汇总当前配置的所有问题，供编辑器面板展示。</summary>
        public static List<string> Validate(NodeDef def)
        {
            var issues = new List<string>();

            if (def.Shape.Grids.Count == 0)
                issues.Add("形状为空：请先在画布上绘制格子。");

            // 重复格（画布不会产生，防手改数据）
            var seen = new HashSet<Vector2Int>();
            foreach (var g in def.Shape.Grids)
                if (!seen.Add(g.DeltaPosition))
                    issues.Add($"形状存在重复格 {g.DeltaPosition}。");

            for (int i = 0; i < def.Pins.Count; i++)
            {
                var p = def.Pins[i];
                if (!def.Shape.ContainsDelta(p.LocalCell))
                    issues.Add($"Pin #{i} 所在格 {p.LocalCell} 不在形状内。");
                else if (def.Shape.ContainsDelta(p.LocalCell + Direction4.ToOffset(p.Facing)))
                    issues.Add($"Pin #{i} 朝向的相邻格仍在形状内，连线无法从该侧接入（§4.2）。");

                for (int j = i + 1; j < def.Pins.Count; j++)
                    if (def.Pins[j].LocalCell == p.LocalCell && def.Pins[j].Facing == p.Facing)
                        issues.Add($"Pin #{i} 与 Pin #{j} 的位置与朝向完全重合。");
            }

            var forced = ForcedDirection(def);
            if (forced != null)
                for (int i = 0; i < def.Pins.Count; i++)
                    if (def.Pins[i].Pin.Direction != forced.Value)
                        issues.Add($"Pin #{i} 方向应为「{DirName(forced.Value)}」，可点「按类型规则修正 Pin 方向」。");

            switch (def)
            {
                case ResourceNodeDef _:
                    if (def.Pins.Count == 0)
                        issues.Add("电源没有任何输出口，供不出电。");
                    for (int i = 0; i < def.Pins.Count; i++)
                        if (def.Pins[i].Pin.MaxRate <= 0)
                            issues.Add($"Pin #{i} 的输出电量应大于 0。");
                    break;

                case ConditionNodeDef c:
                    if (def.Pins.Count == 0)
                        issues.Add("电池没有任何输入口，永远收不到电。");
                    if (c.Conditions.Count == 0)
                        issues.Add("未配置任何点亮条件：该电池恒亮，等同于白送分。");
                    for (int i = 0; i < c.Conditions.Count; i++)
                    {
                        var entry = c.Conditions[i];
                        if (entry == null)
                        {
                            issues.Add($"条件 #{i} 为空。");
                            continue;
                        }
                        if (entry.RequiredAmount <= 0)
                            issues.Add($"条件 #{i} 的需求电量应大于 0。");
                    }
                    break;

                case TransitNodeDef _:
                    ValidateTransitGroups(def, issues);
                    break;
            }

            return issues;
        }

        /// <summary>
        /// 中转件的分组校验（§4.7）。求解公式是
        ///     每个输出口 = floor(组内输入之和 / 组内输出口总数)
        /// 所以真正致命的只有「没分组」和「组里缺进或缺出」。
        /// </summary>
        static void ValidateTransitGroups(NodeDef def, List<string> issues)
        {
            if (def.Pins.Count == 0)
            {
                issues.Add("中转件没有任何 Pin。");
                return;
            }

            // 分组号 → (输入数, 输出数, 未定向数)。按分组号排序输出，保证提示顺序稳定（§11.2）
            var groups = new SortedDictionary<int, Vector3Int>();
            for (int i = 0; i < def.Pins.Count; i++)
            {
                int g = def.Pins[i].Pin.PinGroup;
                if (g < 0)
                {
                    issues.Add($"Pin #{i} 没有分组（组号 {g}）：不属于任何分组的中转口不导电。");
                    continue;
                }
                groups.TryGetValue(g, out var counts);
                switch (def.Pins[i].Pin.Direction)
                {
                    case EPinDirection.Input: counts.x++; break;
                    case EPinDirection.Output: counts.y++; break;
                    default: counts.z++; break;
                }
                groups[g] = counts;
            }

            foreach (var pair in groups)
            {
                int group = pair.Key;
                int inputs = pair.Value.x, outputs = pair.Value.y, undirected = pair.Value.z;

                if (undirected > 0)
                {
                    // 「同步」方向只在 1 进 1 出的十字件上成立：整组恰好两个口、且都留同步
                    if (undirected != 2 || inputs + outputs > 0)
                        issues.Add($"第 {group} 组混用了「同步」方向：只有恰好两个口且都留同步的组" +
                                   $"（十字件那种 1 进 1 出）才能交给运行时定向，其余请配死进出。");
                    continue;
                }

                if (inputs == 0)
                    issues.Add($"第 {group} 组没有输入口，这组永远送不出电。");
                if (outputs == 0)
                    issues.Add($"第 {group} 组没有输出口，进来的电会原地消失。");
                if (inputs > 1 && outputs > 1)
                    issues.Add($"第 {group} 组是 {inputs} 进 {outputs} 出：公式照样成立" +
                               $"（每个出口 = floor(总输入 / {outputs})），但本轮只验收了一对多与多对一，" +
                               $"这种配法未经测试。");
            }
        }
    }
}

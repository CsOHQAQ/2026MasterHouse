using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// 节点编辑器的数据操作与校验工具（仅编辑器，待定 #11 的一部分）。
    /// 对 NodeDef 的所有修改统一走这里，保证 Undo 记录与各节点类型的 Pin 规则一致：
    /// - 资源型/仓库型：可自由增删 Pin 与物资种类，方向固定（资源=输出，仓库=输入）；
    /// - 中转型：Pin 必须成对配置，互为配对 Pin，物资/方向留空由运行时同步（§6.3）；
    /// - 加工型：Pin 数量与物资由配方的输入/产出一一对应决定，不允许手动增删。
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

        // ==================== Pin 规则 ====================

        /// <summary>该类型是否允许自由增删 Pin（§7：仓库/资源可自由调整数量与物资种类）。</summary>
        public static bool AllowFreePinEdit(NodeDef def)
        {
            return def is ResourceNodeDef || def is StorageNodeDef;
        }

        /// <summary>
        /// 按节点类型返回 Pin 的固定方向；加工型方向由配方逐 Pin 决定，返回 null。
        /// 中转型固定 None——方向运行时随连接同步（§6.3）。
        /// </summary>
        public static EPinDirection? ForcedDirection(NodeDef def)
        {
            if (def is ResourceNodeDef) return EPinDirection.Output;
            if (def is StorageNodeDef) return EPinDirection.Input;
            if (def is TransitNodeDef) return EPinDirection.None;
            return null;
        }

        /// <summary>资源型/仓库型：添加一个 Pin，方向按类型固定，资源型默认物资为产出物资。</summary>
        public static void AddPin(NodeDef def)
        {
            if (!AllowFreePinEdit(def)) return;
            Undo.RecordObject(def, "添加 Pin");
            var layout = new PinLayout
            {
                Pin = new PinDef
                {
                    ItemType = def is ResourceNodeDef r ? r.OutputItem : null,
                    Direction = ForcedDirection(def) ?? EPinDirection.Output,
                    MaxRate = 1,
                    PairedPinIndex = -1,
                },
                LocalCell = Vector2Int.zero,
                Facing = def is StorageNodeDef ? EDirection4.Left : EDirection4.Right,
            };
            AutoFacing(def, layout);
            def.Pins.Add(layout);
            EditorUtility.SetDirty(def);
        }

        /// <summary>中转型：一次添加一对互为配对的 Pin（物资/方向留空，运行时同步）。</summary>
        public static void AddTransitPair(TransitNodeDef def)
        {
            Undo.RecordObject(def, "添加配对 Pin");
            int a = def.Pins.Count;
            def.Pins.Add(new PinLayout
            {
                Pin = new PinDef { ItemType = null, Direction = EPinDirection.None, MaxRate = 1, PairedPinIndex = a + 1 },
                LocalCell = Vector2Int.zero,
                Facing = EDirection4.Left,
            });
            def.Pins.Add(new PinLayout
            {
                Pin = new PinDef { ItemType = null, Direction = EPinDirection.None, MaxRate = 1, PairedPinIndex = a },
                LocalCell = Vector2Int.zero,
                Facing = EDirection4.Right,
            });
            EditorUtility.SetDirty(def);
        }

        /// <summary>
        /// 删除 Pin。中转型会连同其配对 Pin 一起删除，并修正其余 Pin 的配对索引；
        /// 加工型不允许删除（由配方决定，UI 不应调到这里）。
        /// </summary>
        public static void RemovePin(NodeDef def, int index)
        {
            if (index < 0 || index >= def.Pins.Count) return;
            if (def is ProcessorNodeDef) return;

            Undo.RecordObject(def, "删除 Pin");
            if (def is TransitNodeDef)
            {
                var removed = new HashSet<int> { index };
                int pair = def.Pins[index].Pin.PairedPinIndex;
                if (pair >= 0 && pair < def.Pins.Count)
                    removed.Add(pair);

                // 旧索引 → 新索引映射，修正剩余 Pin 的配对指向
                var map = new Dictionary<int, int>();
                int next = 0;
                for (int i = 0; i < def.Pins.Count; i++)
                    if (!removed.Contains(i))
                        map[i] = next++;

                var newPins = new List<PinLayout>();
                for (int i = 0; i < def.Pins.Count; i++)
                {
                    if (removed.Contains(i)) continue;
                    var p = def.Pins[i];
                    p.Pin.PairedPinIndex = map.TryGetValue(p.Pin.PairedPinIndex, out int ni) ? ni : -1;
                    newPins.Add(p);
                }
                def.Pins = newPins;
            }
            else
            {
                def.Pins.RemoveAt(index);
            }
            EditorUtility.SetDirty(def);
        }

        /// <summary>
        /// 加工型：Pin 完全由配方决定——每条输入物资生成一个输入 Pin、每条产出物资生成
        /// 一个输出 Pin（一一对应）。同物资同方向的已有 Pin 保留摆位与速率配置。
        /// </summary>
        public static void SyncProcessorPins(ProcessorNodeDef def)
        {
            Undo.RecordObject(def, "按配方同步 Pin");
            var old = new List<PinLayout>(def.Pins);
            var result = new List<PinLayout>();
            if (def.Recipe != null)
            {
                BuildPinsFor(def.Recipe.Inputs, EPinDirection.Input, EDirection4.Left, old, result);
                BuildPinsFor(def.Recipe.Outputs, EPinDirection.Output, EDirection4.Right, old, result);
            }
            def.Pins = result;
            EditorUtility.SetDirty(def);
        }

        static void BuildPinsFor(List<ItemStack> stacks, EPinDirection dir, EDirection4 defaultFacing,
            List<PinLayout> old, List<PinLayout> result)
        {
            foreach (var s in stacks)
            {
                if (s.Item == null) continue;
                int idx = old.FindIndex(p => p?.Pin != null && p.Pin.Direction == dir && p.Pin.ItemType == s.Item);
                if (idx >= 0)
                {
                    result.Add(old[idx]);
                    old.RemoveAt(idx);
                }
                else
                {
                    result.Add(new PinLayout
                    {
                        Pin = new PinDef { ItemType = s.Item, Direction = dir, MaxRate = 1, PairedPinIndex = -1 },
                        LocalCell = Vector2Int.zero,
                        Facing = defaultFacing,
                    });
                }
            }
        }

        /// <summary>加工型 Pin 是否与配方一一对应（数量、物资、方向均匹配）。</summary>
        public static bool ProcessorPinsInSync(ProcessorNodeDef def)
        {
            var expect = new List<(ItemDef item, EPinDirection dir)>();
            if (def.Recipe != null)
            {
                foreach (var s in def.Recipe.Inputs)
                    if (s.Item != null) expect.Add((s.Item, EPinDirection.Input));
                foreach (var s in def.Recipe.Outputs)
                    if (s.Item != null) expect.Add((s.Item, EPinDirection.Output));
            }
            if (def.Pins.Count != expect.Count) return false;
            foreach (var p in def.Pins)
            {
                int i = expect.FindIndex(e => e.item == p.Pin.ItemType && e.dir == p.Pin.Direction);
                if (i < 0) return false;
                expect.RemoveAt(i);
            }
            return true;
        }

        /// <summary>按类型规则一键修正所有 Pin 的方向字段（加工型请走 SyncProcessorPins）。</summary>
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
                default: return "同步"; // None：运行时随连接同步（§6.3）
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
                case ResourceNodeDef r:
                    if (r.OutputItem == null)
                        issues.Add("未配置产出物资。");
                    for (int i = 0; i < def.Pins.Count; i++)
                        if (def.Pins[i].Pin.ItemType == null)
                            issues.Add($"Pin #{i} 未配置物资种类。");
                    break;

                case StorageNodeDef _:
                    for (int i = 0; i < def.Pins.Count; i++)
                        if (def.Pins[i].Pin.ItemType == null)
                            issues.Add($"Pin #{i} 未配置物资种类。");
                    break;

                case ProcessorNodeDef proc:
                    if (proc.Recipe == null)
                        issues.Add("未配置配方——加工节点的 Pin 由配方决定（待定 #3：先按单条配方）。");
                    else if (!ProcessorPinsInSync(proc))
                        issues.Add("Pin 与配方不一致，请点「按配方同步 Pin」。");
                    break;

                case TransitNodeDef _:
                    if (def.Pins.Count % 2 != 0)
                        issues.Add("中转节点的 Pin 应成对出现，当前为奇数个。");
                    for (int i = 0; i < def.Pins.Count; i++)
                    {
                        int pi = def.Pins[i].Pin.PairedPinIndex;
                        if (pi < 0 || pi >= def.Pins.Count || pi == i)
                            issues.Add($"Pin #{i} 的配对索引无效（{pi}）。");
                        else if (def.Pins[pi].Pin.PairedPinIndex != i)
                            issues.Add($"Pin #{i} 与 Pin #{pi} 的配对不互指。");
                    }
                    break;
            }

            return issues;
        }
    }
}

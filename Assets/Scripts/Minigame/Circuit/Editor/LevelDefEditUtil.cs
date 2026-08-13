using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// 关卡编辑器的数据操作与校验工具（仅编辑器，待定 #11 的一部分）。
    /// 画布形状与预置节点的所有修改统一走这里，保证 Undo 记录一致。
    /// 合法性问题（越界/重叠）一律警告不阻止——策划可先排布局再调画布。
    /// </summary>
    public static class LevelDefEditUtil
    {
        // ==================== 画布形状 ====================

        public static void PaintCell(LevelDef def, Vector2Int cell)
        {
            if (def.Canvas.ContainsDelta(cell)) return;
            Undo.RecordObject(def, "绘制画布格");
            def.Canvas.Grids.Add(new GridData { DeltaPosition = cell, Type = EGridType.Default });
            EditorUtility.SetDirty(def);
        }

        public static void EraseCell(LevelDef def, Vector2Int cell)
        {
            int idx = def.Canvas.Grids.FindIndex(g => g.DeltaPosition == cell);
            if (idx < 0) return;
            Undo.RecordObject(def, "擦除画布格");
            def.Canvas.Grids.RemoveAt(idx);
            EditorUtility.SetDirty(def);
        }

        /// <summary>矩形区域整片填充或擦除（框选工具与「生成 W×H」按钮共用）。</summary>
        public static void FillRect(LevelDef def, Vector2Int a, Vector2Int b, bool erase)
        {
            var min = Vector2Int.Min(a, b);
            var max = Vector2Int.Max(a, b);
            Undo.RecordObject(def, erase ? "矩形擦除画布" : "矩形填充画布");

            if (erase)
            {
                def.Canvas.Grids.RemoveAll(g =>
                    g.DeltaPosition.x >= min.x && g.DeltaPosition.x <= max.x &&
                    g.DeltaPosition.y >= min.y && g.DeltaPosition.y <= max.y);
            }
            else
            {
                var existing = new HashSet<Vector2Int>();
                foreach (var g in def.Canvas.Grids)
                    existing.Add(g.DeltaPosition);
                for (int y = min.y; y <= max.y; y++)
                    for (int x = min.x; x <= max.x; x++)
                    {
                        var c = new Vector2Int(x, y);
                        if (existing.Contains(c)) continue;
                        def.Canvas.Grids.Add(new GridData { DeltaPosition = c, Type = EGridType.Default });
                    }
            }
            EditorUtility.SetDirty(def);
        }

        /// <summary>把画布平移到以最左下格为 (0,0)，预置节点的放置格随同平移。</summary>
        public static void Normalize(LevelDef def)
        {
            var grids = def.Canvas.Grids;
            if (grids.Count == 0) return;

            int minX = int.MaxValue, minY = int.MaxValue;
            foreach (var g in grids)
            {
                minX = Mathf.Min(minX, g.DeltaPosition.x);
                minY = Mathf.Min(minY, g.DeltaPosition.y);
            }
            if (minX == 0 && minY == 0) return;

            Undo.RecordObject(def, "归一化画布原点");
            var offset = new Vector2Int(-minX, -minY);
            for (int i = 0; i < grids.Count; i++)
            {
                var g = grids[i];
                g.DeltaPosition += offset;
                grids[i] = g;
            }
            foreach (var e in def.PresetNodes)
                e.Cell += offset;
            EditorUtility.SetDirty(def);
        }

        // ==================== 预置节点 ====================

        public static void AddPreset(LevelDef def)
        {
            Undo.RecordObject(def, "添加预置节点");
            def.PresetNodes.Add(new PresetNodeEntry
            {
                Node = null,
                Cell = Vector2Int.zero,
                CanMove = false,
                CanDelete = false,
            });
            EditorUtility.SetDirty(def);
        }

        public static void RemovePreset(LevelDef def, int index)
        {
            if (index < 0 || index >= def.PresetNodes.Count) return;
            Undo.RecordObject(def, "删除预置节点");
            def.PresetNodes.RemoveAt(index);
            EditorUtility.SetDirty(def);
        }

        public static void PlacePreset(LevelDef def, int index, Vector2Int cell)
        {
            if (index < 0 || index >= def.PresetNodes.Count) return;
            Undo.RecordObject(def, "摆放预置节点");
            def.PresetNodes[index].Cell = cell;
            EditorUtility.SetDirty(def);
        }

        /// <summary>返回占格覆盖指定格的预置节点索引（取最小索引），无则 -1。画布点击选中用。</summary>
        public static int PresetAt(LevelDef def, Vector2Int cell)
        {
            for (int i = 0; i < def.PresetNodes.Count; i++)
            {
                var e = def.PresetNodes[i];
                if (e.Node != null && e.Node.Shape.ContainsDelta(cell - e.Cell))
                    return i;
            }
            return -1;
        }

        // ==================== 校验（警告不阻止） ====================

        public static List<string> Validate(LevelDef def)
        {
            var issues = new List<string>();

            if (def.Canvas.Grids.Count == 0)
                issues.Add("画布为空：请先绘制画布形状。");

            // 画布重复格（防手改数据）
            var canvasSet = new HashSet<Vector2Int>();
            foreach (var g in def.Canvas.Grids)
                if (!canvasSet.Add(g.DeltaPosition))
                    issues.Add($"画布存在重复格 {g.DeltaPosition}。");

            // 预置节点：越界 / 相互重叠
            var owner = new Dictionary<Vector2Int, int>();
            var reportedPairs = new HashSet<long>();
            for (int i = 0; i < def.PresetNodes.Count; i++)
            {
                var e = def.PresetNodes[i];
                if (e.Node == null)
                {
                    issues.Add($"预置节点 #{i} 未指定节点。");
                    continue;
                }
                if (e.Node.Shape.Grids.Count == 0)
                {
                    issues.Add($"预置节点 #{i}「{e.Node.name}」形状为空，请先在节点编辑器中绘制。");
                    continue;
                }

                int outside = 0;
                foreach (var cell in e.Node.Shape.CellsAt(e.Cell))
                {
                    if (!canvasSet.Contains(cell)) outside++;
                    if (owner.TryGetValue(cell, out int other))
                    {
                        long pairKey = (long)other * 100000 + i;
                        if (reportedPairs.Add(pairKey))
                            issues.Add($"预置节点 #{other} 与 #{i} 占格重叠。");
                    }
                    else
                    {
                        owner[cell] = i;
                    }
                }
                if (outside > 0)
                    issues.Add($"预置节点 #{i}「{e.Node.name}」有 {outside} 格越出画布。");
            }

            // 可建列表
            var seenBuildable = new HashSet<NodeDef>();
            for (int i = 0; i < def.BuildableNodes.Count; i++)
            {
                var b = def.BuildableNodes[i];
                if (b.Node == null)
                {
                    issues.Add($"可建列表第 {i + 1} 条未指定节点。");
                    continue;
                }
                if (!seenBuildable.Add(b.Node))
                    issues.Add($"可建列表中「{b.Node.name}」重复出现。");
                if (b.MaxCount < 1)
                    issues.Add($"可建「{b.Node.name}」数量上限应 ≥ 1。");
                if (!(b.Node is TransitNodeDef))
                    issues.Add($"可建列表里的「{b.Node.name}」不是中转件——电源与电池是题面，" +
                               $"玩家永远摆不出来，本条无效，请删除。");
            }

            ValidateCircuit(def, issues);
            return issues;
        }

        /// <summary>
        /// 电路层面的关卡校验（小游戏说明 §5.4）：
        /// 报错 —— 画布为空 / 没有电源 / 没有电池；
        /// 警告 —— 电源总供电 &lt; 电池总需求（本关不可能全亮）、导线预算小到显然连不通。
        /// </summary>
        static void ValidateCircuit(LevelDef def, List<string> issues)
        {
            int sources = 0, batteries = 0;
            int totalSupply = 0, totalDemand = 0;

            foreach (var e in def.PresetNodes)
            {
                if (e?.Node == null) continue;
                switch (e.Node)
                {
                    case ResourceNodeDef _:
                        sources++;
                        foreach (var layout in e.Node.Pins)
                            if (layout?.Pin != null && layout.Pin.Direction == EPinDirection.Output)
                                totalSupply += Mathf.Max(0, layout.Pin.MaxRate);
                        break;

                    case ConditionNodeDef battery:
                        batteries++;
                        foreach (var entry in battery.Conditions)
                            if (entry != null)
                                totalDemand += Mathf.Max(0, entry.RequiredAmount);
                        break;
                }
            }

            if (sources == 0)
                issues.Add("本关没有预置任何电源，一个电池也点不亮。");
            if (batteries == 0)
                issues.Add("本关没有预置任何电池：无从计分（分数 = 点亮数 / 电池总数），本关不可玩。");

            if (sources > 0 && batteries > 0 && totalSupply < totalDemand)
                issues.Add($"电源总供电 {totalSupply} < 电池总需求 {totalDemand}：本关**不可能全亮**，" +
                           $"满分无法达成。若是有意设计的取舍关请忽略本条。");

            // 计分粒度提醒：分数只有 (电池数 + 1) 种取值
            if (batteries > 0 && batteries <= 3)
                issues.Add($"本关只有 {batteries} 个电池，分数只可能是 " +
                           $"{ScoreLadder(batteries)} 这几个值。想要更细的评分梯度就多放电池。");

            if (def.MaxLinkCells > 0)
            {
                int canvasCells = def.Canvas.Grids.Count;
                int occupied = 0;
                foreach (var e in def.PresetNodes)
                    if (e?.Node?.Shape != null)
                        occupied += e.Node.Shape.Grids.Count;
                int free = canvasCells - occupied;
                if (def.MaxLinkCells > free)
                    issues.Add($"导线预算 {def.MaxLinkCells} 格超过了画布的空闲格数 {free}，等同于不限。");
                else if (batteries > 0 && def.MaxLinkCells < batteries * 2)
                    issues.Add($"导线预算只有 {def.MaxLinkCells} 格，而本关有 {batteries} 个电池" +
                               $"（每个至少要 2 格线才接得上）：多半连不通。");
            }
        }

        /// <summary>可能出现的分数列表，用于粒度提醒（与 CircuitSolver.Score 同口径的整数四舍五入）。</summary>
        static string ScoreLadder(int batteries)
        {
            var parts = new List<string>();
            for (int lit = 0; lit <= batteries; lit++)
                parts.Add(((lit * 100 + batteries / 2) / batteries).ToString());
            return string.Join("/", parts);
        }
    }
}

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

        /// <summary>把画布平移到以最左下格为 (0,0)，预置节点的放置格随同平移（WorldOrigin 不动）。</summary>
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
                if (b.Node is TransitNodeDef)
                    issues.Add($"可建列表包含中转节点「{b.Node.name}」——§7：中转节点位置由策划在关卡中预置，请确认是否有意。");
                if (b.Node is ConditionNodeDef)
                    issues.Add($"可建列表包含条件节点「{b.Node.name}」——条件节点只能预置，玩家永远造不出来，本条无效，请删除。");
            }

            // 家具效果产出（关卡生效后持续产出到玩家仓库）
            for (int i = 0; i < def.Outputs.Count; i++)
            {
                var o = def.Outputs[i];
                if (o == null || o.Item == null)
                {
                    issues.Add($"产出第 {i + 1} 条未指定物资。");
                    continue;
                }
                if (o.Amount <= 0)
                    issues.Add($"产出「{o.Item.name}」的数量应 ≥ 1。");
                if (o.TicksPerOutput <= 0)
                    issues.Add($"产出「{o.Item.name}」的间隔应 ≥ 1 tick。");
            }

            // 生效判据提示：没有条件节点的关卡恒生效，家具摆下去就在产出
            bool hasCondition = false;
            foreach (var e in def.PresetNodes)
                if (e.Node is ConditionNodeDef)
                {
                    hasCondition = true;
                    break;
                }
            if (!hasCondition && def.Outputs.Count > 0)
                issues.Add("本关没有预置条件节点：家具将**恒定生效**、摆下即产出。若期望「修好才生效」，请预置条件节点。");

            return issues;
        }
    }
}

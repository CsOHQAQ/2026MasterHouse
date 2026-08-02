using System.Collections.Generic;
using UnityEngine;

namespace MasterPotion
{
    /// <summary>
    /// 画布：由若干个 1x1 单元格组成。单元格 (x,y) 覆盖世界区域 [x,x+1)x[y,y+1)。
    /// 负责单元格增删、节点占用登记、坐标换算与格子视觉。
    /// 任何格子/占用变化都会使 Version 自增，供连线等依赖方按需重算。
    /// </summary>
    public class BoardGrid : MonoBehaviour
    {
        public static BoardGrid Instance { get; private set; }

        /// <summary>画布内容（格子或占用）每次变化 +1。</summary>
        public static int Version { get; private set; }

        [Tooltip("启动时生成的初始矩形画布尺寸（单元格数），以原点为中心")]
        public int initialWidth = 26;
        public int initialHeight = 14;

        private readonly HashSet<Vector2Int> cells = new();
        private readonly Dictionary<Vector2Int, SpriteRenderer> cellVisuals = new();
        private readonly Dictionary<Vector2Int, NodeBase> occupants = new();
        private readonly Dictionary<NodeBase, (Vector2Int origin, Vector2Int size)> nodeAreas = new();

        private Transform cellRoot;

        public IEnumerable<Vector2Int> Cells => cells;

        private void Awake()
        {
            Instance = this;
            cellRoot = new GameObject("Cells").transform;
            cellRoot.SetParent(transform, false);

            // 初始矩形画布（以原点为中心）
            int x0 = -initialWidth / 2, y0 = -initialHeight / 2;
            for (int x = x0; x < x0 + initialWidth; x++)
                for (int y = y0; y < y0 + initialHeight; y++)
                    AddCell(new Vector2Int(x, y));
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ---------- 坐标换算 ----------

        public static Vector2Int WorldToCell(Vector2 world) =>
            new Vector2Int(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.y));

        public static Vector3 CellCenter(Vector2Int cell) =>
            new Vector3(cell.x + 0.5f, cell.y + 0.5f, 0f);

        /// <summary>以 origin 为左下角、占 size 个格子的区域的世界中心。</summary>
        public static Vector3 AreaCenter(Vector2Int origin, Vector2Int size) =>
            new Vector3(origin.x + size.x * 0.5f, origin.y + size.y * 0.5f, 0f);

        /// <summary>把期望的世界中心吸附成格子区域左下角。</summary>
        public static Vector2Int SnapOrigin(Vector2 desiredCenter, Vector2Int size) =>
            new Vector2Int(
                Mathf.RoundToInt(desiredCenter.x - size.x * 0.5f),
                Mathf.RoundToInt(desiredCenter.y - size.y * 0.5f));

        // ---------- 单元格增删 ----------

        public bool HasCell(Vector2Int cell) => cells.Contains(cell);

        public bool AddCell(Vector2Int cell)
        {
            if (!cells.Add(cell)) return false;
            CreateCellVisual(cell);
            Version++;
            return true;
        }

        /// <summary>移除单元格；被节点占用时拒绝。</summary>
        public bool TryRemoveCell(Vector2Int cell)
        {
            if (!cells.Contains(cell) || occupants.ContainsKey(cell)) return false;
            cells.Remove(cell);
            if (cellVisuals.TryGetValue(cell, out var sr))
            {
                if (sr != null) Destroy(sr.gameObject);
                cellVisuals.Remove(cell);
            }
            Version++;
            return true;
        }

        // ---------- 节点占用 ----------

        public bool IsOccupied(Vector2Int cell) => occupants.ContainsKey(cell);

        public NodeBase NodeAt(Vector2Int cell) =>
            occupants.TryGetValue(cell, out var n) ? n : null;

        public bool AreaOnBoard(Vector2Int origin, Vector2Int size)
        {
            for (int x = 0; x < size.x; x++)
                for (int y = 0; y < size.y; y++)
                    if (!cells.Contains(new Vector2Int(origin.x + x, origin.y + y)))
                        return false;
            return true;
        }

        public bool AreaFree(Vector2Int origin, Vector2Int size, NodeBase ignore = null)
        {
            for (int x = 0; x < size.x; x++)
                for (int y = 0; y < size.y; y++)
                {
                    var n = NodeAt(new Vector2Int(origin.x + x, origin.y + y));
                    if (n != null && n != ignore) return false;
                }
            return true;
        }

        /// <summary>该区域是否可放置节点（完整落在画布内且不与其他节点重叠）。</summary>
        public bool CanPlace(Vector2Int origin, Vector2Int size, NodeBase ignore = null) =>
            AreaOnBoard(origin, size) && AreaFree(origin, size, ignore);

        public void OccupyArea(NodeBase node, Vector2Int origin, Vector2Int size)
        {
            ReleaseArea(node);
            nodeAreas[node] = (origin, size);
            for (int x = 0; x < size.x; x++)
                for (int y = 0; y < size.y; y++)
                    occupants[new Vector2Int(origin.x + x, origin.y + y)] = node;
            Version++;
        }

        public void ReleaseArea(NodeBase node)
        {
            if (!nodeAreas.TryGetValue(node, out var area)) return;
            nodeAreas.Remove(node);
            for (int x = 0; x < area.size.x; x++)
                for (int y = 0; y < area.size.y; y++)
                    occupants.Remove(new Vector2Int(area.origin.x + x, area.origin.y + y));
            Version++;
        }

        // ---------- 视觉 ----------

        private void CreateCellVisual(Vector2Int cell)
        {
            var go = new GameObject($"Cell_{cell.x}_{cell.y}");
            go.transform.SetParent(cellRoot, false);
            go.transform.position = CellCenter(cell);
            go.transform.localScale = new Vector3(0.96f, 0.96f, 1f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = VisualAssets.WhiteSprite;
            sr.sharedMaterial = VisualAssets.UnlitMaterial;
            // 棋盘式双色，便于辨认格子边界
            bool even = ((cell.x + cell.y) & 1) == 0;
            sr.color = even ? new Color(0.17f, 0.19f, 0.23f) : new Color(0.19f, 0.21f, 0.25f);
            sr.sortingOrder = SortingOrders.Cell;
            cellVisuals[cell] = sr;
        }
    }
}

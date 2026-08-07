using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 运行时网格：占位数据 + 单元格高亮渲染。坐标全部用场景像素，渲染时经控制器换算为世界坐标。
    /// 基础网格来自房间配置表；桌面网格由带桌面格的家具实例动态创建。
    /// </summary>
    public sealed class FurnitureRuntimeGrid
    {
        /// <summary>场景占位符：被背景画面占用、禁止摆放的格子。</summary>
        public const string SceneOccupant = "__scene__";

        private static readonly Color CellIdle = new Color(1f, .97f, .88f, .10f);
        private static readonly Color CellBlocked = new Color(.60f, .63f, .75f, .30f);
        private static readonly Color CellOccupied = new Color(.89f, .40f, .56f, .25f);
        private static readonly Color CellOk = new Color(.35f, .77f, .48f, .48f);
        private static readonly Color CellBad = new Color(.88f, .34f, .30f, .50f);

        public string Id { get; }
        public FurnitureSurfaceType Surface { get; }
        public int Cols { get; }
        public int Rows { get; }
        public float CellWidth { get; }
        public float CellHeight { get; }
        /// <summary>网格左上角（场景像素）。桌面网格随宿主移动而更新。</summary>
        public float X { get; private set; }
        public float Y { get; private set; }
        /// <summary>桌面网格的宿主家具实例 id；基础网格为 null。</summary>
        public string HostItemId;

        private readonly Dictionary<Vector2Int, string> occupancy = new Dictionary<Vector2Int, string>();
        private readonly Func<float, float, float, Vector3> pxToWorld;
        private readonly float zOffset;
        private GameObject root;
        private SpriteRenderer[] cellRenderers;

        public FurnitureRuntimeGrid(FurnitureGridConfig config, Func<float, float, float, Vector3> pxToWorld, float zOffset)
        {
            Id = config.id;
            Surface = config.surface;
            Cols = config.cols;
            Rows = config.rows;
            CellWidth = config.cellWidth;
            CellHeight = config.cellHeight;
            X = config.x;
            Y = config.y;
            this.pxToWorld = pxToWorld;
            this.zOffset = zOffset;
        }

        public GameObject Root => root;

        public void BuildVisual(Transform parent, Sprite cellSprite, int sortingOrder)
        {
            root = new GameObject("Grid_" + Id);
            root.transform.SetParent(parent, false);
            cellRenderers = new SpriteRenderer[Cols * Rows];
            for (var r = 0; r < Rows; r++)
            {
                for (var c = 0; c < Cols; c++)
                {
                    var cell = new GameObject($"Cell_{c}_{r}");
                    cell.transform.SetParent(root.transform, false);
                    var renderer = cell.AddComponent<SpriteRenderer>();
                    renderer.sprite = cellSprite;
                    renderer.color = CellIdle;
                    renderer.sortingOrder = sortingOrder;
                    // 1×1 白色精灵（PPU 100 → 0.01 世界单位）按单元格尺寸缩放，四周留 2px 缝隙形成格线感。
                    cell.transform.localScale = new Vector3(CellWidth - 2f, CellHeight - 2f, 1f);
                    cellRenderers[r * Cols + c] = renderer;
                }
            }
            SyncCellPositions();
            root.SetActive(false);
        }

        /// <summary>移动网格（桌面网格随宿主移动时调用）。</summary>
        public void SetOrigin(float x, float y)
        {
            X = x;
            Y = y;
            SyncCellPositions();
        }

        private void SyncCellPositions()
        {
            if (cellRenderers == null) return;
            for (var r = 0; r < Rows; r++)
                for (var c = 0; c < Cols; c++)
                    cellRenderers[r * Cols + c].transform.position =
                        pxToWorld(X + (c + .5f) * CellWidth, Y + (r + .5f) * CellHeight, zOffset);
        }

        public void SetVisible(bool visible)
        {
            if (root != null) root.SetActive(visible);
        }

        /// <summary>桌面网格随宿主层级变化时同步渲染次序。</summary>
        public void SetSortingOrder(int order)
        {
            if (cellRenderers == null) return;
            for (var i = 0; i < cellRenderers.Length; i++) cellRenderers[i].sortingOrder = order;
        }

        public bool Contains(Vector2 scenePx, float margin)
        {
            return scenePx.x >= X - margin && scenePx.x <= X + Cols * CellWidth + margin &&
                   scenePx.y >= Y - margin && scenePx.y <= Y + Rows * CellHeight + margin;
        }

        /// <summary>网格外接矩形到指定点的距离平方（用于多网格择近吸附）。</summary>
        public float DistanceSq(Vector2 scenePx)
        {
            var cx = Mathf.Clamp(scenePx.x, X, X + Cols * CellWidth);
            var cy = Mathf.Clamp(scenePx.y, Y, Y + Rows * CellHeight);
            return (scenePx.x - cx) * (scenePx.x - cx) + (scenePx.y - cy) * (scenePx.y - cy);
        }

        public bool FootprintFree(int col, int row, int cols, int rows, string ignoreId)
        {
            if (col < 0 || row < 0 || col + cols > Cols || row + rows > Rows) return false;
            for (var r = row; r < row + rows; r++)
                for (var c = col; c < col + cols; c++)
                    if (occupancy.TryGetValue(new Vector2Int(c, r), out var owner) && owner != ignoreId)
                        return false;
            return true;
        }

        public void SetOccupied(int col, int row, int cols, int rows, string ownerId, bool occupied)
        {
            for (var r = row; r < row + rows; r++)
            {
                for (var c = col; c < col + cols; c++)
                {
                    var key = new Vector2Int(c, r);
                    if (occupied) occupancy[key] = ownerId;
                    else occupancy.Remove(key);
                }
            }
        }

        public void MarkSceneBlocked(int col, int row)
        {
            if (col >= 0 && row >= 0 && col < Cols && row < Rows)
                occupancy[new Vector2Int(col, row)] = SceneOccupant;
        }

        /// <summary>按占位状态刷新单元格颜色，并清掉上一次的预览色。</summary>
        public void RefreshCellColors()
        {
            if (cellRenderers == null) return;
            for (var r = 0; r < Rows; r++)
            {
                for (var c = 0; c < Cols; c++)
                {
                    occupancy.TryGetValue(new Vector2Int(c, r), out var owner);
                    cellRenderers[r * Cols + c].color =
                        owner == SceneOccupant ? CellBlocked :
                        owner != null ? CellOccupied : CellIdle;
                }
            }
        }

        /// <summary>把候选落点的格子染成绿/红。调用前应先 RefreshCellColors 清预览。</summary>
        public void PaintPreview(int col, int row, int cols, int rows, bool ok)
        {
            if (cellRenderers == null) return;
            for (var r = row; r < row + rows; r++)
                for (var c = col; c < col + cols; c++)
                    if (c >= 0 && r >= 0 && c < Cols && r < Rows)
                        cellRenderers[r * Cols + c].color = ok ? CellOk : CellBad;
        }

        public void Destroy()
        {
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
            cellRenderers = null;
        }
    }
}

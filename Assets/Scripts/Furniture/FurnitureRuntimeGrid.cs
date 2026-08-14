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
        /// <summary>叠放层占用（可叠放家具专用，与普通占用互不遮挡；见 FootprintFree 注释）。</summary>
        private readonly Dictionary<Vector2Int, string> stackOccupancy = new Dictionary<Vector2Int, string>();
        private readonly Func<float, float, float, Vector3> pxToWorld;
        private readonly float zOffset;
        private GameObject root;
        private SpriteRenderer[] cellRenderers;

        /// <summary>远端宽度比（2.5D 假透视）：最远一行的横向收缩比例，1 = 关闭。</summary>
        public float FarWidthScale { get; }

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
            FarWidthScale = config.farWidthScale <= 0f ? 1f : config.farWidthScale;
            this.pxToWorld = pxToWorld;
            this.zOffset = zOffset;
        }

        // ── 2.5D 假透视（仅横向）：远行向网格中心收拢，行高与纵向坐标不变 ──
        // rowF 语义 = 深度基准行（0 = 最远/顶部，Rows = 最近/底部）；家具与格子统一用**底边所在行**取值，
        // 保证家具中心与其脚下格子的中心始终对齐。

        /// <summary>该深度行的横向收缩比例。</summary>
        public float WidthScaleAt(float rowF) =>
            Mathf.Lerp(FarWidthScale, 1f, Rows > 0 ? Mathf.Clamp01(rowF / Rows) : 1f);

        private float CenterX => X + Cols * CellWidth * .5f;

        /// <summary>均匀网格坐标 → 透视显示坐标（X 向中心收拢）。</summary>
        public float MapX(float x, float rowF) => CenterX + (x - CenterX) * WidthScaleAt(rowF);

        /// <summary>透视显示坐标 → 均匀网格坐标（指针反算吸附用）。</summary>
        public float InvMapX(float x, float rowF)
        {
            var scale = WidthScaleAt(rowF);
            return scale < .0001f ? x : CenterX + (x - CenterX) / scale;
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
                    // 1×1 白色精灵（PPU 100 → 0.01 世界单位）按单元格尺寸缩放，四周留 2px 缝隙形成格线感；
                    // 2.5D 假透视：格宽随深度行收缩（取该格底边行的比例）
                    cell.transform.localScale = new Vector3(
                        CellWidth * WidthScaleAt(r + 1) - 2f, CellHeight - 2f, 1f);
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
                    cellRenderers[r * Cols + c].transform.position = pxToWorld(
                        MapX(X + (c + .5f) * CellWidth, r + 1), // 假透视：格心随深度行向中心收拢
                        Y + (r + .5f) * CellHeight, zOffset);
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

        public bool FootprintFree(int col, int row, int cols, int rows, string ignoreId, bool stackable = false)
        {
            if (col < 0 || row < 0 || col + cols > Cols || row + rows > Rows) return false;
            for (var r = row; r < row + rows; r++)
            {
                for (var c = col; c < col + cols; c++)
                {
                    var key = new Vector2Int(c, r);
                    if (stackable)
                    {
                        // 可叠放（地毯类）：不看普通占用（家具可以压在地毯上、地毯也可铺到家具脚下），
                        // 只挡场景占用格与其他可叠放件（地毯不叠地毯）
                        if (occupancy.TryGetValue(key, out var scene) && scene == SceneOccupant) return false;
                        if (stackOccupancy.TryGetValue(key, out var other) && other != ignoreId) return false;
                    }
                    else
                    {
                        // 普通家具：无视叠放层（地毯不算占格）
                        if (occupancy.TryGetValue(key, out var owner) && owner != ignoreId) return false;
                    }
                }
            }
            return true;
        }

        public void SetOccupied(int col, int row, int cols, int rows, string ownerId, bool occupied, bool stackable = false)
        {
            var layer = stackable ? stackOccupancy : occupancy;
            for (var r = row; r < row + rows; r++)
            {
                for (var c = col; c < col + cols; c++)
                {
                    var key = new Vector2Int(c, r);
                    if (occupied) layer[key] = ownerId;
                    else layer.Remove(key);
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

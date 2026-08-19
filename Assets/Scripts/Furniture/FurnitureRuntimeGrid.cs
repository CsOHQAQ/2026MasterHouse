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
        // 梯形格渲染（2026-08-14）：单张 Mesh、一格一个四边形（上边按上一行收缩、下边按下一行收缩），
        // 竖格线随假透视连续向灭点收拢；占位/预览染色走顶点色
        private Mesh mesh;
        private MeshRenderer meshRenderer;
        private Color32[] vertexColors;

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
            root = new GameObject("Grid_" + Id) { layer = FurnitureRoomController.FurnitureSceneLayer };
            root.transform.SetParent(parent, false);
            mesh = new Mesh { name = "GridMesh_" + Id };
            mesh.MarkDynamic(); // 染色高频更新
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = root.AddComponent<MeshRenderer>();
            // URP 2D 优先用管线自带的精灵无光照着色器，找不到再回落内置 Sprites/Default（都支持顶点色）
            var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var material = new Material(shader) { mainTexture = Texture2D.whiteTexture };
            meshRenderer.sharedMaterial = material;
            meshRenderer.sortingOrder = sortingOrder;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            vertexColors = new Color32[Cols * Rows * 4];
            var triangles = new int[Cols * Rows * 6];
            for (var i = 0; i < Cols * Rows; i++)
            {
                var v = i * 4;
                var t = i * 6;
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
                var idle = (Color32)CellIdle;
                vertexColors[v] = vertexColors[v + 1] = vertexColors[v + 2] = vertexColors[v + 3] = idle;
            }
            RebuildMesh(triangles);
            root.SetActive(false);
        }

        /// <summary>移动网格（桌面网格随宿主移动时调用）。</summary>
        public void SetOrigin(float x, float y)
        {
            X = x;
            Y = y;
            RebuildMesh(null);
        }

        /// <summary>重建梯形格顶点：每格上边取上一行收缩比、下边取下一行收缩比，竖线连续向灭点收拢；
        /// 四周各缩 1px 形成格线缝隙。顶点序：左上、右上、右下、左下。</summary>
        private void RebuildMesh(int[] triangles)
        {
            if (mesh == null || root == null) return;
            var verts = new Vector3[Cols * Rows * 4];
            for (var r = 0; r < Rows; r++)
            {
                var topY = Y + r * CellHeight + 1f;
                var bottomY = Y + (r + 1) * CellHeight - 1f;
                for (var c = 0; c < Cols; c++)
                {
                    var left = X + c * CellWidth + 1f;
                    var right = X + (c + 1) * CellWidth - 1f;
                    var v = (r * Cols + c) * 4;
                    verts[v] = ToLocal(MapX(left, r), topY);
                    verts[v + 1] = ToLocal(MapX(right, r), topY);
                    verts[v + 2] = ToLocal(MapX(right, r + 1), bottomY);
                    verts[v + 3] = ToLocal(MapX(left, r + 1), bottomY);
                }
            }
            mesh.Clear();
            mesh.vertices = verts;
            if (triangles != null) mesh.triangles = triangles;
            else
            {
                var tris = new int[Cols * Rows * 6];
                for (var i = 0; i < Cols * Rows; i++)
                {
                    var v = i * 4;
                    var t = i * 6;
                    tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
                    tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
                }
                mesh.triangles = tris;
            }
            mesh.colors32 = vertexColors;
            mesh.RecalculateBounds();
        }

        private Vector3 ToLocal(float px, float py) =>
            root.transform.InverseTransformPoint(pxToWorld(px, py, zOffset));

        public void SetVisible(bool visible)
        {
            if (root != null) root.SetActive(visible);
        }

        /// <summary>桌面网格随宿主层级变化时同步渲染次序。</summary>
        public void SetSortingOrder(int order)
        {
            if (meshRenderer != null) meshRenderer.sortingOrder = order;
        }

        /// <summary>给单元格染色（顶点色，四顶点同色）。</summary>
        private void SetCellColor(int col, int row, Color color)
        {
            var v = (row * Cols + col) * 4;
            var c32 = (Color32)color;
            vertexColors[v] = vertexColors[v + 1] = vertexColors[v + 2] = vertexColors[v + 3] = c32;
        }

        private void ApplyColors()
        {
            if (mesh != null) mesh.colors32 = vertexColors;
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
            if (vertexColors == null) return;
            for (var r = 0; r < Rows; r++)
            {
                for (var c = 0; c < Cols; c++)
                {
                    occupancy.TryGetValue(new Vector2Int(c, r), out var owner);
                    SetCellColor(c, r, owner == SceneOccupant ? CellBlocked :
                        owner != null ? CellOccupied : CellIdle);
                }
            }
            ApplyColors();
        }

        /// <summary>把候选落点的格子染成绿/红。调用前应先 RefreshCellColors 清预览。</summary>
        public void PaintPreview(int col, int row, int cols, int rows, bool ok)
        {
            if (vertexColors == null) return;
            for (var r = row; r < row + rows; r++)
                for (var c = col; c < col + cols; c++)
                    if (c >= 0 && r >= 0 && c < Cols && r < Rows)
                        SetCellColor(c, r, ok ? CellOk : CellBad);
            ApplyColors();
        }

        public void Destroy()
        {
            if (mesh != null) UnityEngine.Object.Destroy(mesh);
            if (meshRenderer != null && meshRenderer.sharedMaterial != null)
                UnityEngine.Object.Destroy(meshRenderer.sharedMaterial);
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
            mesh = null;
            meshRenderer = null;
            vertexColors = null;
        }
    }
}

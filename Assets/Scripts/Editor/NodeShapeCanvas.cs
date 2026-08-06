using UnityEditor;
using UnityEngine;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// 节点编辑器的网格画布：绘制/擦除形状格、摆放 Pin。
    /// 画布逻辑坐标 y 向上（与格坐标一致），渲染时翻转到 GUI 坐标（y 向下）。
    /// 视野固定从 (0,0) 开始——保存时形状会归一化到最左下 (0,0)，负坐标只是过渡态。
    /// </summary>
    public class NodeShapeCanvas
    {
        public enum EMode
        {
            Shape = 0, // 绘制形状
            Pin = 1,   // 摆放 Pin
        }

        public EMode Mode = EMode.Shape;
        public int CellSize = 30;
        public int ViewCols = 12;
        public int ViewRows = 10;

        /// <summary>当前选中的 Pin 索引（画布点击摆放的目标），-1 = 未选中。</summary>
        public int SelectedPin = -1;

        bool _dragging;
        bool _dragErase;
        static readonly Vector2Int kNoCell = new Vector2Int(int.MinValue, int.MinValue);
        Vector2Int _lastCell = kNoCell;

        static readonly Color kColBackground = new Color(0.15f, 0.15f, 0.15f);
        static readonly Color kColGridLine = new Color(1f, 1f, 1f, 0.08f);
        static readonly Color kColCell = new Color(0.30f, 0.46f, 0.60f, 0.95f);
        static readonly Color kColOrigin = new Color(1f, 0.85f, 0.30f, 0.90f);
        static readonly Color kColInvalidPin = new Color(0.90f, 0.30f, 0.25f);
        static readonly Color kColNoItemPin = new Color(0.70f, 0.70f, 0.70f);

        GUIStyle _pinLabelStyle;
        GUIStyle _hintStyle;

        public float ContentWidth => ViewCols * CellSize + 1;
        public float ContentHeight => ViewRows * CellSize + 1;

        /// <summary>根据形状与 Pin 自动扩展视野，保证整块内容可见。</summary>
        public void FitTo(NodeDef def)
        {
            int maxX = 5, maxY = 5;
            foreach (var g in def.Shape.Grids)
            {
                maxX = Mathf.Max(maxX, g.DeltaPosition.x);
                maxY = Mathf.Max(maxY, g.DeltaPosition.y);
            }
            foreach (var p in def.Pins)
            {
                maxX = Mathf.Max(maxX, p.LocalCell.x);
                maxY = Mathf.Max(maxY, p.LocalCell.y);
            }
            ViewCols = Mathf.Clamp(maxX + 3, 8, 64);
            ViewRows = Mathf.Clamp(maxY + 3, 8, 64);
        }

        public void OnGUI(Rect rect, NodeDef def, EditorWindow host)
        {
            EnsureStyles();

            if (Event.current.type == EventType.Repaint)
                DrawAll(rect, def);

            HandleEvents(rect, def, host);
        }

        void EnsureStyles()
        {
            if (_pinLabelStyle == null)
            {
                _pinLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.white },
                };
                _hintStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(1f, 0.7f, 0.3f) },
                };
            }
        }

        // ==================== 渲染 ====================

        void DrawAll(Rect rect, NodeDef def)
        {
            EditorGUI.DrawRect(rect, kColBackground);

            // 网格线
            for (int x = 0; x <= ViewCols; x++)
                EditorGUI.DrawRect(new Rect(rect.x + x * CellSize, rect.y, 1, ViewRows * CellSize), kColGridLine);
            for (int y = 0; y <= ViewRows; y++)
                EditorGUI.DrawRect(new Rect(rect.x, rect.y + y * CellSize, ViewCols * CellSize, 1), kColGridLine);

            // 形状格
            int outOfView = 0;
            foreach (var g in def.Shape.Grids)
            {
                if (!InView(g.DeltaPosition)) { outOfView++; continue; }
                var r = CellRect(rect, g.DeltaPosition);
                EditorGUI.DrawRect(new Rect(r.x + 1, r.y + 1, r.width - 1, r.height - 1), kColCell);
            }

            // 原点 (0,0) 标记——保存时形状归一化到这里
            var originRect = CellRect(rect, Vector2Int.zero);
            DrawBorder(originRect, 2, kColOrigin);
            GUI.Label(new Rect(originRect.x, originRect.yMax - 14, 30, 14), "0,0", _pinLabelStyle);

            // Pin 标记
            for (int i = 0; i < def.Pins.Count; i++)
            {
                var p = def.Pins[i];
                if (!InView(p.LocalCell)) { outOfView++; continue; }

                bool invalid = !def.Shape.ContainsDelta(p.LocalCell)
                               || def.Shape.ContainsDelta(p.LocalCell + Direction4.ToOffset(p.Facing));
                var col = invalid ? kColInvalidPin
                    : p.Pin.ItemType != null ? p.Pin.ItemType.DisplayColor : kColNoItemPin;

                var r = CellRect(rect, p.LocalCell);
                DrawPinMarker(r, p.Facing, p.Pin.Direction, col, i == SelectedPin);

                var mid = EdgeMid(r, p.Facing);
                GUI.Label(new Rect(mid.x - 11, mid.y - 8, 22, 16), i.ToString(), _pinLabelStyle);
            }

            if (outOfView > 0)
                GUI.Label(new Rect(rect.x + 4, rect.y + 2, rect.width - 8, 16),
                    $"⚠ 有 {outOfView} 个格子/Pin 在视野外（负坐标可点「保存（归一化）」拉回原点）", _hintStyle);
        }

        /// <summary>
        /// 在格子朝向边上画 Pin 标记：输出=指向外的三角，输入=指向内的三角，
        /// 同步（中转配对 Pin）=菱形；选中时加白色描边。
        /// </summary>
        void DrawPinMarker(Rect cellRect, EDirection4 facing, EPinDirection dir, Color color, bool selected)
        {
            Vector2 mid = EdgeMid(cellRect, facing);
            Vector2 outward = EdgeOutward(facing);
            Vector2 tangent = new Vector2(-outward.y, outward.x);
            float s = CellSize * 0.26f;

            Vector3[] pts;
            if (dir == EPinDirection.Output)
            {
                pts = new Vector3[]
                {
                    mid + outward * s,
                    mid - outward * s * 0.3f + tangent * s * 0.8f,
                    mid - outward * s * 0.3f - tangent * s * 0.8f,
                };
            }
            else if (dir == EPinDirection.Input)
            {
                pts = new Vector3[]
                {
                    mid - outward * s,
                    mid + outward * s * 0.3f + tangent * s * 0.8f,
                    mid + outward * s * 0.3f - tangent * s * 0.8f,
                };
            }
            else
            {
                pts = new Vector3[]
                {
                    mid + outward * s * 0.8f,
                    mid + tangent * s * 0.8f,
                    mid - outward * s * 0.8f,
                    mid - tangent * s * 0.8f,
                };
            }

            Handles.color = color;
            Handles.DrawAAConvexPolygon(pts);

            if (selected)
            {
                var loop = new Vector3[pts.Length + 1];
                pts.CopyTo(loop, 0);
                loop[pts.Length] = pts[0];
                Handles.color = Color.white;
                Handles.DrawAAPolyLine(3f, loop);
            }
        }

        static void DrawBorder(Rect r, float w, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, w), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - w, r.width, w), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, w, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - w, r.y, w, r.height), c);
        }

        // ==================== 交互 ====================

        void HandleEvents(Rect rect, NodeDef def, EditorWindow host)
        {
            var e = Event.current;

            if (e.type == EventType.MouseUp)
            {
                _dragging = false;
                _lastCell = kNoCell;
                return;
            }

            // Ctrl + 滚轮缩放
            if (e.type == EventType.ScrollWheel && e.control && rect.Contains(e.mousePosition))
            {
                CellSize = Mathf.Clamp(CellSize - (int)Mathf.Sign(e.delta.y) * 2, 16, 48);
                e.Use();
                host.Repaint();
                return;
            }

            if (e.type != EventType.MouseDown && e.type != EventType.MouseDrag) return;
            if (e.button > 1) return;
            if (!rect.Contains(e.mousePosition)) return;

            var cellOpt = CellAt(rect, e.mousePosition);
            if (cellOpt == null) return;
            var cell = cellOpt.Value;

            if (Mode == EMode.Shape)
            {
                if (e.type == EventType.MouseDown)
                {
                    _dragging = true;
                    _lastCell = kNoCell;
                    // 右键始终擦除；左键从已有格开始整段拖动视为擦除，否则绘制
                    _dragErase = e.button == 1 || def.Shape.ContainsDelta(cell);
                }
                if (_dragging && cell != _lastCell)
                {
                    if (_dragErase) NodeDefEditUtil.EraseCell(def, cell);
                    else NodeDefEditUtil.PaintCell(def, cell);
                    _lastCell = cell;
                }
                e.Use();
                host.Repaint();
            }
            else // Pin 摆放模式
            {
                if (e.type != EventType.MouseDown) return;
                if (SelectedPin < 0 || SelectedPin >= def.Pins.Count) return;

                if (e.button == 0)
                {
                    // 只能摆在形状内的格子上
                    if (def.Shape.ContainsDelta(cell))
                        NodeDefEditUtil.PlacePin(def, SelectedPin, cell);
                }
                else
                {
                    NodeDefEditUtil.CycleFacing(def, SelectedPin);
                }
                e.Use();
                host.Repaint();
            }
        }

        // ==================== 坐标换算 ====================

        bool InView(Vector2Int c)
        {
            return c.x >= 0 && c.x < ViewCols && c.y >= 0 && c.y < ViewRows;
        }

        /// <summary>格坐标 → GUI 矩形（y 翻转：格 y 向上，GUI y 向下）。</summary>
        Rect CellRect(Rect canvas, Vector2Int c)
        {
            return new Rect(canvas.x + c.x * CellSize, canvas.y + (ViewRows - 1 - c.y) * CellSize, CellSize, CellSize);
        }

        /// <summary>GUI 坐标 → 格坐标；视野外返回 null。</summary>
        Vector2Int? CellAt(Rect canvas, Vector2 mouse)
        {
            int x = Mathf.FloorToInt((mouse.x - canvas.x) / CellSize);
            int yFromTop = Mathf.FloorToInt((mouse.y - canvas.y) / CellSize);
            int y = ViewRows - 1 - yFromTop;
            if (x < 0 || x >= ViewCols || y < 0 || y >= ViewRows) return null;
            return new Vector2Int(x, y);
        }

        /// <summary>格子朝向边的中点（GUI 坐标；格 Up = GUI 上边即 yMin）。</summary>
        static Vector2 EdgeMid(Rect r, EDirection4 facing)
        {
            switch (facing)
            {
                case EDirection4.Up: return new Vector2(r.center.x, r.yMin);
                case EDirection4.Right: return new Vector2(r.xMax, r.center.y);
                case EDirection4.Down: return new Vector2(r.center.x, r.yMax);
                default: return new Vector2(r.xMin, r.center.y);
            }
        }

        /// <summary>朝向的 GUI 空间外法线。</summary>
        static Vector2 EdgeOutward(EDirection4 facing)
        {
            switch (facing)
            {
                case EDirection4.Up: return new Vector2(0, -1);
                case EDirection4.Right: return new Vector2(1, 0);
                case EDirection4.Down: return new Vector2(0, 1);
                default: return new Vector2(-1, 0);
            }
        }
    }
}

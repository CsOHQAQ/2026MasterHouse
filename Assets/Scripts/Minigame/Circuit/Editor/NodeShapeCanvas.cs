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
        bool _shapeChangedDuringDrag;
        bool _panning;
        bool _draggingPin;
        static readonly Vector2Int kNoCell = new Vector2Int(int.MinValue, int.MinValue);
        Vector2Int _lastCell = kNoCell;
        Vector2Int _hoverCell = kNoCell;
        Vector2Int _pinDragTarget = kNoCell;

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

        public void OnGUI(Rect rect, NodeDef def, EditorWindow host, ref Vector2 scrollPosition,
            System.Action onShapeChanged = null)
        {
            EnsureStyles();

            if (Event.current.type == EventType.Repaint)
                DrawAll(rect, def);

            HandleEvents(rect, def, host, ref scrollPosition, onShapeChanged);
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
            CanvasDrawUtil.DrawBorder(originRect, 2, kColOrigin);
            GUI.Label(new Rect(originRect.x, originRect.yMax - 14, 30, 14), "0,0", _pinLabelStyle);

            // Pin 标记
            for (int i = 0; i < def.Pins.Count; i++)
            {
                var p = def.Pins[i];
                if (!InView(p.LocalCell)) { outOfView++; continue; }

                bool invalid = !def.Shape.ContainsDelta(p.LocalCell)
                               || def.Shape.ContainsDelta(p.LocalCell + Direction4.ToOffset(p.Facing));
                var col = invalid ? kColInvalidPin : CanvasDrawUtil.PinColor(def, p.Pin);

                var r = CellRect(rect, p.LocalCell);
                CanvasDrawUtil.DrawPinMarker(r, p.Facing, p.Pin.Direction, col, i == SelectedPin);

                var mid = CanvasDrawUtil.EdgeMid(r, p.Facing);
                GUI.Label(new Rect(mid.x - 11, mid.y - 8, 22, 16), i.ToString(), _pinLabelStyle);
            }

            DrawPinPreview(rect, def);

            if (outOfView > 0)
                GUI.Label(new Rect(rect.x + 4, rect.y + 2, rect.width - 8, 16),
                    $"⚠ 有 {outOfView} 个格子/Pin 在视野外（负坐标可点「保存（归一化）」拉回原点）", _hintStyle);
        }

        void DrawPinPreview(Rect rect, NodeDef def)
        {
            if (Mode != EMode.Pin || SelectedPin < 0 || SelectedPin >= def.Pins.Count) return;

            var cell = _draggingPin ? _pinDragTarget : _hoverCell;
            if (cell == kNoCell || !InView(cell)) return;

            var pin = def.Pins[SelectedPin];
            var facing = PreviewFacing(def, pin.Facing, cell);
            bool legal = IsLegalPinPlacement(def, SelectedPin, cell, facing);
            var color = legal ? CanvasDrawUtil.PinColor(def, pin.Pin) : kColInvalidPin;
            color.a = 0.65f;

            var cellRect = CellRect(rect, cell);
            EditorGUI.DrawRect(new Rect(cellRect.x + 2, cellRect.y + 2, cellRect.width - 3, cellRect.height - 3),
                legal ? new Color(color.r, color.g, color.b, 0.18f) : new Color(color.r, color.g, color.b, 0.28f));
            CanvasDrawUtil.DrawPinMarker(cellRect, facing, pin.Pin.Direction, color, true);

            string label = legal ? "松开摆放" : "此处不可摆放";
            GUI.Label(new Rect(cellRect.x + 2, cellRect.y + 1, 80, 14), label, _hintStyle);
        }

        // ==================== 交互 ====================

        void HandleEvents(Rect rect, NodeDef def, EditorWindow host, ref Vector2 scrollPosition,
            System.Action onShapeChanged)
        {
            var e = Event.current;

            // Esc 取消当前 Pin 摆放，但保留 Pin 工具页，避免下一次画布点击误改节点形状。
            if (Mode == EMode.Pin && e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape
                && (SelectedPin >= 0 || _draggingPin))
            {
                SelectedPin = -1;
                _draggingPin = false;
                _pinDragTarget = kNoCell;
                _hoverCell = kNoCell;
                GUIUtility.keyboardControl = 0;
                e.Use();
                host.Repaint();
                return;
            }

            // 中键拖动画布。优先于形状绘制与 Pin 摆放，避免平移时误改节点配置。
            if (e.type == EventType.MouseDown && e.button == 2 && rect.Contains(e.mousePosition))
            {
                _panning = true;
                e.Use();
                return;
            }

            if (_panning && e.type == EventType.MouseDrag)
            {
                scrollPosition -= e.delta;
                scrollPosition.x = Mathf.Max(0f, scrollPosition.x);
                scrollPosition.y = Mathf.Max(0f, scrollPosition.y);
                e.Use();
                host.Repaint();
                return;
            }

            if (_panning && e.type == EventType.MouseUp && e.button == 2)
            {
                _panning = false;
                e.Use();
                host.Repaint();
                return;
            }

            if (_draggingPin && e.type == EventType.MouseUp && e.button == 0)
            {
                if (_pinDragTarget != kNoCell)
                {
                    var facing = PreviewFacing(def, def.Pins[SelectedPin].Facing, _pinDragTarget);
                    if (IsLegalPinPlacement(def, SelectedPin, _pinDragTarget, facing)
                        && def.Pins[SelectedPin].LocalCell != _pinDragTarget)
                    {
                        NodeDefEditUtil.PlacePin(def, SelectedPin, _pinDragTarget);
                    }
                }
                _draggingPin = false;
                _pinDragTarget = kNoCell;
                e.Use();
                host.Repaint();
                return;
            }

            if (e.type == EventType.MouseUp)
            {
                bool shapeChanged = _dragging && _shapeChangedDuringDrag;
                _dragging = false;
                _shapeChangedDuringDrag = false;
                _lastCell = kNoCell;
                if (shapeChanged) onShapeChanged?.Invoke();
                return;
            }

            // 滚轮直接缩放，并补偿滚动位置，使鼠标指向的画布位置在缩放前后保持不动。
            if (e.type == EventType.ScrollWheel && rect.Contains(e.mousePosition))
            {
                int oldCellSize = CellSize;
                int newCellSize = Mathf.Clamp(oldCellSize - (int)Mathf.Sign(e.delta.y) * 2, 16, 48);
                if (newCellSize != oldCellSize)
                {
                    Vector2 pointerInContent = e.mousePosition - rect.position;
                    Vector2 pointerInViewport = pointerInContent - scrollPosition;
                    float scale = (float)newCellSize / oldCellSize;

                    CellSize = newCellSize;
                    scrollPosition = pointerInContent * scale - pointerInViewport;
                    scrollPosition.x = Mathf.Max(0f, scrollPosition.x);
                    scrollPosition.y = Mathf.Max(0f, scrollPosition.y);
                }
                e.Use();
                host.Repaint();
                return;
            }

            if (Mode == EMode.Pin && (e.type == EventType.MouseMove || e.type == EventType.MouseDrag))
            {
                var hover = rect.Contains(e.mousePosition) ? CellAt(rect, e.mousePosition) : null;
                var nextHover = hover ?? kNoCell;
                if (_hoverCell != nextHover)
                {
                    _hoverCell = nextHover;
                    host.Repaint();
                }
                if (_draggingPin)
                    _pinDragTarget = nextHover;
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
                    _shapeChangedDuringDrag = false;
                    _lastCell = kNoCell;
                    // 右键始终擦除；左键从已有格开始整段拖动视为擦除，否则绘制
                    _dragErase = e.button == 1 || def.Shape.ContainsDelta(cell);
                }
                if (_dragging && cell != _lastCell)
                {
                    int oldCount = def.Shape.Grids.Count;
                    if (_dragErase) NodeDefEditUtil.EraseCell(def, cell);
                    else NodeDefEditUtil.PaintCell(def, cell);
                    _shapeChangedDuringDrag |= def.Shape.Grids.Count != oldCount;
                    _lastCell = cell;
                }
                e.Use();
                host.Repaint();
            }
            else // Pin 摆放模式
            {
                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    int hit = PinAt(def, cell);
                    if (hit >= 0) SelectedPin = hit;
                    if (SelectedPin < 0 || SelectedPin >= def.Pins.Count) return;

                    _draggingPin = true;
                    _pinDragTarget = cell;
                }
                else if (e.type == EventType.MouseDown && e.button == 1)
                {
                    int hit = PinAt(def, cell);
                    if (hit >= 0) SelectedPin = hit;
                    if (SelectedPin < 0 || SelectedPin >= def.Pins.Count) return;
                    NodeDefEditUtil.CycleFacing(def, SelectedPin);
                }
                else if (!_draggingPin)
                {
                    return;
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

        int PinAt(NodeDef def, Vector2Int cell)
        {
            if (SelectedPin >= 0 && SelectedPin < def.Pins.Count
                && def.Pins[SelectedPin].LocalCell == cell)
                return SelectedPin;

            for (int i = 0; i < def.Pins.Count; i++)
                if (def.Pins[i].LocalCell == cell)
                    return i;
            return -1;
        }

        static EDirection4 PreviewFacing(NodeDef def, EDirection4 current, Vector2Int cell)
        {
            if (!def.Shape.ContainsDelta(cell + Direction4.ToOffset(current))) return current;
            for (int d = 0; d < 4; d++)
                if (!def.Shape.ContainsDelta(cell + Direction4.Offsets[d]))
                    return (EDirection4)d;
            return current;
        }

        static bool IsLegalPinPlacement(NodeDef def, int pinIndex, Vector2Int cell, EDirection4 facing)
        {
            if (!def.Shape.ContainsDelta(cell)) return false;
            if (def.Shape.ContainsDelta(cell + Direction4.ToOffset(facing))) return false;

            for (int i = 0; i < def.Pins.Count; i++)
            {
                if (i == pinIndex) continue;
                if (def.Pins[i].LocalCell == cell && def.Pins[i].Facing == facing)
                    return false;
            }
            return true;
        }

    }
}

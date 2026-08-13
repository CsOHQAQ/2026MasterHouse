using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// 关卡编辑器的网格画布：绘制/擦除画布形状（逐格 + 矩形框选）、摆放预置节点。
    /// 画布逻辑坐标 y 向上，渲染时翻转到 GUI 坐标；坐标一律是画布局部格
    /// （保存时归一化到最左下 (0,0)，世界原点 WorldOrigin 已随字段瘦身删除）。
    /// 预置节点渲染占格（按类型着色，非法格标红）与 Pin 标记。
    /// </summary>
    public class LevelCanvas
    {
        public enum EMode
        {
            Paint = 0, // 逐格绘制
            Rect = 1,  // 矩形框选填充/擦除
            Node = 2,  // 摆放预置节点
        }

        public EMode Mode = EMode.Paint;
        public int CellSize = 20;
        public int ViewCols = 25;
        public int ViewRows = 15;

        /// <summary>当前选中的预置节点索引（画布点击摆放的目标），-1 = 未选中。</summary>
        public int SelectedPreset = -1;

        bool _dragging;
        bool _dragErase;
        bool _panning;
        static readonly Vector2Int kNoCell = new Vector2Int(int.MinValue, int.MinValue);
        Vector2Int _lastCell = kNoCell;

        bool _marquee;
        int _marqueeButton;
        Vector2Int _marqueeStart, _marqueeEnd;

        static readonly Color kColBackground = new Color(0.15f, 0.15f, 0.15f);
        static readonly Color kColGridLine = new Color(1f, 1f, 1f, 0.08f);
        static readonly Color kColCanvasCell = new Color(0.27f, 0.32f, 0.38f, 0.95f);
        static readonly Color kColOrigin = new Color(1f, 0.85f, 0.30f, 0.90f);
        static readonly Color kColIllegal = new Color(0.90f, 0.30f, 0.25f, 0.90f);
        static readonly Color kColMarqueeFill = new Color(1f, 0.85f, 0.30f, 0.15f);
        static readonly Color kColMarqueeErase = new Color(0.90f, 0.30f, 0.25f, 0.18f);

        GUIStyle _labelStyle;
        GUIStyle _hintStyle;

        public float ContentWidth => ViewCols * CellSize + 1;
        public float ContentHeight => ViewRows * CellSize + 1;

        /// <summary>按节点类型着色（占位配色，无美术阶段用）。</summary>
        static Color TypeColor(NodeDef def)
        {
            switch (def)
            {
                case ResourceNodeDef _: return new Color(0.35f, 0.65f, 0.35f, 0.90f); // 电源
                case ConditionNodeDef _: return new Color(0.35f, 0.55f, 0.85f, 0.90f); // 电池
                case TransitNodeDef _: return new Color(0.65f, 0.45f, 0.80f, 0.90f);  // 中转件
                default: return Color.gray;
            }
        }

        /// <summary>根据画布与预置节点自动扩展视野。</summary>
        public void FitTo(LevelDef def)
        {
            int maxX = 11, maxY = 8;
            foreach (var g in def.Canvas.Grids)
            {
                maxX = Mathf.Max(maxX, g.DeltaPosition.x);
                maxY = Mathf.Max(maxY, g.DeltaPosition.y);
            }
            foreach (var e in def.PresetNodes)
            {
                if (e.Node == null) continue;
                foreach (var cell in e.Node.Shape.CellsAt(e.Cell))
                {
                    maxX = Mathf.Max(maxX, cell.x);
                    maxY = Mathf.Max(maxY, cell.y);
                }
            }
            ViewCols = Mathf.Clamp(maxX + 4, 25, 128);
            ViewRows = Mathf.Clamp(maxY + 4, 15, 128);
        }

        public void OnGUI(Rect rect, LevelDef def, EditorWindow host, ref Vector2 scrollPosition)
        {
            EnsureStyles();

            if (Event.current.type == EventType.Repaint)
                DrawAll(rect, def);

            HandleEvents(rect, def, host, ref scrollPosition);
        }

        void EnsureStyles()
        {
            if (_labelStyle != null) return;
            _labelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                normal = { textColor = Color.white },
            };
            _hintStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(1f, 0.7f, 0.3f) },
            };
        }

        // ==================== 渲染 ====================

        void DrawAll(Rect rect, LevelDef def)
        {
            EditorGUI.DrawRect(rect, kColBackground);

            for (int x = 0; x <= ViewCols; x++)
                EditorGUI.DrawRect(new Rect(rect.x + x * CellSize, rect.y, 1, ViewRows * CellSize), kColGridLine);
            for (int y = 0; y <= ViewRows; y++)
                EditorGUI.DrawRect(new Rect(rect.x, rect.y + y * CellSize, ViewCols * CellSize, 1), kColGridLine);

            // 画布形状格
            int outOfView = 0;
            var canvasSet = new HashSet<Vector2Int>();
            foreach (var g in def.Canvas.Grids)
            {
                canvasSet.Add(g.DeltaPosition);
                if (!InView(g.DeltaPosition)) { outOfView++; continue; }
                var r = CellRect(rect, g.DeltaPosition);
                EditorGUI.DrawRect(new Rect(r.x + 1, r.y + 1, r.width - 1, r.height - 1), kColCanvasCell);
            }

            // 原点标记
            var originRect = CellRect(rect, Vector2Int.zero);
            CanvasDrawUtil.DrawBorder(originRect, 2, kColOrigin);
            GUI.Label(new Rect(originRect.x, originRect.yMax - 14, 30, 14), "0,0", _labelStyle);

            // 预置节点占格：按类型着色；越界或与先前节点重叠的格标红（警告不阻止）
            var owner = new Dictionary<Vector2Int, int>();
            for (int i = 0; i < def.PresetNodes.Count; i++)
            {
                var e = def.PresetNodes[i];
                if (e.Node == null) continue;

                var col = TypeColor(e.Node);
                foreach (var cell in e.Node.Shape.CellsAt(e.Cell))
                {
                    bool illegal = !canvasSet.Contains(cell) || owner.ContainsKey(cell);
                    if (!owner.ContainsKey(cell)) owner[cell] = i;
                    if (!InView(cell)) { outOfView++; continue; }

                    var r = CellRect(rect, cell);
                    EditorGUI.DrawRect(new Rect(r.x + 2, r.y + 2, r.width - 3, r.height - 3),
                        illegal ? kColIllegal : col);
                    if (i == SelectedPreset)
                        CanvasDrawUtil.DrawBorder(r, 2, Color.white);
                }

                // Pin 标记（Pin 合法性归节点编辑器管，这里只做展示）
                foreach (var p in e.Node.Pins)
                {
                    var pinCell = e.Cell + p.LocalCell;
                    if (!InView(pinCell)) continue;
                    var pinCol = CanvasDrawUtil.PinColor(e.Node, p.Pin);
                    CanvasDrawUtil.DrawPinMarker(CellRect(rect, pinCell), p.Facing, p.Pin.Direction, pinCol, false);
                }

                // 锚点格标签：序号 + 名称
                if (InView(e.Cell))
                {
                    var anchor = CellRect(rect, e.Cell);
                    string label = string.IsNullOrEmpty(e.Node.DisplayName) ? e.Node.name : e.Node.DisplayName;
                    GUI.Label(new Rect(anchor.x + 2, anchor.y + 1, 120, 14), $"#{i} {label}", _labelStyle);
                }
            }

            // 矩形框选预览
            if (_marquee)
            {
                var min = Vector2Int.Min(_marqueeStart, _marqueeEnd);
                var max = Vector2Int.Max(_marqueeStart, _marqueeEnd);
                var rA = CellRect(rect, new Vector2Int(min.x, max.y)); // GUI 左上 = 逻辑 (minX, maxY)
                var rB = CellRect(rect, new Vector2Int(max.x, min.y));
                var region = Rect.MinMaxRect(rA.xMin, rA.yMin, rB.xMax, rB.yMax);
                bool erase = _marqueeButton == 1;
                EditorGUI.DrawRect(region, erase ? kColMarqueeErase : kColMarqueeFill);
                CanvasDrawUtil.DrawBorder(region, 2, erase ? kColIllegal : kColOrigin);
            }

            if (outOfView > 0)
                GUI.Label(new Rect(rect.x + 4, rect.y + 2, rect.width - 8, 16),
                    $"⚠ 有 {outOfView} 个格子在视野外（负坐标可点「保存（归一化）」拉回原点，或调大视野）", _hintStyle);
        }

        // ==================== 交互 ====================

        void HandleEvents(Rect rect, LevelDef def, EditorWindow host, ref Vector2 scrollPosition)
        {
            var e = Event.current;

            // 中键拖动画布。优先于所有编辑操作处理，避免误触绘制、框选或节点摆放。
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

            if (e.type == EventType.MouseUp)
            {
                if (_marquee && e.button == _marqueeButton)
                {
                    LevelDefEditUtil.FillRect(def, _marqueeStart, _marqueeEnd, erase: _marqueeButton == 1);
                    _marquee = false;
                    e.Use();
                    host.Repaint();
                }
                _dragging = false;
                _lastCell = kNoCell;
                return;
            }

            // 滚轮直接缩放，并补偿滚动位置，使鼠标指向的画布位置在缩放前后保持不动。
            if (e.type == EventType.ScrollWheel && rect.Contains(e.mousePosition))
            {
                int oldCellSize = CellSize;
                int newCellSize = Mathf.Clamp(oldCellSize - (int)Mathf.Sign(e.delta.y) * 2, 8, 48);
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

            // 框选拖动允许移出画布区域，坐标截断到视野内
            if (_marquee && e.type == EventType.MouseDrag)
            {
                _marqueeEnd = ClampToView(RawCell(rect, e.mousePosition));
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

            switch (Mode)
            {
                case EMode.Paint:
                    if (e.type == EventType.MouseDown)
                    {
                        _dragging = true;
                        _lastCell = kNoCell;
                        // 右键始终擦除；左键从已有格开始整段拖动视为擦除，否则绘制
                        _dragErase = e.button == 1 || def.Canvas.ContainsDelta(cell);
                    }
                    if (_dragging && cell != _lastCell)
                    {
                        if (_dragErase) LevelDefEditUtil.EraseCell(def, cell);
                        else LevelDefEditUtil.PaintCell(def, cell);
                        _lastCell = cell;
                    }
                    e.Use();
                    host.Repaint();
                    break;

                case EMode.Rect:
                    if (e.type == EventType.MouseDown)
                    {
                        _marquee = true;
                        _marqueeButton = e.button;
                        _marqueeStart = _marqueeEnd = cell;
                        e.Use();
                        host.Repaint();
                    }
                    break;

                case EMode.Node:
                    if (e.type != EventType.MouseDown) break;
                    if (e.button == 0)
                    {
                        if (SelectedPreset >= 0 && SelectedPreset < def.PresetNodes.Count)
                        {
                            // 警告不阻止：任意格都可放置，越界/重叠由标红与校验面板提示
                            LevelDefEditUtil.PlacePreset(def, SelectedPreset, cell);
                        }
                        else
                        {
                            int hit = LevelDefEditUtil.PresetAt(def, cell);
                            if (hit >= 0) SelectedPreset = hit;
                        }
                    }
                    else
                    {
                        SelectedPreset = -1; // 右键取消选中
                    }
                    e.Use();
                    host.Repaint();
                    break;
            }
        }

        // ==================== 坐标换算 ====================

        bool InView(Vector2Int c)
        {
            return c.x >= 0 && c.x < ViewCols && c.y >= 0 && c.y < ViewRows;
        }

        Rect CellRect(Rect canvas, Vector2Int c)
        {
            return new Rect(canvas.x + c.x * CellSize, canvas.y + (ViewRows - 1 - c.y) * CellSize, CellSize, CellSize);
        }

        /// <summary>GUI 坐标 → 格坐标，不判界（框选截断用）。</summary>
        Vector2Int RawCell(Rect canvas, Vector2 mouse)
        {
            int x = Mathf.FloorToInt((mouse.x - canvas.x) / CellSize);
            int y = ViewRows - 1 - Mathf.FloorToInt((mouse.y - canvas.y) / CellSize);
            return new Vector2Int(x, y);
        }

        Vector2Int ClampToView(Vector2Int c)
        {
            return new Vector2Int(Mathf.Clamp(c.x, 0, ViewCols - 1), Mathf.Clamp(c.y, 0, ViewRows - 1));
        }

        /// <summary>GUI 坐标 → 格坐标；视野外返回 null。</summary>
        Vector2Int? CellAt(Rect canvas, Vector2 mouse)
        {
            var c = RawCell(canvas, mouse);
            if (c.x < 0 || c.x >= ViewCols || c.y < 0 || c.y >= ViewRows) return null;
            return c;
        }
    }
}

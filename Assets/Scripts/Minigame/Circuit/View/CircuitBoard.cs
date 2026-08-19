using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 棋盘：坐标换算 + 渲染 + 鼠标交互。普通 C# 类，由 CircuitMinigame 每帧驱动。
    ///
    /// 渲染全部走 UGUI（Image/RectTransform），不碰 SpriteRenderer / 世界空间 / Camera。
    /// 所有画出来的件 `raycastTarget = false`：命中判定统一用「鼠标屏幕坐标 → 棋盘局部坐标 → 格」
    /// 一条路，不靠逐件 raycast——格子数量大时那样既慢又难保证优先级。
    ///
    /// **关于 §16.2「Prefab 是布局唯一真相源」**：本类会写 boardArea 下四个 Root 的尺寸与位置，
    /// 那不是绕过硬约定——格子大小取决于关卡画布的行列数，是运行时数据，Prefab 无从预知。
    /// Prefab 权威定义的是 boardArea **在屏幕上的位置与大小**，本类只负责把格子摆进它里面。
    ///
    /// 交互属 View 层豁免区（小游戏说明 §3.3）：允许 Time.unscaledTime、允许 Input 轮询。
    ///
    /// **音效（2026-08-20）**：本类引用了 <see cref="SfxManager"/>，是 §8.5「小游戏不认识任何 Manager」
    /// 的一处窄口豁免（同 CoffeeMinigame 的理由）——那条约束管的是**业务** Manager 与宿主类型，
    /// SfxManager 是 View 层的全局音频出口，只发声、不读写任何业务状态。
    /// 剪辑一律从 <see cref="CircuitMinigameView"/> 上取，本类不出现任何文件名或路径。
    /// </summary>
    public sealed class CircuitBoard
    {
        /// <summary>格子中心死区半径（相对格子边长）：press 落在这个圈内算「抓节点」，圈外算「抓最近边的 Pin」。
        /// 1×1 的十字件四个 Pin 全在同一格上，没有这个死区就永远拖不动它。</summary>
        private const float NodeGrabDeadZone = 0.30f;

        private const float WireWidthFactor = 0.30f;
        private const float DefaultPinSizeInCells = 0.75f;
        private const float CellGap = 2f;
        private const float FunctionIconPaddingFactor = 0.18f;
        private const float MessageSeconds = 3.5f;

        /// <summary>当前在画的那一关。课程包换关时由 <see cref="SetLevel"/> 换掉（不是 readonly 的唯一理由）。</summary>
        private LevelData level;

        private readonly LevelManager levelManager;
        private readonly LinkManager linkManager;
        private readonly CircuitMinigameView view;
        private readonly Camera uiCamera;

        private readonly Pool<Image> gridPool;
        private readonly Pool<Image> nodePool;
        private readonly Pool<Image> iconPool;
        private readonly Pool<Image> linkPool;
        private readonly Pool<Image> previewPool;
        private readonly Pool<Text> labelPool;

        private float cellSize;
        private Vector2Int boardOrigin; // 画布最小格（LevelDefEditUtil 归一化后通常是 0,0，但不假设）

        // ── 描格状态（§4.6）──
        private PinData drawFromPin;
        private readonly List<Vector2Int> drawPath = new List<Vector2Int>();

        // ── 摆件 / 移动状态：一律先幽灵预览、松手才提交 ──
        private NodeDef pendingPlacement;
        private NodeData draggingNode;
        private Vector2Int dragGrabOffset;
        private Vector2Int hoverCell;
        private bool hoverValid;

        /// <summary>上一次结算时处于「满足」的电池（NodeId）。只用来找翻转的那一刻——
        /// 由不满足变满足响一次、由满足变回不满足响一次，搭建途中「一直没满足」是安静的。
        /// 换关时由 <see cref="SeedLitBaseline"/> 静默重置：翻页看到的是上次留下的布线，不该当成刚做出来的事。</summary>
        private readonly HashSet<long> litNodes = new HashSet<long>();

        // ── 右键短击（拖动不算，留给将来的平移）──
        private bool rmbHeld;
        private Vector3 rmbDownScreen;
        private const float DragThresholdPixels = 6f;

        private float messageEndTime;

        /// <summary>布局发生了改变（增删线、摆件、移件）：预算条与件库余量要重刷。</summary>
        public event Action LayoutChanged;

        /// <summary>正在描的线长度变了：只有导线预算那一个标签要重刷，别走整套 <see cref="LayoutChanged"/>。</summary>
        public event Action DrawingChanged;

        /// <summary>玩家当前从件库选中的中转件；null = 没选。件库拖放期间也走它，预览与高亮共用一套。</summary>
        public NodeDef PendingPlacement => pendingPlacement;

        /// <summary>当前格子边长（像素）。跟随关卡行列数与分辨率变化，件库的跟手图标按它取尺寸。</summary>
        public float CellSize => cellSize;

        /// <summary>正在描的这条线已经占了几格，没在描线时为 0。
        /// **含起点接线格**——与 <see cref="LinkManager.TryCreateLink"/> 的预算口径一致（§8.3）。</summary>
        public int PendingLinkCells => drawFromPin != null ? drawPath.Count : 0;

        public CircuitBoard(LevelData level, LevelManager levelManager, LinkManager linkManager,
            CircuitMinigameView view, Camera uiCamera)
        {
            this.level = level;
            this.levelManager = levelManager;
            this.linkManager = linkManager;
            this.view = view;
            this.uiCamera = uiCamera;

            gridPool = new Pool<Image>(view.gridRoot, NewImage);
            nodePool = new Pool<Image>(view.nodeRoot, NewImage);
            iconPool = new Pool<Image>(view.nodeRoot, NewImage);
            linkPool = new Pool<Image>(view.linkRoot, NewImage);
            previewPool = new Pool<Image>(view.previewRoot, NewImage);
            labelPool = new Pool<Text>(view.nodeRoot, NewLabel);

            SeedLitBaseline();
        }

        /// <summary>
        /// 换一关（课程包逐关推进用）。调用方随后必须自己调 <see cref="LayoutRoots"/> + <see cref="RebuildAll"/>：
        /// 换关同时也可能换画布尺寸，格子大小得重算。
        ///
        /// **必须换关而不是每关 new 一个 CircuitBoard**：对象池绑在共享的 gridRoot/nodeRoot/linkRoot 上、
        /// 且各自持有自己的 items 列表，新 board 不认识旧 board 造的图元，上一关的线会原样留在屏幕上。
        ///
        /// 顺带清掉所有握着上一关对象引用的瞬时状态（正在描的线、正在拖的节点）——
        /// 玩家在换关按钮上松手时这些状态未必是干净的。
        /// </summary>
        public void SetLevel(LevelData next)
        {
            level = next;

            drawFromPin = null;
            drawPath.Clear();
            pendingPlacement = null;
            draggingNode = null;
            hoverValid = false;
            rmbHeld = false;

            SeedLitBaseline();
        }

        // ═══════════ 布局与坐标 ═══════════

        /// <summary>按画布行列数把四个 Root 摆进 boardArea 并算出格子大小。开局与分辨率变化时调。</summary>
        public void LayoutRoots()
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var grid in level.Def.Canvas.Grids)
            {
                minX = Mathf.Min(minX, grid.DeltaPosition.x);
                minY = Mathf.Min(minY, grid.DeltaPosition.y);
                maxX = Mathf.Max(maxX, grid.DeltaPosition.x);
                maxY = Mathf.Max(maxY, grid.DeltaPosition.y);
            }
            if (minX > maxX)
            {
                // 空画布：编辑器校验会报错，这里只保证不除零
                boardOrigin = Vector2Int.zero;
                cellSize = 32f;
                return;
            }

            boardOrigin = new Vector2Int(minX, minY);
            int cols = maxX - minX + 1;
            int rows = maxY - minY + 1;

            var area = view.boardArea.rect.size;
            cellSize = Mathf.Clamp(Mathf.Min(area.x / cols, area.y / rows), 12f, 96f);

            var size = new Vector2(cols * cellSize, rows * cellSize);
            SetupRoot(view.gridRoot, size);
            SetupRoot(view.nodeRoot, size);
            SetupRoot(view.linkRoot, size);
            SetupRoot(view.previewRoot, size);
        }

        /// <summary>四个 Root 共用同一坐标系：pivot 左下、在 boardArea 中居中。</summary>
        private static void SetupRoot(RectTransform root, Vector2 size)
        {
            root.anchorMin = root.anchorMax = new Vector2(.5f, .5f);
            root.pivot = Vector2.zero;
            root.sizeDelta = size;
            root.anchoredPosition = -size * .5f;
        }

        private Vector2 CellToLocal(Vector2Int cell) => new Vector2(
            (cell.x - boardOrigin.x + .5f) * cellSize,
            (cell.y - boardOrigin.y + .5f) * cellSize);

        /// <summary>鼠标 → 棋盘格；指针不在棋盘范围内时返回 false。</summary>
        private bool TryGetPointerCell(out Vector2Int cell, out Vector2 offsetInCell)
            => TryGetCellAt(Input.mousePosition, out cell, out offsetInCell);

        /// <summary>任意屏幕坐标 → 棋盘格。件库拖放的落点判定也走这里，坐标换算只此一份。</summary>
        private bool TryGetCellAt(Vector2 screenPosition, out Vector2Int cell, out Vector2 offsetInCell)
        {
            cell = default;
            offsetInCell = default;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    view.gridRoot, screenPosition, uiCamera, out var local))
                return false;

            var fx = local.x / cellSize;
            var fy = local.y / cellSize;
            cell = new Vector2Int(Mathf.FloorToInt(fx) + boardOrigin.x, Mathf.FloorToInt(fy) + boardOrigin.y);
            // 相对格心的偏移，单位 = 格边长；用于「按最近边挑 Pin」与中心死区判定
            offsetInCell = new Vector2(fx - Mathf.Floor(fx) - .5f, fy - Mathf.Floor(fy) - .5f);
            return level.IsInCanvas(cell);
        }

        // ═══════════ 输入 ═══════════

        /// <summary>由 CircuitMinigame 每帧调用一次。</summary>
        public void HandleInput()
        {
            bool overBoard = TryGetPointerCell(out var cell, out var offset);
            hoverValid = overBoard;
            hoverCell = cell;

            HandleLeftButton(overBoard, cell, offset);
            HandleRightButton(overBoard, cell);

            if (Time.unscaledTime > messageEndTime && view.messageLabel != null &&
                !string.IsNullOrEmpty(view.messageLabel.text))
                view.messageLabel.text = string.Empty;

            DrawPreview();
        }

        private void HandleLeftButton(bool overBoard, Vector2Int cell, Vector2 offset)
        {
            if (Input.GetMouseButtonDown(0) && overBoard)
                BeginLeftGesture(cell, offset);

            if (drawFromPin != null)
            {
                int before = drawPath.Count;
                UpdateDrawPath();
                if (drawPath.Count != before) DrawingChanged?.Invoke();

                // 描线音：只有「往前画」才响——退格截断是撤销，不该和画出去一个声音。
                // 判定放在这里而不是 UpdateDrawPath 的循环里：鼠标快扫时那个循环一帧能吃十几格，
                // 逐格发声会糊成一片；按帧比总数，天然就是「一帧最多一声」
                if (drawPath.Count > before)
                    SfxManager.PlayOnce(view.drawStepClip, view.drawStepVolume);
            }

            if (!Input.GetMouseButtonUp(0)) return;

            bool wasDrawing = drawFromPin != null;
            if (wasDrawing) FinishLinkDrag();
            else if (draggingNode != null) FinishNodeDrag();

            drawFromPin = null;
            draggingNode = null;
            drawPath.Clear();

            // 描格一结束预算标签就得把「正在画」的部分退回去。建线成功那条路径已经自己清干净并发过
            // LayoutChanged 了，这里再发一次也只是重刷一个标签；而没接到 Pin 的静默作废根本不发
            // LayoutChanged，数字会卡在松手前的值上——所以这一发不能省
            if (wasDrawing) DrawingChanged?.Invoke();
        }

        private void BeginLeftGesture(Vector2Int cell, Vector2 offset)
        {
            // 件库选中状态下，左键就是落子
            if (pendingPlacement != null)
            {
                TryPlacePending(cell);
                return;
            }

            var node = PickNode(cell);
            if (node == null) return; // 空格或线上：左键无操作（删线走右键）

            var pin = PickPinOnCell(node, cell, offset);
            if (pin != null)
            {
                if (pin.Link != null)
                {
                    ShowMessage("这个接口已经接了线，先右键删掉它");
                    return;
                }
                drawFromPin = pin;
                drawPath.Clear();
                drawPath.Add(pin.Owner.GetPinPortCell(pin.IndexInNode));
                SfxManager.PlayOnce(view.linkConnectClip, view.linkConnectVolume); // 从节点连出
                DrawingChanged?.Invoke(); // 起点那一格也计入预算，按下即可见
                return;
            }

            // 落在中心死区：抓件移动（题面的电源电池 CanMove 为 false，MoveNode 会拒）
            if (node.Def.NodeType != ENodeType.Transit || !node.CanMove) return;
            draggingNode = node;
            dragGrabOffset = cell - node.Origin;
        }

        /// <summary>
        /// 描格（§4.6，手感规则原样沿用旧实现）：
        /// - 鼠标落回已描过的格 → 截断到那一格（可一次退多格，鼠标快也不卡）；
        /// - 斜向移动 → 优先沿上一段方向先走一格再转弯（拐角更少）；
        /// - 撞到非法格 → **停住不延伸、不作废**，玩家绕开或退回继续描。
        ///
        /// **预算不拦描格**（2026-08-16 改）：可以照画不误，超出的那一段画成非法色，
        /// 松手提交时才由 <see cref="LinkManager.TryCreateLink"/> 拒掉（§8.3）。
        /// 旧版在这里夹死，玩家撞到预算墙时手感与撞画布边界无异，分不清是「没地方了」还是「没预算了」。
        /// </summary>
        private void UpdateDrawPath()
        {
            if (drawPath.Count == 0) return;
            if (!TryGetPointerCell(out var target, out _)) return;

            var tail = drawPath[drawPath.Count - 1];
            if (target == tail) return;

            int back = drawPath.LastIndexOf(target);
            if (back >= 0)
            {
                drawPath.RemoveRange(back + 1, drawPath.Count - back - 1);
                return;
            }

            int guard = 256; // 鼠标跨屏跳跃时的步数上限，防单帧死循环
            while (tail != target && guard-- > 0)
            {
                var next = tail + NextStepToward(tail, target);
                if (!CanDrawInto(next)) break;
                drawPath.Add(next);
                tail = next;
            }
        }

        /// <summary>下一格走向：能延续上一段方向就延续（减少拐角），否则先走横向。</summary>
        private Vector2Int NextStepToward(Vector2Int from, Vector2Int target)
        {
            var diff = target - from;
            var lastDir = drawPath.Count >= 2
                ? drawPath[drawPath.Count - 1] - drawPath[drawPath.Count - 2]
                : Vector2Int.zero;

            if (lastDir.x != 0 && diff.x != 0 && lastDir.x > 0 == diff.x > 0)
                return new Vector2Int(diff.x > 0 ? 1 : -1, 0);
            if (lastDir.y != 0 && diff.y != 0 && lastDir.y > 0 == diff.y > 0)
                return new Vector2Int(0, diff.y > 0 ? 1 : -1);
            if (diff.x != 0)
                return new Vector2Int(diff.x > 0 ? 1 : -1, 0);
            return new Vector2Int(0, diff.y > 0 ? 1 : -1);
        }

        /// <summary>能否把线延伸进这一格。**不看预算**——超预算由提交时拒绝，见 <see cref="UpdateDrawPath"/>。</summary>
        private bool CanDrawInto(Vector2Int cell)
        {
            if (!level.IsInCanvas(cell) || level.IsOccupied(cell)) return false;
            return !drawPath.Contains(cell);
        }

        private void FinishLinkDrag()
        {
            if (drawPath.Count == 0) return;
            var endCell = drawPath[drawPath.Count - 1];

            var toPin = FindPinAtPortCell(endCell, drawFromPin.Owner);
            if (toPin == null) return; // 没描到任何 Pin 的接口格：本次绘制静默作废（§4.6）

            var link = linkManager.TryCreateLink(level, drawFromPin, toPin, out var reason, drawPath);
            if (link == null)
            {
                ShowMessage($"连线失败：{reason}"); // 失败原因必须在界面可见（超预算也走这里）
                return;
            }

            SfxManager.PlayOnce(view.linkConnectClip, view.linkConnectVolume); // 连到节点

            // 先清描格状态再播 LayoutChanged：这条线的格数此刻已经进了 UsedLinkCells，
            // drawPath 不清的话标签会把它和 PendingLinkCells 重复加一遍
            drawFromPin = null;
            drawPath.Clear();

            RebuildLinks();
            RebuildNodes(); // 点亮状态可能变了
            CommitLayoutChange();
        }

        private void FinishNodeDrag()
        {
            var target = hoverCell - dragGrabOffset;
            if (target == draggingNode.Origin) return; // 原地松手：当作一次点击，什么都不做
            if (!hoverValid || !levelManager.CanMoveNodeTo(level, draggingNode, target)) return;
            if (!levelManager.MoveNode(level, draggingNode, target)) return;

            SfxManager.PlayOnce(view.nodePlaceClip, view.nodePlaceVolume); // 挪件成功落位，与摆件同一个落件音

            RebuildLinks(); // 附着导线已被删除并退还预算
            RebuildNodes();
            CommitLayoutChange();
        }

        /// <summary>点选落子：摆完保持选中，可以连摆同一种件。</summary>
        private void TryPlacePending(Vector2Int cell)
        {
            if (!TryPlace(pendingPlacement, cell)) return;

            // 摆满上限就自动取消选中，免得玩家一直点空气
            if (!levelManager.CanBuild(level, pendingPlacement))
                SetPendingPlacement(null);
        }

        /// <summary>把件落到 cell（作为 Origin）。失败原因一律在界面上说清楚。</summary>
        private bool TryPlace(NodeDef def, Vector2Int cell)
        {
            if (!levelManager.CanBuild(level, def))
            {
                ShowMessage("这种中转件已经用完了");
                return false;
            }
            if (!levelManager.CanPlaceNode(level, def, cell))
            {
                ShowMessage("这里放不下");
                return false;
            }
            levelManager.PlaceNode(level, def, cell);
            SfxManager.PlayOnce(view.nodePlaceClip, view.nodePlaceVolume);
            RebuildNodes();
            CommitLayoutChange();
            return true;
        }

        private void HandleRightButton(bool overBoard, Vector2Int cell)
        {
            if (Input.GetMouseButtonDown(1))
            {
                rmbHeld = true;
                rmbDownScreen = Input.mousePosition;
            }
            if (!rmbHeld || !Input.GetMouseButtonUp(1)) return;
            rmbHeld = false;

            // 右键选中件库时，第一下先取消选中而不是删东西
            if (pendingPlacement != null)
            {
                SetPendingPlacement(null);
                return;
            }
            if (!overBoard) return;
            if (((Vector2)(Input.mousePosition - rmbDownScreen)).magnitude > DragThresholdPixels) return;

            var occupant = level.GetOccupant(cell);
            if (occupant == null) return;

            // 删除本身不发声（静默即反馈，同家具无效落点的口径）；但走 CommitLayoutChange 是必须的——
            // 拆掉一条线正是电池「由满足变回不满足」的典型来源，那一声得响
            if (occupant.Link != null)
            {
                linkManager.DeleteLink(level, occupant.Link);
                RebuildLinks();
                RebuildNodes();
                CommitLayoutChange();
                return;
            }

            if (occupant.Node != null && occupant.Node.Def.NodeType == ENodeType.Transit)
            {
                if (!levelManager.RemoveNode(level, occupant.Node)) return;
                RebuildLinks();
                RebuildNodes();
                CommitLayoutChange();
            }
        }

        // ═══════════ 布局提交与电池状态音 ═══════════

        /// <summary>
        /// 一次**改变了布局**的操作收尾：先把电池的满足状态翻转播成声音，再通知界面刷新。
        ///
        /// 增删线、摆件、挪件、删件五条路径全走这里，是为了让「谁会改变点亮状态」这件事只有一个答案——
        /// 供电是纯函数（<see cref="CircuitSolver"/>），各 Manager 改完布局都已经重算过了，
        /// 到这里只需要拿结果与上一次比对。纯高亮变化（如件库选中）不走本方法，直接发 LayoutChanged。
        /// </summary>
        private void CommitLayoutChange()
        {
            ReportLitChanges();
            LayoutChanged?.Invoke();
        }

        /// <summary>
        /// 比对电池的满足状态并发声：新满足响一声正向音，失去满足响一声负向音。
        /// 「一直没满足」不响——搭建途中大半时间都不满足，那会变成噪音。
        ///
        /// 一次操作同时点亮一个又弄灭另一个时（挪件才可能），**正向优先只响一声**：
        /// 两声叠在同一帧只会糊成一团，而玩家刚做成的事比顺带弄坏的更值得先听见。
        /// </summary>
        private void ReportLitChanges()
        {
            bool anyLit = false, anyUnlit = false;
            foreach (var node in level.Nodes)
            {
                if (node.Def.NodeType != ENodeType.Condition) continue;
                if (node.IsLit) anyLit |= litNodes.Add(node.NodeId);
                else anyUnlit |= litNodes.Remove(node.NodeId);
            }

            if (anyLit) SfxManager.PlayOnce(view.batteryLitClip, view.batteryLitVolume);
            else if (anyUnlit) SfxManager.PlayOnce(view.batteryUnlitClip, view.batteryUnlitVolume);
        }

        /// <summary>把当前满足状态**静默**记为基线（开局与换关时）：翻页看到的是上次留下的布线，
        /// 不该被当成刚刚做成的事而响一片音。</summary>
        private void SeedLitBaseline()
        {
            litNodes.Clear();
            foreach (var node in level.Nodes)
                if (node.Def.NodeType == ENodeType.Condition && node.IsLit) litNodes.Add(node.NodeId);
        }

        // ═══════════ 拾取 ═══════════

        private NodeData PickNode(Vector2Int cell)
        {
            var occupant = level.GetOccupant(cell);
            return occupant?.Node;
        }

        /// <summary>
        /// 这一格上、离按下点最近的那条边所对应的 Pin。
        /// 按下点落在格心死区内返回 null（表示玩家想抓的是件本身而不是接口）。
        /// </summary>
        private static PinData PickPinOnCell(NodeData node, Vector2Int cell, Vector2 offsetInCell)
        {
            if (offsetInCell.magnitude < NodeGrabDeadZone) return null;

            var localCell = cell - node.Origin;
            PinData best = null;
            float bestDot = float.NegativeInfinity;
            foreach (var pin in node.Pins)
            {
                if (pin.Layout.LocalCell != localCell) continue;
                var outward = Direction4.ToOffset(pin.Layout.Facing);
                // 按下方向与 Pin 朝向的贴合度：同一格上多个 Pin 时取最贴合的那个
                float dot = offsetInCell.x * outward.x + offsetInCell.y * outward.y;
                if (dot <= 0f || dot <= bestDot) continue;
                bestDot = dot;
                best = pin;
            }
            return best;
        }

        /// <summary>按接线格反查 Pin（排除起点所在节点）。遍历按 NodeId 稳定顺序。</summary>
        private PinData FindPinAtPortCell(Vector2Int cell, NodeData exclude)
        {
            foreach (var node in level.Nodes)
            {
                if (node == exclude) continue;
                for (int i = 0; i < node.Pins.Count; i++)
                    if (node.GetPinPortCell(i) == cell)
                        return node.Pins[i];
            }
            return null;
        }

        // ═══════════ 件库联动 ═══════════

        public void SetPendingPlacement(NodeDef def)
        {
            pendingPlacement = def;
            LayoutChanged?.Invoke(); // 件库高亮跟着变
        }

        /// <summary>
        /// 把件落在给定屏幕坐标所在的格上（件库拖放松手时调）。
        /// 落点不在画布上直接返回 false 且**不提示**——那是玩家把件拖回去的反悔动作，不是操作失败。
        /// </summary>
        public bool TryPlaceAt(NodeDef def, Vector2 screenPosition)
        {
            if (def == null) return false;
            if (!TryGetCellAt(screenPosition, out var cell, out _)) return false;
            return TryPlace(def, cell);
        }

        // ═══════════ 渲染 ═══════════

        public void RebuildAll()
        {
            RebuildGrid();
            RebuildNodes();
            RebuildLinks();
            DrawPreview();
        }

        private void RebuildGrid()
        {
            gridPool.Begin();
            foreach (var grid in level.Def.Canvas.Grids)
            {
                var image = gridPool.Next();
                var style = view.visualStyle;
                image.sprite = style != null ? style.cellSprite : null;
                image.color = view.cellColor;
                var rect = image.rectTransform;
                rect.sizeDelta = new Vector2(cellSize - CellGap, cellSize - CellGap);
                rect.anchoredPosition = CellToLocal(grid.DeltaPosition);
            }
            gridPool.End();
        }

        private void RebuildNodes()
        {
            nodePool.Begin();
            iconPool.Begin();
            labelPool.Begin();

            foreach (var node in level.Nodes) // NodeId 稳定顺序
            {
                DrawNodeBody(node);

                for (int i = 0; i < node.Pins.Count; i++)
                    DrawPinMarker(node, node.Pins[i]);

                DrawMobilityIcon(node);

                var caption = Caption(node);
                if (string.IsNullOrEmpty(caption)) continue;

                var visualStyle = view.visualStyle;
                bool hasDigitSprites = visualStyle != null && visualStyle.captionDigits != null
                                       && visualStyle.captionDigits.Length >= 10;
                if (hasDigitSprites)
                {
                    DrawCaptionSprites(node, caption);
                }
                else
                {
                    var label = labelPool.Next();
                    label.text = caption;
                    label.fontSize = Mathf.Max(10, Mathf.RoundToInt(cellSize * 0.34f));
                    var labelRect = label.rectTransform;
                    labelRect.sizeDelta = new Vector2(cellSize * 2.4f, cellSize);
                    if (TryGetShapeBounds(node.Def.Shape, out int cMinX, out int cMinY, out int cW, out int cH))
                    {
                        var cBottomLeft = CellToLocal(node.Origin + new Vector2Int(cMinX, cMinY));
                        var cCenter = cBottomLeft + new Vector2((cW - 1) * cellSize, (cH - 1) * cellSize) * .5f;
                        var cVis = new Vector2(cW * cellSize - CellGap, cH * cellSize - CellGap);
                        labelRect.anchoredPosition = new Vector2(
                            cCenter.x + cVis.x * .5f - cellSize * 0.15f,
                            cCenter.y + cVis.y * .5f - cellSize * 0.5f);
                    }
                    else
                    {
                        labelRect.anchoredPosition = CellToLocal(node.Origin) + new Vector2(0, cellSize * .1f);
                    }
                }
            }

            nodePool.End();
            iconPool.End();
            labelPool.End();
        }

        /// <summary>
        /// 标准节点皮肤只覆盖完整矩形：一张九宫格底图 + 一张等比功能图标。
        /// Shape 的玩法判定仍逐格进行；未来若真做异形节点，安全地退回旧逐格表现，
        /// 而不是把一张矩形底图错误铺进空格。
        /// </summary>
        private void DrawNodeBody(NodeData node)
        {
            if (node.Def.BackgroundSprite != null && TryGetFilledRectangle(node.Def.Shape,
                    out int minX, out int minY, out int width, out int height))
            {
                var background = nodePool.Next();
                background.sprite = node.Def.BackgroundSprite;
                background.type = Image.Type.Sliced;
                background.preserveAspect = false;
                background.color = BodyColor(node);
                var backgroundRect = background.rectTransform;
                backgroundRect.sizeDelta = new Vector2(width * cellSize - CellGap, height * cellSize - CellGap);
                backgroundRect.anchoredPosition = CellToLocal(node.Origin + new Vector2Int(minX, minY)) +
                                                 new Vector2((width - 1) * cellSize, (height - 1) * cellSize) * .5f;

                if (node.Def.FunctionIconSprite == null) return;

                var icon = iconPool.Next();
                icon.sprite = node.Def.FunctionIconSprite;
                icon.type = Image.Type.Simple;
                icon.preserveAspect = true;
                icon.color = node.Def.IconColor;
                var iconRect = icon.rectTransform;
                float padding = cellSize * FunctionIconPaddingFactor;
                iconRect.sizeDelta = new Vector2(
                    Mathf.Max(0f, backgroundRect.sizeDelta.x - padding * 2f),
                    Mathf.Max(0f, backgroundRect.sizeDelta.y - padding * 2f));
                iconRect.anchoredPosition = backgroundRect.anchoredPosition;
                return;
            }

            // 无美术或非矩形节点的兼容表现：保持原先逐格渲染，绝不改变占格可读性。
            var body = BodyColor(node);
            foreach (var cell in node.Def.Shape.CellsAt(node.Origin))
            {
                var image = nodePool.Next();
                image.sprite = null;
                image.type = Image.Type.Simple;
                image.preserveAspect = false;
                image.color = body;
                var rect = image.rectTransform;
                rect.sizeDelta = new Vector2(cellSize - CellGap, cellSize - CellGap);
                rect.anchoredPosition = CellToLocal(cell);
            }
        }

        private static bool TryGetFilledRectangle(GridGroup shape, out int minX, out int minY,
            out int width, out int height)
        {
            if (!TryGetShapeBounds(shape, out minX, out minY, out width, out height)) return false;
            return shape.Grids.Count == width * height;
        }

        private static bool TryGetShapeBounds(GridGroup shape, out int minX, out int minY,
            out int width, out int height)
        {
            minX = minY = width = height = 0;
            if (shape == null || shape.Grids == null || shape.Grids.Count == 0) return false;

            int maxX, maxY;
            minX = maxX = shape.Grids[0].DeltaPosition.x;
            minY = maxY = shape.Grids[0].DeltaPosition.y;
            foreach (var grid in shape.Grids)
            {
                minX = Mathf.Min(minX, grid.DeltaPosition.x);
                maxX = Mathf.Max(maxX, grid.DeltaPosition.x);
                minY = Mathf.Min(minY, grid.DeltaPosition.y);
                maxY = Mathf.Max(maxY, grid.DeltaPosition.y);
            }

            width = maxX - minX + 1;
            height = maxY - minY + 1;
            return true;
        }

        /// <summary>
        /// 用 Sprite 逐字符绘制 Caption 数字（电源供电量 / 电池 received/required）。
        /// 字符 unrecognized 时静默跳过，不影响前后字符的排列。
        /// </summary>
        private void DrawCaptionSprites(NodeData node, string caption)
        {
            var style = view.visualStyle;
            if (style == null || style.captionDigits == null || style.captionDigits.Length < 10) return;
            if (!TryGetShapeBounds(node.Def.Shape, out int minX, out int minY, out int width, out int height)) return;

            var bottomLeft = CellToLocal(node.Origin + new Vector2Int(minX, minY));
            var nodeCenter = bottomLeft + new Vector2((width - 1) * cellSize, (height - 1) * cellSize) * .5f;
            var visualSize = new Vector2(width * cellSize - CellGap, height * cellSize - CellGap);

            float dSize = cellSize * Mathf.Max(0f, style.captionDigitSize);
            float dSpacing = cellSize * Mathf.Max(0f, style.captionDigitSpacing);

            bool hasPowerIcon = node.Def.NodeType == ENodeType.Resource
                                  && style.captionPowerIcon != null;
            float iconSize = hasPowerIcon
                ? cellSize * Mathf.Max(0f, style.captionPowerIconSize)
                : 0f;

            float totalW = 0f;
            if (hasPowerIcon)
                totalW += iconSize;

            for (int i = 0; i < caption.Length; i++)
            {
                char c = caption[i];
                if (c >= '0' && c <= '9')
                    totalW += dSize;
                else if (c == '/' && style.captionSlashSprite != null)
                    totalW += dSize * 0.5f;
                else
                    continue;
                if (i < caption.Length - 1) totalW += dSpacing;
            }

            float x = nodeCenter.x + visualSize.x * .5f - cellSize * 0.15f - totalW;
            float y = nodeCenter.y + visualSize.y * .5f - cellSize * 0.5f;

            // 电池节点：先画闪电图标
            if (hasPowerIcon)
            {
                var iconImage = iconPool.Next();
                iconImage.sprite = style.captionPowerIcon;
                iconImage.type = Image.Type.Simple;
                iconImage.preserveAspect = true;
                iconImage.color = style.captionDigitColor;
                var iconRect = iconImage.rectTransform;
                iconRect.sizeDelta = new Vector2(iconSize, iconSize);
                iconRect.anchoredPosition = new Vector2(x + iconSize * .5f, y);
                x += iconSize;
            }

            // 再画数字
            for (int i = 0; i < caption.Length; i++)
            {
                char c = caption[i];
                Sprite sprite = null;
                float w = 0f;

                if (c >= '0' && c <= '9')
                {
                    sprite = style.captionDigits[c - '0'];
                    w = dSize;
                }
                else if (c == '/' && style.captionSlashSprite != null)
                {
                    sprite = style.captionSlashSprite;
                    w = dSize * 0.5f;
                }

                if (sprite == null) continue;

                var image = iconPool.Next();
                image.sprite = sprite;
                image.type = Image.Type.Simple;
                image.preserveAspect = true;
                image.color = style.captionDigitColor;
                var rect = image.rectTransform;
                rect.sizeDelta = new Vector2(w, dSize);
                rect.anchoredPosition = new Vector2(x + w * .5f, y);

                x += w + dSpacing;
            }
        }

        /// <summary>
        /// 节点右上角的全局移动状态图标。实际可移动性必须同时满足「中转件」与关卡实例 CanMove：
        /// LevelManager 会拒绝移动电源/电池，即使资产误把它们的 CanMove 勾上，表现也不能撒谎。
        /// </summary>
        private void DrawMobilityIcon(NodeData node)
        {
            var style = view.visualStyle;
            if (style == null ||
                !TryGetShapeBounds(node.Def.Shape, out int minX, out int minY, out int width, out int height))
                return;

            bool movable = node.Def.NodeType == ENodeType.Transit && node.CanMove;
            var sprite = movable ? style.movableIcon : style.immovableIcon;
            if (sprite == null) return;

            var icon = iconPool.Next();
            icon.sprite = sprite;
            icon.type = Image.Type.Simple;
            icon.preserveAspect = true;
            icon.color = movable ? style.movableIconColor : style.immovableIconColor;

            var bottomLeftCell = CellToLocal(node.Origin + new Vector2Int(minX, minY));
            var nodeCenter = bottomLeftCell +
                             new Vector2((width - 1) * cellSize, (height - 1) * cellSize) * .5f;
            var visualSize = new Vector2(width * cellSize - CellGap, height * cellSize - CellGap);
            float size = cellSize * Mathf.Max(0f, style.mobilityIconSizeInCells);
            float padding = cellSize * Mathf.Max(0f, style.mobilityIconPaddingInCells);

            var rect = icon.rectTransform;
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = new Vector2(
                nodeCenter.x - visualSize.x * .5f + padding + size * .5f,
                nodeCenter.y - visualSize.y * .5f + padding + size * .5f);
        }

        private void DrawPinMarker(NodeData node, PinData pin)
        {
            var layout = pin.Layout;
            var outward = Direction4.ToOffset(layout.Facing);
            var image = nodePool.Next();
            var style = view.visualStyle;
            var sprite = style != null ? style.pinSprite : null;
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = sprite != null;
            image.color = PinColor(node, pin) * (style != null ? style.pinColorMultiplier : Color.white);
            var rect = image.rectTransform;
            float sizeInCells = style != null
                ? Mathf.Max(0f, style.pinSizeInCells)
                : DefaultPinSizeInCells;
            float s = cellSize * sizeInCells;
            rect.sizeDelta = new Vector2(s, s);
            rect.anchoredPosition = CellToLocal(node.Origin + layout.LocalCell) +
                                    new Vector2(outward.x, outward.y) * (cellSize * .34f);
        }

        private void RebuildLinks()
        {
            linkPool.Begin();
            var style = view.visualStyle;
            foreach (var link in level.Links) // LinkId 稳定顺序
                DrawPolyline(linkPool, link.PathCells,
                    link.Power > 0 ? style.wirePoweredColor : style.wireUnpoweredColor, WireWidthFactor,
                    link.FromPin, link.ToPin);
            linkPool.End();
        }

        /// <summary>每帧重画：描线轨迹、摆件/移件的幽灵预览。</summary>
        private void DrawPreview()
        {
            previewPool.Begin();

            if (drawFromPin != null && drawPath.Count > 0)
            {
                // 超出预算的那一段直接画成非法色：顶栏数字之外，棋盘上也要一眼看得出超了多少（§8.3）。
                // 不限预算时 RemainingLinkCells 是 int.MaxValue，整条线都在预算内
                var style = view.visualStyle;
                DrawPolyline(previewPool, drawPath, style.wirePreviewColor, WireWidthFactor * 0.85f,
                    level.RemainingLinkCells, style.wireOverflowColor, true, drawFromPin);
            }

            var ghostDef = pendingPlacement ?? draggingNode?.Def;
            if (ghostDef != null && hoverValid)
            {
                var ghostOrigin = draggingNode != null ? hoverCell - dragGrabOffset : hoverCell;
                bool legal = draggingNode != null
                    ? levelManager.CanMoveNodeTo(level, draggingNode, ghostOrigin)
                    : levelManager.CanPlaceNode(level, ghostDef, ghostOrigin);
                var color = legal ? view.legalColor : view.illegalColor;
                foreach (var cell in ghostDef.Shape.CellsAt(ghostOrigin))
                {
                    var image = previewPool.Next();
                    image.color = color;
                    var rect = image.rectTransform;
                    rect.sizeDelta = new Vector2(cellSize - CellGap, cellSize - CellGap);
                    rect.anchoredPosition = CellToLocal(cell);
                }
            }

            previewPool.End();
        }

        private void DrawPolyline(Pool<Image> pool, IReadOnlyList<Vector2Int> cells, Color color, float widthFactor,
            PinData startPin = null, PinData endPin = null)
            => DrawPolyline(pool, cells, color, widthFactor, int.MaxValue, color, false, startPin, endPin);

        /// <summary>
        /// 折线；下标 ≥ <paramref name="overflowFrom"/> 的格与其连接段改用 <paramref name="overflowColor"/>。
        /// 传下标而不是传委托：本方法逐帧跑，闭包会churn 出 GC。
        /// </summary>
        private void DrawPolyline(Pool<Image> pool, IReadOnlyList<Vector2Int> cells, Color color, float widthFactor,
            int overflowFrom, Color overflowColor, bool isPreview = false, PinData startPin = null, PinData endPin = null)
        {
            var style = view.visualStyle;
            if (style != null && style.wireStraightSprite != null && style.wireCornerSprite != null)
            {
                DrawStyledPolyline(pool, cells, color, overflowFrom, overflowColor, isPreview, startPin, endPin, style);
                return;
            }

            float w = cellSize * widthFactor;
            for (int i = 0; i < cells.Count; i++)
            {
                var tint = i < overflowFrom ? color : overflowColor;
                var joint = pool.Next();
                joint.sprite = null;
                joint.type = Image.Type.Simple;
                joint.preserveAspect = false;
                joint.color = tint;
                var jointRect = joint.rectTransform;
                jointRect.localRotation = Quaternion.identity;
                jointRect.sizeDelta = new Vector2(w, w);
                jointRect.anchoredPosition = CellToLocal(cells[i]);

                if (i == 0) continue;
                // 相邻格必然 4 向相接，所以连接段长度恒为一个格边长
                var a = CellToLocal(cells[i - 1]);
                var b = CellToLocal(cells[i]);
                var segment = pool.Next();
                segment.sprite = null;
                segment.type = Image.Type.Simple;
                segment.preserveAspect = false;
                segment.color = tint;
                var segmentRect = segment.rectTransform;
                segmentRect.localRotation = Quaternion.identity;
                segmentRect.anchoredPosition = (a + b) * .5f;
                segmentRect.sizeDelta = Mathf.Approximately(a.x, b.x)
                    ? new Vector2(w, cellSize)
                    : new Vector2(cellSize, w);
            }
        }

        /// <summary>
        /// 美术版折线：每个路径格恰好一张图，按相邻格决定直线、转角或端点，并旋转到正确朝向。
        /// PathCells 已由 LinkManager 保证是 from→to 的四向连续路径；本方法只读取它，不参与任何判定。
        /// </summary>
        private void DrawStyledPolyline(Pool<Image> pool, IReadOnlyList<Vector2Int> cells, Color color,
            int overflowFrom, Color overflowColor, bool isPreview, PinData startPin, PinData endPin,
            CircuitVisualStyleConfig style)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                var image = pool.Next();
                var tint = i < overflowFrom ? color : overflowColor;
                Sprite sprite;
                float rotation;
                bool mirrorX = false;

                if (cells.Count == 1)
                {
                    // 刚按下 Pin、还没经过第二格时，显示一个朝默认方向的断头，给玩家明确的起笔反馈。
                    sprite = style.wireOpenEndSprite != null ? style.wireOpenEndSprite : style.wireStraightSprite;
                    rotation = 0f;
                }
                else if (i == 0)
                {
                    var toNext = cells[1] - cells[0];
                    var toNode = startPin != null
                        ? -Direction4.ToOffset(startPin.Layout.Facing)
                        : -toNext;
                    if (startPin != null && toNext + toNode != Vector2Int.zero)
                    {
                        // 起点格也可能立即转弯后才离开节点 Pin，和终点的情况完全对称。
                        if (style.wireCornerPinSprite != null &&
                            TryGetCornerPinTransform(toNext, toNode, out rotation, out mirrorX))
                            sprite = style.wireCornerPinSprite;
                        else
                        {
                            sprite = style.wireCornerSprite;
                            rotation = CornerRotation(toNext, toNode);
                        }
                    }
                    else
                    {
                        sprite = style.wireConnectedEndSprite != null
                            ? style.wireConnectedEndSprite
                            : style.wireStraightSprite;
                        rotation = RotationFromRight(toNode);
                    }
                }
                else if (i == cells.Count - 1)
                {
                    var toPrevious = cells[i - 1] - cells[i];
                    var toNode = endPin != null
                        ? -Direction4.ToOffset(endPin.Layout.Facing)
                        : -toPrevious;
                    if (!isPreview && endPin != null && toPrevious + toNode != Vector2Int.zero)
                    {
                        // CornerPin 本身是一张「右侧带接口的转角」完整图；按两条臂的方向选旋转/镜像，
                        // 不与普通 Corner 叠加，避免在拐角中心出现双重管线。
                        if (style.wireCornerPinSprite != null &&
                            TryGetCornerPinTransform(toPrevious, toNode, out rotation, out mirrorX))
                            sprite = style.wireCornerPinSprite;
                        else
                        {
                            sprite = style.wireCornerSprite;
                            rotation = CornerRotation(toPrevious, toNode);
                        }
                    }
                    else
                    {
                        sprite = isPreview && style.wireOpenEndSprite != null
                            ? style.wireOpenEndSprite
                            : style.wireConnectedEndSprite != null ? style.wireConnectedEndSprite : style.wireStraightSprite;
                        rotation = RotationFromRight(toNode);
                    }
                }
                else
                {
                    var toPrevious = cells[i - 1] - cells[i];
                    var toNext = cells[i + 1] - cells[i];
                    if (toPrevious + toNext == Vector2Int.zero)
                    {
                        sprite = style.wireStraightSprite;
                        rotation = RotationFromUp(toNext);
                    }
                    else
                    {
                        sprite = style.wireCornerSprite;
                        rotation = CornerRotation(toPrevious, toNext);
                    }
                }

                image.sprite = sprite;
                image.type = Image.Type.Simple;
                image.preserveAspect = false;
                image.color = tint;
                var rect = image.rectTransform;
                rect.localRotation = Quaternion.Euler(0f, 0f, rotation);
                rect.localScale = new Vector3(mirrorX ? -1f : 1f, 1f, 1f);
                rect.sizeDelta = new Vector2(cellSize, cellSize);
                rect.anchoredPosition = CellToLocal(cells[i]);
            }
        }

        /// <summary>
        /// CornerPin 的原图有两条臂：接口在右，另一条线朝下。
        /// 四种旋转只能覆盖同一手性的转角；另一手性必须先左右镜像，再旋转。
        /// </summary>
        private static bool TryGetCornerPinTransform(Vector2Int toLine, Vector2Int toNode,
            out float rotation, out bool mirrorX)
        {
            for (int mirror = 0; mirror <= 1; mirror++)
            {
                bool mirrored = mirror != 0;
                for (int quarterTurns = 0; quarterTurns < 4; quarterTurns++)
                {
                    var pinDirection = RotateCounterClockwise(mirrored ? Vector2Int.left : Vector2Int.right,
                        quarterTurns);
                    var lineDirection = RotateCounterClockwise(Vector2Int.down, quarterTurns);
                    if (pinDirection != toNode || lineDirection != toLine) continue;

                    rotation = quarterTurns * 90f;
                    mirrorX = mirrored;
                    return true;
                }
            }

            rotation = 0f;
            mirrorX = false;
            return false;
        }

        private static Vector2Int RotateCounterClockwise(Vector2Int direction, int quarterTurns)
        {
            for (int i = 0; i < quarterTurns; i++)
                direction = new Vector2Int(-direction.y, direction.x);
            return direction;
        }

        private static float RotationFromUp(Vector2Int direction)
        {
            if (direction == Vector2Int.up) return 0f;
            if (direction == Vector2Int.right) return -90f;
            if (direction == Vector2Int.down) return 180f;
            return 90f; // left
        }

        private static float RotationFromRight(Vector2Int direction)
        {
            if (direction == Vector2Int.right) return 0f;
            if (direction == Vector2Int.down) return -90f;
            if (direction == Vector2Int.left) return 180f;
            return 90f; // up
        }

        /// <summary>Corner Sprite 的默认连接方向为下 + 右。</summary>
        private static float CornerRotation(Vector2Int a, Vector2Int b)
        {
            bool up = a == Vector2Int.up || b == Vector2Int.up;
            bool right = a == Vector2Int.right || b == Vector2Int.right;
            bool down = a == Vector2Int.down || b == Vector2Int.down;
            bool left = a == Vector2Int.left || b == Vector2Int.left;

            if (down && right) return 0f;
            if (up && right) return 90f;
            if (up && left) return 180f;
            return -90f; // down + left
        }

        private Color BodyColor(NodeData node)
        {
            var configured = node.Def.BackgroundColor;
            if (configured.a > 0f)
            {
                // 点亮是运行时状态，不占用第三张美术图；先以颜色反馈保留扩展位。
                return node.Def.NodeType == ENodeType.Condition && node.IsLit
                    ? Color.Lerp(configured, view.batteryLitColor, .45f)
                    : configured;
            }

            switch (node.Def.NodeType)
            {
                case ENodeType.Resource: return view.sourceColor;
                case ENodeType.Condition: return node.IsLit ? view.batteryLitColor : view.batteryColor;
                default: return view.transitColor;
            }
        }

        /// <summary>中转件的 Pin 按分组配色（分组是它唯一的语义），其余按方向。</summary>
        private Color PinColor(NodeData node, PinData pin)
        {
            if (node.Def.NodeType == ENodeType.Transit)
                return CircuitPalette.GroupColor(pin.Group);
            switch (pin.RuntimeDirection)
            {
                case EPinDirection.Output: return new Color(0.55f, 0.95f, 0.60f);
                case EPinDirection.Input: return new Color(0.60f, 0.78f, 1f);
                default: return new Color(0.75f, 0.75f, 0.75f);
            }
        }

        private static string Caption(NodeData node)
        {
            switch (node.Def.NodeType)
            {
                case ENodeType.Resource:
                    int supply = 0;
                    foreach (var pin in node.Pins)
                        if (pin.RuntimeDirection == EPinDirection.Output)
                            supply += Mathf.Max(0, pin.Def.MaxRate);
                    return supply.ToString();

                case ENodeType.Condition:
                    var conditions = ((ConditionNodeDef)node.Def).Conditions;
                    int required = 0;
                    foreach (var entry in conditions)
                        if (entry != null)
                            required = Mathf.Max(required, entry.RequiredAmount);
                    int deficit = Mathf.Max(0, required - node.ReceivedPower);
                    return deficit > 0 ? deficit.ToString() : null;

                default:
                    return null;
            }
        }

        public void ShowMessage(string text)
        {
            if (view.messageLabel == null) return;
            view.messageLabel.text = text;
            messageEndTime = Time.unscaledTime + MessageSeconds; // View 层豁免区（§3.3）
        }

        // ═══════════ 对象池 ═══════════

        /// <summary>只启停不销毁：描线预览逐帧重画，Instantiate/Destroy 会churn 出一堆 GC。</summary>
        private sealed class Pool<T> where T : Component
        {
            private readonly RectTransform root;
            private readonly Func<RectTransform, T> factory;
            private readonly List<T> items = new List<T>();
            private int used;

            public Pool(RectTransform root, Func<RectTransform, T> factory)
            {
                this.root = root;
                this.factory = factory;
            }

            public void Begin() => used = 0;

            public T Next()
            {
                if (used == items.Count) items.Add(factory(root));
                var item = items[used++];
                if (!item.gameObject.activeSelf) item.gameObject.SetActive(true);
                return item;
            }

            public void End()
            {
                for (int i = used; i < items.Count; i++)
                    if (items[i].gameObject.activeSelf)
                        items[i].gameObject.SetActive(false);
            }
        }

        private static Image NewImage(RectTransform parent)
        {
            var go = new GameObject("cell", typeof(RectTransform), typeof(Image));
            go.layer = 5;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(.5f, .5f);
            var image = go.GetComponent<Image>();
            image.raycastTarget = false; // 命中判定统一走坐标换算（见类注释）
            return image;
        }

        private Text NewLabel(RectTransform parent)
        {
            var go = new GameObject("label", typeof(RectTransform), typeof(Text));
            go.layer = 5;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(.5f, .5f);
            var text = go.GetComponent<Text>();
            text.font = view.uiStyle != null && view.uiStyle.uiFont != null
                ? view.uiStyle.uiFont
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }
    }

    /// <summary>棋盘与件库共用的分组配色（同一件上的不同分组要一眼分得开）。</summary>
    public static class CircuitPalette
    {
        private static readonly Color[] GroupColors =
        {
            new Color(0.95f, 0.75f, 0.25f),
            new Color(0.35f, 0.80f, 0.95f),
            new Color(0.95f, 0.45f, 0.75f),
            new Color(0.55f, 0.90f, 0.45f),
        };

        public static Color GroupColor(int group) =>
            group < 0 ? new Color(0.9f, 0.3f, 0.3f) : GroupColors[group % GroupColors.Length];
    }
}

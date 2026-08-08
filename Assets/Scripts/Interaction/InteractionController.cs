using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MasterHouse
{
    /// <summary>
    /// 玩家世界内交互 Controller（§2/§9）：选中、Pin-Pin 拉线、移动节点、删除节点/链接。
    /// 只把输入翻译成对 Manager 的调用，不直接修改任何数据类（§2）。
    /// 资格校验（CanMove/CanDelete/可建列表）在本层执行——自由模式只绕过这里（权限模型）。
    /// 理线（手动拖排走线）暂不做（需求记录·决策 5），后补为纯增量。
    /// </summary>
    public class InteractionController : MonoBehaviour
    {
        public static InteractionController Instance { get; private set; }

        /// <summary>区分单击与拖拽的位移阈值（屏幕像素）。左右键共用。</summary>
        private const float DragThresholdPixels = 6f;

        private const float MessageSeconds = 3f;

        private Camera cam;
        private LevelManager levelManager;
        private LinkManager linkManager;

        // ── 选中态（调试面板读取详情用）──
        public NodeData SelectedNode { get; private set; }
        public LinkData SelectedLink { get; private set; }

        // ── 左键手势状态 ──
        private bool lmbHeld;
        private Vector3 lmbDownScreen;
        private bool lmbDragging;          // 已越过拖拽阈值
        private PinData dragFromPin;       // 拉线起点（按下 Pin 即生效）
        private NodeData dragNode;         // 拖拽移动中的节点
        private Vector2Int dragGrabOffset; // 抓取点相对节点原点的格偏移

        // ── 右键手势状态（短击删线；拖动让给相机平移）──
        private bool rmbHeld;
        private Vector3 rmbDownScreen;

        // ── 临时消息（连线失败原因等，需求 §三）──
        private string message;
        private float messageEndTime;
        private static GUIStyle messageStyle;

        // ── 交互层自有表现物（不碰 View 的渲染器）──
        private readonly List<SpriteRenderer> overlays = new List<SpriteRenderer>();
        private Transform overlayRoot;
        private LineRenderer dragLine;

        private void Awake()
        {
            Instance = this;
            cam = Camera.main;
        }

        private void Start()
        {
            var gm = GameManager.Instance;
            if (gm == null)
            {
                Debug.LogError("场景缺少 GameManager，InteractionController 停用");
                enabled = false;
                return;
            }
            levelManager = gm.LevelManager;
            linkManager = gm.LinkManager;
            // 结构变化时清理失效引用（选中/拖拽对象可能被删除或随关卡卸载）
            levelManager.OnLevelUnloaded += HandleLevelUnloaded;
            levelManager.OnNodeRemoved += HandleNodeRemoved;
            linkManager.OnLinkDeleted += HandleLinkDeleted;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (levelManager != null)
            {
                levelManager.OnLevelUnloaded -= HandleLevelUnloaded;
                levelManager.OnNodeRemoved -= HandleNodeRemoved;
            }
            if (linkManager != null)
                linkManager.OnLinkDeleted -= HandleLinkDeleted;
        }

        /// <summary>单关独占（需求记录·决策 2）：当前关取已加载列表首个。</summary>
        private LevelData CurrentLevel =>
            levelManager != null && levelManager.LoadedLevels.Count > 0
                ? levelManager.LoadedLevels[0]
                : null;

        private void Update()
        {
            if (cam == null)
            {
                cam = Camera.main;
                if (cam == null) return;
            }

            var level = CurrentLevel;
            bool placing = PlacementController.Instance != null && PlacementController.Instance.IsPlacing;

            if (level != null && !placing)
            {
                HandleLeftButton(level);
                HandleRightButton(level);
                HandleDeleteKey(level);
                if (Input.GetKeyDown(KeyCode.Escape))
                    Deselect();
            }

            UpdateDragLine();
            UpdateOverlays();
        }

        // ───────────────── 左键：拉线 / 拖动节点 / 点选 ─────────────────

        private void HandleLeftButton(LevelData level)
        {
            if (Input.GetMouseButtonDown(0) && !IsPointerOverUI())
            {
                lmbHeld = true;
                lmbDragging = false;
                lmbDownScreen = Input.mousePosition;

                var world = GridPicker.ScreenToWorld(cam, Input.mousePosition);
                // 先 Pin 后占用索引：Pin 命中优先当作拉线起手（避免误选端点格上的旧线）
                dragFromPin = GridPicker.PickPin(level, world);
                if (dragFromPin != null && dragFromPin.Owner.IsIllegal)
                {
                    ShowMessage("节点处于位置冲突中，先解决冲突再连线");
                    dragFromPin = null;
                    lmbHeld = false;
                    return;
                }
                if (dragFromPin == null)
                {
                    var cell = GridPicker.WorldToCell(world);
                    var node = GridPicker.PickNode(level, cell);
                    if (node != null)
                    {
                        dragNode = node;
                        dragGrabOffset = cell - node.Origin;
                    }
                }
            }

            if (!lmbHeld) return;

            if (!lmbDragging &&
                ((Vector2)(Input.mousePosition - lmbDownScreen)).magnitude > DragThresholdPixels)
            {
                lmbDragging = true;
                // 移动资格校验在 Controller 层：自由模式无视预置约束（权限模型）
                if (dragNode != null && !dragNode.CanMove && !DebugOptions.FreeMode)
                {
                    ShowMessage("该节点不可移动（预置约束）");
                    dragNode = null;
                }
            }

            // 拖拽移动：目标格变化即调 MoveNode（实时呈现断线态与非法临时态）
            if (lmbDragging && dragNode != null)
            {
                var targetOrigin = GridPicker.ScreenToCell(cam, Input.mousePosition) - dragGrabOffset;
                if (targetOrigin != dragNode.Origin)
                    levelManager.MoveNode(level, dragNode, targetOrigin);
            }

            if (Input.GetMouseButtonUp(0))
            {
                if (dragFromPin != null)
                    FinishLinkDrag(level);
                else if (!lmbDragging)
                    ClickSelect(level);
                lmbHeld = false;
                lmbDragging = false;
                dragFromPin = null;
                dragNode = null;
            }
        }

        private void FinishLinkDrag(LevelData level)
        {
            var toPin = GridPicker.PickPin(level, GridPicker.ScreenToWorld(cam, Input.mousePosition));
            if (toPin == null || toPin == dragFromPin) return; // 空放/原地松开：静默取消

            if (toPin.Owner.IsIllegal)
            {
                ShowMessage("目标节点处于位置冲突中，先解决冲突再连线");
                return;
            }

            var link = linkManager.TryCreateLink(level, dragFromPin, toPin, out var failReason);
            if (link == null)
                ShowMessage($"连线失败：{failReason}"); // 需求 §三：失败原因必须在界面可见
        }

        private void ClickSelect(LevelData level)
        {
            var cell = GridPicker.ScreenToCell(cam, Input.mousePosition);
            SelectedNode = GridPicker.PickNode(level, cell);
            SelectedLink = SelectedNode == null ? GridPicker.PickLink(level, cell) : null;
        }

        private void Deselect()
        {
            SelectedNode = null;
            SelectedLink = null;
        }

        // ───────────────── 右键：短击快删链接 ─────────────────

        private void HandleRightButton(LevelData level)
        {
            if (Input.GetMouseButtonDown(1) && !IsPointerOverUI())
            {
                rmbHeld = true;
                rmbDownScreen = Input.mousePosition;
            }

            if (!rmbHeld || !Input.GetMouseButtonUp(1)) return;
            rmbHeld = false;

            // 越过阈值 = 相机平移（CameraController），不删
            if (((Vector2)(Input.mousePosition - rmbDownScreen)).magnitude > DragThresholdPixels)
                return;

            var link = GridPicker.PickLink(level, GridPicker.ScreenToCell(cam, Input.mousePosition));
            if (link != null)
                linkManager.DeleteLink(level, link);
        }

        // ───────────────── Delete 键：删除选中对象 ─────────────────

        private void HandleDeleteKey(LevelData level)
        {
            if (!Input.GetKeyDown(KeyCode.Delete)) return;

            if (SelectedLink != null)
            {
                linkManager.DeleteLink(level, SelectedLink); // 选中清理走 OnLinkDeleted 回调
            }
            else if (SelectedNode != null)
            {
                // 删除资格校验在 Controller 层：自由模式无视预置约束（权限模型）
                if (!SelectedNode.CanDelete && !DebugOptions.FreeMode)
                {
                    ShowMessage("该节点不可删除（预置约束）");
                    return;
                }
                levelManager.RemoveNode(level, SelectedNode);
            }
        }

        // ───────────────── 结构变化回调：清理失效引用 ─────────────────

        private void HandleLevelUnloaded(LevelData level)
        {
            Deselect();
            dragFromPin = null;
            dragNode = null;
            lmbHeld = false;
            lmbDragging = false;
        }

        private void HandleNodeRemoved(LevelData level, NodeData node)
        {
            if (SelectedNode == node) SelectedNode = null;
            if (dragNode == node) dragNode = null;
        }

        private void HandleLinkDeleted(LevelData level, LinkData link)
        {
            if (SelectedLink == link) SelectedLink = null;
        }

        // ───────────────── 交互层表现：拉线预览 / 选中高亮 ─────────────────

        private void UpdateDragLine()
        {
            bool active = dragFromPin != null;
            if (dragLine == null)
            {
                if (!active) return;
                var go = new GameObject("拉线预览");
                go.transform.SetParent(transform, false);
                dragLine = go.AddComponent<LineRenderer>();
                dragLine.sharedMaterial = VisualAssets.UnlitMaterial;
                dragLine.widthMultiplier = 0.1f * ViewUtil.GridSize;
                dragLine.positionCount = 2;
                dragLine.useWorldSpace = true;
                dragLine.sortingOrder = SortingOrders.DragLine;
            }

            dragLine.enabled = active;
            if (!active) return;

            dragLine.SetPosition(0, GridPicker.PinMarkWorldPos(dragFromPin.Owner, dragFromPin.IndexInNode));
            dragLine.SetPosition(1, GridPicker.ScreenToWorld(cam, Input.mousePosition));
            var color = dragFromPin.RuntimeItemType != null
                ? dragFromPin.RuntimeItemType.DisplayColor
                : Color.white;
            color.a = 0.8f;
            dragLine.startColor = color;
            dragLine.endColor = color;
        }

        private void UpdateOverlays()
        {
            // 高亮当前选中：节点占格 / 链接途径格，覆盖半透明白块（每帧跟随数据，天然覆盖移动）
            int needed = SelectedNode != null ? SelectedNode.Def.Shape.Grids.Count
                : SelectedLink != null ? SelectedLink.PathCells.Count
                : 0;
            EnsureOverlays(needed);
            if (needed == 0) return;

            if (SelectedNode != null)
            {
                int i = 0;
                foreach (var cell in SelectedNode.Def.Shape.CellsAt(SelectedNode.Origin))
                    overlays[i++].transform.position = ViewUtil.CellCenter(cell);
            }
            else
            {
                for (int i = 0; i < SelectedLink.PathCells.Count; i++)
                    overlays[i].transform.position = ViewUtil.CellCenter(SelectedLink.PathCells[i]);
            }
        }

        private void EnsureOverlays(int count)
        {
            if (overlayRoot == null)
            {
                overlayRoot = new GameObject("选中高亮").transform;
                overlayRoot.SetParent(transform, false);
            }
            while (overlays.Count < count)
                overlays.Add(VisualAssets.CreateSpriteSquare(overlayRoot, $"高亮{overlays.Count}",
                    Vector3.zero, ViewUtil.GridSize, new Color(1f, 1f, 1f, 0.25f),
                    SortingOrders.Text - 1));
            for (int i = 0; i < overlays.Count; i++)
                overlays[i].enabled = i < count;
        }

        // ───────────────── 临时消息 ─────────────────

        public void ShowMessage(string text)
        {
            message = text;
            messageEndTime = Time.unscaledTime + MessageSeconds;
        }

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(message) || Time.unscaledTime > messageEndTime) return;
            if (messageStyle == null)
                messageStyle = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 14,
                    wordWrap = true,
                };
            GUI.Box(new Rect(Screen.width * 0.5f - 260f, 12f, 520f, 32f), message, messageStyle);
        }

        public static bool IsPointerOverUI()
        {
            // IMGUI（调试面板）不走 EventSystem，需单独判定，防止面板点击穿透到世界交互
            if (DebugPanel.IsPointerOverPanel()) return true;
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }
    }
}

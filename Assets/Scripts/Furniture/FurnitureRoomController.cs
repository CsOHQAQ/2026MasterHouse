using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using F = MasterHouse.HouseUIRuntime; // §16.7 毒点②已断：不再依赖退役中的 OutGameUIFactory

namespace MasterHouse
{
    /// <summary>
    /// 家具摆放模式：配置表驱动的房间就地编辑。
    /// 摆放（网格吸附 + 绿/红占位预览）、收纳（拖回收纳栏/双击，桌面家具级联）、解锁（HOUSE CREDIT），
    /// 地面家具按底边深度行排层级；背景为透视相机下的 3D 感平面，带远景模糊与拖拽失焦景深。
    /// </summary>
    public sealed class FurnitureRoomController : MonoBehaviour
    {
        private const float PixelsPerUnit = 100f;
        private const float CameraFov = 25f;
        private const float SnapMarginPx = 50f;
        private const float DoubleClickSeconds = .35f;

        // 渲染次序（同一 sortingLayer 内）
        private const int OrderBackground = 0;
        private const int OrderDepthBlur = 2;
        private const int OrderFocusBlur = 4;
        private const int OrderWallGrid = 10;
        private const int OrderWallItemBase = 20;
        private const int OrderFloorGrid = 60;
        private const int OrderFloorItemBase = 100;
        private const int OrderGhost = 400;

        // 分层 Z 偏移（只用于相机视差，绘制次序由 sortingOrder 决定）
        private const float ZBackground = .15f;
        private const float ZDepthBlur = .14f;
        private const float ZFocusBlur = .13f;
        private const float ZWall = .07f;
        private const float ZFloorPerRow = -.022f;
        private const float ZGhost = -.6f;

        /// <summary>过渡桥接：家具模式读写新 Economy 模块（§16.3）；GameManager 由 OutGameBootstrap 保证存在。</summary>
        private static EconomyManager Economy => GameManager.Instance.EconomyManager;

        /// <summary>会话内保留的摆放布局（进出模式不丢，重开进程重置）。货币/声望/所有权由 Economy 模块统一管理。</summary>
        private sealed class SessionState
        {
            public readonly List<FurniturePlacementConfig> Placements = new List<FurniturePlacementConfig>();
        }

        private sealed class DragState
        {
            public FurnitureEntry Entry;
            public FurnitureRuntimeItem Item;      // null = 从收纳栏拖出
            public bool Flipped;                   // F 键左右镜像
            public FurnitureRuntimeGrid CandidateGrid;
            public int CandidateCol;
            public int CandidateRow;
            public bool CandidateOk;
            public bool OverInventory;
            public GameObject Ghost;
            public SpriteRenderer GhostRenderer;
        }

        private static FurnitureRoomController active;

        /// <summary>会话布局按房间 id 分桶（家具模式随 Hub 当前房间动态加载）；未编辑过的房间沿用其默认摆放。</summary>
        private static readonly Dictionary<string, SessionState> sessions = new Dictionary<string, SessionState>();

        private FurnitureTable furnitureTable;
        private FurnitureRoomEntry room;
        private Action onClosed;
        private bool closing;

        private Transform stageRoot;
        private Vector3 stageOrigin;
        private FurnitureCameraRig rig;
        private FurnitureRoomHud hud;
        private SpriteRenderer focusBlurRenderer;

        private readonly Dictionary<string, FurnitureRuntimeGrid> grids = new Dictionary<string, FurnitureRuntimeGrid>();
        private readonly Dictionary<string, FurnitureRuntimeItem> items = new Dictionary<string, FurnitureRuntimeItem>();
        private int nextItemId = 1;
        private bool gridToggleOn;
        private DragState drag;
        private string lastClickItemId;
        private float lastClickTime;

        /// <summary>打开家具模式并加载指定房间（随 Hub 当前房间动态加载；下标越界回落 0）。配置表缺失时返回 false。</summary>
        public static bool Open(int roomIndex, Action onClosed)
        {
            if (active != null) return true;
            // 家具表并入 Def 体系（§16.7）：统一由 GameManager 加载
            var furniture = GameManager.Instance.FurnitureTable;
            var rooms = GameManager.Instance.FurnitureRoomTable;
            if (furniture == null || rooms == null || rooms.rooms.Count == 0)
            {
                Debug.LogWarning("[Furniture] 配置表缺失，请先执行菜单 MasterHouse → 家具系统 → 创建配置表。");
                return false;
            }
            var roomEntry = roomIndex >= 0 && roomIndex < rooms.rooms.Count ? rooms.rooms[roomIndex] : rooms.rooms[0];
            if (roomEntry == null)
            {
                Debug.LogWarning($"[Furniture] 房间配置为空（下标 {roomIndex}），无法进入家具模式。");
                return false;
            }
            var go = new GameObject("FurnitureRoomMode");
            active = go.AddComponent<FurnitureRoomController>();
            active.Init(furniture, roomEntry, onClosed);
            return true;
        }

        private void Init(FurnitureTable furniture, FurnitureRoomEntry roomEntry, Action closedCallback)
        {
            furnitureTable = furniture;
            room = roomEntry;
            onClosed = closedCallback;
            // 把舞台放到远离节点玩法棋盘的位置，避免主相机内容互相穿帮。
            stageOrigin = new Vector3(500f, 0f, 0f);

            stageRoot = new GameObject("FurnitureStage").transform;
            stageRoot.position = stageOrigin;

            BuildBackground();
            BuildCamera();
            BuildGrids();

            hud = new FurnitureRoomHud();
            hud.Build(furnitureTable, GetSlotState, Economy.PriceOf, Economy.UnlockReputationOf);
            hud.ExitClicked += Close;
            hud.GridToggleClicked += ToggleGrids;
            hud.SlotPressed += OnSlotPressed;
            hud.PurchaseConfirmed += OnPurchaseConfirmed;
            Economy.Changed += OnEconomyChanged;

            RestoreState();
            RecomputeDecorationScore();
            OnEconomyChanged();
            hud.RefreshInventory();
            hud.SetGridToggle(false);
            RefreshAllGridColors();

            // 进入时闪现全部网格，提示可编辑区域。
            foreach (var grid in grids.Values) grid.SetVisible(true);
            DOVirtual.DelayedCall(1.6f, ApplyGridVisibility).SetTarget(this);
        }

        #region 舞台搭建

        private void BuildBackground()
        {
            CreateLayer("Background", room.background, OrderBackground, ZBackground, 1f);
            if (room.depthBlurOverlay != null)
                CreateLayer("DepthBlur", room.depthBlurOverlay, OrderDepthBlur, ZDepthBlur, 1f);
            if (room.focusBlurOverlay != null)
                focusBlurRenderer = CreateLayer("FocusBlur", room.focusBlurOverlay, OrderFocusBlur, ZFocusBlur, 0f);
        }

        private SpriteRenderer CreateLayer(string name, Sprite sprite, int order, float z, float alpha)
        {
            var go = new GameObject(name);
            go.transform.SetParent(stageRoot, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = order;
            renderer.color = new Color(1f, 1f, 1f, alpha);
            if (sprite == null)
            {
                Debug.LogWarning($"[Furniture] 房间背景层 {name} 精灵缺失。");
                return renderer;
            }
            renderer.sprite = sprite;
            go.transform.position = PxToWorld(room.sceneWidth * .5f, room.sceneHeight * .5f, z);
            go.transform.localScale = SpriteScale(sprite, room.sceneWidth, room.sceneHeight);
            return renderer;
        }

        private void BuildCamera()
        {
            var pivot = PxToWorld(room.sceneWidth * .5f, room.sceneHeight * .5f, 0f);
            var halfExtents = new Vector2(room.sceneWidth, room.sceneHeight) / PixelsPerUnit * .5f;
            rig = new GameObject("FurnitureCameraRig").AddComponent<FurnitureCameraRig>();
            rig.Init(pivot, halfExtents, CameraFov); // 初始完整显示背景；滚轮缩放 + 右键/中键平移在吊架内
        }

        private void BuildGrids()
        {
            foreach (var config in room.grids)
            {
                if (config == null || string.IsNullOrEmpty(config.id)) continue;
                var order = config.surface == FurnitureSurfaceType.Floor ? OrderFloorGrid : OrderWallGrid;
                var z = config.surface == FurnitureSurfaceType.Floor ? 0f : ZWall - .01f;
                var grid = new FurnitureRuntimeGrid(config, PxToWorld, z);
                grid.BuildVisual(stageRoot, F.WhiteSprite, order);
                grids[grid.Id] = grid;
            }
            foreach (var blocked in room.blockedCells)
                if (blocked != null && grids.TryGetValue(blocked.gridId, out var grid))
                    grid.MarkSceneBlocked(blocked.col, blocked.row);
        }

        #endregion

        #region 坐标换算

        /// <summary>
        /// 把精灵缩放到指定的场景像素尺寸。必须用 bounds（世界单位）而非 rect（导入后像素）：
        /// 原图超过导入 Max Size 被降采样时，rect 变小但 bounds 不变，用 rect 会把大图放大（如 5120px 房间背景放大 2.5 倍）。
        /// </summary>
        private static Vector3 SpriteScale(Sprite sprite, float scenePxWidth, float scenePxHeight) =>
            new Vector3(
                scenePxWidth / PixelsPerUnit / Mathf.Max(1e-4f, sprite.bounds.size.x),
                scenePxHeight / PixelsPerUnit / Mathf.Max(1e-4f, sprite.bounds.size.y), 1f);

        /// <summary>场景像素（左上原点、Y 向下）→ 世界坐标。</summary>
        private Vector3 PxToWorld(float px, float py, float z)
        {
            return stageOrigin + new Vector3(px / PixelsPerUnit, (room.sceneHeight - py) / PixelsPerUnit, z);
        }

        /// <summary>鼠标 → 舞台平面（z=stageOrigin.z）上的场景像素坐标。</summary>
        private Vector2 MouseScenePx()
        {
            var ray = rig.Camera.ScreenPointToRay(Input.mousePosition);
            if (Mathf.Abs(ray.direction.z) < 1e-5f) return Vector2.zero;
            var point = ray.GetPoint((stageOrigin.z - ray.origin.z) / ray.direction.z);
            return new Vector2((point.x - stageOrigin.x) * PixelsPerUnit,
                room.sceneHeight - (point.y - stageOrigin.y) * PixelsPerUnit);
        }

        #endregion

        #region 家具实例

        private static string TableGridId(string itemId) => "tbl_" + itemId;

        private List<FurnitureRuntimeItem> ChildrenOf(FurnitureRuntimeItem host)
        {
            var result = new List<FurnitureRuntimeItem>();
            var gridId = TableGridId(host.Id);
            foreach (var item in items.Values)
                if (item.GridId == gridId) result.Add(item);
            return result;
        }

        private void AnchorOf(FurnitureRuntimeItem item, out float leftPx, out float bottomPy, out int order, out float z)
        {
            var grid = grids[item.GridId];
            var entry = item.Entry;
            leftPx = grid.X + item.Col * grid.CellWidth + (entry.cols * grid.CellWidth - entry.displayWidth) * .5f;
            if (grid.Surface == FurnitureSurfaceType.Floor)
            {
                var bottomRow = item.Row + entry.rows;
                bottomPy = grid.Y + bottomRow * grid.CellHeight;
                order = OrderFloorItemBase + bottomRow * 10;
                z = ZFloorPerRow * bottomRow;
                return;
            }
            if (grid.Surface == FurnitureSurfaceType.Wall)
            {
                bottomPy = grid.Y + (item.Row + entry.rows) * grid.CellHeight;
                order = OrderWallItemBase + item.Row + entry.rows;
                z = ZWall;
                return;
            }
            // 桌面家具：跟随宿主层级
            var host = items[grids[item.GridId].HostItemId];
            AnchorOf(host, out _, out _, out var hostOrder, out var hostZ);
            bottomPy = grid.Y + grid.CellHeight;
            order = hostOrder + 3;
            z = hostZ - .02f;
        }

        private void LayoutItem(FurnitureRuntimeItem item)
        {
            AnchorOf(item, out var left, out var bottom, out var order, out var z);
            item.Root.transform.position = PxToWorld(
                left + item.Entry.displayWidth * .5f, bottom - item.Entry.displayHeight * .5f, z);
            item.Renderer.sortingOrder = order;
        }

        private void SyncTableGrid(FurnitureRuntimeItem host)
        {
            var config = host.Entry.tableSurface;
            if (config == null || !config.enabled) return;
            if (!grids.TryGetValue(TableGridId(host.Id), out var grid)) return;
            AnchorOf(host, out var left, out var bottom, out var order, out _);
            grid.SetOrigin(left + config.offsetX, bottom - config.surfaceHeight - config.cellHeight);
            grid.SetSortingOrder(order + 2);
            foreach (var child in ChildrenOf(host)) LayoutItem(child);
        }

        private FurnitureRuntimeItem PlaceItem(FurnitureEntry entry, string gridId, int col, int row, bool silent,
            bool flipped = false)
        {
            var grid = grids[gridId];
            var item = new FurnitureRuntimeItem
            {
                Id = "it" + nextItemId++,
                Entry = entry,
                GridId = gridId,
                Col = col,
                Row = row,
                Flipped = flipped,
            };
            item.Root = new GameObject("Furniture_" + entry.id);
            item.Root.transform.SetParent(stageRoot, false);
            item.Renderer = item.Root.AddComponent<SpriteRenderer>();
            item.Renderer.sprite = entry.sprite;
            item.Renderer.flipX = flipped;
            if (entry.sprite != null)
                item.Root.transform.localScale = SpriteScale(entry.sprite, entry.displayWidth, entry.displayHeight);
            items[item.Id] = item;
            grid.SetOccupied(col, row, entry.cols, entry.rows, item.Id, true);

            if (entry.tableSurface != null && entry.tableSurface.enabled)
            {
                var config = new FurnitureGridConfig
                {
                    id = TableGridId(item.Id),
                    surface = FurnitureSurfaceType.Table,
                    cols = entry.tableSurface.cols,
                    rows = 1,
                    cellWidth = entry.tableSurface.cellWidth,
                    cellHeight = entry.tableSurface.cellHeight,
                };
                var tableGrid = new FurnitureRuntimeGrid(config, PxToWorld, ZFloorPerRow * (row + entry.rows) - .01f);
                tableGrid.HostItemId = item.Id;
                tableGrid.BuildVisual(stageRoot, F.WhiteSprite, OrderFloorItemBase);
                grids[tableGrid.Id] = tableGrid;
            }

            LayoutItem(item);
            SyncTableGrid(item);
            if (!silent && item.Root != null)
                item.Root.transform.DOPunchScale(Vector3.one * .045f, .28f, 6, .7f).SetTarget(this);
            RefreshAllGridColors();
            ApplyGridVisibility();
            RecomputeDecorationScore();
            return item;
        }

        private void StoreItem(FurnitureRuntimeItem item, bool silent)
        {
            var names = new List<string> { item.Entry.displayName };
            if (item.Entry.tableSurface != null && item.Entry.tableSurface.enabled)
            {
                foreach (var child in ChildrenOf(item))
                {
                    names.Add(child.Entry.displayName);
                    StoreItem(child, true);
                }
                if (grids.TryGetValue(TableGridId(item.Id), out var tableGrid))
                {
                    tableGrid.Destroy();
                    grids.Remove(tableGrid.Id);
                }
            }
            if (grids.TryGetValue(item.GridId, out var grid))
                grid.SetOccupied(item.Col, item.Row, item.Entry.cols, item.Entry.rows, item.Id, false);
            Destroy(item.Root);
            items.Remove(item.Id);
            RefreshAllGridColors();
            ApplyGridVisibility();
            RecomputeDecorationScore();
            if (!silent)
            {
                hud.RefreshInventory();
                hud.ShowToast("已收纳：" + string.Join("、", names));
            }
        }

        private void MoveItem(FurnitureRuntimeItem item, string gridId, int col, int row)
        {
            if (grids.TryGetValue(item.GridId, out var from))
                from.SetOccupied(item.Col, item.Row, item.Entry.cols, item.Entry.rows, item.Id, false);
            item.GridId = gridId;
            item.Col = col;
            item.Row = row;
            grids[gridId].SetOccupied(col, row, item.Entry.cols, item.Entry.rows, item.Id, true);
            LayoutItem(item);
            SyncTableGrid(item);
            item.Root.transform.DOPunchScale(Vector3.one * .045f, .28f, 6, .7f).SetTarget(this);
            RefreshAllGridColors();
        }

        private bool IsPlaced(string furnitureId)
        {
            foreach (var item in items.Values)
                if (item.Entry.id == furnitureId) return true;
            return false;
        }

        #endregion

        #region 输入与拖拽

        private void Update()
        {
            if (closing) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (hud.PopupOpen) hud.CloseUnlockPopup();
                else if (drag != null) EndDrag(false);
                else Close();
                return;
            }

            if (drag != null)
            {
                if (Input.GetKeyDown(KeyCode.F)) // 拖拽中 F 翻转幽灵
                {
                    drag.Flipped = !drag.Flipped;
                    if (drag.GhostRenderer != null) drag.GhostRenderer.flipX = drag.Flipped;
                }
                UpdateDrag();
                if (Input.GetMouseButtonUp(0)) EndDrag(true);
                return;
            }

            if (Input.GetKeyDown(KeyCode.F) && !hud.PopupOpen)
            {
                // 非拖拽态：F 翻转鼠标下的已摆放家具，立即落会话
                var hover = HitItem(MouseScenePx());
                if (hover != null)
                {
                    hover.Flipped = !hover.Flipped;
                    if (hover.Renderer != null) hover.Renderer.flipX = hover.Flipped;
                    SaveState();
                }
            }

            if (Input.GetMouseButtonDown(0) && !hud.PopupOpen)
            {
                // 指针落在 HUD 控件（收纳栏、按钮）上时交给 uGUI，槽位经 SlotPressed 起手。
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
                TryPickItem();
            }
        }

        private FurnitureRuntimeItem HitItem(Vector2 scenePx)
        {
            FurnitureRuntimeItem hit = null;
            var hitOrder = int.MinValue;
            foreach (var item in items.Values)
            {
                AnchorOf(item, out var left, out var bottom, out var order, out _);
                if (scenePx.x >= left && scenePx.x <= left + item.Entry.displayWidth &&
                    scenePx.y >= bottom - item.Entry.displayHeight && scenePx.y <= bottom &&
                    order > hitOrder)
                {
                    hit = item;
                    hitOrder = order;
                }
            }
            return hit;
        }

        private void TryPickItem()
        {
            var hit = HitItem(MouseScenePx());
            if (hit == null) return;
            if (hit.Id == lastClickItemId && Time.unscaledTime - lastClickTime < DoubleClickSeconds)
            {
                lastClickItemId = null;
                StoreItem(hit, false);
                return;
            }
            lastClickItemId = hit.Id;
            lastClickTime = Time.unscaledTime;
            BeginDrag(hit.Entry, hit);
        }

        private void OnSlotPressed(string furnitureId)
        {
            if (drag != null || closing) return;
            var entry = furnitureTable.Find(furnitureId);
            if (entry == null) return;
            if (hud.PopupOpen) return;
            if (!Economy.IsFurnitureOwned(furnitureId))
            {
                if (!Economy.IsFurnitureRevealed(entry))
                {
                    hud.ShowToast($"「？」声望达到 {Economy.UnlockReputationOf(entry)} 后解禁（当前 {Economy.Data.Reputation}）");
                    return;
                }
                hud.ShowPurchasePopup(entry, Economy.Data.Currency);
                return;
            }
            if (IsPlaced(furnitureId))
            {
                hud.ShowToast($"「{entry.displayName}」已在房间中，可直接拖动它");
                return;
            }
            BeginDrag(entry, null);
        }

        private void BeginDrag(FurnitureEntry entry, FurnitureRuntimeItem item)
        {
            SfxManager.Play(ESfx.FurniturePickup); // 音效需求 #2：拾起（收纳栏拖出与场上拿起两条路径都汇到这里）
            drag = new DragState { Entry = entry, Item = item, Flipped = item != null && item.Flipped };
            hud.SetDragDimming(true); // 布置中顶部 UI 淡出让位（收纳栏保留作拖回落点）
            drag.Ghost = new GameObject("DragGhost");
            drag.Ghost.transform.SetParent(stageRoot, false);
            drag.GhostRenderer = drag.Ghost.AddComponent<SpriteRenderer>();
            drag.GhostRenderer.sprite = entry.sprite;
            drag.GhostRenderer.flipX = drag.Flipped;
            drag.GhostRenderer.sortingOrder = OrderGhost;
            drag.GhostRenderer.color = new Color(1f, 1f, 1f, .85f);
            if (entry.sprite != null)
                drag.Ghost.transform.localScale = SpriteScale(entry.sprite, entry.displayWidth, entry.displayHeight);

            if (item != null)
            {
                // 拖动带桌面的家具时，桌上家具跟着幽灵走
                if (entry.tableSurface != null && entry.tableSurface.enabled)
                {
                    AnchorOf(item, out var hostLeft, out var hostBottom, out _, out _);
                    foreach (var child in ChildrenOf(item))
                    {
                        AnchorOf(child, out var childLeft, out var childBottom, out _, out _);
                        var childGhost = new GameObject("GhostChild_" + child.Entry.id);
                        childGhost.transform.SetParent(drag.Ghost.transform, false);
                        var renderer = childGhost.AddComponent<SpriteRenderer>();
                        renderer.sprite = child.Entry.sprite;
                        renderer.sortingOrder = OrderGhost + 1;
                        renderer.color = new Color(1f, 1f, 1f, .85f);
                        // 子物体位置相对宿主锚点（像素差换算为本地偏移；父物体已缩放，先除回）
                        var hostScale = drag.Ghost.transform.localScale;
                        var offsetX = (childLeft + child.Entry.displayWidth * .5f) - (hostLeft + entry.displayWidth * .5f);
                        var offsetY = (hostBottom - entry.displayHeight * .5f) - (childBottom - child.Entry.displayHeight * .5f);
                        childGhost.transform.localPosition = new Vector3(
                            offsetX / PixelsPerUnit / hostScale.x, offsetY / PixelsPerUnit / hostScale.y, 0f);
                        if (child.Entry.sprite != null)
                        {
                            var childScale = SpriteScale(child.Entry.sprite, child.Entry.displayWidth, child.Entry.displayHeight);
                            childGhost.transform.localScale = new Vector3(
                                childScale.x / hostScale.x, childScale.y / hostScale.y, 1f);
                        }
                        child.Root.SetActive(false);
                    }
                    if (grids.TryGetValue(TableGridId(item.Id), out var tableGrid)) tableGrid.SetVisible(false);
                }
                grids[item.GridId].SetOccupied(item.Col, item.Row, entry.cols, entry.rows, item.Id, false);
                item.Root.SetActive(false);
            }

            ShowGridsForEntry(entry);
            RefreshAllGridColors();
            if (focusBlurRenderer != null)
            {
                focusBlurRenderer.DOKill();
                focusBlurRenderer.DOFade(.85f, .3f);
            }
            UpdateDrag();
        }

        private void UpdateDrag()
        {
            var entry = drag.Entry;
            var px = MouseScenePx();
            var wantLeft = px.x - entry.displayWidth * .5f;
            var wantBottom = px.y + entry.displayHeight * .25f;

            // 拖回收纳优先：指针悬在收纳栏面板上即判定收回（面板亮粉提示）。
            // 收纳栏正后方的地板行想摆放：先「隐藏界面」或滚轮放大，让收纳栏离开指针下方。
            drag.OverInventory = drag.Item != null && hud.IsPointerOverInventory(Input.mousePosition);
            hud.SetInventoryDropHint(drag.OverInventory);

            RefreshAllGridColors();
            drag.CandidateGrid = null;
            if (drag.OverInventory)
            {
                drag.Ghost.transform.position = PxToWorld(
                    wantLeft + entry.displayWidth * .5f, wantBottom - entry.displayHeight * .5f, ZGhost);
                drag.GhostRenderer.color = new Color(1f, 1f, 1f, .85f);
                return;
            }
            FurnitureRuntimeGrid best = null;
            var bestDist = float.MaxValue;
            var bestIsTable = false;
            foreach (var grid in grids.Values)
            {
                if (!entry.Supports(grid.Surface)) continue; // 表面类型可多选（如纸箱：地面/桌面）
                if (drag.Item != null && grid.HostItemId == drag.Item.Id) continue;
                if (!grid.Contains(px, SnapMarginPx)) continue;
                var dist = grid.DistanceSq(px);
                // 桌面格与地面格空间重叠：指针悬在桌面格内时桌面格优先，
                // 否则多选表面的小件永远被大范围地面格抢走、落到桌子身后的地板行（层级被桌子压住）
                var isTable = grid.Surface == FurnitureSurfaceType.Table;
                var better = best == null || (isTable && !bestIsTable) ||
                             (isTable == bestIsTable && dist < bestDist);
                if (better)
                {
                    best = grid;
                    bestDist = dist;
                    bestIsTable = isTable;
                }
            }
            if (best != null)
            {
                var footWidth = entry.cols * best.CellWidth;
                var footHeight = entry.rows * best.CellHeight;
                var col = Mathf.Clamp(Mathf.RoundToInt(
                    (wantLeft + (entry.displayWidth - footWidth) * .5f - best.X) / best.CellWidth), 0, best.Cols - entry.cols);
                var row = Mathf.Clamp(Mathf.RoundToInt(
                    (wantBottom - footHeight - best.Y) / best.CellHeight), 0, best.Rows - entry.rows);
                drag.CandidateGrid = best;
                drag.CandidateCol = col;
                drag.CandidateRow = row;
                drag.CandidateOk = best.FootprintFree(col, row, entry.cols, entry.rows, drag.Item?.Id);
                best.PaintPreview(col, row, entry.cols, entry.rows, drag.CandidateOk);
            }

            float ghostLeft, ghostBottom;
            if (drag.CandidateGrid != null)
            {
                var grid = drag.CandidateGrid;
                ghostLeft = grid.X + drag.CandidateCol * grid.CellWidth + (entry.cols * grid.CellWidth - entry.displayWidth) * .5f;
                ghostBottom = grid.Surface == FurnitureSurfaceType.Table
                    ? grid.Y + grid.CellHeight
                    : grid.Y + (drag.CandidateRow + entry.rows) * grid.CellHeight;
            }
            else
            {
                ghostLeft = wantLeft;
                ghostBottom = wantBottom;
            }
            drag.Ghost.transform.position = PxToWorld(
                ghostLeft + entry.displayWidth * .5f, ghostBottom - entry.displayHeight * .5f, ZGhost);
            var invalid = !drag.OverInventory && !(drag.CandidateGrid != null && drag.CandidateOk);
            drag.GhostRenderer.color = invalid ? new Color(1f, .58f, .52f, .85f) : new Color(1f, 1f, 1f, .85f);
        }

        private void EndDrag(bool commit)
        {
            var state = drag;
            drag = null;
            hud.SetDragDimming(false);
            hud.SetInventoryDropHint(false);
            if (focusBlurRenderer != null)
            {
                focusBlurRenderer.DOKill();
                focusBlurRenderer.DOFade(0f, .3f);
            }

            void RestoreDragged()
            {
                if (state.Item == null) return;
                state.Item.Root.SetActive(true);
                grids[state.Item.GridId].SetOccupied(state.Item.Col, state.Item.Row,
                    state.Entry.cols, state.Entry.rows, state.Item.Id, true);
                if (state.Entry.tableSurface != null && state.Entry.tableSurface.enabled)
                {
                    if (grids.TryGetValue(TableGridId(state.Item.Id), out var tableGrid)) tableGrid.SetVisible(true);
                    foreach (var child in ChildrenOf(state.Item)) child.Root.SetActive(true);
                }
            }

            if (commit && state.OverInventory && state.Item != null)
            {
                SfxManager.Play(ESfx.FurniturePlace); // 音效需求 #2：放置（收回收纳栏也算落定）
                RestoreDragged();
                Destroy(state.Ghost);
                StoreItem(state.Item, false);
            }
            else if (commit && state.CandidateGrid != null && state.CandidateOk)
            {
                SfxManager.Play(ESfx.FurniturePlace); // 音效需求 #2：放置（落地）
                Destroy(state.Ghost);
                if (state.Item != null)
                {
                    state.Item.Root.SetActive(true);
                    state.Item.Flipped = state.Flipped;
                    if (state.Item.Renderer != null) state.Item.Renderer.flipX = state.Flipped;
                    if (state.Entry.tableSurface != null && state.Entry.tableSurface.enabled)
                    {
                        if (grids.TryGetValue(TableGridId(state.Item.Id), out var tableGrid)) tableGrid.SetVisible(true);
                        foreach (var child in ChildrenOf(state.Item)) child.Root.SetActive(true);
                    }
                    MoveItem(state.Item, state.CandidateGrid.Id, state.CandidateCol, state.CandidateRow);
                }
                else
                {
                    PlaceItem(state.Entry, state.CandidateGrid.Id, state.CandidateCol, state.CandidateRow, false, state.Flipped);
                    hud.RefreshInventory();
                }
            }
            else
            {
                if (commit && state.Item == null)
                {
                    var hasSurface = false;
                    foreach (var grid in grids.Values)
                        if (state.Entry.Supports(grid.Surface)) { hasSurface = true; break; }
                    if (!hasSurface && state.Entry.Supports(FurnitureSurfaceType.Table))
                        hud.ShowToast("需要先摆放带桌面格的家具（如茶几）");
                    else if (state.CandidateGrid != null && !state.CandidateOk)
                        hud.ShowToast("该位置无法摆放");
                }
                RestoreDragged();
                Destroy(state.Ghost);
            }
            RefreshAllGridColors();
            ApplyGridVisibility();
        }

        #endregion

        #region 网格显隐

        /// <summary>拖拽中显示该家具支持的全部表面网格（表面类型可多选）。</summary>
        private void ShowGridsForEntry(FurnitureEntry entry)
        {
            foreach (var grid in grids.Values) grid.SetVisible(entry.Supports(grid.Surface));
        }

        private void ApplyGridVisibility()
        {
            if (drag != null) return;
            foreach (var grid in grids.Values) grid.SetVisible(gridToggleOn);
        }

        private void ToggleGrids()
        {
            gridToggleOn = !gridToggleOn;
            hud.SetGridToggle(gridToggleOn);
            ApplyGridVisibility();
        }

        private void RefreshAllGridColors()
        {
            foreach (var grid in grids.Values) grid.RefreshCellColors();
        }

        #endregion

        #region 流通数值与收纳栏

        private FurnitureSlotState GetSlotState(string furnitureId)
        {
            var entry = furnitureTable.Find(furnitureId);
            if (!Economy.IsFurnitureOwned(furnitureId))
                return Economy.IsFurnitureRevealed(entry) ? FurnitureSlotState.Locked : FurnitureSlotState.Unknown;
            return IsPlaced(furnitureId) ? FurnitureSlotState.Placed : FurnitureSlotState.Available;
        }

        private void OnPurchaseConfirmed(string furnitureId)
        {
            var entry = furnitureTable.Find(furnitureId);
            if (entry == null) return;
            var result = Economy.TryPurchaseFurniture(entry);
            if (result == FurniturePurchaseResult.Success)
            {
                SfxManager.Play(ESfx.Reward); // 音效需求 #7：商城购买成功（家具模式内的购买入口）
                hud.RefreshInventory();
                hud.ShowToast($"已购入「{entry.displayName}」 · ◈ -{Economy.PriceOf(entry)}");
            }
            else if (result == FurniturePurchaseResult.NotEnoughCurrency)
            {
                hud.ShowToast("货币不足，先去完成客人服务吧");
            }
        }

        /// <summary>装饰分来源之一：全部房间已摆放装饰品的得分总和（当前房间先落会话，再逐房间求和）。</summary>
        private void RecomputeDecorationScore()
        {
            SaveState();
            SyncDecorationFromSession();
        }

        private void OnEconomyChanged()
        {
            hud?.SetEconomy(Economy.Data.Currency, Economy.Data.Reputation, Economy.DecorationScore);
        }

        #endregion

        #region 会话状态

        /// <summary>导出指定房间的当前布局供存档/烘焙。null = 该房间从未编辑过（沿用其默认摆放）。</summary>
        public static List<FurniturePlacementConfig> CaptureSessionPlacements(string roomId)
        {
            if (active != null && active.room != null && active.room.id == roomId) active.SaveState();
            if (string.IsNullOrEmpty(roomId) || !sessions.TryGetValue(roomId, out var state)) return null;
            return new List<FurniturePlacementConfig>(state.Placements);
        }

        /// <summary>从存档恢复指定房间的布局；placements 为 null 时回落到该房间默认摆放（存档接缝，待定 #9）。</summary>
        public static void RestoreSessionPlacements(string roomId, List<FurniturePlacementConfig> placements)
        {
            if (string.IsNullOrEmpty(roomId)) return;
            if (placements == null)
            {
                sessions.Remove(roomId);
            }
            else
            {
                var state = new SessionState();
                state.Placements.AddRange(placements);
                sessions[roomId] = state;
            }
            SyncDecorationFromSession();
        }

        /// <summary>关闭当前打开的家具模式（GM 全量重置等外部流程使用）。</summary>
        public static void CloseActive()
        {
            if (active != null) active.Close();
        }

        /// <summary>清空全部房间布局（新游戏）。</summary>
        public static void ResetSession()
        {
            sessions.Clear();
            SyncDecorationFromSession();
        }

        /// <summary>不打开家具模式也能把布局对应的装饰品得分回写流通服务（读档/新游戏后 Hub 立即显示正确装饰分）。
        /// 多房间：逐房间取会话布局（无会话则用该房间默认摆放）求和。</summary>
        private static void SyncDecorationFromSession()
        {
            var table = GameManager.Instance.FurnitureTable;
            var rooms = GameManager.Instance.FurnitureRoomTable;
            if (table == null || rooms == null)
            {
                Economy.SetFurnitureDecorationScore(0);
                return;
            }
            var sum = 0;
            foreach (var roomEntry in rooms.rooms)
            {
                if (roomEntry == null) continue;
                var placements = sessions.TryGetValue(roomEntry.id, out var state)
                    ? state.Placements
                    : roomEntry.initialPlacements;
                foreach (var placement in placements)
                {
                    var entry = placement == null ? null : table.Find(placement.furnitureId);
                    if (entry != null) sum += entry.decorationScore;
                }
            }
            Economy.SetFurnitureDecorationScore(sum);
        }

        private void RestoreState()
        {
            var placements = sessions.TryGetValue(room.id, out var state) ? state.Placements : room.initialPlacements;

            // 先摆基础网格上的家具，再摆桌面家具（宿主此时已生成桌面网格）
            foreach (var placement in placements)
            {
                if (placement == null || !string.IsNullOrEmpty(placement.hostFurnitureId)) continue;
                var entry = furnitureTable.Find(placement.furnitureId);
                if (entry == null || !grids.TryGetValue(placement.gridId ?? string.Empty, out var grid)) continue;
                if (!grid.FootprintFree(placement.col, placement.row, entry.cols, entry.rows, null)) continue;
                PlaceItem(entry, placement.gridId, placement.col, placement.row, true, placement.flipped);
            }
            foreach (var placement in placements)
            {
                if (placement == null || string.IsNullOrEmpty(placement.hostFurnitureId)) continue;
                var entry = furnitureTable.Find(placement.furnitureId);
                if (entry == null) continue;
                FurnitureRuntimeItem host = null;
                foreach (var item in items.Values)
                    if (item.Entry.id == placement.hostFurnitureId) { host = item; break; }
                if (host == null || !grids.TryGetValue(TableGridId(host.Id), out var grid)) continue;
                if (!grid.FootprintFree(placement.col, placement.row, entry.cols, entry.rows, null)) continue;
                PlaceItem(entry, grid.Id, placement.col, placement.row, true, placement.flipped);
            }
        }

        private void SaveState()
        {
            var state = new SessionState();
            foreach (var item in items.Values)
            {
                if (item.IsOnTableGrid) continue;
                state.Placements.Add(new FurniturePlacementConfig
                {
                    furnitureId = item.Entry.id,
                    gridId = item.GridId,
                    col = item.Col,
                    row = item.Row,
                    flipped = item.Flipped,
                });
            }
            foreach (var item in items.Values)
            {
                if (!item.IsOnTableGrid) continue;
                var host = items[grids[item.GridId].HostItemId];
                state.Placements.Add(new FurniturePlacementConfig
                {
                    furnitureId = item.Entry.id,
                    hostFurnitureId = host.Entry.id,
                    col = item.Col,
                    row = item.Row,
                    flipped = item.Flipped,
                });
            }
            sessions[room.id] = state; // 按房间分桶保存
        }

        #endregion

        private void Close()
        {
            if (closing) return;
            closing = true;
            if (drag != null)
            {
                Destroy(drag.Ghost);
                drag = null;
            }
            SaveState();
            Economy.Changed -= OnEconomyChanged;
            DOTween.Kill(this);
            if (focusBlurRenderer != null) focusBlurRenderer.DOKill();
            hud?.Destroy();
            if (rig != null) Destroy(rig.gameObject);
            if (stageRoot != null) Destroy(stageRoot.gameObject);
            var callback = onClosed;
            onClosed = null;
            Destroy(gameObject);
            callback?.Invoke();
        }

        private void OnDestroy()
        {
            // 应用退出时各常驻对象的销毁顺序不确定，GameManager 可能先没
            if (GameManager.Instance != null) Economy.Changed -= OnEconomyChanged;
            if (active == this) active = null;
        }
    }
}

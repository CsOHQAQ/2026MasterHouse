using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 「修理电路」的根组件，挂在小游戏 Prefab 的根节点上，是这个小游戏对外的**唯一**面孔。
    ///
    /// 它只认识 IMinigame 契约和自己的关卡类型 LevelDef（小游戏说明 §3.1）——
    /// 不引用 GameManager / VisitorManager / EconomyManager / HouseClockManager，
    /// 也不知道分数会被拿去干什么。改完请对 Minigame/Circuit/ 全文检索一遍。
    ///
    /// 时间自治（§3.3）：本类走 Update 逐帧轮询输入，与全局 tick 零关系——
    /// 小游戏期间营业闸门是关的，跟着全局心跳走会被一起冻住。
    /// 实际上修理电路一拍都不需要：供电是纯函数，每次改动后重算一次即可。
    /// </summary>
    [RequireComponent(typeof(CircuitMinigameView))]
    public sealed class CircuitMinigame : MonoBehaviour, IMinigame
    {
        private CircuitMinigameView view;

        private LevelData level;
        private LevelManager levelManager;
        private LinkManager linkManager;
        private CircuitBoard board;

        private Action<int> onFinish;
        private Action onAbort;
        private bool running;

        /// <summary>件库条目：运行时由模板克隆（§16.2 动态列表项）。</summary>
        private readonly List<CircuitPaletteItemView> paletteItems = new List<CircuitPaletteItemView>();
        private readonly List<NodeDef> paletteDefs = new List<NodeDef>();

        private Vector2 lastBoardAreaSize;

        // ══════════ IMinigame ══════════

        public void Launch(MinigameLevelDef levelDef, Action<int> finish, Action abort)
        {
            onFinish = finish;
            onAbort = abort;

            view = GetComponent<CircuitMinigameView>();
            if (view == null)
            {
                Debug.LogError("[修理电路] Prefab 根节点缺少 CircuitMinigameView 组件", gameObject);
                abort?.Invoke();
                return;
            }
            if (!ValidateView())
            {
                // 缺件是报错不是回退（§16.2）。直接放弃本局，宿主会把页面关掉、访客保持「服务中」
                abort?.Invoke();
                return;
            }

            var circuitLevel = levelDef as LevelDef;
            if (circuitLevel == null)
            {
                Debug.LogError($"[修理电路] 拿到的不是修理电路的关卡（{(levelDef != null ? levelDef.name : "null")}）：" +
                               $"请检查 MinigameDef 的关卡池里是不是混进了别的小游戏的关卡", levelDef);
                abort?.Invoke();
                return;
            }

            linkManager = new LinkManager();
            levelManager = new LevelManager(linkManager);
            level = levelManager.BuildLevel(circuitLevel);

            // Prefab 刚 Instantiate 出来，boardArea 的 rect 还没经过一次布局解算，
            // 直接读会拿到零尺寸、把格子算成最小值。这里先逼一次布局；
            // 万一仍不准，Update 里的尺寸变化检测会在下一帧纠正（它同时也是分辨率变化的处理路径）
            Canvas.ForceUpdateCanvases();

            board = new CircuitBoard(level, levelManager, linkManager, view, ResolveUiCamera());
            board.LayoutChanged += Refresh;
            board.LayoutRoots();
            board.RebuildAll();

            BuildPalette();
            BindButtons();
            Refresh();

            lastBoardAreaSize = view.boardArea.rect.size;
            running = true;
        }

        private void Update()
        {
            if (!running) return;

            // 分辨率/窗口变化时重算格子大小。比每帧无脑重排便宜，也不需要监听事件
            var size = view.boardArea.rect.size;
            if ((size - lastBoardAreaSize).sqrMagnitude > 1f)
            {
                lastBoardAreaSize = size;
                board.LayoutRoots();
                board.RebuildAll();
            }

            board.HandleInput();
        }

        // ══════════ 结束 ══════════

        /// <summary>【完成】：按当前点亮占比结算，随时可点（§4.5，没有失败条件）。</summary>
        private void OnFinishClicked()
        {
            if (!running) return;
            running = false;
            var score = CircuitSolver.Score(level);
            var finish = onFinish;
            onFinish = null;
            onAbort = null;
            finish?.Invoke(score);
        }

        /// <summary>【放弃】：不结算，访客保持「服务中」，再次进入局面重置。</summary>
        private void OnAbortClicked()
        {
            if (!running) return;
            running = false;
            var abort = onAbort;
            onFinish = null;
            onAbort = null;
            abort?.Invoke();
        }

        private void OnDestroy()
        {
            if (board != null) board.LayoutChanged -= Refresh;
            // 页面被壳直接销毁（ESC / 遮罩）时，宿主已经按「关掉页面且不结算」处理，
            // 这里不再补调 onAbort——重复回调会违反「只调一次」的契约
            running = false;
        }

        // ══════════ 界面刷新 ══════════

        /// <summary>预算条与件库余量。布局一改就刷，不逐帧刷。</summary>
        private void Refresh()
        {
            int usedCells = level.UsedLinkCells;
            int budget = level.LinkCellBudget;
            if (view.linkBudgetLabel != null)
            {
                view.linkBudgetLabel.text = budget > 0 ? $"导线 {usedCells}/{budget}" : $"导线 {usedCells}";
                view.linkBudgetLabel.color = budget > 0 && usedCells >= budget
                    ? view.budgetWarnColor
                    : view.budgetNormalColor;
            }

            int placed = 0, cap = 0;
            foreach (var entry in level.Def.BuildableNodes)
            {
                if (entry?.Node == null) continue;
                placed += levelManager.CountNodesOf(level, entry.Node);
                cap += entry.MaxCount;
            }
            if (view.pieceBudgetLabel != null)
            {
                view.pieceBudgetLabel.text = $"中转件 {placed}/{cap}";
                view.pieceBudgetLabel.color = cap > 0 && placed >= cap
                    ? view.budgetWarnColor
                    : view.budgetNormalColor;
            }

            if (view.litLabel != null)
                view.litLabel.text = $"已点亮 {CircuitSolver.CountLit(level)}/{CircuitSolver.CountBatteries(level)}";

            RefreshPalette();
        }

        // ══════════ 件库 ══════════

        private void BuildPalette()
        {
            var template = view.paletteItemTemplate;
            template.gameObject.SetActive(false);

            foreach (var entry in level.Def.BuildableNodes)
            {
                if (entry?.Node == null) continue;
                var item = Instantiate(template, view.paletteRoot, false);
                item.gameObject.SetActive(true);
                item.name = "PaletteItem_" + entry.Node.name;

                var def = entry.Node; // 闭包捕获：不能直接用循环变量
                if (item.button != null)
                    item.button.onClick.AddListener(() => OnPaletteClicked(def));
                if (item.label != null)
                    item.label.text = string.IsNullOrEmpty(def.DisplayName) ? def.name : def.DisplayName;

                paletteItems.Add(item);
                paletteDefs.Add(def);
            }
        }

        private void OnPaletteClicked(NodeDef def)
        {
            if (!running) return;
            // 再点一次同一个 = 取消选中
            board.SetPendingPlacement(board.PendingPlacement == def ? null : def);
        }

        private void RefreshPalette()
        {
            for (int i = 0; i < paletteItems.Count; i++)
            {
                var item = paletteItems[i];
                var def = paletteDefs[i];
                int remaining = levelManager.RemainingBuildCount(level, def);
                int max = MaxCountOf(def);

                if (item.count != null)
                {
                    item.count.text = $"{max - remaining}/{max}";
                    item.count.color = remaining <= 0 ? view.budgetWarnColor : view.budgetNormalColor;
                }
                if (item.button != null)
                    item.button.interactable = remaining > 0;
                if (item.background != null)
                    item.background.color = board.PendingPlacement == def
                        ? view.legalColor
                        : new Color(1f, 1f, 1f, 0.10f);
            }
        }

        private int MaxCountOf(NodeDef def)
        {
            foreach (var entry in level.Def.BuildableNodes)
                if (entry?.Node == def)
                    return entry.MaxCount;
            return 0;
        }

        // ══════════ 杂项 ══════════

        private void BindButtons()
        {
            if (view.finishButton != null) view.finishButton.onClick.AddListener(OnFinishClicked);
            if (view.abortButton != null) view.abortButton.onClick.AddListener(OnAbortClicked);
        }

        /// <summary>Screen Space Overlay 的 Canvas 传 null 相机；其余模式取 Canvas 自己的。</summary>
        private Camera ResolveUiCamera()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return null;
            var root = canvas.rootCanvas;
            return root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        }

        /// <summary>Prefab 缺件是 LogError，不做代码兜底布局（§16.2）。</summary>
        private bool ValidateView()
        {
            var missing = new List<string>();
            if (view.boardArea == null) missing.Add(nameof(view.boardArea));
            if (view.gridRoot == null) missing.Add(nameof(view.gridRoot));
            if (view.nodeRoot == null) missing.Add(nameof(view.nodeRoot));
            if (view.linkRoot == null) missing.Add(nameof(view.linkRoot));
            if (view.previewRoot == null) missing.Add(nameof(view.previewRoot));
            if (view.paletteRoot == null) missing.Add(nameof(view.paletteRoot));
            if (view.paletteItemTemplate == null) missing.Add(nameof(view.paletteItemTemplate));
            if (missing.Count == 0) return true;

            Debug.LogError($"[修理电路] Prefab 缺少必需的布局引用：{string.Join("、", missing)}。" +
                           $"请执行菜单 MasterHouse → 小游戏 → 重建修理电路 Prefab（会覆盖手调）", gameObject);
            return false;
        }
    }
}

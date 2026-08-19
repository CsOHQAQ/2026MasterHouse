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
    ///
    /// ══ 单关与课程包：一条代码路径（2026-08-16）══
    ///
    /// 宿主递进来的可能是一张 <see cref="LevelDef"/>（单关，访客点单的常规玩法），
    /// 也可能是一张 <see cref="CircuitLessonPackDef"/>（课程包，一局连打 N 关的教程）。
    /// 内部统一成一张课程表，**单关就是长度 1 的课程表**，逐关推进的代码只有一份。
    ///
    /// 两者只在这几处分叉，全部由 <see cref="isLessonPack"/> 一个开关控制：
    /// <list type="bullet">
    /// <item>单关的【完成】随时可点、按当前点亮占比结算（§4.5 原语义，一行没改）；
    ///       课程包必须**全亮**才放行，最后一关交卷才结算。</item>
    /// <item>教学栏 / 进度 / 上一关 / 重试 / 小结面板只在课程包模式下出现，
    ///       且只在课程包模式下参与 Prefab 缺件校验——否则手调过的旧 Prefab 连单关都开不了。</item>
    /// </list>
    ///
    /// **每关一份 LevelData、开局一次性全部建好、常驻到本局结束**：这就是「回上一关保留布线」的
    /// 全部实现——玩家的连线本来就存在 LevelData 里，只要不重建它，回看就是原样。
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

        // ── 课程表（单关 = 长度 1）──
        private readonly List<CircuitLessonEntry> lessons = new List<CircuitLessonEntry>();
        private readonly List<LevelData> levels = new List<LevelData>();
        private int currentIndex;
        private bool isLessonPack;
        private bool summaryOpen;

        /// <summary>件库条目：运行时由模板克隆（§16.2 动态列表项）。</summary>
        private readonly List<CircuitPaletteItemView> paletteItems = new List<CircuitPaletteItemView>();
        private readonly List<NodeDef> paletteDefs = new List<NodeDef>();

        private Vector2 lastBoardAreaSize;
        private Sprite defaultLevelBackgroundSprite;
        private Color defaultLevelBackgroundColor;

        private bool IsLastLesson => currentIndex >= levels.Count - 1;

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

            CacheDefaultLevelBackground();
            ApplyUiStyle();

            if (!ResolveLessons(levelDef))
            {
                abort?.Invoke();
                return;
            }
            if (isLessonPack && !ValidateLessonView())
            {
                abort?.Invoke();
                return;
            }

            linkManager = new LinkManager();
            levelManager = new LevelManager(linkManager);

            // 每关一份 LevelData 全部先建好并常驻（见类注释）：回上一关时布线原样还在。
            // LevelManager 自己不存关卡状态（所有方法都收 level 参数），一个实例服务多关是安全的
            foreach (var lesson in lessons)
                levels.Add(levelManager.BuildLevel(lesson.Level));

            // Prefab 刚 Instantiate 出来，boardArea 的 rect 还没经过一次布局解算，
            // 直接读会拿到零尺寸、把格子算成最小值。这里先逼一次布局；
            // 万一仍不准，Update 里的尺寸变化检测会在下一帧纠正（它同时也是分辨率变化的处理路径）
            Canvas.ForceUpdateCanvases();

            board = new CircuitBoard(levels[0], levelManager, linkManager, view, ResolveUiCamera());
            board.LayoutChanged += Refresh;
            board.DrawingChanged += RefreshLinkBudget;

            BindButtons();
            SetupChrome();
            EnterLesson(0);
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

            // 小结面板开着时不接受棋盘操作。**面板挡不住棋盘**——棋盘的命中判定走
            // 「鼠标屏幕坐标 → 棋盘局部坐标」一条路，不靠 raycast，全屏面板对它是透明的
            if (summaryOpen) return;

            board.HandleInput();
        }

        // ══════════ 课程表 ══════════

        /// <summary>把宿主递来的关卡解析成课程表。单关 = 长度 1，之后的代码不再区分。</summary>
        private bool ResolveLessons(MinigameLevelDef levelDef)
        {
            if (levelDef is CircuitLessonPackDef pack)
            {
                isLessonPack = true;
                foreach (var entry in pack.Lessons)
                {
                    if (entry == null || entry.Level == null) continue; // 空行是编辑期的常态，跳过即可
                    lessons.Add(entry);
                }
                if (lessons.Count == 0)
                {
                    Debug.LogError($"[修理电路] 课程包「{pack.name}」里一关都没配，开不了局", pack);
                    return false;
                }
                return true;
            }

            if (levelDef is LevelDef single)
            {
                isLessonPack = false;
                lessons.Add(new CircuitLessonEntry { Level = single });
                return true;
            }

            Debug.LogError($"[修理电路] 拿到的不是修理电路的关卡（{(levelDef != null ? levelDef.name : "null")}）：" +
                           $"请检查 MinigameDef 的关卡池 / 需求点名的关卡里是不是混进了别的小游戏的关卡", levelDef);
            return false;
        }

        /// <summary>切到第 index 关。局面不重建，所以来回翻页保留布线。</summary>
        private void EnterLesson(int index)
        {
            currentIndex = Mathf.Clamp(index, 0, levels.Count - 1);
            level = levels[currentIndex];

            RefreshLevelBackground();

            board.SetLevel(level);
            board.LayoutRoots();
            board.RebuildAll();

            BuildPalette();
            CloseSummary();
            RefreshLessonChrome();
            Refresh();

            lastBoardAreaSize = view.boardArea.rect.size;
        }

        /// <summary>开局定妆：课程包专属的控件在单关模式下整体隐藏（Prefab 是同一个）。</summary>
        private void SetupChrome()
        {
            // 这里逐个判空是给「课程包字段没配的旧 Prefab + 单关关卡」留的路：
            // 课程包模式下 ValidateLessonView 已经保证它们都非空
            if (view.lessonPanel != null) view.lessonPanel.SetActive(isLessonPack);
            if (view.topStatusBar != null && view.topStatusBar.progressLabel != null)
                view.topStatusBar.progressLabel.gameObject.SetActive(isLessonPack);
            if (view.prevLessonButton != null) view.prevLessonButton.gameObject.SetActive(isLessonPack);
            if (view.retryLessonButton != null) view.retryLessonButton.gameObject.SetActive(isLessonPack);
            if (view.summaryPanel != null) view.summaryPanel.SetActive(false);
            summaryOpen = false;
        }

        /// <summary>刷新教学栏、进度与按钮文案。单关模式什么都不做。</summary>
        private void RefreshLessonChrome()
        {
            if (!isLessonPack) return;

            var entry = lessons[currentIndex];
            view.topStatusBar.progressLabel.text = $"第 {currentIndex + 1}/{lessons.Count} 关";
            view.lessonTitleLabel.text = string.IsNullOrEmpty(entry.Title) ? entry.Level.name : entry.Title;
            view.lessonBriefLabel.text = entry.Brief;

            // 第一关没有「上一关」可回，藏掉而不是置灰：置灰的按钮玩家读不出原因
            view.prevLessonButton.gameObject.SetActive(currentIndex > 0);

            var caption = IsLastLesson ? "交卷" : "下一关";
            view.finishButtonLabel.text = caption;
            view.summaryContinueLabel.text = caption;
        }

        // ══════════ 结束 / 推进 ══════════

        /// <summary>
        /// 右下主按钮。单关是【完成】：随时可点、按当前点亮占比结算（§4.5）。
        /// 课程包是【下一关】/【交卷】：**必须全亮才放行**（2026-08-16 拍板）。
        ///
        /// 未全亮时按钮**不置灰而是给理由**——置灰的按钮点下去没有任何反馈，
        /// 玩家不会知道自己卡在哪，而这是教程。
        /// </summary>
        private void OnFinishClicked()
        {
            if (!running || summaryOpen) return;

            if (!isLessonPack)
            {
                Settle();
                return;
            }

            int lit = CircuitSolver.CountLit(level);
            int total = CircuitSolver.CountBatteries(level);
            if (lit < total)
            {
                board.ShowMessage($"还有 {total - lit} 个电池没点亮，全部点亮才能" +
                                  (IsLastLesson ? "交卷" : "进入下一关"));
                return;
            }

            OpenSummary(lit, total);
        }

        /// <summary>小结面板的主按钮：进下一关，或者在最后一关交卷。</summary>
        private void OnSummaryContinueClicked()
        {
            if (!running || !summaryOpen) return;

            if (IsLastLesson)
            {
                Settle();
                return;
            }
            EnterLesson(currentIndex + 1);
        }

        /// <summary>小结面板的次按钮：关掉面板，留在本关继续调整。</summary>
        private void OnSummaryStayClicked()
        {
            if (!running) return;
            CloseSummary();
        }

        /// <summary>回上一关。局面不重建，之前的布线原样还在（可看可改）。</summary>
        private void OnPrevLessonClicked()
        {
            if (!running || summaryOpen || currentIndex <= 0) return;
            EnterLesson(currentIndex - 1);
        }

        /// <summary>
        /// 重试本关：把这一关的 LevelData 重建一份（回到只有题面的初始态），其他关不受影响。
        /// 不做二次确认——教程语境下重来是常规操作，弹窗比误点更烦。
        /// </summary>
        private void OnRetryLessonClicked()
        {
            if (!running || summaryOpen) return;
            levels[currentIndex] = levelManager.BuildLevel(lessons[currentIndex].Level);
            EnterLesson(currentIndex);
            board.ShowMessage("本关已重置");
        }

        /// <summary>【放弃】：不结算，访客保持「服务中」，再次进入局面重置（课程包也是从第一关重来）。</summary>
        private void OnAbortClicked()
        {
            if (!running) return;
            running = false;
            var abort = onAbort;
            onFinish = null;
            onAbort = null;
            abort?.Invoke();
        }

        /// <summary>交卷：算总分交给宿主。契约要求 onFinish/onAbort 只调一次，running 标记挡住重入。</summary>
        private void Settle()
        {
            running = false;
            var score = AggregateScore();
            var finish = onFinish;
            onFinish = null;
            onAbort = null;
            finish?.Invoke(score);
        }

        /// <summary>
        /// 本局得分：**各关分数取平均**（2026-08-16 拍板），四舍五入，整数运算不碰 float。
        ///
        /// 课程包因为「必须全亮才能往前」，能走到交卷时每关必定都是 100 分，所以实际恒为 100
        /// ——配套 MinigameDef 的四档阈值因此配成 100/100/100。这里仍老实按平均算而不是硬编码 100：
        /// 门槛口径将来若放宽（比如允许跳过），分数不必跟着改。
        ///
        /// 重算一遍 Solve 是为了不依赖各关最后一次编辑留下的缓存状态：它是纯函数，重算很便宜。
        /// </summary>
        private int AggregateScore()
        {
            if (levels.Count == 0) return 0;

            long sum = 0;
            foreach (var data in levels)
            {
                CircuitSolver.Solve(data);
                sum += CircuitSolver.Score(data);
            }
            return (int)((sum + levels.Count / 2) / levels.Count);
        }

        private void OnDestroy()
        {
            if (board != null)
            {
                board.LayoutChanged -= Refresh;
                board.DrawingChanged -= RefreshLinkBudget;
            }
            // 页面被壳直接销毁（ESC / 遮罩）时，宿主已经按「关掉页面且不结算」处理，
            // 这里不再补调 onAbort——重复回调会违反「只调一次」的契约
            running = false;
        }

        // ══════════ 小结面板 ══════════

        private void OpenSummary(int lit, int total)
        {
            summaryOpen = true;
            view.summaryPanel.SetActive(true);

            view.summaryTitleLabel.text = IsLastLesson
                ? "全部完成"
                : $"第 {currentIndex + 1}/{lessons.Count} 关 完成";

            var budget = level.LinkCellBudget;
            var wire = budget > 0 ? $"导线 {level.UsedLinkCells}/{budget}" : $"导线 {level.UsedLinkCells}";
            view.summaryBodyLabel.text = IsLastLesson
                ? $"已点亮 {lit}/{total}　{wire}\n交卷后本次教程结束。"
                : $"已点亮 {lit}/{total}　{wire}";
        }

        private void CloseSummary()
        {
            summaryOpen = false;
            if (view.summaryPanel != null) view.summaryPanel.SetActive(false);
        }

        // ══════════ 界面刷新 ══════════

        /// <summary>预算条与件库余量。布局一改就刷，不逐帧刷。</summary>
        private void Refresh()
        {
            RefreshLinkBudget();

            int placed = 0, cap = 0;
            foreach (var entry in level.Def.BuildableNodes)
            {
                if (entry?.Node == null) continue;
                placed += levelManager.CountNodesOf(level, entry.Node);
                cap += entry.MaxCount;
            }
            if (view.topStatusBar.pieceBudgetLabel != null)
            {
                view.topStatusBar.pieceBudgetLabel.text = $"中转件 {placed}/{cap}";
                // 摆满 = 提示色而非报红：CanBuild 硬拦着，摆满是常态不是错误（与导线栏同一套语义）
                view.topStatusBar.pieceBudgetLabel.color = cap > 0 && placed >= cap
                    ? view.budgetFullColor
                    : view.budgetNormalColor;
            }

            if (view.topStatusBar.litLabel != null)
                view.topStatusBar.litLabel.text = $"已点亮 {CircuitSolver.CountLit(level)}/{CircuitSolver.CountBatteries(level)}";

            RefreshPalette();
        }

        /// <summary>
        /// 导线预算标签。描格途中每变一格就刷一次（<see cref="CircuitBoard.DrawingChanged"/>），
        /// 所以这里**只碰这一个标签**——整套 <see cref="Refresh"/> 有 CountLit 遍历与件库重建，逐格调不划算。
        ///
        /// 口径 = 已成线格数 +（正在描的格数），与建线校验一致（§8.3）；
        /// 正在描时分开显示成 `12(+5)/30`，玩家才知道退回能省下多少。
        /// 三档配色：未满白、正好用满提示色、**超出才报红**——用满是合法解，报红会误导（2026-08-16 改）。
        /// </summary>
        private void RefreshLinkBudget()
        {
            if (view.topStatusBar.linkBudgetLabel == null) return;

            int committed = level.UsedLinkCells;
            int pending = board != null ? board.PendingLinkCells : 0;
            int total = committed + pending;
            int budget = level.LinkCellBudget;

            var used = pending > 0 ? $"{committed}(+{pending})" : committed.ToString();
            view.topStatusBar.linkBudgetLabel.text = budget > 0 ? $"导线 {used}/{budget}" : $"导线 {used}";

            if (budget <= 0 || total < budget) view.topStatusBar.linkBudgetLabel.color = view.budgetNormalColor;
            else if (total == budget) view.topStatusBar.linkBudgetLabel.color = view.budgetFullColor;
            else view.topStatusBar.linkBudgetLabel.color = view.budgetWarnColor;
        }

        // ══════════ 件库 ══════════

        /// <summary>
        /// 按当前关卡的可建件重建件库。**课程包换关会反复调**，所以先清干净再造。
        /// 清理时先 SetActive(false) 再 Destroy：Destroy 要到帧末才生效，
        /// 而 paletteRoot 上挂着 VerticalLayoutGroup，留着的旧条目会让新条目错位一帧（隐藏的不参与布局）。
        /// </summary>
        private void BuildPalette()
        {
            foreach (var old in paletteItems)
            {
                if (old == null) continue;
                old.gameObject.SetActive(false);
                Destroy(old.gameObject);
            }
            paletteItems.Clear();
            paletteDefs.Clear();

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

                ConfigurePaletteItem(item, def);

                paletteItems.Add(item);
                paletteDefs.Add(def);
            }
        }

        private void OnPaletteClicked(NodeDef def)
        {
            if (!running || summaryOpen) return;
            // 再点一次同一个 = 取消选中
            board.SetPendingPlacement(board.PendingPlacement == def ? null : def);
        }

        private void RefreshPalette()
        {
            var style = view.uiStyle;
            for (int i = 0; i < paletteItems.Count; i++)
            {
                var item = paletteItems[i];
                var def = paletteDefs[i];
                int remaining = levelManager.RemainingBuildCount(level, def);
                int max = MaxCountOf(def);

                if (item.count != null)
                {
                    item.count.text = $"{max - remaining}/{max}";
                    item.count.color = remaining <= 0
                        ? style.paletteCountDisabledColor
                        : style.paletteCountColor;
                }
                RefreshPaletteCountDigits(item, remaining, style);
                if (item.label != null) item.label.color = style.paletteNameColor;
                if (item.button != null)
                    item.button.interactable = remaining > 0;
                if (item.background != null)
                    item.background.color = remaining <= 0
                        ? style.palettePieceDisabledColor
                        : board.PendingPlacement == def
                            ? style.palettePieceSelectedColor
                            : style.palettePieceNormalColor;
                if (item.functionIcon != null)
                {
                    var iconColor = def.IconColor;
                    item.functionIcon.color = remaining <= 0
                        ? new Color(iconColor.r, iconColor.g, iconColor.b, iconColor.a * .55f)
                        : iconColor;
                }
            }
        }

        /// <summary>
        /// 件库卡的轮廓跟随节点 Shape 的外接长宽；卡面和图标分别来自 UI 主题与节点定义。
        /// 预览在主题定义的最大盒子内等比缩放，因此纵向件不会把列表无限撑高，横向件也不会被压扁。
        /// </summary>
        private void ConfigurePaletteItem(CircuitPaletteItemView item, NodeDef def)
        {
            var style = view.uiStyle;
            var previewSize = PalettePreviewSize(def, style);

            if (item.background != null)
            {
                item.background.sprite = style.palettePieceBackgroundSprite;
                item.background.type = item.background.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
                item.background.preserveAspect = false;
                item.background.rectTransform.sizeDelta = previewSize;
                // PieceBackground 的 pivot 是中心：要让它的上沿距条目顶部 TopPadding，
                // 位置必须再减去半个自身高度，否则卡面会向上溢出并压住「件库」标题。
                item.background.rectTransform.anchoredPosition = new Vector2(0f,
                    -style.palettePieceTopPadding - previewSize.y * .5f);
            }

            if (item.functionIcon != null)
            {
                item.functionIcon.sprite = def.FunctionIconSprite;
                item.functionIcon.type = Image.Type.Simple;
                item.functionIcon.preserveAspect = true;
                item.functionIcon.rectTransform.sizeDelta = Vector2.one * (-style.palettePieceIconPadding * 2f);
                item.functionIcon.rectTransform.anchoredPosition = Vector2.zero;
            }

            if (item.countDigitTemplate != null)
                item.countDigitTemplate.gameObject.SetActive(false);

            if (item.layoutElement != null)
            {
                float height = style.palettePieceTopPadding + previewSize.y + style.palettePieceTextHeight;
                item.layoutElement.minHeight = height;
                item.layoutElement.preferredHeight = height;
            }

            if (item.button != null)
            {
                // 选中/耗尽状态由 RefreshPalette 的主题色唯一决定，Button 不再叠加一层默认 Tint。
                var colors = item.button.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = Color.white;
                colors.pressedColor = Color.white;
                colors.selectedColor = Color.white;
                colors.disabledColor = Color.white;
                colors.colorMultiplier = 1f;
                item.button.colors = colors;
            }
        }

        private static Vector2 PalettePreviewSize(NodeDef def, CircuitUIStyleConfig style)
        {
            int width = 1;
            int height = 1;
            var shape = def != null ? def.Shape : null;
            if (shape?.Grids != null && shape.Grids.Count > 0)
            {
                int minX = shape.Grids[0].DeltaPosition.x;
                int maxX = minX;
                int minY = shape.Grids[0].DeltaPosition.y;
                int maxY = minY;
                foreach (var grid in shape.Grids)
                {
                    minX = Mathf.Min(minX, grid.DeltaPosition.x);
                    maxX = Mathf.Max(maxX, grid.DeltaPosition.x);
                    minY = Mathf.Min(minY, grid.DeltaPosition.y);
                    maxY = Mathf.Max(maxY, grid.DeltaPosition.y);
                }
                width = maxX - minX + 1;
                height = maxY - minY + 1;
            }

            float scale = Mathf.Min(style.palettePiecePreviewMaxWidth / width,
                                    style.palettePiecePreviewMaxHeight / height);
            return new Vector2(width * scale, height * scale);
        }

        /// <summary>用主题中的数字 SpriteSheet 显示当前剩余可摆数量；缺图时才退回旧文本。</summary>
        private static void RefreshPaletteCountDigits(CircuitPaletteItemView item, int remaining,
            CircuitUIStyleConfig style)
        {
            bool canDraw = item.countDigitRoot != null && item.countDigitTemplate != null &&
                           style.paletteCountDigits != null && style.paletteCountDigits.Length >= 10;
            if (!canDraw)
            {
                if (item.count != null) item.count.gameObject.SetActive(true);
                return;
            }

            if (item.count != null) item.count.gameObject.SetActive(false);
            item.countDigitTemplate.gameObject.SetActive(false);

            var digits = Mathf.Max(0, remaining).ToString();
            float size = style.paletteCountDigitSize;
            float spacing = style.paletteCountDigitSpacing;
            float totalWidth = digits.Length * size + Mathf.Max(0, digits.Length - 1) * spacing;
            item.countDigitRoot.sizeDelta = new Vector2(totalWidth, size);
            item.countDigitRoot.anchoredPosition = new Vector2(-8f, -8f);

            for (int i = 0; i < digits.Length; i++)
            {
                Image digit;
                if (i < item.countDigitInstances.Count)
                    digit = item.countDigitInstances[i];
                else
                {
                    digit = Instantiate(item.countDigitTemplate, item.countDigitRoot, false);
                    digit.raycastTarget = false;
                    item.countDigitInstances.Add(digit);
                }

                digit.gameObject.SetActive(true);
                digit.sprite = style.paletteCountDigits[digits[i] - '0'];
                digit.type = Image.Type.Simple;
                digit.preserveAspect = true;
                digit.color = remaining <= 0 ? style.paletteCountDigitDisabledColor : style.paletteCountDigitColor;
                var rect = digit.rectTransform;
                rect.anchorMin = new Vector2(0f, .5f);
                rect.anchorMax = new Vector2(0f, .5f);
                rect.pivot = new Vector2(.5f, .5f);
                rect.sizeDelta = new Vector2(size, size);
                rect.anchoredPosition = new Vector2(size * (.5f + i) + spacing * i, 0f);
            }

            for (int i = digits.Length; i < item.countDigitInstances.Count; i++)
                item.countDigitInstances[i].gameObject.SetActive(false);
        }

        private void ApplyUiStyle()
        {
            var style = view.uiStyle;
            if (style.uiFont != null)
            {
                // 静态 Prefab（含嵌套顶部状态条）和已有模板统一在这里换字；
                // 棋盘后续动态创建的 Text 则在 CircuitBoard.NewLabel 中读取同一份样式。
                foreach (var text in GetComponentsInChildren<Text>(true))
                    text.font = style.uiFont;
            }
            if (view.topStatusBar != null)
            {
                var bar = view.topStatusBar;
                if (bar.background != null)
                {
                    bar.background.sprite = style.topBarBackgroundSprite;
                    bar.background.type = style.topBarBackgroundSprite != null ? Image.Type.Sliced : Image.Type.Simple;
                    bar.background.preserveAspect = false;
                    bar.background.color = style.topBarBackgroundColor;
                }
                var barRect = bar.transform as RectTransform;
                if (barRect != null)
                {
                    var size = barRect.sizeDelta;
                    size.y = style.topBarHeight;
                    barRect.sizeDelta = size;
                }
                if (bar.progressLabel != null) bar.progressLabel.color = style.topBarProgressTextColor;
                if (bar.linkBudgetLabel != null) bar.linkBudgetLabel.color = style.topBarTextColor;
                if (bar.pieceBudgetLabel != null) bar.pieceBudgetLabel.color = style.topBarTextColor;
                if (bar.litLabel != null) bar.litLabel.color = style.topBarTextColor;
            }
            if (view.palettePanelBackground != null)
            {
                view.palettePanelBackground.sprite = style.palettePanelBackgroundSprite;
                view.palettePanelBackground.type = style.palettePanelBackgroundSprite != null
                    ? Image.Type.Sliced
                    : Image.Type.Simple;
                view.palettePanelBackground.preserveAspect = false;
                view.palettePanelBackground.color = style.palettePanelColor;
            }

            var palettePosition = view.paletteRoot.anchoredPosition;
            palettePosition.y = -style.paletteContentTopPadding;
            view.paletteRoot.anchoredPosition = palettePosition;

            if (view.lessonPanelBackground != null)
            {
                view.lessonPanelBackground.sprite = style.lessonPanelBackgroundSprite;
                view.lessonPanelBackground.type = style.lessonPanelBackgroundSprite != null
                    ? Image.Type.Sliced
                    : Image.Type.Simple;
                view.lessonPanelBackground.preserveAspect = false;
                view.lessonPanelBackground.color = style.lessonPanelColor;
            }

            ApplyButtonStyle(view.finishButton, style);
            ApplyButtonStyle(view.abortButton, style);
            ApplyButtonStyle(view.prevLessonButton, style);
            ApplyButtonStyle(view.retryLessonButton, style);
            ApplyButtonStyle(view.summaryContinueButton, style);
            ApplyButtonStyle(view.summaryStayButton, style);
        }

        /// <summary>把主题中的按钮底图覆盖到 Button 的 TargetGraphic 上；缺图则保持 Prefab 原样。</summary>
        private static void ApplyButtonStyle(Button button, CircuitUIStyleConfig style)
        {
            if (button == null) return;
            var image = button.targetGraphic as Image;
            if (image == null) return;
            if (style.buttonBackgroundSprite == null) return;

            image.sprite = style.buttonBackgroundSprite;
            image.type = Image.Type.Sliced;
            image.preserveAspect = false;
        }

        private void CacheDefaultLevelBackground()
        {
            defaultLevelBackgroundSprite = view.levelBackground.sprite;
            defaultLevelBackgroundColor = view.levelBackground.color;
        }

        private void RefreshLevelBackground()
        {
            var backgroundSprite = level?.Def?.BackgroundSprite;
            view.levelBackground.sprite = backgroundSprite != null ? backgroundSprite : defaultLevelBackgroundSprite;
            view.levelBackground.type = Image.Type.Simple;
            view.levelBackground.preserveAspect = false;
            view.levelBackground.color = backgroundSprite != null ? Color.white : defaultLevelBackgroundColor;
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
            if (view.prevLessonButton != null) view.prevLessonButton.onClick.AddListener(OnPrevLessonClicked);
            if (view.retryLessonButton != null) view.retryLessonButton.onClick.AddListener(OnRetryLessonClicked);
            if (view.summaryContinueButton != null)
                view.summaryContinueButton.onClick.AddListener(OnSummaryContinueClicked);
            if (view.summaryStayButton != null) view.summaryStayButton.onClick.AddListener(OnSummaryStayClicked);
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
            if (view.visualStyle == null) missing.Add(nameof(view.visualStyle));
            if (view.uiStyle == null) missing.Add(nameof(view.uiStyle));
            if (view.levelBackground == null) missing.Add(nameof(view.levelBackground));
            if (view.topStatusBar == null) missing.Add(nameof(view.topStatusBar));
            else
            {
                if (view.topStatusBar.background == null) missing.Add("topStatusBar.background");
                if (view.topStatusBar.linkBudgetLabel == null) missing.Add("topStatusBar.linkBudgetLabel");
                if (view.topStatusBar.pieceBudgetLabel == null) missing.Add("topStatusBar.pieceBudgetLabel");
                if (view.topStatusBar.litLabel == null) missing.Add("topStatusBar.litLabel");
            }
            if (view.palettePanelBackground == null) missing.Add(nameof(view.palettePanelBackground));
            if (view.paletteRoot == null) missing.Add(nameof(view.paletteRoot));
            if (view.paletteItemTemplate == null) missing.Add(nameof(view.paletteItemTemplate));
            else
            {
                if (view.paletteItemTemplate.background == null) missing.Add("paletteItemTemplate.background");
                if (view.paletteItemTemplate.functionIcon == null) missing.Add("paletteItemTemplate.functionIcon");
                if (view.paletteItemTemplate.layoutElement == null) missing.Add("paletteItemTemplate.layoutElement");
                if (view.paletteItemTemplate.countDigitRoot == null) missing.Add("paletteItemTemplate.countDigitRoot");
                if (view.paletteItemTemplate.countDigitTemplate == null) missing.Add("paletteItemTemplate.countDigitTemplate");
            }
            return ReportMissing(missing);
        }

        /// <summary>
        /// 课程包专属控件的缺件校验，**只在课程包模式下跑**：
        /// 手调过的旧 Prefab 没有这些控件，让它连单关都开不了是本轮不该有的回归。
        /// </summary>
        private bool ValidateLessonView()
        {
            var missing = new List<string>();
            if (view.lessonPanel == null) missing.Add(nameof(view.lessonPanel));
            if (view.lessonTitleLabel == null) missing.Add(nameof(view.lessonTitleLabel));
            if (view.lessonBriefLabel == null) missing.Add(nameof(view.lessonBriefLabel));
            if (view.topStatusBar == null) missing.Add(nameof(view.topStatusBar));
            else if (view.topStatusBar.progressLabel == null) missing.Add("topStatusBar.progressLabel");
            if (view.prevLessonButton == null) missing.Add(nameof(view.prevLessonButton));
            if (view.retryLessonButton == null) missing.Add(nameof(view.retryLessonButton));
            if (view.summaryPanel == null) missing.Add(nameof(view.summaryPanel));
            if (view.summaryTitleLabel == null) missing.Add(nameof(view.summaryTitleLabel));
            if (view.summaryBodyLabel == null) missing.Add(nameof(view.summaryBodyLabel));
            if (view.summaryContinueButton == null) missing.Add(nameof(view.summaryContinueButton));
            if (view.summaryContinueLabel == null) missing.Add(nameof(view.summaryContinueLabel));
            if (view.summaryStayButton == null) missing.Add(nameof(view.summaryStayButton));
            if (view.finishButtonLabel == null) missing.Add(nameof(view.finishButtonLabel));
            return ReportMissing(missing);
        }

        private bool ReportMissing(List<string> missing)
        {
            if (missing.Count == 0) return true;

            Debug.LogError($"[修理电路] Prefab 缺少必需的布局引用：{string.Join("、", missing)}。" +
                           $"请执行菜单 MasterHouse → 小游戏 → 重建修理电路 Prefab（会覆盖手调）", gameObject);
            return false;
        }
    }
}

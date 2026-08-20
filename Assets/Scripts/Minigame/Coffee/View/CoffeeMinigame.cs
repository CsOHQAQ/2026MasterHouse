using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 「制作咖啡」的根组件，挂在小游戏 Prefab 的根节点上，是这个小游戏对外的**唯一**面孔。
    ///
    /// 它只认识 IMinigame 契约和自己的关卡类型 CoffeeLevelDef（架构 §8.5）——
    /// 不引用 GameManager / VisitorManager / EconomyManager / HouseClockManager，
    /// 也不知道分数会被拿去干什么。改完请对 Minigame/Coffee/ 全文检索一遍。
    ///
    /// 时间自治（View 豁免区）：走 Update + Time.deltaTime，与全局 tick 零关系。
    ///
    /// ⚠ §8.5「不认识任何 Manager」的一处窄口豁免（2026-08-20 加音效时）：本类引用 SfxManager /
    /// BgmManager。那条硬约束管的是**业务** Manager（GameManager / Visitor / Economy / HouseClock /
    /// HouseUIManager）与宿主类型——它们会把玩法和外部状态缠在一起；音频这两位是 View 层的全局出口
    /// （见 SfxManager 类注释），本类调它们只是"发声"，不读也不写任何业务状态，依赖方向没有掰弯。
    ///
    /// 流程：磨豆子（环上避障，上限 50 分）→ 冲咖啡（匀速移动，三档 50/30/20 分）
    /// → 灌满当帧弹通关结算弹窗（三栏统计 + 按总分点星，入场淡入上浮）→ 点【ESC 返回】
    /// 或按 ESC 才 onFinish(两环节相加)。没有失败条件。
    ///
    /// ESC / 左下角键位条是**暂停**（2026-08-20 按设计图改）：弹出局内暂停弹窗、整局冻结，
    /// 【继续】回去接着玩，【放弃】才走 onAbort（不结算，访客保持「服务中」，重开全重置）。
    /// 页面上不再单独摆一颗放弃按钮。ESC 由壳收键、经 ConsumeEscape 问下来，本类不读 KeyCode。
    /// </summary>
    [RequireComponent(typeof(CoffeeMinigameView))]
    public sealed class CoffeeMinigame : MonoBehaviour, IMinigame
    {
        private enum EPhase { Grind, Transition, Pour, Settle }

        private const string GrindPhaseTitle = "① 磨豆子";
        private const string PourPhaseTitle = "② 冲咖啡";

        private CoffeeMinigameView view;
        private CoffeeLevelDef level;

        /// <summary>本页的 UI 相机（Overlay 画布为 null）。命中测试与鼠标换算共用一份。</summary>
        private Camera uiCamera;
        private GrindGame grind;
        private PourGame pour;

        private static readonly int WaterFillRadiusId = Shader.PropertyToID("_FillRadius");
        private static readonly int WaterWobblePhaseId = Shader.PropertyToID("_WobblePhase");
        private static readonly int WaterWobbleAmpId = Shader.PropertyToID("_WobbleAmp");
        private static readonly int WaterRingsId = Shader.PropertyToID("_Rings");
        private static readonly int WaterRingThicknessId = Shader.PropertyToID("_RingThickness");
        private static readonly int WaterEdgeWobbleId = Shader.PropertyToID("_EdgeWobble");
        private static readonly int WaterProgressId = Shader.PropertyToID("_Progress");

        /// <summary>与 shader 的 RING_SLOTS 一致；默认间隔 0.03s × 寿命 0.9s ≈ 30 个并存 + 水花，32 够用</summary>
        private const int WaterRingSlots = 32;

        private EPhase phase;

        /// <summary>暂停弹窗是否开着。开着 = 整局冻结（见 Update 的暂停闸）。</summary>
        private bool paused;

        /// <summary>过场已经走了多久，以及环节根是否已经在幕布后面换过（见 TickTransition）。</summary>
        private float transitionElapsed;
        private bool transitionSwapped;

        private int grindScore;
        private float messageResetRemaining;

        /// <summary>ESC 请求退出的标记（下一帧才 Finish，见 ConsumeEscape 的理由）。</summary>
        private bool settleExitAsked;

        /// <summary>入场动画：已播秒数，以及底板/按钮的落点（开窗那一刻从 Prefab 上读，尊重手调）。</summary>
        private float settleIntroElapsed;
        private Vector2 settleBoardBasePos;
        private Vector2 settleButtonBasePos;

        private Material waterMaterial;
        private float wobblePhase;
        private float wobbleAmp;
        private bool wasPouring;
        private float ringTimer;
        private int ringCursor;
        private readonly Vector2[] ringCenter = new Vector2[WaterRingSlots];
        private readonly float[] ringAge = new float[WaterRingSlots];
        private readonly float[] ringStrength = new float[WaterRingSlots];
        private readonly Vector4[] ringUpload = new Vector4[WaterRingSlots];

        private Action<int> onFinish;
        private Action onAbort;
        private bool running;

        // ══════════ IMinigame ══════════

        public void Launch(MinigameLevelDef levelDef, Action<int> finish, Action abort)
        {
            onFinish = finish;
            onAbort = abort;

            view = GetComponent<CoffeeMinigameView>();
            if (view == null)
            {
                Debug.LogError("[制作咖啡] Prefab 根节点缺少 CoffeeMinigameView 组件", gameObject);
                abort?.Invoke();
                return;
            }
            if (!ValidateView())
            {
                // 缺件是报错不是回退（§16.2）。直接放弃本局，宿主会把页面关掉、访客保持「服务中」
                abort?.Invoke();
                return;
            }

            level = levelDef as CoffeeLevelDef;
            if (level == null)
            {
                Debug.LogError($"[制作咖啡] 拿到的不是制作咖啡的关卡（{(levelDef != null ? levelDef.name : "null")}）：" +
                               $"请检查 MinigameDef 的关卡池里是不是混进了别的小游戏的关卡", levelDef);
                abort?.Invoke();
                return;
            }

            // Prefab 刚 Instantiate 出来，区域 rect 还没经过一次布局解算，先逼一次（与修理电路同例）
            Canvas.ForceUpdateCanvases();

            // 磨豆的摇柄模式要把鼠标换算到圆盘局部坐标，与冲泡环节共用同一个 UI 相机
            uiCamera = ResolveUiCamera();
            grind = new GrindGame(view, level, uiCamera);
            grind.Hit += OnGrindHit;
            grind.Init();
            pour = new PourGame(view, level, uiCamera);
            SetupWater();

            view.abortButton.onClick.AddListener(OnAbortClicked);
            view.escButton.onClick.AddListener(TogglePause);
            view.resumeButton.onClick.AddListener(ClosePause);
            view.settleReturnButton.onClick.AddListener(OnSettleReturnClicked);
            view.pauseRoot.gameObject.SetActive(false);
            view.settleRoot.gameObject.SetActive(false);
            view.transitionGroup.alpha = 0f;
            view.transitionRoot.gameObject.SetActive(false);

            phase = EPhase.Grind;
            view.grindRoot.gameObject.SetActive(true);
            view.pourRoot.gameObject.SetActive(false);
            ShowPhaseMessage();
            RefreshHud();

            // 整局期间把 BGM 让开一点，好让研磨/冲泡声站到前面来（倍率配在 View 上）。
            // 压低是**整局**而不是"循环音响的时候"——后者会让 BGM 随撞障碍/松手一跳一跳，很难听
            BgmManager.SetDuck(view.bgmDuckFactor);

            running = true;
        }

        private void Update()
        {
            if (!running) return;

            // 暂停闸（2026-08-20）：整局冻住——不推进任何计时、不喂水面（相位停在原地）、
            // 不读输入。循环音要单独掐一次，因为下面那句 UpdateLoopSfx 被 return 跳过了
            if (paused)
            {
                UpdateLoopSfx();
                return;
            }

            float dt = Time.deltaTime;

            // 撞击提示到时后恢复环节说明
            if (messageResetRemaining > 0f)
            {
                messageResetRemaining -= dt;
                if (messageResetRemaining <= 0f) ShowPhaseMessage();
            }

            switch (phase)
            {
                case EPhase.Grind:
                    grind.RelayoutIfResized();
                    // 左下角那颗 ESC 压在页面上，而磨豆读的是裸鼠标：点它的那一下不该顺带切一次环
                    // （切进障碍是要扣分的）。UI 事件与本组件 Update 谁先跑没有保证，所以自己挡一道。
                    // 不用 EventSystem.IsPointerOverGameObject——整页底图都是 raycastTarget，那样会全挡掉
                    if (!IsPointerOver(view.escButton)) grind.HandleInput();
                    grind.Tick(dt);
                    RefreshHud();
                    if (grind.IsComplete) EnterTransition();
                    break;

                case EPhase.Transition:
                    TickTransition(dt);
                    break;

                case EPhase.Pour:
                    pour.Tick(dt);
                    RefreshHud();
                    if (pour.IsComplete) EnterSettle();
                    break;

                case EPhase.Settle:
                    // ESC 请求的退出在这里兑现（不在收键调用栈里递归弹栈，见 ConsumeEscape）
                    if (settleExitAsked)
                    {
                        Finish();
                        break;
                    }
                    TickSettleIntro(dt);
                    break;
            }

            // 水面是纯表现：磨豆与过场期间 pourRoot 还没上场，不喂；结算展示期间余波继续
            if (phase == EPhase.Pour || phase == EPhase.Settle) TickWater(dt);

            // 放在环节切换之后：磨满的那一帧已经切到冲泡，研磨声当帧就停
            UpdateLoopSfx();
        }

        // ══════════ 环节切换 ══════════

        /// <summary>
        /// 磨豆磨满 → 进过场（2026-08-20 反馈「两个阶段之间没有切换过渡」后加）。
        /// 得分在这一刻就定死：幕布后面磨盘已经不动了。环节根的互换留到幕布全满时做。
        /// </summary>
        private void EnterTransition()
        {
            grindScore = grind.Score;
            phase = EPhase.Transition;
            transitionElapsed = 0f;
            transitionSwapped = false;
            SfxManager.PlayOnce(view.stageClearClip, view.stageClearVolume); // ① 磨豆通关

            view.transitionRoot.gameObject.SetActive(true);
            view.transitionGroup.alpha = 0f;
            if (view.transitionLabel != null) view.transitionLabel.text = PourPhaseTitle;
        }

        /// <summary>
        /// 过场三段：淡入 → 停留 → 淡出。**环节根在幕布全满的那一帧才互换**，玩家看不到硬切。
        /// 整段期间不读输入、不喂水面，两路循环音也都静（UpdateLoopSfx 只认 Grind / Pour）。
        /// 时间走本组件的 dt，所以暂停时过场也跟着停。
        /// </summary>
        private void TickTransition(float dt)
        {
            transitionElapsed += dt;

            float fadeIn = Mathf.Max(0.01f, view.transitionInSeconds);
            float hold = Mathf.Max(0f, view.transitionHoldSeconds);
            float fadeOut = Mathf.Max(0.01f, view.transitionOutSeconds);

            if (transitionElapsed < fadeIn)
            {
                view.transitionGroup.alpha = transitionElapsed / fadeIn;
                return;
            }

            if (!transitionSwapped)
            {
                transitionSwapped = true;
                view.transitionGroup.alpha = 1f;
                view.grindRoot.gameObject.SetActive(false);
                view.pourRoot.gameObject.SetActive(true);
                messageResetRemaining = 0f;
                // 幕布还没退，但 HUD 与提示行当帧就换成冲泡的——两者都只看「不是磨豆」，
                // 所以过场态天然读作冲泡态，幕布退开时底下已经是新环节的样子了
                ShowPhaseMessage();
                RefreshHud();
            }

            float since = transitionElapsed - fadeIn;
            if (since < hold)
            {
                view.transitionGroup.alpha = 1f;
                return;
            }

            float outT = (since - hold) / fadeOut;
            if (outT < 1f)
            {
                view.transitionGroup.alpha = 1f - outT;
                return;
            }

            EnterPour();
        }

        /// <summary>过场走完，正式交给冲泡环节接管输入。</summary>
        private void EnterPour()
        {
            phase = EPhase.Pour;
            view.transitionGroup.alpha = 0f;
            view.transitionRoot.gameObject.SetActive(false);
        }

        private void EnterSettle()
        {
            phase = EPhase.Settle;
            SfxManager.PlayOnce(view.stageClearClip, view.stageClearVolume); // ② 冲泡通关（同时是全局结算）

            // 结算已定，别让玩家手滑把到手的分弃掉
            if (view.abortButton != null) view.abortButton.interactable = false;

            int total = TotalScore();
            if (view.phaseLabel != null) view.phaseLabel.text = "完成！";
            if (view.messageLabel != null)
            {
                view.messageLabel.color = view.messageNormalColor;
                view.messageLabel.text =
                    $"研磨 {grindScore} ＋ 冲泡 {pour.Score}（{pour.GradeName}）＝ {total} 分";
            }
            messageResetRemaining = 0f;
            view.progressFill.anchorMax = new Vector2(1f, 1f);

            // 弹窗当帧就出，不再停留（2026-08-20 反馈「不需要延迟」后去掉）。
            // 上面那行明细文字仍保留：入场淡入的前几帧遮罩还透着，页面读作完成态更顺
            OpenSettlePopup();
        }

        /// <summary>
        /// 打开通关结算弹窗（2026-08-20 按设计图接入）：填得分明细与三栏统计（研磨/冲泡/评级）、
        /// 按总分阈值点星，并从头播入场动画（整体淡入 + 底板与按钮上浮，见 TickSettleIntro）。
        /// 弹窗一开就等玩家：点【ESC 返回】或按 ESC 键才真正 Finish，通关不再自动退出。
        /// </summary>
        private void OpenSettlePopup()
        {
            int total = TotalScore();
            view.settleDetailLabel.text = $"研磨 {grindScore} ＋ 冲泡 {pour.Score} ＝ {total} 分";
            view.settleGrindValue.text = grindScore.ToString();
            view.settlePourValue.text = pour.Score.ToString();
            view.settleGradeValue.text = pour.GradeName;

            int stars = total >= view.settleThreeStarScore ? 3
                : total >= view.settleTwoStarScore ? 2
                : 1; // 没有失败条件，通关就至少给一颗
            for (int i = 0; i < view.settleStars.Length; i++)
                view.settleStars[i].color = i < stars ? Color.white : view.settleStarDimColor;

            // 入场动画从第 0 帧摆好：整体全透、底板与按钮沉到落点下方，再由 TickSettleIntro 推上来。
            // 落点每次开窗现读——Prefab 上的位置是手调的真相源，不在代码里写死
            settleIntroElapsed = 0f;
            settleBoardBasePos = view.settleBoard.anchoredPosition;
            settleButtonBasePos = ((RectTransform)view.settleReturnButton.transform).anchoredPosition;
            view.settleGroup.alpha = 0f;
            view.settleRoot.gameObject.SetActive(true);
            TickSettleIntro(0f);
        }

        /// <summary>
        /// 结算弹窗入场（与二次确认弹窗同观感：淡入 + 上浮，缓出曲线）。
        /// 与过场幕布同例走本组件的 dt 手推，不引 DOTween——本类的时间都自治在 Update 里。
        /// 播完后幂等：底板与按钮停在落点、alpha 停在 1，重复调用无害。
        /// </summary>
        private void TickSettleIntro(float dt)
        {
            settleIntroElapsed += dt;
            float t = Mathf.Clamp01(settleIntroElapsed / Mathf.Max(0.01f, view.settleIntroSeconds));
            float ease = 1f - (1f - t) * (1f - t) * (1f - t); // 缓出三次方：起步快、收尾轻
            view.settleGroup.alpha = ease;
            var lift = new Vector2(0f, view.settleIntroRise * (1f - ease));
            view.settleBoard.anchoredPosition = settleBoardBasePos - lift;
            ((RectTransform)view.settleReturnButton.transform).anchoredPosition =
                settleButtonBasePos - lift;
        }

        /// <summary>弹窗上的【ESC 返回】：分已到手，结算退出（走 onFinish，不是放弃）。</summary>
        private void OnSettleReturnClicked()
        {
            if (!running || phase != EPhase.Settle) return;
            Finish();
        }

        /// <summary>结算：两环节相加。宿主那边还会再 Clamp 一次，这里也守住 0~100 的契约。</summary>
        private void Finish()
        {
            if (!running) return;
            running = false;
            StopAudio();
            var finish = onFinish;
            onFinish = null;
            onAbort = null;
            finish?.Invoke(TotalScore());
        }

        /// <summary>【放弃】：不结算，访客保持「服务中」，再次进入局面重置。结算展示期间无效。</summary>
        private void OnAbortClicked()
        {
            if (!running || phase == EPhase.Settle) return;
            running = false;
            StopAudio();
            var abort = onAbort;
            onFinish = null;
            onAbort = null;
            abort?.Invoke();
        }

        // ══════════ 暂停（页面级：两个环节共用）══════════

        /// <summary>
        /// ESC 语义（2026-08-20 按设计图接入局内暂停）：
        /// <list type="bullet">
        /// <item>弹窗开着 → 关掉它，本次 ESC 被消费，页面不退；</item>
        /// <item>局面进行中 → 打开弹窗，同样消费；</item>
        /// <item>结算弹窗开着（灌满当帧就开）→ 消费掉，请求退出，下一帧照常走 Finish。
        ///   分已经挣到手了，这一下不该把它弄丢
        ///   （置标记而不是当场调 Finish，是为了不在壳的收键调用栈里递归弹栈）。</item>
        /// <item>已经结束 / 放弃过 → 不消费，交回给壳去弹栈关页面。</item>
        /// </list>
        /// 壳侧的转发见 <see cref="MinigameOverlay.ConsumeEscape"/>——ESC 是页面级语义，
        /// 由壳统一收键、逐层问下来，小游戏自己不去读 KeyCode.Escape。
        /// </summary>
        public bool ConsumeEscape()
        {
            if (paused)
            {
                ClosePause();
                return true;
            }
            if (!running) return false;

            if (phase == EPhase.Settle)
            {
                settleExitAsked = true;
                return true;
            }

            // 过场就一秒多，中途弹暂停既没必要、幕布半透时弹窗也难看。吞掉不处理
            if (phase == EPhase.Transition) return true;

            OpenPause();
            return true;
        }

        /// <summary>左下角那颗「ESC 暂停」：与按 ESC 键完全同义，所以直接走同一条路。</summary>
        private void TogglePause() => ConsumeEscape();

        private void OpenPause()
        {
            paused = true;
            view.pauseRoot.gameObject.SetActive(true);
            pour.DropTracking(); // 暂停期间挪的鼠标不该算进冲泡的速度采样（见 DropTracking 注释）
            UpdateLoopSfx();     // 当帧就掐掉两路循环音，别让磨豆声在弹窗上继续响
        }

        private void ClosePause()
        {
            paused = false;
            view.pauseRoot.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (grind != null) grind.Hit -= OnGrindHit;
            if (waterMaterial != null) Destroy(waterMaterial);
            // 页面被壳直接销毁（ESC / 遮罩）时，宿主已经按「关掉页面且不结算」处理，
            // 这里不再补调 onAbort——重复回调会违反「只调一次」的契约；
            // 但音频必须在这里兜住：这条路 Finish/OnAbortClicked 都没走过，
            // 不掐的话循环音与 BGM 压低会跟着玩家回到主界面
            StopAudio();
            running = false;
        }

        private int TotalScore() => Mathf.Clamp(grindScore + pour.Score, 0, 100);

        // ══════════ 界面刷新 ══════════

        private void OnGrindHit()
        {
            // 撞击音的剪辑默认留空（素材还没选定），留空时 PlayOnce 直接返回 = 不响，不报错。
            // 指针本身的「压暗」由 GrindGame 在硬直期间做（pointerStunColor），这里只管声音与文字
            SfxManager.PlayOnce(view.grindHitClip, view.grindHitVolume);

            if (view.messageLabel != null)
            {
                view.messageLabel.color = view.messageWarnColor;
                view.messageLabel.text = $"撞到障碍！－{level.HitScorePenalty} 分";
            }
            messageResetRemaining = view.hitMessageSeconds;
        }

        private void ShowPhaseMessage()
        {
            if (view.messageLabel == null) return;
            view.messageLabel.color = view.messageNormalColor;
            if (phase != EPhase.Grind)
            {
                view.messageLabel.text = "按住左键，在杯内匀速移动，速度越均匀得分越高！";
                return;
            }

            // 磨豆两套操作的说明各一份（关卡的 GrindMode 决定，2026-08-19 试玩）
            view.messageLabel.text = level.GrindMode == EGrindMode.MouseCrank
                ? "按住左键绕圆心顺时针画圈研磨，靠近/远离圆心换轨道，避开红色的珠子！"
                : "点击左键切换轨道，避开红色的珠子！";
        }

        private void RefreshHud()
        {
            float progress = phase == EPhase.Grind ? grind.Progress : pour.Progress;
            view.progressFill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);

            if (view.phaseLabel != null)
                view.phaseLabel.text = phase == EPhase.Grind ? GrindPhaseTitle : PourPhaseTitle;

            // 底卡只有 188 宽（素材原尺寸 ÷ 2.667），这两行都得短——长句放底部提示行
            if (view.scoreLabel != null)
                view.scoreLabel.text = phase == EPhase.Grind
                    ? $"得分 {grind.Score}/{level.GrindMaxScore}"
                    : $"研磨 {grindScore} 分";

            if (view.tuningLabel != null)
            {
                if (phase == EPhase.Pour)
                {
                    var (mean, variance, count) = pour.Stats();
                    view.tuningLabel.text =
                        $"均速 {mean:0.00} ｜ 方差 {variance:0.0000} ｜ 样本 {count}（调参用，杯径/秒）";
                }
                else if (phase == EPhase.Grind && level.GrindMode == EGrindMode.MouseCrank)
                {
                    view.tuningLabel.text =
                        $"磨柄 {(grind.CrankEngaged ? "已握住" : "未握住（按下左键开始摇）")} ｜ " +
                        $"进度 {grind.Progress:P0}（磨满 = 净顺时针 {level.CrankTotalDegrees:0}°）";
                }
                else
                {
                    view.tuningLabel.text = string.Empty;
                }
            }
        }

        // ══════════ 音效 ══════════

        /// <summary>
        /// 两路循环音（2026-08-20 拍板）：
        /// - **研磨音**：磨豆环节一直响，只在撞障碍的硬直期间停——「进度条还在推进就继续响」；
        /// - **冲泡音**：只在按住左键且鼠标在杯内时响（PourGame.IsPouring，与进度增长同一条件），
        ///   松手或滑出杯即停。
        ///
        /// 硬起硬停不做淡入淡出、再响从头播（同日拍板）。逐帧调用即可：
        /// SfxManager.SetLoop 对同状态是空操作，本类不必自己记上一帧。
        /// 剪辑留空（View 上没配）时 SetLoop 直接返回，等于该环节静音，不报错。
        /// </summary>
        private void UpdateLoopSfx()
        {
            bool grinding = running && !paused && phase == EPhase.Grind && !grind.IsStunned;
            bool pouring = running && !paused && phase == EPhase.Pour && pour.IsPouring;
            SfxManager.SetLoop(view.grindLoopClip, grinding, view.grindLoopVolume);
            SfxManager.SetLoop(view.pourLoopClip, pouring, view.pourLoopVolume);
        }

        /// <summary>收尾：掐掉两路循环音、把 BGM 还回去。结束 / 放弃 / 被销毁三条路都要走，重复调用无害。</summary>
        private void StopAudio()
        {
            if (view != null)
            {
                SfxManager.SetLoop(view.grindLoopClip, false);
                SfxManager.SetLoop(view.pourLoopClip, false);
            }
            BgmManager.SetDuck(1f);
        }

        // ══════════ 水面表现 ══════════

        /// <summary>
        /// 水面材质运行时创建（Prefab 不挂材质资产，同 HubSceneBinder 的延时序列做法）。
        /// shader 缺失只失去特效、照常游玩——它不是布局，不走 §16.2 的缺件即中止。
        /// </summary>
        private void SetupWater()
        {
            var shader = Resources.Load<Shader>("Shaders/UIWater");
            if (shader == null)
            {
                Debug.LogWarning("[制作咖啡] 水面 shader 缺失（Resources/Shaders/UIWater），本局不显示水面");
                view.waterImage.gameObject.SetActive(false);
                return;
            }

            waterMaterial = new Material(shader);
            waterMaterial.SetColor("_WaterColor", view.waterColor);
            waterMaterial.SetColor("_RippleColor", view.waterRippleColor);
            // 这两个原来只在 shader 里给默认值，调不了。2026-08-20 反馈「水波不够明显」后提到 View 上，
            // 因为它们正是决定明显程度的两把尺（环带越厚越容易叠亮，边缘幅度越大晃得越看得见）
            waterMaterial.SetFloat(WaterRingThicknessId, view.waterRingThickness);
            waterMaterial.SetFloat(WaterEdgeWobbleId, view.waterEdgeWobble);

            // 进度环：形状参数一次设定，进度值逐帧喂（见 TickWater）
            waterMaterial.SetColor("_ProgressColor", view.waterProgressColor);
            waterMaterial.SetFloat("_ProgressWidth", view.waterProgressWidth);
            waterMaterial.SetFloat("_ProgressInset", view.waterProgressInset);
            waterMaterial.SetFloat("_ProgressTrackAlpha", view.waterProgressTrackAlpha);
            waterMaterial.SetFloat(WaterProgressId, 0f);
            // 满杯底图 + 液面常驻满（2026-08-20 拍板）：0.5 是 uv 半径的上限，设一次此后不再改。
            // 进度改由 HUD 的进度条表达；这一层只剩波纹与边缘晃动
            waterMaterial.SetFloat(WaterFillRadiusId, 0.5f);
            view.waterImage.gameObject.SetActive(true);
            view.waterImage.material = waterMaterial;

            wobblePhase = 0f;
            wobbleAmp = view.waterWobbleAmpIdle;
            wasPouring = false;
            ringTimer = 0f;
            ringCursor = 0;
            for (int i = 0; i < WaterRingSlots; i++)
            {
                ringStrength[i] = 0f;
                ringUpload[i] = Vector4.zero;
            }
            waterMaterial.SetVectorArray(WaterRingsId, ringUpload);
        }

        /// <summary>
        /// 逐帧喂水面材质（俯视·尾迹，2026-08-17 访谈拍板后同日改为船尾波观感）：
        /// - 液面半径随进度扩展；
        /// - 边缘晃动：速度由最近一段的速度方差线性归一映射（手越抖晃越快），幅度由倒水状态阻尼趋近；
        /// - 尾迹：按下瞬间冒落水水花；倒水期间按固定高频间隔在倒水点冒微弱的细波元，
        ///   每个波元记住出生点、自行扩散变淡。拖动比波元扩散快时，波元包络自动叠出
        ///   船尾那样的 V 形尾迹（开尔文尾迹的成因），原地不动则是同点持续搅动的光斑；
        ///   松手不再冒新波元，旧波元飘完即静。
        /// 相位不用 shader 的 _Time——时间统一走本组件的 dt，暂停时水面跟着停；
        /// 单局时长有限，相位不做回绕（float 精度在这个量级绰绰有余）。
        /// </summary>
        private void TickWater(float dt)
        {
            if (waterMaterial == null) return;

            bool pouring = phase == EPhase.Pour && pour.IsPouring;

            // ① 边缘晃动：方差 → 晃动速度（线性归一），倒水状态 → 幅度（指数趋近，帧率无关）
            float variance = pour.RecentVariance(view.waterVarianceWindowSeconds);
            float unrest = Mathf.Clamp01(variance / Mathf.Max(1e-4f, view.waterVarianceNormalizer));
            wobblePhase += Mathf.Lerp(view.waterWobbleSpeedMin, view.waterWobbleSpeedMax, unrest) * dt;
            float ampTarget = pouring ? view.waterWobbleAmpPouring : view.waterWobbleAmpIdle;
            wobbleAmp = Mathf.Lerp(ampTarget, wobbleAmp, Mathf.Exp(-view.waterWaveDamping * dt));

            // ② 尾迹：落水水花（按下/回杯瞬间）＋ 高频波元（固定时间节奏，原地不动也冒）
            if (pouring && !wasPouring)
            {
                SpawnRing(view.waterSplashStrength);
                ringTimer = view.waterWakeSpawnInterval; // 水花已即时反馈，波元从下个间隔起算
            }
            else if (pouring)
            {
                ringTimer -= dt;
                if (ringTimer <= 0f)
                {
                    SpawnRing(view.waterWakeStrength);
                    ringTimer += Mathf.Max(0.01f, view.waterWakeSpawnInterval);
                }
            }
            wasPouring = pouring;

            // 波元老化：半径线性长，强度按剩余寿命平方衰减（先快后慢地淡出）
            float life = Mathf.Max(0.1f, view.waterWakeLifetime);
            for (int i = 0; i < WaterRingSlots; i++)
            {
                ringAge[i] += dt;
                float remain = 1f - ringAge[i] / life;
                float fade = remain > 0f ? ringStrength[i] * remain * remain : 0f;
                ringUpload[i] = new Vector4(
                    ringCenter[i].x, ringCenter[i].y, ringAge[i] * view.waterWakeWaveSpeed, fade);
            }

            // 液面半径不再喂：底图已是满杯，_FillRadius 在 SetupWater 里就设到上限了。
            // 进度改由杯壁内侧那一圈进度环表达（结算展示期间进度已满，环是整圈的）
            waterMaterial.SetFloat(WaterProgressId, Mathf.Clamp01(pour.Progress));
            waterMaterial.SetFloat(WaterWobblePhaseId, wobblePhase);
            waterMaterial.SetFloat(WaterWobbleAmpId, wobbleAmp);
            waterMaterial.SetVectorArray(WaterRingsId, ringUpload);
        }

        /// <summary>在当前倒水点冒一个新波元。槽位循环复用：满了就顶掉最老的（正常节奏刚好用不满）。</summary>
        private void SpawnRing(float strength)
        {
            ringCenter[ringCursor] = pour.PourPointUv;
            ringAge[ringCursor] = 0f;
            ringStrength[ringCursor] = strength;
            ringCursor = (ringCursor + 1) % WaterRingSlots;
        }

        // ══════════ 杂项 ══════════

        /// <summary>鼠标当前是否压在某个控件上（用来把裸鼠标输入从 UI 上让开）。</summary>
        private bool IsPointerOver(Component target) =>
            target != null && RectTransformUtility.RectangleContainsScreenPoint(
                (RectTransform)target.transform, Input.mousePosition, uiCamera);

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
            if (view.grindRoot == null) missing.Add(nameof(view.grindRoot));
            if (view.pourRoot == null) missing.Add(nameof(view.pourRoot));
            if (view.grindBackground == null) missing.Add(nameof(view.grindBackground));
            if (view.grindArea == null) missing.Add(nameof(view.grindArea));
            if (view.grindContentRoot == null) missing.Add(nameof(view.grindContentRoot));
            if (view.obstacleBeadTemplate == null) missing.Add(nameof(view.obstacleBeadTemplate));
            if (view.pointer == null) missing.Add(nameof(view.pointer));
            if (view.pourBackground == null) missing.Add(nameof(view.pourBackground));
            if (view.cupArea == null) missing.Add(nameof(view.cupArea));
            if (view.waterImage == null) missing.Add(nameof(view.waterImage));
            if (view.progressFill == null) missing.Add(nameof(view.progressFill));
            if (view.escButton == null) missing.Add(nameof(view.escButton));
            if (view.pauseRoot == null) missing.Add(nameof(view.pauseRoot));
            if (view.resumeButton == null) missing.Add(nameof(view.resumeButton));
            if (view.transitionRoot == null) missing.Add(nameof(view.transitionRoot));
            if (view.transitionGroup == null) missing.Add(nameof(view.transitionGroup));
            if (view.abortButton == null) missing.Add(nameof(view.abortButton));
            if (view.settleRoot == null) missing.Add(nameof(view.settleRoot));
            if (view.settleGroup == null) missing.Add(nameof(view.settleGroup));
            if (view.settleBoard == null) missing.Add(nameof(view.settleBoard));
            if (view.settleReturnButton == null) missing.Add(nameof(view.settleReturnButton));
            if (view.settleDetailLabel == null) missing.Add(nameof(view.settleDetailLabel));
            if (view.settleGrindValue == null) missing.Add(nameof(view.settleGrindValue));
            if (view.settlePourValue == null) missing.Add(nameof(view.settlePourValue));
            if (view.settleGradeValue == null) missing.Add(nameof(view.settleGradeValue));
            // 数组要整包校验：空数组或任一空槽都算缺（用 Unity 的 == 判空，兜住失引用的假 null）
            var starsOk = view.settleStars != null && view.settleStars.Length > 0;
            if (starsOk)
                foreach (var star in view.settleStars)
                    if (star == null) starsOk = false;
            if (!starsOk) missing.Add(nameof(view.settleStars));
            if (missing.Count == 0) return true;

            // 2026-08-20 换 2.0 版式后，缺的多半是整页改版带来的新节点——那是「补齐缺失」补不出来的，
            // 所以这里直接指路重建，别再让人先去点一遍没用的菜单
            var is2point0 = view.pourBackground == null || view.grindBackground == null ||
                            view.obstacleBeadTemplate == null || view.escButton == null ||
                            view.pauseRoot == null || view.resumeButton == null ||
                            view.transitionRoot == null;
            Debug.LogError($"[制作咖啡] Prefab 缺少必需的布局引用：{string.Join("、", missing)}。" +
                           (is2point0
                               ? "这些是 2.0 版式（整屏底图 / ESC 暂停 / 暂停弹窗）的新节点，" +
                                 "「补齐缺失」补不出来——请执行菜单 " +
                                 "MasterHouse → 小游戏 → 重建制作咖啡 Prefab（覆盖手调）"
                               : "请执行菜单 MasterHouse → 小游戏 → 创建制作咖啡资产（补齐缺失）；" +
                                 "仍缺再重建（覆盖手调）"), gameObject);
            return false;
        }
    }
}

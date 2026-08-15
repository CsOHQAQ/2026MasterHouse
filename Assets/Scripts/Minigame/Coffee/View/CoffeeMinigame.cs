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
    /// 流程：磨豆子（环上避障，上限 50 分）→ 冲咖啡（匀速移动，三档 50/30/20 分）
    /// → 结算展示片刻 → onFinish(两环节相加)。没有失败条件；【放弃】/ESC 走 onAbort，重开全重置。
    /// </summary>
    [RequireComponent(typeof(CoffeeMinigameView))]
    public sealed class CoffeeMinigame : MonoBehaviour, IMinigame
    {
        private enum EPhase { Grind, Pour, Settle }

        private CoffeeMinigameView view;
        private CoffeeLevelDef level;
        private GrindGame grind;
        private PourGame pour;

        private EPhase phase;
        private int grindScore;
        private float settleRemaining;
        private float messageResetRemaining;

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

            grind = new GrindGame(view, level);
            grind.Hit += OnGrindHit;
            grind.Init();
            pour = new PourGame(view, level, ResolveUiCamera());

            if (view.abortButton != null) view.abortButton.onClick.AddListener(OnAbortClicked);

            phase = EPhase.Grind;
            view.grindRoot.gameObject.SetActive(true);
            view.pourRoot.gameObject.SetActive(false);
            ShowPhaseMessage();
            RefreshHud();

            running = true;
        }

        private void Update()
        {
            if (!running) return;
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
                    grind.HandleInput();
                    grind.Tick(dt);
                    RefreshHud();
                    if (grind.IsComplete) EnterPour();
                    break;

                case EPhase.Pour:
                    pour.Tick(dt);
                    RefreshHud();
                    if (pour.IsComplete) EnterSettle();
                    break;

                case EPhase.Settle:
                    settleRemaining -= dt;
                    if (settleRemaining <= 0f) Finish();
                    break;
            }
        }

        // ══════════ 环节切换 ══════════

        private void EnterPour()
        {
            grindScore = grind.Score;
            phase = EPhase.Pour;
            view.grindRoot.gameObject.SetActive(false);
            view.pourRoot.gameObject.SetActive(true);
            messageResetRemaining = 0f;
            ShowPhaseMessage();
            RefreshHud();
        }

        private void EnterSettle()
        {
            phase = EPhase.Settle;
            settleRemaining = Mathf.Max(0f, view.settleShowSeconds);

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
        }

        /// <summary>结算：两环节相加。宿主那边还会再 Clamp 一次，这里也守住 0~100 的契约。</summary>
        private void Finish()
        {
            if (!running) return;
            running = false;
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
            var abort = onAbort;
            onFinish = null;
            onAbort = null;
            abort?.Invoke();
        }

        private void OnDestroy()
        {
            if (grind != null) grind.Hit -= OnGrindHit;
            // 页面被壳直接销毁（ESC / 遮罩）时，宿主已经按「关掉页面且不结算」处理，
            // 这里不再补调 onAbort——重复回调会违反「只调一次」的契约
            running = false;
        }

        private int TotalScore() => Mathf.Clamp(grindScore + pour.Score, 0, 100);

        // ══════════ 界面刷新 ══════════

        private void OnGrindHit()
        {
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
            view.messageLabel.text = phase == EPhase.Grind
                ? "点击左键切换圆环，避开红色障碍，磨满进度条"
                : "按住左键，在杯内匀速移动——越匀速档位越高";
        }

        private void RefreshHud()
        {
            float progress = phase == EPhase.Grind ? grind.Progress : pour.Progress;
            view.progressFill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);

            if (view.phaseLabel != null)
                view.phaseLabel.text = phase == EPhase.Grind ? "① 磨豆子" : "② 冲咖啡";

            if (view.scoreLabel != null)
                view.scoreLabel.text = phase == EPhase.Grind
                    ? $"研磨得分 {grind.Score}/{level.GrindMaxScore}"
                    : $"研磨 {grindScore} ｜ 冲泡按匀速程度结算";

            if (view.tuningLabel != null)
            {
                if (phase == EPhase.Pour)
                {
                    var (mean, variance, count) = pour.Stats();
                    view.tuningLabel.text =
                        $"均速 {mean:0.00} ｜ 方差 {variance:0.0000} ｜ 样本 {count}（调参用，杯径/秒）";
                }
                else
                {
                    view.tuningLabel.text = string.Empty;
                }
            }
        }

        // ══════════ 杂项 ══════════

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
            if (view.grindArea == null) missing.Add(nameof(view.grindArea));
            if (view.grindContentRoot == null) missing.Add(nameof(view.grindContentRoot));
            if (view.grindDotTemplate == null) missing.Add(nameof(view.grindDotTemplate));
            if (view.pointer == null) missing.Add(nameof(view.pointer));
            if (view.cupArea == null) missing.Add(nameof(view.cupArea));
            if (view.cupImage == null) missing.Add(nameof(view.cupImage));
            if (view.progressFill == null) missing.Add(nameof(view.progressFill));
            if (missing.Count == 0) return true;

            Debug.LogError($"[制作咖啡] Prefab 缺少必需的布局引用：{string.Join("、", missing)}。" +
                           $"请执行菜单 MasterHouse → 小游戏 → 重建制作咖啡 Prefab（会覆盖手调）", gameObject);
            return false;
        }
    }
}

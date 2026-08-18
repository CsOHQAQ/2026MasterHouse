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

        private static readonly int WaterFillRadiusId = Shader.PropertyToID("_FillRadius");
        private static readonly int WaterWobblePhaseId = Shader.PropertyToID("_WobblePhase");
        private static readonly int WaterWobbleAmpId = Shader.PropertyToID("_WobbleAmp");
        private static readonly int WaterRingsId = Shader.PropertyToID("_Rings");

        /// <summary>与 shader 的 RING_SLOTS 一致；默认间隔 0.03s × 寿命 0.9s ≈ 30 个并存 + 水花，32 够用</summary>
        private const int WaterRingSlots = 32;

        private EPhase phase;
        private int grindScore;
        private float settleRemaining;
        private float messageResetRemaining;

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
            var uiCamera = ResolveUiCamera();
            grind = new GrindGame(view, level, uiCamera);
            grind.Hit += OnGrindHit;
            grind.Init();
            pour = new PourGame(view, level, uiCamera);
            SetupWater();

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

            // 水面是纯表现：磨豆阶段 pourRoot 未激活，不喂；结算展示期间余波继续
            if (phase != EPhase.Grind) TickWater(dt);
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
            if (waterMaterial != null) Destroy(waterMaterial);
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
            if (phase != EPhase.Grind)
            {
                view.messageLabel.text = "按住左键，在杯内匀速移动——越匀速档位越高";
                return;
            }

            // 磨豆两套操作的说明各一份（关卡的 GrindMode 决定，2026-08-19 试玩）
            view.messageLabel.text = level.GrindMode == EGrindMode.MouseCrank
                ? "按住左键绕圆心顺时针画圈研磨；靠近/远离圆心换内外环，避开红色障碍"
                : "点击左键切换圆环，避开红色障碍，磨满进度条";
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
            waterMaterial.SetFloat(WaterFillRadiusId, 0f);
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

            // 半径取进度的开方：液面面积 ∝ 半径²，开方后面积随进度线性长，观感上是匀速灌满
            waterMaterial.SetFloat(WaterFillRadiusId, 0.5f * Mathf.Sqrt(Mathf.Clamp01(pour.Progress)));
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
            if (view.waterImage == null) missing.Add(nameof(view.waterImage));
            if (view.progressFill == null) missing.Add(nameof(view.progressFill));
            if (missing.Count == 0) return true;

            Debug.LogError($"[制作咖啡] Prefab 缺少必需的布局引用：{string.Join("、", missing)}。" +
                           $"请先执行菜单 MasterHouse → 小游戏 → 创建制作咖啡资产（补齐缺失，会给老 Prefab 补新节点）；" +
                           $"仍缺再重建（覆盖手调）", gameObject);
            return false;
        }
    }
}

using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 小游戏宿主（小游戏说明 §3.4）：压进 HouseUIManager 的叠加层栈，与 DialogueOverlay 同例。
    ///
    /// 它是**唯一**认识两侧的人——上认 VisitorManager 与闸门，下认 IMinigame 与 MinigameDef。
    /// 具体小游戏透过 IMinigame 与它对话，因此永远不知道自己活在一个经营游戏里（§3.1）。
    ///
    /// ══ 两处退栈顺序是本类最容易写错的地方，都已踩过 ══
    ///
    /// **入口侧（本轮新发现，说明文档 §3.4 原文只写了出口侧）**：
    /// StartMinigameAction 是在 DialogueManager.ChooseOption 内执行的，此时对话层还在栈顶、
    /// IsPlaying 仍为 true。若当场 PushOverlay 开小游戏，紧接着的
    /// Finish() → PlaybackEnded → DialogueOverlay.CloseFromPlaybackEnded → PopOverlay
    /// 弹掉的会是**栈顶的小游戏层**，对话层反而留下。
    /// 所以事件只调 Request 登记意图，真正打开由 HubPage 在 PlaybackEnded 之后调 ConsumePending。
    ///
    /// **出口侧（照抄交付页的教训）**：
    /// CompleteNeed 内部会同步请求【完成服务】对话、进而压入对话层。所以必须
    /// **先弹栈关掉小游戏页、再调业务**，否则那次 PopOverlay 弹掉的会是刚压进来的对话框。
    /// 两步在同一调用栈内完成，tick 插不进来。
    /// </summary>
    public sealed class MinigameOverlay : IHouseOverlay
    {
        /// <summary>当前打开的小游戏层；null = 没开。</summary>
        private static MinigameOverlay current;

        /// <summary>待打开的请求（入口侧延后打开，见类注释）。Def 非空即表示有待处理请求。</summary>
        private static PendingRequest pending;

        private struct PendingRequest
        {
            public MinigameDef Def;
            public int VisitorInstanceId;
            public string NeedId;

            /// <summary>需求点名的关卡；null = 回落关卡池抽取（§8.4）。</summary>
            public MinigameLevelDef FixedLevel;
        }

        private readonly HouseUIManager ui;
        private readonly GameObject instance;
        private readonly MinigameDef def;
        private readonly int visitorInstanceId;

        /// <summary>本局的小游戏实现。只用来把 ESC 往下问一层（见 ConsumeEscape），不碰别的。</summary>
        private readonly IMinigame game;

        /// <summary>已经收到过 onFinish：契约要求「只调一次」，但宿主不信任实现方（§11.4）。</summary>
        private bool settled;

        private bool closing;

        // ── 开局教程图（2026-08-22 一轮测试改进 #2）──

        /// <summary>本次运行内已经弹过教程图的关卡（按关卡记；不持久化，存档接入是待定 #9）。</summary>
        private static readonly System.Collections.Generic.HashSet<MinigameLevelDef> tutorialShown =
            new System.Collections.Generic.HashSet<MinigameLevelDef>();

        /// <summary>教程图遮罩实例；非空 = 遮罩开着、Launch 还压着没发。</summary>
        private GameObject tutorial;

        /// <summary>遮罩关闭后要开的那张关卡（教程门只延后 Launch，不改选关结果）。</summary>
        private MinigameLevelDef pendingLaunchLevel;

        private MinigameOverlay(HouseUIManager ui, GameObject instance, MinigameDef def, int visitorInstanceId,
            IMinigame game)
        {
            this.ui = ui;
            this.instance = instance;
            this.def = def;
            this.visitorInstanceId = visitorInstanceId;
            this.game = game;
        }

        public static bool IsOpen => current != null;

        public static bool HasPending => pending.Def != null;

        // ══════════ 入口：登记 → 延后打开 ══════════

        /// <summary>
        /// 登记一次「该开小游戏了」的意图（由 StartMinigameAction 调用）。
        /// **不当场打开**——原因见类注释的入口侧退栈顺序。
        ///
        /// <para><paramref name="fixedLevel"/> 是需求点名的关卡，留 null 则回落关卡池抽取（§8.4）。
        /// 收的是一张关卡资产而不是 NeedDef——宿主继续不认识需求侧的类型，§8.5 的依赖方向不因选关而破。</para>
        /// </summary>
        public static void Request(MinigameDef def, int visitorInstanceId, string needId,
            MinigameLevelDef fixedLevel = null)
        {
            if (def == null) return;
            if (pending.Def != null)
                Debug.LogWarning($"[小游戏] 上一个待打开请求（{pending.Def.DisplayId}）尚未消化就来了新的" +
                                 $"（{def.DisplayId}），按新的算");
            pending = new PendingRequest
            {
                Def = def,
                VisitorInstanceId = visitorInstanceId,
                NeedId = needId,
                FixedLevel = fixedLevel,
            };
        }

        /// <summary>
        /// 消化待打开请求（由 HubPage 在对话播放结束、对话层已退栈之后调用）。
        /// 没有待处理请求时什么都不做。
        /// </summary>
        public static void ConsumePending(HouseUIManager ui)
        {
            if (pending.Def == null) return;
            var request = pending;
            pending = default; // 先清再开：开失败也不能让请求卡在这儿反复重试
            Open(ui, request);
        }

        /// <summary>丢弃待打开请求（页面切走等场景，避免下次进 Hub 冷不丁弹一局出来）。</summary>
        public static void DiscardPending() => pending = default;

        // ══════════ 打开 ══════════

        private static void Open(HouseUIManager ui, PendingRequest request)
        {
            if (ui == null) return;
            if (current != null)
            {
                Debug.LogWarning("[小游戏] 已经有一局开着，忽略本次打开请求");
                return;
            }

            var def = request.Def;
            if (def.prefab == null)
            {
                Debug.LogError($"[小游戏] 「{def.DisplayId}」没有配 Prefab，无法打开（§16.2 不回退代码布局）", def);
                return;
            }

            // 选关（§8.4）：需求点名了就打那一关，没点名才回落确定性抽取
            // ——同一位访客反复进出恒定抽到同一张
            var level = request.FixedLevel;
            if (level == null)
            {
                var visitors = GameManager.Instance != null ? GameManager.Instance.VisitorManager : null;
                var runSeed = visitors != null ? visitors.Data.RunSeed : VisitorManager.DefaultRunSeed;
                level = def.PickLevel(runSeed, request.VisitorInstanceId, request.NeedId);
            }
            if (level == null)
            {
                Debug.LogError($"[小游戏] 「{def.DisplayId}」开不了局：需求没有点名关卡，" +
                               "而它的关卡池也是空的——两者至少要有一个", def);
                return;
            }

            var instance = Object.Instantiate(def.prefab, ui.PageRoot, false);
            instance.name = "MinigameLayer_" + def.DisplayId;
            ((RectTransform)instance.transform).SetAsLastSibling();

            var game = instance.GetComponent<IMinigame>();
            if (game == null)
            {
                Debug.LogError($"[小游戏] 「{def.DisplayId}」的 Prefab 根节点没有挂实现 IMinigame 的组件", def.prefab);
                Object.Destroy(instance);
                return;
            }

            var overlay = new MinigameOverlay(ui, instance, def, request.VisitorInstanceId, game);
            current = overlay;

            // 关营业闸门（§3.4 ①）：时钟停走、访客各类倒计时停表
            SetGate(true);

            HouseUIUtil.ApplyFallbackFont(instance.transform);
            HouseDayLightTint.Attach(instance.transform); // 底图随时钟慢慢变天色
            ui.PushOverlay(overlay);

            // 开局教程门（#2）：关卡配了教程图且本次运行还没看过 → 先盖遮罩，点击任意处才 Launch。
            // 遮罩期间小游戏未开始（不吃输入、不走它自己的时间），闸门在上面已经关了
            if (ShouldShowTutorial(level))
            {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
                tutorialShown.Add(level);
#endif
                overlay.OpenTutorial(level);
                return;
            }

            // 放在压栈之后：小游戏可能在 Launch 里就同步结束（空关卡等极端情况），
            // 那时 HandleFinish 要弹的栈得先存在
            game.Launch(level, overlay.HandleFinish, overlay.HandleAbort);
        }

        // ══════════ 开局教程图（#2）══════════

        /// <summary>
        /// 正式包内同一关卡只弹首次；Editor / Development Build 每次打开都弹，方便策划连续调图。
        /// 这个分支只影响表现门，不改变关卡启动与结算逻辑。
        /// </summary>
        private static bool ShouldShowTutorial(MinigameLevelDef level)
        {
            if (level == null || level.tutorialImage == null) return false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return true;
#else
            return !tutorialShown.Contains(level);
#endif
        }

        /// <summary>盖上教程图遮罩并压住 Launch；点击任意处 / ESC 都走 <see cref="CloseTutorialAndLaunch"/>。</summary>
        private void OpenTutorial(MinigameLevelDef level)
        {
            pendingLaunchLevel = level;
            tutorial = BuildTutorial(level.tutorialImage);
        }

        /// <summary>关掉教程图遮罩，把压着的 Launch 发出去。页面已在关闭途中时只清遮罩不开局。</summary>
        private void CloseTutorialAndLaunch()
        {
            if (tutorial != null) Object.Destroy(tutorial);
            tutorial = null;
            var level = pendingLaunchLevel;
            pendingLaunchLevel = null;
            if (closing || settled || level == null) return;
            game.Launch(level, HandleFinish, HandleAbort);
        }

        /// <summary>
        /// 教程图遮罩是宿主层的运行时表现件（同 Toast 一类，不属于任何页面布局）：
        /// 全屏半透黑底 + 等比居中的教程图 + 底部一行「点击任意处开始」。
        /// 挂在小游戏实例底下、置顶——随实例一起销毁，Close() 不用单独收拾它。
        /// </summary>
        private GameObject BuildTutorial(Sprite sprite)
        {
            var root = new GameObject("MinigameTutorial", typeof(RectTransform), typeof(UnityEngine.UI.Image),
                typeof(UnityEngine.UI.Button));
            root.layer = 5;
            var rect = (RectTransform)root.transform;
            rect.SetParent(instance.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.SetAsLastSibling();

            var scrim = root.GetComponent<UnityEngine.UI.Image>();
            scrim.color = new Color(0f, 0f, 0f, 0.6f);
            scrim.raycastTarget = true; // 教程期间点不到底下的小游戏页

            var button = root.GetComponent<UnityEngine.UI.Button>();
            button.transition = UnityEngine.UI.Selectable.Transition.None;
            button.onClick.AddListener(CloseTutorialAndLaunch);

            var imageGo = new GameObject("Image", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            imageGo.layer = 5;
            var imageRect = (RectTransform)imageGo.transform;
            imageRect.SetParent(rect, false);
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = Vector2.zero;
            imageRect.offsetMax = Vector2.zero;
            var image = imageGo.GetComponent<UnityEngine.UI.Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            image.preserveAspect = true; // 等比缩放居中，不同比例的图不被拉变形

            var hintGo = new GameObject("Hint", typeof(RectTransform), typeof(UnityEngine.UI.Text));
            hintGo.layer = 5;
            var hintRect = (RectTransform)hintGo.transform;
            hintRect.SetParent(rect, false);
            hintRect.anchorMin = new Vector2(0.5f, 0f);
            hintRect.anchorMax = new Vector2(0.5f, 0f);
            hintRect.anchoredPosition = new Vector2(0f, 48f);
            hintRect.sizeDelta = new Vector2(600f, 44f);
            var hint = hintGo.GetComponent<UnityEngine.UI.Text>();
            hint.text = "点击任意处开始";
            hint.fontSize = 26;
            hint.alignment = TextAnchor.MiddleCenter;
            hint.color = new Color(1f, 1f, 1f, 0.85f);
            hint.raycastTarget = false;
            HouseUIUtil.ApplyFallbackFont(rect);
            return root;
        }

        // ══════════ 结束 ══════════

        /// <summary>
        /// 玩家点【完成】：按分数定档 → 先弹栈关页面 → 再调业务结算（顺序见类注释的出口侧）。
        /// </summary>
        private void HandleFinish(int score)
        {
            if (settled || closing) return;
            settled = true;

            var satisfaction = def.Evaluate(Mathf.Clamp(score, 0, 100));

            // ① 先弹栈：Close() 会销毁实例并开闸门
            if (current == this) ui.PopOverlay();

            // ② 再调业务：CompleteNeed 内部会压入【完成服务·档位】对话层，
            //    此时栈顶已经不是小游戏了，那一层压得进来、也弹得对
            var visitors = GameManager.Instance != null ? GameManager.Instance.VisitorManager : null;
            if (visitors == null) return;
            if (!visitors.CompleteNeed(visitorInstanceId, satisfaction))
                Debug.LogWarning($"[小游戏] 结算被拒：访客 #{visitorInstanceId} 已不在「服务中」" +
                                 $"（可能已超时离场）。本局分数 {score} 作废");
        }

        /// <summary>玩家点【放弃】：不结算，访客保持「服务中」，可再次点开重玩（局面会重置）。</summary>
        private void HandleAbort()
        {
            if (settled || closing) return;
            if (current == this) ui.PopOverlay();
        }

        /// <summary>
        /// 由壳在弹栈时调用。三条来路殊途同归——ESC / 点遮罩 / 小游戏自己调 onAbort，
        /// 都是「关掉页面且不结算」，也就是 onAbort 的语义本身，所以这里不需要额外分支。
        /// 唯一要小心的是别把 HandleFinish 的结算重跑一遍：settled 标记挡住了。
        /// </summary>
        /// <summary>
        /// ESC 先问小游戏自己（2026-08-20 加局内暂停）：它可能只是想开/关自己的暂停弹窗。
        /// 不消费才落到壳的默认语义——弹栈 = 关页面且不结算（见 Close 注释）。
        /// </summary>
        public bool ConsumeEscape()
        {
            // 教程图开着：ESC 等价于点击关闭（#2 定案），别把这一下漏给壳去关整页
            if (tutorial != null)
            {
                CloseTutorialAndLaunch();
                return true;
            }
            return game != null && game.ConsumeEscape();
        }

        public void Close()
        {
            if (closing) return;
            closing = true;
            if (current == this) current = null;

            if (instance != null) Object.Destroy(instance);

            // 开闸门放在最后：与 HandleFinish 里紧接着的 CompleteNeed 处在同一调用栈，
            // 中间插不进 tick，所以「先解冻再结算」不会让访客倒计时偷跑
            SetGate(false);
        }

        private static void SetGate(bool active)
        {
            var clock = GameManager.Instance != null ? GameManager.Instance.HouseClockManager : null;
            clock?.SetStopReason(EClockStopReason.Minigame, active);
        }
    }
}

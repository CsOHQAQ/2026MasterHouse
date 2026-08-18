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

        /// <summary>已经收到过 onFinish：契约要求「只调一次」，但宿主不信任实现方（§11.4）。</summary>
        private bool settled;

        private bool closing;

        private MinigameOverlay(HouseUIManager ui, GameObject instance, MinigameDef def, int visitorInstanceId)
        {
            this.ui = ui;
            this.instance = instance;
            this.def = def;
            this.visitorInstanceId = visitorInstanceId;
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

            var overlay = new MinigameOverlay(ui, instance, def, request.VisitorInstanceId);
            current = overlay;

            // 关营业闸门（§3.4 ①）：时钟停走、访客各类倒计时停表
            SetGate(true);

            HouseUIUtil.ApplyFallbackFont(instance.transform);
            HouseDayLightTint.Attach(instance.transform); // 底图随时钟慢慢变天色
            ui.PushOverlay(overlay);

            // 放在压栈之后：小游戏可能在 Launch 里就同步结束（空关卡等极端情况），
            // 那时 HandleFinish 要弹的栈得先存在
            game.Launch(level, overlay.HandleFinish, overlay.HandleAbort);
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

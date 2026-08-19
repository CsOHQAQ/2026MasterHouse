using System;
using DG.Tweening;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 结束今天的全屏过场（黑夜→白天）：当日结算出现时，直接驱动 Hub 中的整栋房子
    /// 从夜晚播放到次日开门时间；玩家确认后只收起结算层，立即揭开已经就绪的新一天。
    /// 不进叠加层栈（ESC 关不掉），透明 Sky Image 只负责挡住底层输入，不再绘制蓝色蒙版。
    /// </summary>
    public static class DayTransitionFx
    {
        /// <summary>
        /// 结束今天：显示结算的同时播放整栋房子的夜转昼，确认后揭开新一天。
        ///
        /// onFinished 在整层淡出并销毁**之后**回调（可空），用于接 demo 结局的感谢试玩页——
        /// 早一步回调会让死层压在结局页上方一帧。
        ///
        /// ⚠ 下面两条 Prefab/视图缺失的早退路径**也必须回调**：报错归报错（§16.2 不回退**布局**），
        /// 但业务路由不能被表现件缺失吞掉，否则玩家永远走不到结局、且再也结束不了 demo。
        /// </summary>
        /// <summary>夜色扫描的起点（分钟）：21:30 入夜；终点取次日实际开门时间。</summary>
        private const float SweepFrom = 21.5f * 60f;
        /// <summary>破晓时整晚扫过去的时长（秒）。</summary>
        private const float SweepSeconds = 3.2f;

        public static void PlayEndDay(HouseUIManager ui, int endedDay, VisitorDaySummary summary, Action onFinished,
            Action<float> cycleDriver = null, Action cycleRelease = null)
        {
            var prefab = Resources.Load<GameObject>(OutGamePrefabResourcePaths.DayTransition);
            if (prefab == null)
            {
                Debug.LogError("[HouseUI] 日出过场 Prefab 缺失，无法播放（§16.2 不回退代码布局）：" + OutGamePrefabResourcePaths.DayTransition);
                onFinished?.Invoke();
                return;
            }
            var instance = UnityEngine.Object.Instantiate(prefab, ui.PageRoot, false);
            instance.name = "DayTransitionLayer";
            var view = instance.GetComponent<OutGameDayTransitionView>();
            if (view == null || view.sky == null || view.glow == null)
            {
                Debug.LogError("[HouseUI] 日出过场 Prefab 缺少视图组件或引用：OutGameDayTransitionView");
                UnityEngine.Object.Destroy(instance);
                onFinished?.Invoke();
                return;
            }
            ((RectTransform)instance.transform).SetAsLastSibling();
            HouseUIUtil.ApplyFallbackFont(instance.transform);

            // 结算信息板套用通用面板皮肤（旧版式）；2.0 底板自带外观，别再盖
            var settleScrim = view.transform.Find("SettleScrim");
            if (settleScrim != null)
            {
                var scrimImage = settleScrim.GetComponent<UnityEngine.UI.Image>();
                if (scrimImage != null && scrimImage.sprite == null) HouseUIUtil.ApplyPanelSkin(scrimImage, .92f, 2.5f);
            }

            SetText(view.dayLabel, $"DAY {endedDay:00}　结算");
            SetText(view.subLabel, "新的一天，开门迎客");
            SetText(view.bodyLabel, summary != null ? BuildBody(summary) : string.Empty);
            SetText(view.hintLabel, "确认后 · 开始新的一天");
            // 结算板 2.0 的三项（2026-08-20 设计图）：客人小费 / 声望值 / 装饰分。
            // 前两项是当日累计；装饰分是全局展示值（当日增量没有单独口径，见 §6.1）
            if (view.tipValue != null && summary != null)
                view.tipValue.text = $"+{summary.TipEarned + summary.CurrencyEarned + summary.DialogueCurrencyEarned:N0}";
            if (view.reputationValue != null && summary != null)
                view.reputationValue.text = $"+{summary.ReputationEarned + summary.DialogueReputationEarned}";
            if (view.decorationValue != null)
                view.decorationValue.text = GameManager.Instance != null
                    ? GameManager.Instance.EconomyManager.DecorationScore.ToString("N0") : "0";

            // 蓝色 Sky/HorizonGlow 只属于旧纯色兜底，本流程始终保持透明。
            // 正常 Hub 路径直接驱动底下真实的整栋房子；分帧仅保留为 cycleDriver 缺失时的容错。
            view.sky.color = Color.clear;
            view.glow.gameObject.SetActive(false);
            if (view.cycleFrames != null) view.cycleFrames.gameObject.SetActive(false);

            Tween backgroundTween = null;
            var cycleReleased = false;
            void ReleaseCycle()
            {
                if (cycleReleased) return;
                cycleReleased = true;
                cycleRelease?.Invoke();
            }

            if (cycleDriver != null)
            {
                cycleDriver(SweepFrom);
                var sweepTo = ResolveSweepTo();
                backgroundTween = DOTween.To(() => SweepFrom, value => cycleDriver(value), sweepTo, SweepSeconds)
                    .SetEase(Ease.InOutSine).SetUpdate(true).SetAutoKill(false).SetLink(instance)
                    .OnKill(ReleaseCycle); // 播完停在晨景；确认或异常销毁时才解除覆盖
            }
            var frames = cycleDriver == null ? LoadCycleFrames() : null;
            var useFrames = frames != null && view.cycleFrames != null;
            if (useFrames)
            {
                view.cycleFrames.gameObject.SetActive(true);
                view.cycleFrames.texture = frames[0];
                view.sky.enabled = false;
                view.glow.enabled = false;
                var shown = 0;
                backgroundTween = DOTween.To(() => 0f, value =>
                {
                    var frame = Mathf.Min((int)value, frames.Length - 1);
                    if (frame == shown) return;
                    shown = frame;
                    view.cycleFrames.texture = frames[frame];
                }, frames.Length - 1, (frames.Length - 1) / CycleFps)
                    .SetEase(Ease.Linear).SetUpdate(true).SetLink(instance); // 只播一遍，停在最后一帧（2026-08-16 用户定案）
            }
            else if (cycleDriver == null)
            {
                // 表现资源缺失不能卡住业务；底下时钟已经在次日开门时间，透明显示即可。
                ReleaseCycle();
            }

            var group = HouseUIUtil.Group(instance, 0);
            group.blocksRaycasts = true;
            SfxManager.Play(ESfx.PageTransition);

            // 结算板淡入时，背景的房屋夜转昼已经同步开始播放。
            var nightIn = DOTween.Sequence().SetUpdate(true).SetLink(instance);
            nightIn.Append(group.DOFade(1, .5f).SetEase(Ease.InOutSine));
            if (view.dayLabel != null) nightIn.Insert(.35f, view.dayLabel.DOFade(1, .4f));
            if (view.bodyLabel != null) nightIn.Insert(.55f, view.bodyLabel.DOFade(1, .45f));
            if (view.hintLabel != null) nightIn.Insert(1f, view.hintLabel.DOFade(1, .4f));

            var confirmed = false;
            void Confirm()
            {
                if (confirmed || instance == null) return;
                confirmed = true;
                if (view.settleConfirm != null) view.settleConfirm.interactable = false;
                SfxManager.Play(ESfx.UiClick);
                nightIn.Kill(true); // 入场段若未播完，快进到位再接收尾

                // 玩家可以随时确认；若夜转昼尚未走完，直接快进到次日开门帧再揭开页面。
                if (backgroundTween != null && backgroundTween.IsActive()) backgroundTween.Complete();
                ReleaseCycle();
                if (backgroundTween != null && backgroundTween.IsActive()) backgroundTween.Kill();
                RevealNewDay(instance, view, group, onFinished);
            }
            if (view.settleConfirm != null)
            {
                view.settleConfirm.onClick.AddListener(Confirm);
            }
            else
            {
                // 旧 Prefab 缺少明确确认按钮时才允许点击透明挡板继续，避免业务被表现件卡死。
                var clickGraphic = useFrames ? (UnityEngine.UI.Graphic)view.cycleFrames : view.sky;
                var clickButton = clickGraphic.gameObject.AddComponent<UnityEngine.UI.Button>();
                clickButton.transition = UnityEngine.UI.Selectable.Transition.None;
                clickButton.targetGraphic = clickGraphic;
                clickButton.onClick.AddListener(Confirm);
            }
        }

        /// <summary>分帧序列播放帧率（分帧脚本按 12fps 抽帧，同步改）。</summary>
        private const float CycleFps = 12f;

        private static Texture2D[] cycleFrames;

        /// <summary>加载日夜交替分帧序列（按帧名排序，缓存复用）；目录为空返回 null。</summary>
        private static Texture2D[] LoadCycleFrames()
        {
            if (cycleFrames != null && cycleFrames.Length > 0) return cycleFrames;
            var loaded = Resources.LoadAll<Texture2D>("OutGameUI/DayCycle");
            if (loaded == null || loaded.Length == 0) return null;
            System.Array.Sort(loaded, (a, b) => string.CompareOrdinal(a.name, b.name));
            cycleFrames = loaded;
            return cycleFrames;
        }

        /// <summary>扫描终点与时钟实际开门分钟一致，解除覆盖时不会从 6:00 突跳到配置的开门时间。</summary>
        private static float ResolveSweepTo()
        {
            var clock = GameManager.Instance != null ? GameManager.Instance.HouseClockManager : null;
            var openMinute = clock != null ? clock.OpenMinute : 8 * 60;
            return 24f * 60f + openMinute;
        }

        /// <summary>确认后只收起结算层；房屋动画已在结算出现时完成或被快进到次日开门帧。</summary>
        private static void RevealNewDay(GameObject instance, OutGameDayTransitionView view, CanvasGroup group,
            Action onFinished)
        {
            var seq = DOTween.Sequence().SetUpdate(true).SetLink(instance);
            if (view.settleBoard != null)
            {
                var boardGroup = HouseUIUtil.Group(view.settleBoard.gameObject);
                seq.Join(boardGroup.DOFade(0, .22f));
            }
            if (view.bodyLabel != null) seq.Join(view.bodyLabel.DOFade(0, .2f));
            if (view.hintLabel != null) seq.Join(view.hintLabel.DOFade(0, .18f));
            seq.Join(group.DOFade(0, .35f).SetEase(Ease.InOutSine));
            seq.OnComplete(() =>
            {
                if (instance != null) UnityEngine.Object.Destroy(instance);
                onFinished?.Invoke(); // 销毁之后再回调，免得死层压在结局页上方一帧
            });
        }

        /// <summary>设文案并把初始透明度归零（各文字都由时间轴自己淡入）。</summary>
        private static void SetText(UnityEngine.UI.Text label, string value)
        {
            if (label == null) return;
            label.text = value;
            label.color = new Color(label.color.r, label.color.g, label.color.b, 0);
        }

        /// <summary>
        /// 当日结算正文（自退役的 DaySettleOverlay 迁入）：只展示不结算，钱在当时就已逐次入账。
        /// 「服务奖励」与「客人小费」分两行（家具库存说明 §6.3）——合成一个数的话玩家看不出
        /// 「装修给我多赚了多少」，装饰分那条循环就等于不存在。
        /// 「声望损失」一项已随拒绝惩罚移除删除（§6.4）。
        /// </summary>
        private static string BuildBody(VisitorDaySummary summary)
        {
            // 对话奖励多数日子为 0，只在有变动时占一行；净值可正可负，带符号显示
            var dialogueLine = string.Empty;
            if (summary.DialogueCurrencyEarned != 0 || summary.DialogueReputationEarned != 0)
            {
                dialogueLine = "对话奖励　";
                if (summary.DialogueCurrencyEarned != 0)
                    dialogueLine += $"<color=#D4A46B>货币 {summary.DialogueCurrencyEarned:+0;-0}</color>　";
                if (summary.DialogueReputationEarned != 0)
                    dialogueLine += $"<color=#74D8D1>声望 {summary.DialogueReputationEarned:+0;-0}</color>";
                dialogueLine += "\n";
            }
            return
                $"完成服务　{summary.ServedTotal} 位" +
                $"（完美 {summary.ServedBySatisfaction[(int)EServeSatisfaction.Perfect]}" +
                $" · 满意 {summary.ServedBySatisfaction[(int)EServeSatisfaction.Satisfied]}" +
                $" · 一般 {summary.ServedBySatisfaction[(int)EServeSatisfaction.Plain]}" +
                $" · 不对味 {summary.ServedBySatisfaction[(int)EServeSatisfaction.Mismatch]}）\n" +
                $"拒绝 / 超时　{summary.RefusedCount} 位\n" +
                $"闲逛后离场　{summary.WanderDepartCount} 位 · 跨天留宿 {summary.StayOvernightCount} 位\n\n" +
                $"<color=#D4A46B>服务奖励 +{summary.CurrencyEarned:N0}</color>　" +
                $"<color=#D4A46B>客人小费 +{summary.TipEarned:N0}</color>　" +
                $"<color=#74D8D1>声望 +{summary.ReputationEarned}</color>\n" +
                dialogueLine +
                "<size=14>以上均为当日逐次结算的累计，日结不重复计算。</size>";
        }
    }
}

using System;
using DG.Tweening;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 结束今天的全屏过场（黑夜→白天，2026-08-14）：确认结束后入夜盖屏，
    /// 夜幕上直接亮出当日结算（原独立结算面板 DaySettleOverlay 已并入本过场退役），
    /// 玩家点击任意处 → 标题切成新一天、夜空推成晨色、地平线光晕转暖上升 → 淡出揭开新的一天。
    /// 不进叠加层栈（ESC 关不掉），播放期间靠夜空 Image 挡住全部点击；播完自毁。
    /// Prefab 存入夜静态状态，这里只做颜色/位移/透明度推移（表现，不碰布局）。
    /// </summary>
    public static class DayTransitionFx
    {
        // 破晓配色：夜色存在 Prefab 里，晨色是表现参数放代码
        private static readonly Color DawnSky = new Color(.29f, .35f, .55f, 1f);
        private static readonly Color DawnGlow = new Color(.94f, .63f, .38f, .9f);

        /// <summary>
        /// 结束今天：入夜 + 夜幕结算 + 点击破晓。时间此刻已跳到次日开门。
        ///
        /// onFinished 在整层淡出并销毁**之后**回调（可空），用于接 demo 结局的感谢试玩页——
        /// 早一步回调会让死层压在结局页上方一帧。
        ///
        /// ⚠ 下面两条 Prefab/视图缺失的早退路径**也必须回调**：报错归报错（§16.2 不回退**布局**），
        /// 但业务路由不能被表现件缺失吞掉，否则玩家永远走不到结局、且再也结束不了 demo。
        /// </summary>
        public static void PlayEndDay(HouseUIManager ui, int endedDay, VisitorDaySummary summary, Action onFinished)
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

            // 结算信息板套用通用面板皮肤（2026-08-16：与 Hub 各卡片同一套 common 框，替掉纯黑底）
            var settleScrim = view.transform.Find("SettleScrim");
            if (settleScrim != null)
            {
                var scrimImage = settleScrim.GetComponent<UnityEngine.UI.Image>();
                if (scrimImage != null) HouseUIUtil.ApplyPanelSkin(scrimImage, .92f, 2.5f);
            }

            SetText(view.dayLabel, $"DAY {endedDay:00} 结算");
            SetText(view.subLabel, "新的一天，开门迎客");
            SetText(view.bodyLabel, summary != null ? BuildBody(summary) : string.Empty);
            SetText(view.hintLabel, "点击任意处 · 开始新的一天");

            // 日夜交替分帧背景（2026-08-14）：有帧素材就循环播放绘本动画，纯色夜空/光晕退场；
            // 无素材（尚未导入）时回落到原来的纯色入夜表现
            var frames = LoadCycleFrames();
            var useFrames = frames != null && view.cycleFrames != null;
            if (useFrames)
            {
                view.cycleFrames.gameObject.SetActive(true);
                view.cycleFrames.texture = frames[0];
                view.sky.enabled = false;
                view.glow.enabled = false;
                var shown = 0;
                DOTween.To(() => 0f, value =>
                {
                    var frame = Mathf.Min((int)value, frames.Length - 1);
                    if (frame == shown) return;
                    shown = frame;
                    view.cycleFrames.texture = frames[frame];
                }, frames.Length - 1, (frames.Length - 1) / CycleFps)
                    .SetEase(Ease.Linear).SetUpdate(true).SetLink(instance); // 只播一遍，停在最后一帧（2026-08-16 用户定案）
            }

            var group = HouseUIUtil.Group(instance, 0);
            group.blocksRaycasts = true;
            SfxManager.Play(ESfx.PageTransition);

            // 第一段：盖屏亮出结算，然后停住等点击
            var nightIn = DOTween.Sequence().SetUpdate(true).SetLink(instance);
            nightIn.Append(group.DOFade(1, .5f).SetEase(Ease.InOutSine));
            if (view.dayLabel != null) nightIn.Insert(.35f, view.dayLabel.DOFade(1, .4f));
            if (view.bodyLabel != null) nightIn.Insert(.55f, view.bodyLabel.DOFade(1, .45f));
            if (view.hintLabel != null) nightIn.Insert(1f, view.hintLabel.DOFade(1, .4f));

            // 点击任意处破晓（背景本来就拦点击，借它当按钮；不进叠加层栈所以不占 ESC 语义）
            var clickGraphic = useFrames ? (UnityEngine.UI.Graphic)view.cycleFrames : view.sky;
            var clickButton = clickGraphic.gameObject.AddComponent<UnityEngine.UI.Button>();
            clickButton.transition = UnityEngine.UI.Selectable.Transition.None;
            clickButton.targetGraphic = clickGraphic;
            var started = false;
            clickButton.onClick.AddListener(() =>
            {
                if (started || instance == null) return;
                started = true;
                SfxManager.Play(ESfx.UiClick);
                nightIn.Kill(true); // 入场段若未播完，快进到位再接收尾
                PlayDawn(instance, view, group, useFrames, onFinished);
            });
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

        /// <summary>第二段：结算收起，标题切成新一天，破晓后整层淡出。分帧背景继续循环，只在纯色回落时推天色。</summary>
        private static void PlayDawn(GameObject instance, OutGameDayTransitionView view, CanvasGroup group,
            bool useFrames, Action onFinished)
        {
            var newDay = GameManager.Instance.HouseClockManager.Data.Day; // EndDay 之后时钟已在次日
            var seq = DOTween.Sequence().SetUpdate(true).SetLink(instance);
            if (view.bodyLabel != null) seq.Join(view.bodyLabel.DOFade(0, .3f));
            if (view.hintLabel != null) seq.Join(view.hintLabel.DOFade(0, .25f));
            if (view.dayLabel != null)
            {
                seq.Join(view.dayLabel.DOFade(0, .25f));
                seq.AppendCallback(() => { if (view.dayLabel != null) view.dayLabel.text = $"DAY {newDay:00}"; });
                seq.Append(view.dayLabel.DOFade(1, .35f));
            }
            if (view.subLabel != null) seq.Join(view.subLabel.DOFade(1, .35f));
            if (!useFrames)
            {
                seq.Join(view.sky.DOColor(DawnSky, .9f).SetEase(Ease.InOutSine));
                seq.Join(view.glow.DOColor(DawnGlow, .9f).SetEase(Ease.InOutSine));
                seq.Join(view.glow.rectTransform.DOAnchorPosY(view.glow.rectTransform.anchoredPosition.y + 130f, 1.3f)
                    .SetEase(Ease.OutCubic));
            }
            seq.AppendInterval(useFrames ? .5f : .2f);
            seq.Append(group.DOFade(0, .65f).SetEase(Ease.InOutSine)); // CanvasGroup 淡出连带所有文字
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
                "<size=14>以上均为当日逐次结算的累计，日结不重复计算。</size>";
        }
    }
}

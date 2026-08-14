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

        /// <summary>结束今天：入夜 + 夜幕结算 + 点击破晓。时间此刻已跳到次日开门。</summary>
        public static void PlayEndDay(HouseUIManager ui, int endedDay, VisitorDaySummary summary)
        {
            var prefab = Resources.Load<GameObject>(OutGamePrefabResourcePaths.DayTransition);
            if (prefab == null)
            {
                Debug.LogError("[HouseUI] 日出过场 Prefab 缺失，无法播放（§16.2 不回退代码布局）：" + OutGamePrefabResourcePaths.DayTransition);
                return;
            }
            var instance = Object.Instantiate(prefab, ui.PageRoot, false);
            instance.name = "DayTransitionLayer";
            var view = instance.GetComponent<OutGameDayTransitionView>();
            if (view == null || view.sky == null || view.glow == null)
            {
                Debug.LogError("[HouseUI] 日出过场 Prefab 缺少视图组件或引用：OutGameDayTransitionView");
                Object.Destroy(instance);
                return;
            }
            ((RectTransform)instance.transform).SetAsLastSibling();
            HouseUIUtil.ApplyFallbackFont(instance.transform);

            SetText(view.dayLabel, $"DAY {endedDay:00} 结算");
            SetText(view.subLabel, "新的一天，开门迎客");
            SetText(view.bodyLabel, summary != null ? BuildBody(summary) : string.Empty);
            SetText(view.hintLabel, "点击任意处 · 开始新的一天");

            var group = HouseUIUtil.Group(instance, 0);
            group.blocksRaycasts = true;
            SfxManager.Play(ESfx.PageTransition);

            // 第一段：入夜盖屏，夜幕上亮出结算，然后停住等点击
            var nightIn = DOTween.Sequence().SetUpdate(true).SetLink(instance);
            nightIn.Append(group.DOFade(1, .5f).SetEase(Ease.InOutSine));
            if (view.dayLabel != null) nightIn.Insert(.35f, view.dayLabel.DOFade(1, .4f));
            if (view.bodyLabel != null) nightIn.Insert(.55f, view.bodyLabel.DOFade(1, .45f));
            if (view.hintLabel != null) nightIn.Insert(1f, view.hintLabel.DOFade(1, .4f));

            // 点击任意处破晓（夜空本来就拦点击，借它当按钮；不进叠加层栈所以不占 ESC 语义）
            var skyButton = view.sky.gameObject.AddComponent<UnityEngine.UI.Button>();
            skyButton.transition = UnityEngine.UI.Selectable.Transition.None;
            skyButton.targetGraphic = view.sky;
            var started = false;
            skyButton.onClick.AddListener(() =>
            {
                if (started || instance == null) return;
                started = true;
                SfxManager.Play(ESfx.UiClick);
                nightIn.Kill(true); // 入夜段若未播完，快进到位再接破晓
                PlayDawn(instance, view, group);
            });
        }

        /// <summary>第二段：结算收起，标题切成新一天，天色破晓后整层淡出。</summary>
        private static void PlayDawn(GameObject instance, OutGameDayTransitionView view, CanvasGroup group)
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
            seq.Join(view.sky.DOColor(DawnSky, .9f).SetEase(Ease.InOutSine));
            seq.Join(view.glow.DOColor(DawnGlow, .9f).SetEase(Ease.InOutSine));
            seq.Join(view.glow.rectTransform.DOAnchorPosY(view.glow.rectTransform.anchoredPosition.y + 130f, 1.3f)
                .SetEase(Ease.OutCubic));
            seq.AppendInterval(.2f);
            seq.Append(group.DOFade(0, .65f).SetEase(Ease.InOutSine)); // CanvasGroup 淡出连带所有文字
            seq.OnComplete(() =>
            {
                if (instance != null) Object.Destroy(instance);
            });
        }

        /// <summary>设文案并把初始透明度归零（各文字都由时间轴自己淡入）。</summary>
        private static void SetText(UnityEngine.UI.Text label, string value)
        {
            if (label == null) return;
            label.text = value;
            label.color = new Color(label.color.r, label.color.g, label.color.b, 0);
        }

        /// <summary>当日结算正文（自退役的 DaySettleOverlay 迁入）：只展示不惩罚，惩罚已在超时/拒绝当时结清。</summary>
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
                $"<color=#D4A46B>货币 +{summary.CurrencyEarned:N0}</color>　" +
                $"<color=#74D8D1>声望 +{summary.ReputationEarned}</color>　" +
                $"<color=#E22D76>声望 -{summary.ReputationLost}</color>\n" +
                "<size=14>以上均为当日逐次结算的累计，日结不重复扣减。</size>";
        }
    }
}

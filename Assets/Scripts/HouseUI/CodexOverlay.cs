using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 访客图鉴叠加层（2026-08-18 按 2.0 设计图新建，从档案页进入）：
    /// 一排档案卡横向铺开、正中一张是焦点卡，中键/←→/滚轮切换选项，空格「查看」把焦点卡翻成彩色。
    /// 未接待过的种族查看不了（只给剪影）——解锁判据是 <see cref="VisitorManager.HasMetRace"/>。
    ///
    /// 卡面是整图素材（每族一张彩色 + 一张剪影），全部烘在 Prefab 上（§16.6 内容进资产）；
    /// 卡位的位置尺寸同样以 Prefab 为准（§16.2），本类只决定「哪个槽位放哪张图」。
    /// </summary>
    public sealed class CodexOverlay : IHouseOverlay
    {
        private readonly RectTransform root;
        private readonly OutGameCodexPageView view;
        private readonly HouseUIManager ui;

        /// <summary>当前焦点的种族下标（对 raceIds 取模）。</summary>
        private int focusIndex;
        private bool closing;

        /// <summary>卡片层（卡位布局仍以 Prefab 为准，这里只在卡位之间插值）。</summary>
        private RectTransform cardsRoot;
        private CanvasGroup cardsGroup;
        private Tween slideTween;

        /// <summary>各卡位在 Prefab 上的权威布局（位置/尺寸/倾角），动画在它们之间插值。</summary>
        private Vector2[] slotPositions;
        private Vector2[] slotSizes;
        private float[] slotTilts;
        /// <summary>翻页进度：刚翻完是 ±1（卡还在上一格的位置），缓动到 0 = 落位。</summary>
        private float slideDelta;

        /// <summary>连着翻时最多落后几格（再多就追不上了，看着像卡）。</summary>
        private const float SlideMaxSteps = 1.6f;
        private const float SlideSeconds = .34f;

        private CodexOverlay(RectTransform root, OutGameCodexPageView view, HouseUIManager ui)
        {
            this.root = root;
            this.view = view;
            this.ui = ui;
        }

        public static void Open(HouseUIManager ui)
        {
            var prefab = Resources.Load<GameObject>(OutGamePrefabResourcePaths.CodexPage);
            if (prefab == null)
            {
                Debug.LogError("[HouseUI] 图鉴 Prefab 缺失，无法打开（§16.2 不回退代码布局）：" +
                               OutGamePrefabResourcePaths.CodexPage);
                return;
            }
            var instance = Object.Instantiate(prefab, ui.PageRoot, false);
            instance.name = "CodexLayer";
            var view = instance.GetComponent<OutGameCodexPageView>();
            if (view == null)
            {
                Debug.LogError("[HouseUI] 图鉴 Prefab 缺少视图组件：OutGameCodexPageView");
                Object.Destroy(instance);
                return;
            }
            var rect = (RectTransform)instance.transform;
            rect.SetAsLastSibling();
            var overlay = new CodexOverlay(rect, view, ui);
            overlay.Bind();
            var hotkeys = instance.AddComponent<CodexHotkeys>();
            hotkeys.Bind(() => overlay.Step(-1), () => overlay.Step(1), overlay.ShowFocusInfo);
            HouseUIUtil.ApplyFallbackFont(instance.transform);
            HouseUIBackgroundFit.Apply(view.background); // 非 16:9 屏上底图铺满不变形
            // 图鉴不随时钟变色（2026-08-19 反馈：商店/设置/图鉴关闭变色功能）
            var group = HouseUIUtil.Group(rect.gameObject, 0);
            group.DOFade(1, .25f).SetUpdate(true);
            ui.PushOverlay(overlay);
        }

        public void Close()
        {
            if (closing || root == null) return;
            closing = true;
            slideTween?.Kill();
            var group = HouseUIUtil.Group(root.gameObject);
            group.blocksRaycasts = false;
            group.DOFade(0, .2f).SetUpdate(true).OnComplete(() =>
            {
                if (root == null) return;
                HouseUIUtil.KillTweensUnder(root);
                Object.Destroy(root.gameObject);
            });
        }

        private int RaceCount => view.races != null ? view.races.Length : 0;

        private void Bind()
        {
            if (view.title != null) view.title.text = "Illustrated Guide";
            if (view.backButton != null) HouseUIUtil.BindButton(view.backButton, ui.PopOverlay, ESfx.None);
            if (view.switchButton != null) HouseUIUtil.BindButton(view.switchButton, () => Step(1));
            if (view.viewButton != null) HouseUIUtil.BindButton(view.viewButton, ShowFocusInfo);
            // 卡位点击：点侧卡把它转到焦点位，点焦点卡等同「查看」
            if (view.cardButtons != null)
            {
                var focus = view.cardButtons.Length / 2;
                for (var i = 0; i < view.cardButtons.Length; i++)
                {
                    if (view.cardButtons[i] == null) continue;
                    var offset = i - focus;
                    if (offset == 0) HouseUIUtil.BindButton(view.cardButtons[i], ShowFocusInfo);
                    else HouseUIUtil.BindButton(view.cardButtons[i], () => Step(offset));
                }
            }
            CacheCardsRoot();
            Refresh();
        }

        /// <summary>缓存卡片层与各卡位的权威布局（动画在卡位之间插值；布局本身仍以 Prefab 为准）。</summary>
        private void CacheCardsRoot()
        {
            if (view.cardSlots == null || view.cardSlots.Length < 2 || view.cardSlots[0] == null) return;
            cardsRoot = view.cardSlots[0].rectTransform.parent as RectTransform;
            if (cardsRoot == null) return;
            cardsGroup = cardsRoot.GetComponent<CanvasGroup>();
            if (cardsGroup == null) cardsGroup = cardsRoot.gameObject.AddComponent<CanvasGroup>();
            // 记下每个卡位的权威布局：翻页时在相邻卡位之间插值，
            // 卡片就是「滑过去 + 慢慢长大/缩小」，而不是瞬间跳到下一格（2026-08-19 反馈）
            var count = view.cardSlots.Length;
            slotPositions = new Vector2[count];
            slotSizes = new Vector2[count];
            slotTilts = new float[count];
            for (var i = 0; i < count; i++)
            {
                if (view.cardSlots[i] == null) continue;
                var slotRect = view.cardSlots[i].rectTransform;
                slotPositions[i] = slotRect.anchoredPosition;
                slotSizes[i] = slotRect.sizeDelta;
                slotTilts[i] = slotRect.localEulerAngles.z;
            }
        }

        /// <summary>
        /// 按翻页进度摆卡：delta 不为 0 时，每张卡落在两个卡位之间
        /// （位置、尺寸、倾角一起插值）。于是翻一页看到的是整排一起挪、
        /// 正中那张慢慢长大，而不是各自跳格。
        /// </summary>
        private void ApplySlide(float delta)
        {
            slideDelta = delta;
            if (view.cardSlots == null || slotPositions == null) return;
            for (var i = 0; i < view.cardSlots.Length; i++)
            {
                var slot = view.cardSlots[i];
                if (slot == null) continue;
                var at = i + delta;                 // 这张卡此刻落在第几个卡位上
                var lo = Mathf.FloorToInt(at);
                var t = at - lo;
                var rect = slot.rectTransform;
                rect.anchoredPosition = Vector2.Lerp(Sample(slotPositions, lo), Sample(slotPositions, lo + 1), t);
                rect.sizeDelta = Vector2.Lerp(Sample(slotSizes, lo), Sample(slotSizes, lo + 1), t);
                rect.localEulerAngles = new Vector3(0, 0,
                    Mathf.LerpAngle(SampleTilt(lo), SampleTilt(lo + 1), t));
            }
            ReorderByCenter(delta);
        }

        /// <summary>按「离正中的远近」重排层序：远的沉底、正中那张压最上。</summary>
        private void ReorderByCenter(float delta)
        {
            var center = view.cardSlots.Length / 2;
            var order = new List<int>();
            for (var i = 0; i < view.cardSlots.Length; i++)
                if (view.cardSlots[i] != null) order.Add(i);
            order.Sort((a, b) => Mathf.Abs(b + delta - center).CompareTo(Mathf.Abs(a + delta - center)));
            foreach (var i in order) view.cardSlots[i].rectTransform.SetAsLastSibling();
        }

        private float SampleTilt(int i) =>
            slotTilts == null || slotTilts.Length == 0 ? 0f : slotTilts[Mathf.Clamp(i, 0, slotTilts.Length - 1)];

        /// <summary>
        /// 越界的卡位按边缘那两格的差值外推——翻页时新进场/退场的卡要落在屏幕外，
        /// 直接钳到边缘会让它们贴着边挤成一堆。
        /// </summary>
        private static Vector2 Sample(Vector2[] set, int i)
        {
            if (set == null || set.Length == 0) return Vector2.zero;
            if (i >= 0 && i < set.Length) return set[i];
            if (set.Length == 1) return set[0];
            return i < 0
                ? set[0] + (set[0] - set[1]) * -i
                : set[set.Length - 1] + (set[set.Length - 1] - set[set.Length - 2]) * (i - set.Length + 1);
        }

        /// <summary>切换焦点（循环）：转到正中就直接翻开，不用再点一下（2026-08-18 反馈）。</summary>
        private void Step(int direction)
        {
            if (RaceCount == 0 || direction == 0) return;
            focusIndex = ((focusIndex + direction) % RaceCount + RaceCount) % RaceCount;
            Refresh();
            PlaySlide(direction);
        }

        /// <summary>
        /// 翻页动画（2026-08-19 重做）：刚翻完把进度置为 ±1（卡还在上一格的位置与尺寸），
        /// 再缓动到 0。于是整排卡沿着各自的卡位滑过去，正中那张一路长大、
        /// 退出正中的一路缩小，不再是换图式的跳变。
        /// 连着滚时从当前进度接着累加（并限幅），是一条连续缓动而不是一顿一顿地抽。
        /// </summary>
        private void PlaySlide(int direction)
        {
            if (view.cardSlots == null || slotPositions == null) return;
            slideTween?.Kill();
            var from = Mathf.Clamp(slideDelta + direction, -SlideMaxSteps, SlideMaxSteps);
            ApplySlide(from);
            slideTween = DOTween.To(() => slideDelta, ApplySlide, 0f, SlideSeconds)
                .SetEase(Ease.OutCubic).SetUpdate(true).SetLink(cardsRoot.gameObject);
            if (cardsGroup == null) return;
            cardsGroup.DOKill();
            cardsGroup.alpha = .88f;
            cardsGroup.DOFade(1f, SlideSeconds).SetUpdate(true).SetLink(cardsRoot.gameObject);
        }

        /// <summary>「查看」：翻开详情页（2026-08-19 详情页就位）；没接待过的看不了。</summary>
        private void ShowFocusInfo()
        {
            if (RaceCount == 0) return;
            if (!IsUnlocked(focusIndex))
            {
                ui.ShowToast("还没有接待过这位客人");
                return;
            }
            CodexDetailOverlay.Open(ui, Race(focusIndex));
        }

        private bool IsUnlocked(int index)
        {
            var manager = GameManager.Instance != null ? GameManager.Instance.VisitorManager : null;
            var race = Race(index);
            return manager != null && race != null && manager.HasMetRace(race.raceId);
        }

        /// <summary>
        /// 按当前焦点铺卡：正中放焦点种族，两侧依次向外取相邻种族（循环）。
        /// 侧卡一律剪影（设计图观感）；**焦点卡只要解锁了就直接是彩色**，不需要再点一下。
        /// </summary>
        private void Refresh()
        {
            if (view.cardSlots == null || RaceCount == 0) return;
            var focus = view.cardSlots.Length / 2;
            for (var i = 0; i < view.cardSlots.Length; i++)
            {
                var slot = view.cardSlots[i];
                if (slot == null) continue;
                var offset = i - focus;
                var raceIndex = ((focusIndex + offset) % RaceCount + RaceCount) % RaceCount;
                var showColor = offset == 0 && IsUnlocked(raceIndex);
                slot.sprite = Sprite(showColor ? view.revealedCards : view.hiddenCards, raceIndex);
                slot.enabled = slot.sprite != null;
            }
            // 焦点卡编号：只有翻开的彩色卡才有 GUEST FILE 抬头，剪影上不该出现编号
            if (view.focusNumberRoot != null)
                view.focusNumberRoot.gameObject.SetActive(IsUnlocked(focusIndex));
            if (view.focusNumber != null)
                view.focusNumber.text = "NO." + (focusIndex + 1).ToString("000");
            if (view.focusName != null)
                view.focusName.text = IsUnlocked(focusIndex) ? RaceName(focusIndex) : "？？？";
            if (view.focusNote != null)
                view.focusNote.text = IsUnlocked(focusIndex) ? "已归档" : "尚未接待";
        }

        private static Sprite Sprite(Sprite[] set, int index) =>
            set != null && index >= 0 && index < set.Length ? set[index] : null;

        private VisitorRaceDef Race(int index) =>
            view.races != null && index >= 0 && index < view.races.Length ? view.races[index] : null;

        private string RaceName(int index)
        {
            var race = Race(index);
            return race != null ? race.displayName : string.Empty;
        }
    }
}

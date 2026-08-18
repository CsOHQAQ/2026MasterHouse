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

        /// <summary>卡片层（翻页时整条横移做出滑动感；卡位本身仍按 Prefab 摆）。</summary>
        private RectTransform cardsRoot;
        private CanvasGroup cardsGroup;
        private Tween slideTween;

        /// <summary>翻一张的横移距离：取相邻卡位的平均间距，滑动幅度才跟排布对得上。</summary>
        private float slideDistance = 370f;
        private const float SlideSeconds = .28f;

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
            HouseDayLightTint.Attach(instance.transform, view.background); // 底图随时钟慢慢变天色
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

        /// <summary>取卡片层与相邻卡位的平均间距（滑动动画用；卡位本身仍以 Prefab 为准）。</summary>
        private void CacheCardsRoot()
        {
            if (view.cardSlots == null || view.cardSlots.Length < 2 || view.cardSlots[0] == null) return;
            cardsRoot = view.cardSlots[0].rectTransform.parent as RectTransform;
            if (cardsRoot == null) return;
            cardsGroup = cardsRoot.GetComponent<CanvasGroup>();
            if (cardsGroup == null) cardsGroup = cardsRoot.gameObject.AddComponent<CanvasGroup>();
            var span = 0f;
            var pairs = 0;
            for (var i = 1; i < view.cardSlots.Length; i++)
            {
                if (view.cardSlots[i] == null || view.cardSlots[i - 1] == null) continue;
                span += Mathf.Abs(view.cardSlots[i].rectTransform.anchoredPosition.x -
                                  view.cardSlots[i - 1].rectTransform.anchoredPosition.x);
                pairs++;
            }
            if (pairs > 0) slideDistance = span / pairs;
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
        /// 翻页滑动（2026-08-18 反馈「一格一格的有些僵硬」）：换图是瞬时的，
        /// 但把整条卡片层先摆回上一张的位置再缓动回原位，眼睛读到的就是滑过去而不是跳过去。
        /// 连着滚时重启同一个补间，速度自然叠上去。
        /// </summary>
        private void PlaySlide(int direction)
        {
            if (cardsRoot == null) return;
            slideTween?.Kill();
            cardsRoot.anchoredPosition = new Vector2(direction * slideDistance, 0f);
            slideTween = cardsRoot.DOAnchorPos(Vector2.zero, SlideSeconds)
                .SetEase(Ease.OutCubic).SetUpdate(true).SetLink(cardsRoot.gameObject);
            if (cardsGroup == null) return;
            // 焦点卡的尺寸是瞬间变的，配一点淡入盖住那一下突变
            cardsGroup.DOKill();
            cardsGroup.alpha = .55f;
            cardsGroup.DOFade(1f, SlideSeconds).SetUpdate(true).SetLink(cardsRoot.gameObject);
        }

        /// <summary>「查看」：焦点卡已经自动翻开了，这里只报一下是谁 / 还没接待过。</summary>
        private void ShowFocusInfo()
        {
            if (RaceCount == 0) return;
            ui.ShowToast(IsUnlocked(focusIndex) ? RaceName(focusIndex) : "还没有接待过这位客人");
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

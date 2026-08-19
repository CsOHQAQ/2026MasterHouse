using DG.Tweening;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 图鉴详情页（2026-08-19 按 2.0 设计图新建）：从图鉴页按空格/点焦点卡进来的那一页。
    /// 中键（或 ←→/QE/滚轮）在角色之间切换，ESC 退回图鉴页。
    ///
    /// 文案全部读 <see cref="VisitorRaceDef"/> 上的图鉴字段（策划在访客种族表里填）——
    /// 没填的条目按占位文案兜底，不会空着一片白。
    /// </summary>
    public sealed class CodexDetailOverlay : IHouseOverlay
    {
        private readonly RectTransform root;
        private readonly OutGameCodexDetailView view;
        private readonly HouseUIManager ui;
        private int index;
        private bool closing;

        private CodexDetailOverlay(RectTransform root, OutGameCodexDetailView view, HouseUIManager ui, int index)
        {
            this.root = root;
            this.view = view;
            this.ui = ui;
            this.index = index;
        }

        /// <summary>
        /// 打开详情页。**按种族定位，不传下标**（2026-08-19 修）：
        /// 图鉴页与详情页各有一份自己的种族数组，两边生成时机不同、顺序可能不一致，
        /// 靠下标对齐会串页——点开的卡是甲，翻出来的档案是乙。
        /// </summary>
        public static void Open(HouseUIManager ui, VisitorRaceDef race)
        {
            var prefab = Resources.Load<GameObject>(OutGamePrefabResourcePaths.CodexDetail);
            if (prefab == null)
            {
                Debug.LogError("[HouseUI] 图鉴详情页 Prefab 缺失（§16.2 不回退代码布局）：" +
                               OutGamePrefabResourcePaths.CodexDetail);
                return;
            }
            var instance = Object.Instantiate(prefab, ui.PageRoot, false);
            instance.name = "CodexDetailLayer";
            var view = instance.GetComponent<OutGameCodexDetailView>();
            if (view == null)
            {
                Debug.LogError("[HouseUI] 图鉴详情页 Prefab 缺少视图组件：OutGameCodexDetailView");
                Object.Destroy(instance);
                return;
            }
            var rect = (RectTransform)instance.transform;
            rect.SetAsLastSibling();
            var overlay = new CodexDetailOverlay(rect, view, ui, IndexOf(view, race));
            overlay.Bind();
            var hotkeys = instance.AddComponent<CodexHotkeys>();
            hotkeys.Bind(() => overlay.Step(-1), () => overlay.Step(1), () => { });
            HouseUIUtil.ApplyFallbackFont(instance.transform);
            HouseUIBackgroundFit.Apply(view.background);
            HouseDayLightTint.Attach(instance.transform, view.background);
            var group = HouseUIUtil.Group(rect.gameObject, 0);
            group.DOFade(1, .22f).SetUpdate(true);
            ui.PushOverlay(overlay);
        }

        public void Close()
        {
            if (closing || root == null) return;
            closing = true;
            var group = HouseUIUtil.Group(root.gameObject);
            group.blocksRaycasts = false;
            group.DOFade(0, .18f).SetUpdate(true).OnComplete(() =>
            {
                if (root == null) return;
                HouseUIUtil.KillTweensUnder(root);
                Object.Destroy(root.gameObject);
            });
        }

        /// <summary>未解锁时统一的占位串。</summary>
        private const string Unknown = "？？？";

        /// <summary>在详情页自己的数组里找这一族；找不到就落到第一条。</summary>
        private static int IndexOf(OutGameCodexDetailView view, VisitorRaceDef race)
        {
            if (view.races == null || race == null) return 0;
            for (var i = 0; i < view.races.Length; i++)
                if (view.races[i] == race) return i;
            // 资产引用对不上时再按 raceId 兜一次（同一份表导出的资产被重建过也能命中）
            for (var i = 0; i < view.races.Length; i++)
                if (view.races[i] != null && view.races[i].raceId == race.raceId) return i;
            return 0;
        }

        private int Count => view.races != null ? view.races.Length : 0;

        /// <summary>这一族接待过没有——没接待过的，整页只给问号（2026-08-19）。</summary>
        private static bool IsUnlocked(VisitorRaceDef race)
        {
            var manager = GameManager.Instance != null ? GameManager.Instance.VisitorManager : null;
            return manager != null && race != null && manager.HasMetRace(race.raceId);
        }

        private void Bind()
        {
            if (view.title != null) view.title.text = "CHARACTER";
            if (view.backButton != null) HouseUIUtil.BindButton(view.backButton, ui.PopOverlay, ESfx.None);
            if (view.switchButton != null) HouseUIUtil.BindButton(view.switchButton, () => Step(1));
            Refresh();
        }

        private void Step(int direction)
        {
            if (Count == 0 || direction == 0) return;
            index = ((index + direction) % Count + Count) % Count;
            Refresh();
        }

        private void Refresh()
        {
            if (Count == 0) return;
            var race = view.races[Mathf.Clamp(index, 0, Count - 1)];
            if (race == null) return;
            var known = IsUnlocked(race);

            // 没接待过：名字、称号、爱好、介绍、语录一律问号，立绘与证件照不给看。
            // 页上还剩书页、纸张这些装饰，一眼能看出「这一页还没填」而不是页面坏了。
            SetText(view.nameLabel, known ? race.displayName : Unknown);
            SetText(view.aliasLabel, known ? race.aliasName : string.Empty);
            SetText(view.titleLabel, !known ? Unknown : string.IsNullOrEmpty(race.title) ? "——" : race.title);
            SetText(view.idName, !known ? Unknown
                : string.IsNullOrEmpty(race.aliasName) ? race.displayName : race.aliasName);
            SetText(view.hobbiesLabel, "爱好：　" + (!known ? Unknown
                : string.IsNullOrEmpty(race.hobbies) ? "待补充" : race.hobbies));
            SetText(view.introLabel, "介绍：\n\n" + (!known ? Unknown
                : string.IsNullOrEmpty(race.intro) ? "这位客人的档案还没有写完。" : race.intro));
            SetText(view.quoteLabel, !known ? Unknown
                : string.IsNullOrEmpty(race.quote) ? string.Empty : "“" + race.quote + "”");

            // 星级：多出来的星藏掉（素材是一整块牌子，星是单独三颗压在上面）；没接待过的一颗不给
            if (view.stars != null)
                for (var i = 0; i < view.stars.Length; i++)
                    if (view.stars[i] != null) view.stars[i].enabled = known && i < race.stars;

            // QUOTE 纸有四版，按条目下标轮换，翻角色时纸也跟着换一张
            if (view.quotePaper != null && view.quotePapers != null && view.quotePapers.Length > 0)
            {
                var paper = view.quotePapers[index % view.quotePapers.Length];
                if (paper != null) view.quotePaper.sprite = paper;
            }
            // RawImage 贴图为空会画成一整块白板，缺图时直接关掉这一层（2026-08-19 反馈）
            SetTexture(view.portrait, known ? Pick(view.portraits, index) : null);
            SetTexture(view.idAvatar, known ? Pick(view.avatars, index) : null);
            // 立绘位置没东西时写「未解锁」，别留一块空白书页
            if (view.lockedHint != null)
            {
                view.lockedHint.text = "未解锁";
                view.lockedHint.gameObject.SetActive(!known);
            }
        }

        private static void SetTexture(UnityEngine.UI.RawImage image, Texture2D texture)
        {
            if (image == null) return;
            image.texture = texture;
            image.enabled = texture != null;
        }

        private static Texture2D Pick(Texture2D[] set, int i) =>
            set != null && i >= 0 && i < set.Length ? set[i] : null;

        private static void SetText(UnityEngine.UI.Text label, string value)
        {
            if (label != null) label.text = value ?? string.Empty;
        }
    }
}

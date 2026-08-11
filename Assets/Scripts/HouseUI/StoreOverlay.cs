using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 商店叠加层（STORE，按美术示意图重做；取代旧 MarketPage 面板）：
    /// 分类页签（Q/E 按钮切换）→ 左侧卡片滚动网格（模板实例化，默认/悬停/选中/已售四态）→
    /// 右侧大预览 + 描述 + 价格购买 → 购买成功弹「NEW ITEM OBTAINED」。
    /// 数据源 FurnitureTable（分类/描述来自导表），交易走 EconomyManager（解禁=声望、购买=货币两道门）。
    /// </summary>
    public sealed class StoreOverlay : IHouseOverlay
    {
        /// <summary>分类顺序与 Prefab 上的 categorySprites（素材 1~5.png）一一对应。</summary>
        private static readonly string[] Categories = { "盆栽", "摆件", "桌椅", "壁挂", "灯具" };

        private static readonly string[] CategoryBlurbs =
        {
            "绿意是屋子的呼吸，也是访客的话题。",
            "小小的物件，撑起大大的氛围。",
            "坐下来，故事才会开始。",
            "墙上的东西，替你说出品味。",
            "一盏灯，决定一间屋子的夜晚。",
        };

        private readonly RectTransform root;
        private readonly OutGameStorePageView view;
        private readonly HouseUIManager ui;
        private readonly List<OutGameStoreCardView> cards = new List<OutGameStoreCardView>();
        private readonly List<FurnitureEntry> listed = new List<FurnitureEntry>();
        private int categoryIndex;
        private string selectedId;
        private bool closing;

        private static EconomyManager Economy => GameManager.Instance.EconomyManager;

        private StoreOverlay(RectTransform root, OutGameStorePageView view, HouseUIManager ui)
        {
            this.root = root;
            this.view = view;
            this.ui = ui;
        }

        public static void Open(HouseUIManager ui)
        {
            var prefab = Resources.Load<GameObject>(OutGamePrefabResourcePaths.StorePage);
            if (prefab == null)
            {
                Debug.LogError("[HouseUI] 商店 Prefab 缺失，无法打开（§16.2 不回退代码布局）：" + OutGamePrefabResourcePaths.StorePage);
                return;
            }
            var instance = Object.Instantiate(prefab, ui.PageRoot, false);
            instance.name = "StoreLayer";
            var view = instance.GetComponent<OutGameStorePageView>();
            if (view == null)
            {
                Debug.LogError("[HouseUI] 商店 Prefab 缺少视图组件：OutGameStorePageView");
                Object.Destroy(instance);
                return;
            }
            var rect = (RectTransform)instance.transform;
            rect.SetAsLastSibling();
            var overlay = new StoreOverlay(rect, view, ui);
            overlay.Bind();
            HouseUIUtil.ApplyFallbackFont(instance.transform);
            var group = HouseUIUtil.Group(rect.gameObject, 0);
            group.DOFade(1, .25f).SetUpdate(true);
            ui.PushOverlay(overlay);
        }

        public void Close()
        {
            if (closing || root == null) return;
            closing = true;
            Economy.Changed -= RefreshToken;
            var group = HouseUIUtil.Group(root.gameObject);
            group.blocksRaycasts = false;
            group.DOFade(0, .2f).SetUpdate(true).OnComplete(() =>
            {
                if (root == null) return;
                HouseUIUtil.KillTweensUnder(root);
                Object.Destroy(root.gameObject);
            });
        }

        private void Bind()
        {
            if (view.title != null) view.title.text = "STORE";
            if (view.closeButton != null) HouseUIUtil.BindButton(view.closeButton, ui.PopOverlay);
            if (view.prevCategory != null) HouseUIUtil.BindButton(view.prevCategory, () => SwitchCategory(-1));
            if (view.nextCategory != null) HouseUIUtil.BindButton(view.nextCategory, () => SwitchCategory(1));
            // 键帽换肤（运行时，只动这三个按钮，不碰 Prefab 其他布局）：Q/E 切分类、ESC 返回
            ApplyKeycap(view.prevCategory, "Q");
            ApplyKeycap(view.nextCategory, "E");
            ApplyKeycap(view.closeButton, "ESC");
            if (view.buyButton != null) HouseUIUtil.BindButton(view.buyButton, BuySelected);
            if (view.obtainedClose != null) HouseUIUtil.BindButton(view.obtainedClose, () => ToggleObtained(false));
            // 获得弹窗面板统一走全局底图（9 宫格切片，避免不同宽高比拉伸变形）
            if (view.obtainedName != null)
                HouseUIUtil.ApplyPanelSkin(view.obtainedName.transform.parent.GetComponent<Image>());
            ToggleObtained(false, instant: true);
            Economy.Changed += RefreshToken;
            RefreshToken();
            ShowCategory(0);
        }

        private void RefreshToken()
        {
            if (view.tokenLabel != null) view.tokenLabel.text = Economy.Data.Currency.ToString("N0");
        }

        private void SwitchCategory(int direction) =>
            ShowCategory((categoryIndex + direction + Categories.Length) % Categories.Length);

        /// <summary>切分类：重建卡片列表（模板实例化），默认选中第一件可交互的家具。</summary>
        private void ShowCategory(int index)
        {
            categoryIndex = index;
            if (view.categoryName != null) view.categoryName.text = "FURNITURE · " + Categories[index];
            if (view.categoryDesc != null) view.categoryDesc.text = CategoryBlurbs[index];
            if (view.categoryIcon != null && view.categorySprites != null &&
                index < view.categorySprites.Length && view.categorySprites[index] != null)
                view.categoryIcon.sprite = view.categorySprites[index];

            listed.Clear();
            var table = GameManager.Instance.FurnitureTable;
            foreach (var entry in table.entries)
            {
                if (entry == null) continue;
                var category = string.IsNullOrEmpty(entry.category) ? "摆件" : entry.category;
                if (category == Categories[index]) listed.Add(entry);
            }

            foreach (var card in cards)
                if (card != null) Object.Destroy(card.gameObject);
            cards.Clear();

            if (view.emptyLabel != null) view.emptyLabel.gameObject.SetActive(listed.Count == 0);

            var template = Resources.Load<GameObject>(OutGamePrefabResourcePaths.StoreCard);
            if (template == null)
            {
                Debug.LogError("[HouseUI] 商店卡片模板缺失（§16.2）：" + OutGamePrefabResourcePaths.StoreCard);
                return;
            }
            selectedId = null;
            foreach (var entry in listed)
            {
                var cardGo = Object.Instantiate(template, view.gridContent, false);
                var card = cardGo.GetComponent<OutGameStoreCardView>();
                if (card == null) { Object.Destroy(cardGo); continue; }
                cards.Add(card);
                BindCard(card, entry);
                if (selectedId == null && Economy.IsFurnitureRevealed(entry)) selectedId = entry.id;
            }
            if (selectedId == null && listed.Count > 0) selectedId = listed[0].id;
            RefreshCards();
            RefreshPreview();
            if (view.scroll != null) view.scroll.verticalNormalizedPosition = 1f;
        }

        private void BindCard(OutGameStoreCardView card, FurnitureEntry entry)
        {
            var id = entry.id;
            HouseUIUtil.BindButton(card.button, () =>
            {
                selectedId = id;
                RefreshCards();
                RefreshPreview();
            });
        }

        /// <summary>刷新全部卡片视觉：选中换 selected 框；已售置灰 +「已售罄」；声望未解禁呈「？」。</summary>
        private void RefreshCards()
        {
            for (var i = 0; i < cards.Count && i < listed.Count; i++)
            {
                var card = cards[i];
                var entry = listed[i];
                var revealed = Economy.IsFurnitureRevealed(entry);
                var owned = Economy.IsFurnitureOwned(entry.id);
                var selected = entry.id == selectedId;

                if (card.frame != null)
                {
                    card.frame.sprite = selected && card.selectedSprite != null ? card.selectedSprite : card.normalSprite;
                    card.frame.color = owned ? new Color(.62f, .6f, .64f, 1f) : Color.white;
                }
                if (card.button != null)
                {
                    // 悬停粉框（SpriteSwap）；选中/已售时改由 normal 图表达，悬停仍可见反馈
                    card.button.transition = Selectable.Transition.SpriteSwap;
                    card.button.spriteState = new SpriteState
                    {
                        highlightedSprite = card.hoverSprite,
                        pressedSprite = card.hoverSprite,
                        selectedSprite = card.frame != null ? card.frame.sprite : card.normalSprite,
                        disabledSprite = card.normalSprite,
                    };
                }
                if (card.thumb != null)
                {
                    SetThumb(card.thumb, revealed ? entry.sprite : null);
                    card.thumb.color = owned ? new Color(1f, 1f, 1f, .45f) : Color.white;
                }
                if (card.priceLabel != null)
                    card.priceLabel.text = revealed ? $"◈ {entry.price:N0}" : string.Empty;
                if (card.mark != null)
                    card.mark.text = !revealed ? "？" : owned ? "已售罄" : string.Empty;
            }
        }

        private FurnitureEntry Selected()
        {
            foreach (var entry in listed)
                if (entry != null && entry.id == selectedId) return entry;
            return null;
        }

        private void RefreshPreview()
        {
            var entry = Selected();
            var revealed = entry != null && Economy.IsFurnitureRevealed(entry);
            if (view.preview != null) SetThumb(view.preview, revealed ? entry?.sprite : null);
            if (view.itemName != null)
                view.itemName.text = entry == null ? string.Empty : revealed ? entry.displayName : "？？？";
            if (view.itemDesc != null)
                view.itemDesc.text = entry == null ? string.Empty
                    : !revealed ? $"声望达到 {entry.unlockReputation} 后解禁（当前 {Economy.Data.Reputation}）"
                    : string.IsNullOrEmpty(entry.description) ? "还没有人为它写下介绍……" : entry.description;
            var owned = entry != null && Economy.IsFurnitureOwned(entry.id);
            if (view.priceLabel != null)
                view.priceLabel.text = entry == null ? string.Empty : owned ? "已拥有" : revealed ? $"{entry.price:N0}" : "？";
            if (view.buyButton != null) view.buyButton.interactable = entry != null && revealed && !owned;
        }

        private void BuySelected()
        {
            var entry = Selected();
            if (entry == null) return;
            var result = Economy.TryPurchaseFurniture(entry);
            switch (result)
            {
                case FurniturePurchaseResult.Success:
                    RefreshCards();
                    RefreshPreview();
                    ShowObtained(entry);
                    break;
                case FurniturePurchaseResult.NotEnoughCurrency:
                    ui.ShowToast($"货币不足：需要 ◈ {entry.price:N0}，先去完成客人服务吧");
                    break;
                case FurniturePurchaseResult.ReputationLocked:
                    ui.ShowToast($"声望达到 {entry.unlockReputation} 后解禁");
                    break;
                default:
                    ui.ShowToast("已经拥有这件家具了");
                    break;
            }
        }

        /// <summary>
        /// 键帽换肤（PC ui/button 三态素材，Resources 副本）：贴 default/hover/Disable 三态，
        /// 键帽画面自带文字，按钮原有的 Text 子物体隐藏。素材缺失时保持原样。
        /// </summary>
        private static void ApplyKeycap(Button button, string key)
        {
            if (button == null) return;
            var normal = Resources.Load<Sprite>("OutGameUI/button/default/" + key);
            if (normal == null) return;
            var hover = Resources.Load<Sprite>("OutGameUI/button/hover/" + key);
            var disabled = Resources.Load<Sprite>("OutGameUI/button/Disable/" + key);
            if (button.targetGraphic is Image image)
            {
                image.sprite = normal;
                image.color = Color.white;
                image.preserveAspect = true;
            }
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = hover != null ? hover : normal,
                pressedSprite = hover != null ? hover : normal,
                selectedSprite = normal,
                disabledSprite = disabled != null ? disabled : normal,
            };
            var label = button.GetComponentInChildren<Text>();
            if (label != null) label.gameObject.SetActive(false); // 键帽自带文字
        }

        /// <summary>把精灵填进缩略 RawImage 并按素材真实宽高比修正 AspectRatioFitter（默认 1:1 会把图压变形）。</summary>
        private static void SetThumb(RawImage image, Sprite sprite)
        {
            if (image == null) return;
            var has = sprite != null && sprite.texture != null;
            image.gameObject.SetActive(has);
            if (!has) return;
            image.texture = sprite.texture;
            var fitter = image.GetComponent<AspectRatioFitter>();
            if (fitter != null && sprite.bounds.size.y > 0f)
                fitter.aspectRatio = sprite.bounds.size.x / sprite.bounds.size.y;
        }

        private void ShowObtained(FurnitureEntry entry)
        {
            if (view.obtainedGroup == null) return;
            SetThumb(view.obtainedThumb, entry.sprite);
            if (view.obtainedName != null) view.obtainedName.text = entry.displayName;
            if (view.obtainedDesc != null)
                view.obtainedDesc.text = string.IsNullOrEmpty(entry.description)
                    ? "它已经躺进你的收纳栏，家具模式里随时摆出来。"
                    : entry.description;
            ToggleObtained(true);
        }

        private void ToggleObtained(bool open, bool instant = false)
        {
            if (view.obtainedGroup == null) return;
            view.obtainedGroup.DOKill();
            if (instant) view.obtainedGroup.alpha = open ? 1f : 0f;
            else view.obtainedGroup.DOFade(open ? 1f : 0f, .22f).SetTarget(view.obtainedGroup).SetUpdate(true);
            view.obtainedGroup.blocksRaycasts = open;
            view.obtainedGroup.interactable = open;
        }
    }
}

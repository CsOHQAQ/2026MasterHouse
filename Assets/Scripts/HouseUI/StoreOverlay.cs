using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 商店叠加层（2026-08-14 按设计稿重做）：
    /// **一族一卡 + 选色**——家具 2.0 的素材是「族 × 配色变体」（如台灯 01~06），
    /// 商店按族出卡，右侧色块行选配色（悬停即预览、点击/X 键选中），买哪个颜色背包里就是哪件
    /// （所有权仍按变体粒度，零结构改动）。
    /// 卡片四态：默认/悬停/选中（SpriteSwap+手动换框）/已售置灰（全配色拥有时）；声望未解禁呈「？」。
    /// 键位（StoreHotkeys）：Q/E 切分类（循环）、X 改变颜色、回车购买、空格/ESC 关获得弹窗
    /// （ESC 递归：弹窗开着时只关弹窗，见 ConsumeEscape）。
    /// 大类描述在商店表「分类」sheet 配置；色块颜色取家具表「色值」列（导表按素材平均色生成）。
    /// 数据源 FurnitureTable/StoreTable，交易走 EconomyManager（§11.4 View 不直接摸表）。
    /// </summary>
    public sealed class StoreOverlay : IHouseOverlay
    {
        private static readonly string[] Categories = { "盆栽", "摆件", "桌椅", "壁挂", "灯具" };

        /// <summary>家具族：同一家具的全部配色变体（按家具表行序）。</summary>
        private sealed class Family
        {
            public string Key;
            public string DisplayName;
            public readonly List<FurnitureEntry> Variants = new List<FurnitureEntry>();

            public bool AnyRevealed(EconomyManager economy)
            {
                foreach (var variant in Variants)
                    if (economy.IsFurnitureRevealed(variant)) return true;
                return false;
            }

            public bool AllOwned(EconomyManager economy)
            {
                foreach (var variant in Variants)
                    if (!economy.IsFurnitureOwned(variant.id)) return false;
                return true;
            }
        }

        private readonly RectTransform root;
        private readonly OutGameStorePageView view;
        private readonly HouseUIManager ui;
        private readonly List<OutGameStoreCardView> cards = new List<OutGameStoreCardView>();
        private readonly List<Family> listed = new List<Family>();
        /// <summary>每族记住的配色选择（族键 → 变体下标），切分类/切族不丢。</summary>
        private readonly Dictionary<string, int> colorChoice = new Dictionary<string, int>();
        private readonly List<GameObject> swatches = new List<GameObject>();
        private readonly List<GameObject> obtainedSwatches = new List<GameObject>();
        private int categoryIndex;
        private string selectedFamily;
        /// <summary>色块悬停的临时预览变体（-1 = 无悬停，展示已选配色）。</summary>
        private int hoverVariant = -1;
        private bool closing;

        /// <summary>关店回调（可空）：从家具摆放模式进来的商店，ESC 关店后经它退回摆放模式（UI 递归返回语义）。</summary>
        private System.Action onClosed;

        /// <summary>待确认的购买（弹窗确认制，2026-08-14）：按购买先弹窗，点确认/空格才扣钱；ESC 取消不扣。</summary>
        private FurnitureEntry pendingPurchase;

        private static EconomyManager Economy => GameManager.Instance.EconomyManager;

        private StoreOverlay(RectTransform root, OutGameStorePageView view, HouseUIManager ui)
        {
            this.root = root;
            this.view = view;
            this.ui = ui;
        }

        /// <summary>打开商店。onClosed：关店后的去处（家具摆放模式传「重开摆放」，Hub 直接进不传）。</summary>
        public static void Open(HouseUIManager ui, System.Action onClosed = null)
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
            var overlay = new StoreOverlay(rect, view, ui) { onClosed = onClosed };
            overlay.Bind();
            var hotkeys = instance.AddComponent<StoreHotkeys>(); // 键位组件：非布局件，运行时挂（§16.2 例外口径同打字机）
            hotkeys.Bind(() => overlay.SwitchCategory(-1), () => overlay.SwitchCategory(1),
                overlay.CycleColor, overlay.BuySelected,
                overlay.ConfirmPurchase, () => overlay.IsObtainedOpen); // 弹窗态空格 = 确认购买（此刻才扣钱）
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
            // 递归返回：从家具摆放模式进来的，关店后退回摆放模式（Hub 直接进的无回调，留在 Hub）
            var back = onClosed;
            onClosed = null;
            back?.Invoke();
        }

        /// <summary>确认弹窗开着时 ESC = 取消购买（不扣钱、只关弹窗），再按一次才退店。</summary>
        public bool ConsumeEscape()
        {
            if (!IsObtainedOpen) return false;
            pendingPurchase = null;
            ToggleObtained(false);
            return true;
        }

        private bool IsObtainedOpen => view != null && view.obtainedGroup != null && view.obtainedGroup.alpha > .5f;

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
            if (view.obtainedClose != null) HouseUIUtil.BindButton(view.obtainedClose, ConfirmPurchase);
            // 获得弹窗面板统一走全局底图（9 宫格切片，避免不同宽高比拉伸变形）
            if (view.obtainedName != null)
                HouseUIUtil.ApplyPanelSkin(view.obtainedName.transform.parent.GetComponent<Image>());
            ToggleObtained(false, instant: true);
            Economy.Changed += RefreshToken;
            RefreshToken();
            ShowCategory(0); // 设计稿：打开默认选中第一类第一项
            // 键位提示/色块行的位置尺寸以 Prefab 为准（§16.2），运行时不校位——要调直接在 Prefab 里拖
        }

        private void RefreshToken()
        {
            if (view.tokenLabel != null) view.tokenLabel.text = Economy.Data.Currency.ToString("N0");
        }

        private void SwitchCategory(int direction)
        {
            if (IsObtainedOpen) return; // 弹窗态不切类（键位组件已挡，这里挡按钮点击）
            ShowCategory((categoryIndex + direction + Categories.Length) % Categories.Length); // 循环切换
        }

        /// <summary>切分类：按族聚合重建卡片列表（模板实例化），默认选中第一个已解禁的族。</summary>
        private void ShowCategory(int index)
        {
            categoryIndex = index;
            if (view.categoryName != null) view.categoryName.text = "FURNITURE · " + Categories[index];
            if (view.categoryDesc != null) view.categoryDesc.text = CategoryDesc(index);
            if (view.categoryIcon != null && view.categorySprites != null &&
                index < view.categorySprites.Length && view.categorySprites[index] != null)
                view.categoryIcon.sprite = view.categorySprites[index];

            BuildFamilies(index);

            foreach (var card in cards)
                if (card != null) Object.Destroy(card.gameObject);
            cards.Clear();

            if (view.emptyLabel != null) view.emptyLabel.gameObject.SetActive(listed.Count == 0); // 空类目空状态

            var template = Resources.Load<GameObject>(OutGamePrefabResourcePaths.StoreCard);
            if (template == null)
            {
                Debug.LogError("[HouseUI] 商店卡片模板缺失（§16.2）：" + OutGamePrefabResourcePaths.StoreCard);
                return;
            }
            selectedFamily = null;
            hoverVariant = -1;
            foreach (var family in listed)
            {
                var cardGo = Object.Instantiate(template, view.gridContent, false);
                var card = cardGo.GetComponent<OutGameStoreCardView>();
                if (card == null) { Object.Destroy(cardGo); continue; }
                cards.Add(card);
                BindCard(card, family);
                if (selectedFamily == null && family.AnyRevealed(Economy)) selectedFamily = family.Key;
            }
            if (selectedFamily == null && listed.Count > 0) selectedFamily = listed[0].Key;
            RefreshCards();
            RefreshSwatches();
            RefreshPreview();
            SlideInContent();
            if (view.scroll != null) view.scroll.verticalNormalizedPosition = 1f;
        }

        /// <summary>不做配色聚合的分类：盆栽变体太多（悬挂绿植 22 款），一件一卡直接平铺，不走选色。</summary>
        private static readonly HashSet<string> FlatCategories = new HashSet<string> { "盆栽" };

        /// <summary>按 id 前缀（族键）聚合当前分类的家具（保持表序；同类别相似物品天然排在一起）。
        /// 平铺分类（盆栽）一件一卡不聚合。</summary>
        private void BuildFamilies(int index)
        {
            listed.Clear();
            var flat = FlatCategories.Contains(Categories[index]);
            var byKey = new Dictionary<string, Family>();
            var table = GameManager.Instance.FurnitureTable;
            foreach (var entry in table.entries)
            {
                if (entry == null) continue;
                var category = string.IsNullOrEmpty(entry.category) ? "摆件" : entry.category;
                if (category != Categories[index]) continue;
                if (flat)
                {
                    var single = new Family { Key = entry.id, DisplayName = entry.displayName };
                    single.Variants.Add(entry);
                    listed.Add(single);
                    continue;
                }
                var key = FamilyKey(entry.id);
                if (!byKey.TryGetValue(key, out var family))
                {
                    family = new Family { Key = key, DisplayName = FamilyName(entry.displayName) };
                    byKey[key] = family;
                    listed.Add(family);
                }
                family.Variants.Add(entry);
            }
        }

        private static string FamilyKey(string id)
        {
            var cut = id != null ? id.LastIndexOf('_') : -1;
            return cut > 0 ? id.Substring(0, cut) : id;
        }

        /// <summary>族显示名：变体中文名去掉「·NN」编号后缀。</summary>
        private static string FamilyName(string displayName)
        {
            if (string.IsNullOrEmpty(displayName)) return displayName;
            var cut = displayName.IndexOf('·');
            return cut > 0 ? displayName.Substring(0, cut) : displayName;
        }

        private string CategoryDesc(int index)
        {
            var store = GameManager.Instance.StoreTable;
            var desc = store != null ? store.CategoryDescOf(Categories[index]) : string.Empty;
            return string.IsNullOrEmpty(desc) ? "为你的 House 添置些什么吧。" : desc;
        }

        private Family SelectedFamilyEntry()
        {
            foreach (var family in listed)
                if (family.Key == selectedFamily) return family;
            return null;
        }

        /// <summary>族当前选中的配色下标（记忆化；默认第一个未拥有的，全拥有则 0）。</summary>
        private int ChoiceOf(Family family)
        {
            if (family == null || family.Variants.Count == 0) return 0;
            if (!colorChoice.TryGetValue(family.Key, out var choice))
            {
                choice = 0;
                for (var i = 0; i < family.Variants.Count; i++)
                    if (!Economy.IsFurnitureOwned(family.Variants[i].id)) { choice = i; break; }
                colorChoice[family.Key] = choice;
            }
            return Mathf.Clamp(choice, 0, family.Variants.Count - 1);
        }

        /// <summary>当前应展示的变体：色块悬停临时预览优先，其次已选配色。</summary>
        private FurnitureEntry CurrentVariant(Family family)
        {
            if (family == null || family.Variants.Count == 0) return null;
            if (hoverVariant >= 0 && hoverVariant < family.Variants.Count) return family.Variants[hoverVariant];
            return family.Variants[ChoiceOf(family)];
        }

        private void BindCard(OutGameStoreCardView card, Family family)
        {
            var key = family.Key;
            HouseUIUtil.BindButton(card.button, () =>
            {
                if (selectedFamily == key) return;
                selectedFamily = key;
                hoverVariant = -1;
                RefreshCards();
                RefreshSwatches();
                RefreshPreview();
                SlideInInfo(); // 设计稿 §2：点击切换物品时右侧信息滑入
            });
        }

        /// <summary>刷新全部卡片视觉（四态）：选中换框；全配色拥有 = 已售置灰；声望未解禁呈「？」。</summary>
        private void RefreshCards()
        {
            for (var i = 0; i < cards.Count && i < listed.Count; i++)
            {
                var card = cards[i];
                var family = listed[i];
                var revealed = family.AnyRevealed(Economy);
                var soldOut = family.AllOwned(Economy);
                var selected = family.Key == selectedFamily;
                var showEntry = family.Variants[ChoiceOf(family)];

                if (card.frame != null)
                {
                    card.frame.sprite = selected && card.selectedSprite != null ? card.selectedSprite : card.normalSprite;
                    card.frame.color = soldOut ? new Color(.62f, .6f, .64f, 1f) : Color.white;
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
                    SetThumb(card.thumb, revealed ? showEntry.sprite : null);
                    card.thumb.color = soldOut ? new Color(1f, 1f, 1f, .45f) : Color.white;
                }
                if (card.priceLabel != null)
                    card.priceLabel.text = revealed ? $"◈ {Economy.PriceOf(showEntry):N0}" : string.Empty;
                if (card.mark != null)
                    card.mark.text = !revealed ? "？" : soldOut ? "已售罄" : string.Empty;
            }
        }

        // ══════════ 选色（设计稿 §5）══════════

        /// <summary>X 键：循环切换当前族的配色。</summary>
        private void CycleColor()
        {
            var family = SelectedFamilyEntry();
            if (family == null || family.Variants.Count <= 1) return;
            colorChoice[family.Key] = (ChoiceOf(family) + 1) % family.Variants.Count;
            hoverVariant = -1;
            RefreshSwatchVisuals();
            RefreshCards();   // 卡片缩略图跟随配色
            RefreshPreview();
        }

        /// <summary>重建色块行：一族的每个配色一块（色值来自家具表，导表按素材平均色生成）。</summary>
        private void RefreshSwatches()
        {
            foreach (var chip in swatches)
                if (chip != null) Object.Destroy(chip);
            swatches.Clear();
            if (view.swatchRoot == null) return;
            var family = SelectedFamilyEntry();
            var show = family != null && family.Variants.Count > 1 && family.AnyRevealed(Economy);
            view.swatchRoot.gameObject.SetActive(show);
            if (!show) return;

            const float spacing = 34f;
            var count = family.Variants.Count;
            for (var i = 0; i < count; i++)
            {
                var index = i;
                // 设计稿 §5：小色块、行内左对齐。
                // 结构：根 = 外框素材（color-* 三态，实心图，在底层），子 = 内嵌色芯（素材平均色，盖在框芯上）
                var chip = HouseUIRuntime.Rect(view.swatchRoot, "Swatch" + i,
                    new Vector2(0f, .5f), new Vector2(0f, .5f),
                    new Vector2(14f + i * spacing, 0f), new Vector2(26, 26));
                var frame = chip.gameObject.AddComponent<Image>();
                frame.preserveAspect = true;
                var fillRect = HouseUIRuntime.Rect(chip, "Fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                fillRect.offsetMin = new Vector2(4, 4);
                fillRect.offsetMax = new Vector2(-4, -4);
                var fill = fillRect.gameObject.AddComponent<Image>();
                fill.sprite = HouseUIRuntime.WhiteSprite;
                fill.color = family.Variants[i].swatchColor;
                fill.raycastTarget = false;

                var button = chip.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() =>
                {
                    colorChoice[family.Key] = index;
                    hoverVariant = -1;
                    RefreshSwatchVisuals();
                    RefreshCards();
                    RefreshPreview();
                });
                var trigger = chip.gameObject.AddComponent<EventTrigger>();
                var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                enter.callback.AddListener(_ => { hoverVariant = index; RefreshSwatchVisuals(); RefreshPreview(); });
                trigger.triggers.Add(enter);
                var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                exit.callback.AddListener(_ => { hoverVariant = -1; RefreshSwatchVisuals(); RefreshPreview(); });
                trigger.triggers.Add(exit);
                swatches.Add(chip.gameObject);
            }
            RefreshSwatchVisuals();
        }

        /// <summary>色块外框三态：选中 > 悬停 > 默认；已拥有的配色内芯压暗以示区分。</summary>
        private void RefreshSwatchVisuals()
        {
            var family = SelectedFamilyEntry();
            if (family == null) return;
            var chosen = ChoiceOf(family);
            for (var i = 0; i < swatches.Count && i < family.Variants.Count; i++)
            {
                if (swatches[i] == null) continue;
                if (swatches[i].TryGetComponent<Image>(out var frame))
                {
                    var state = i == chosen ? "selected" : i == hoverVariant ? "hover" : "deault";
                    var sprite = Resources.Load<Sprite>("OutGameUI/store/color-" + state);
                    if (sprite != null) frame.sprite = sprite;
                }
                var fillNode = swatches[i].transform.Find("Fill");
                if (fillNode != null && fillNode.TryGetComponent<Image>(out var fill))
                {
                    var color = family.Variants[i].swatchColor;
                    fill.color = Economy.IsFurnitureOwned(family.Variants[i].id)
                        ? new Color(color.r * .55f, color.g * .55f, color.b * .55f, 1f)
                        : color;
                }
            }
        }

        // ══════════ 预览与购买 ══════════

        private void RefreshPreview()
        {
            var family = SelectedFamilyEntry();
            var entry = CurrentVariant(family);
            var revealed = entry != null && Economy.IsFurnitureRevealed(entry);
            if (view.preview != null) SetThumb(view.preview, revealed ? entry?.sprite : null);
            if (view.itemName != null)
                view.itemName.text = entry == null ? string.Empty : revealed ? entry.displayName : "？？？";
            if (view.itemDesc != null)
                view.itemDesc.text = entry == null ? string.Empty
                    : !revealed ? $"声望达到 {Economy.UnlockReputationOf(entry)} 后解禁（当前 {Economy.Data.Reputation}）"
                    : string.IsNullOrEmpty(entry.description) ? CategoryDesc(categoryIndex) : entry.description;

            var owned = entry != null && Economy.IsFurnitureOwned(entry.id);
            var affordable = entry != null && Economy.Data.Currency >= Economy.PriceOf(entry);
            if (view.priceLabel != null)
                view.priceLabel.text = entry == null ? string.Empty
                    : owned ? "已拥有" : revealed ? $"{Economy.PriceOf(entry):N0}" : "？";
            if (view.buyButton != null)
            {
                // 设计稿 §7：已拥有/代币不足置灰但可点（点了 toast 说明原因）
                view.buyButton.interactable = entry != null && revealed;
                if (view.buyButton.targetGraphic != null)
                    view.buyButton.targetGraphic.color = entry != null && revealed && !owned && affordable
                        ? Color.white : new Color(.6f, .58f, .62f, 1f);
            }

            // 键位提示：没有商品时不显示；X 只在有多配色时显示；购买键随可买态置灰
            var hasProduct = entry != null;
            var multiColor = family != null && family.Variants.Count > 1;
            SetKeycapVisible(view.colorKeycap, view.colorKeycapLabel, hasProduct && multiColor, true);
            SetKeycapVisible(view.buyKeycap, view.buyKeycapLabel, hasProduct,
                revealed && !owned && affordable);
        }

        private static void SetKeycapVisible(Image icon, Text label, bool visible, bool enabled)
        {
            if (icon != null)
            {
                icon.gameObject.SetActive(visible);
                icon.color = enabled ? Color.white : new Color(.55f, .53f, .58f, 1f);
            }
            if (label != null)
            {
                label.gameObject.SetActive(visible);
                label.color = enabled ? new Color(1, 1, 1, .75f) : new Color(1, 1, 1, .4f);
            }
        }

        /// <summary>
        /// 按购买（空格/点价格牌）：先做可买校验并**弹确认窗**，此时不扣钱；
        /// 点「确认」按钮或再按空格才真正扣款（ConfirmPurchase），ESC 取消。买哪色背包里就是哪件。
        /// </summary>
        private void BuySelected()
        {
            if (IsObtainedOpen) return;
            var family = SelectedFamilyEntry();
            var entry = family != null && family.Variants.Count > 0 ? family.Variants[ChoiceOf(family)] : null;
            if (entry == null) return;
            if (Economy.IsFurnitureOwned(entry.id))
            {
                ui.ShowToast("已拥有该家具（换个颜色试试 · X 键切换）");
                return;
            }
            if (!Economy.IsFurnitureRevealed(entry))
            {
                ui.ShowToast($"声望达到 {Economy.UnlockReputationOf(entry)} 后解禁");
                return;
            }
            if (Economy.Data.Currency < Economy.PriceOf(entry))
            {
                ui.ShowToast($"代币不足：需要 ◈ {Economy.PriceOf(entry):N0}，先去完成客人服务吧");
                return;
            }
            pendingPurchase = entry;
            ShowObtained(family, entry); // 确认窗：展示要买的款式与配色，确认后才扣钱
        }

        /// <summary>确认购买（弹窗的确认按钮/空格）：此刻才扣款入账；失败（余额变动等）toast 说明。</summary>
        private void ConfirmPurchase()
        {
            if (!IsObtainedOpen) return;
            var entry = pendingPurchase;
            pendingPurchase = null;
            ToggleObtained(false);
            if (entry == null) return;
            var result = Economy.TryPurchaseFurniture(entry);
            switch (result)
            {
                case FurniturePurchaseResult.Success:
                    SfxManager.Play(ESfx.Reward); // 音效需求 #7：商城购买成功（购买扣款刻意不响负向音，反馈就是这声获得）
                    RefreshCards();
                    RefreshSwatchVisuals();
                    RefreshPreview();
                    break;
                case FurniturePurchaseResult.NotEnoughCurrency:
                    ui.ShowToast($"代币不足：需要 ◈ {Economy.PriceOf(entry):N0}，先去完成客人服务吧");
                    break;
                case FurniturePurchaseResult.ReputationLocked:
                    ui.ShowToast($"声望达到 {Economy.UnlockReputationOf(entry)} 后解禁");
                    break;
                default:
                    ui.ShowToast("已拥有该家具（换个颜色试试 · X 键切换）");
                    break;
            }
        }

        // ══════════ 动效（设计稿 VFX 简版）══════════

        /// <summary>切分类：下方列表与右侧信息从两侧滑入。</summary>
        private void SlideInContent()
        {
            SlideIn(view.gridContent, new Vector2(-46, 0));
            SlideIn(view.itemName != null ? view.itemName.rectTransform : null, new Vector2(46, 0));
            SlideInInfo();
        }

        /// <summary>点击切换物品：右侧信息滑入出现。</summary>
        private void SlideInInfo()
        {
            SlideIn(view.itemDesc != null ? view.itemDesc.rectTransform : null, new Vector2(36, 0));
            if (view.preview != null)
            {
                var group = HouseUIUtil.Group(view.preview.gameObject);
                group.DOKill();
                group.alpha = .25f;
                group.DOFade(1f, .22f).SetUpdate(true);
            }
        }

        private static void SlideIn(RectTransform rect, Vector2 offset)
        {
            if (rect == null) return;
            rect.DOKill();
            var resting = rect.anchoredPosition;
            rect.anchoredPosition = resting + offset;
            rect.DOAnchorPos(resting, .28f).SetEase(Ease.OutCubic).SetUpdate(true);
        }

        // ══════════ 工具 ══════════

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

        /// <summary>把精灵填进缩略 RawImage 并按素材真实宽高比更新 AspectRatioFitter 的比例
        /// （比例随内容变必须代码喂；FitInParent 等模式属布局决策，以 Prefab/卡片模板上的配置为准）。</summary>
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

        // ══════════ 获得弹窗 ══════════

        private void ShowObtained(Family family, FurnitureEntry entry)
        {
            if (view.obtainedGroup == null) return;
            SetThumb(view.obtainedThumb, entry.sprite);
            if (view.obtainedName != null) view.obtainedName.text = entry.displayName;
            if (view.obtainedDesc != null)
                view.obtainedDesc.text = string.IsNullOrEmpty(entry.description)
                    ? $"花费 ◈ {Economy.PriceOf(entry):N0} 把它带回家——确认后收进收纳栏，家具模式里随时摆出来。"
                    : entry.description;
            BuildObtainedSwatches(family, entry);
            ToggleObtained(true);
        }

        /// <summary>弹窗左缘配色列（设计稿获得弹窗）：该族全部配色，买到的那个高亮外框。</summary>
        private void BuildObtainedSwatches(Family family, FurnitureEntry bought)
        {
            foreach (var chip in obtainedSwatches)
                if (chip != null) Object.Destroy(chip);
            obtainedSwatches.Clear();
            if (view.obtainedSwatchRoot == null || family == null || family.Variants.Count <= 1) return;

            const float spacing = 42f;
            var count = family.Variants.Count;
            for (var i = 0; i < count; i++)
            {
                var chip = HouseUIRuntime.Rect(view.obtainedSwatchRoot, "Swatch" + i,
                    new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                    new Vector2(0f, (count - 1) * spacing * .5f - i * spacing), new Vector2(30, 30));
                var frame = chip.gameObject.AddComponent<Image>();
                frame.raycastTarget = false;
                frame.preserveAspect = true;
                var frameSprite = Resources.Load<Sprite>(
                    family.Variants[i] == bought ? "OutGameUI/store/color-selected" : "OutGameUI/store/color-deault");
                if (frameSprite != null) frame.sprite = frameSprite;
                var fillRect = HouseUIRuntime.Rect(chip, "Fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                fillRect.offsetMin = new Vector2(4, 4);
                fillRect.offsetMax = new Vector2(-4, -4);
                var fill = fillRect.gameObject.AddComponent<Image>();
                fill.sprite = HouseUIRuntime.WhiteSprite;
                fill.color = family.Variants[i].swatchColor;
                fill.raycastTarget = false;
                obtainedSwatches.Add(chip.gameObject);
            }
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

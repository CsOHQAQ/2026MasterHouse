using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 商店叠加层（2026-08-14 按设计稿重做）：
    /// **一族一卡 + 选色**——家具 2.0 的素材是「族 × 配色变体」（如经典落地灯 01~06），
    /// 商店按族出卡，右侧色块行选配色（悬停即预览、点击/X 键选中），买哪个颜色背包里就是哪件
    /// （所有权仍按变体粒度，零结构改动）。
    /// 2026-08-15 族化：族键改读家具表的 <c>familyId</c> 列、族名读家具族表，
    /// 原先「按 id 前缀猜族」的 FamilyKey/FamilyName 与盆栽平铺特例 FlatCategories 一并退役；
    /// 色块条改用共用模板（<see cref="ColorSwatchStrip"/> + ColorSwatch.prefab）。
    /// 2026-08-15 库存化：家具**不限数量重复购买**（家具库存说明 §5.8），「已售罄/已拥有」整套语义退役——
    /// 卡片三态（默认/悬停/选中，SpriteSwap+手动换框），角标改显示「已有 n」，价格牌恒显示价格。
    /// 声望未解禁仍呈「？」（解禁门槛是声望现在唯一的作用）。
    /// 键位（StoreHotkeys）：Q/E 切分类（循环）、X 改变颜色、回车购买、空格/ESC 关获得弹窗
    /// （ESC 递归：弹窗开着时只关弹窗，见 ConsumeEscape）。
    /// 大类描述在商店表「分类」sheet 配置；色块颜色取家具表「色值」列（导表按素材平均色生成）。
    /// 数据源 FurnitureTable/StoreTable，交易走 EconomyManager（§11.4 View 不直接摸表）。
    /// </summary>
    public sealed class StoreOverlay : IHouseOverlay
    {
        private static readonly string[] Categories = { "盆栽", "摆件", "桌椅", "壁挂", "灯具" };

        /// <summary>
        /// 不做换色的类目：这些类目里「一族多变体」不是配色关系，
        /// 而是不同的东西（悬挂绿植 01~31 是 22 株不同的植物），所以逐件出卡、不出色块行与 X 键。
        /// </summary>
        private static readonly string[] NoColorCategories = { "盆栽" };

        /// <summary>
        /// 不做换色聚类的指定家具族。床虽然归在「桌椅」大类，但每张造型都是独立商品，不能合成选色变体。
        /// </summary>
        private static readonly string[] NoColorFamilyIds = { "bed_side", "bed_front" };

        /// <summary>卡片角标（「已有 n」/「？」）的文字色：2.0 商店主题蓝。</summary>
        private static readonly Color MarkTint = new Color32(0x4A, 0x6F, 0xA5, 0xFF);

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

            // AllOwned（整族收集完 → 卡片置灰「已售罄」）已于 2026-08-15 删除：
            // 家具改为不限数量重复购买之后（家具库存说明 §5.2/§5.8），"售罄"这个概念不存在了。
            // 空出来的角标改用来显示「已有 n」——比留空更有信息量，且不动 Prefab 布局。
        }

        private readonly RectTransform root;
        private readonly OutGameStorePageView view;
        private readonly HouseUIManager ui;
        private readonly List<OutGameStoreCardView> cards = new List<OutGameStoreCardView>();
        private readonly List<Family> listed = new List<Family>();
        /// <summary>每族记住的配色选择（族键 → 变体下标），切分类/切族不丢。</summary>
        private readonly Dictionary<string, int> colorChoice = new Dictionary<string, int>();
        /// <summary>右侧选色行与获得弹窗配色列：共用色块模板与交互（收纳栏也是同一个类）。</summary>
        private readonly ColorSwatchStrip swatchStrip = new ColorSwatchStrip();
        private readonly ColorSwatchStrip obtainedStrip = new ColorSwatchStrip();
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
            overlay.SinkPreview(); // 大预览压到底层，免得盖住价格面板等右页 UI（运行时层序，不写 Prefab）
            overlay.Bind();
            var hotkeys = instance.AddComponent<StoreHotkeys>(); // 键位组件：非布局件，运行时挂（§16.2 例外口径同打字机）
            hotkeys.Bind(() => overlay.SwitchCategory(-1), () => overlay.SwitchCategory(1),
                overlay.CycleColor, overlay.BuySelected,
                overlay.ConfirmPurchase, () => overlay.IsObtainedOpen); // 弹窗态空格 = 确认购买（此刻才扣钱）
            HouseUIUtil.ApplyFallbackFont(instance.transform);
            HouseUIBackgroundFit.Apply(view.background); // 非 16:9 屏上底图铺满不变形
            // 商店不随时钟变色（2026-08-19 反馈：商店/设置/图鉴关闭变色功能）
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
            // 2.0 的 Q/E/ESC 都是整图素材（键名画在图里），键帽换肤会把 1.0 的灰键帽盖上去，
            // 反而把 2.0 的图顶掉（2026-08-18 反馈），故不再换肤——三态由 Prefab 的 SpriteState 给
            BindColorKeycap();
            if (view.buyButton != null) HouseUIUtil.BindButton(view.buyButton, BuySelected);
            if (view.obtainedClose != null) HouseUIUtil.BindButton(view.obtainedClose, ConfirmPurchase);
            // 获得弹窗面板统一走全局底图（9 宫格切片，避免不同宽高比拉伸变形）
            // 2.0 底板自带外观：只有还没换皮的旧 Prefab（面板没 sprite）才套通用面板皮肤
            var obtainedBoard = view.obtainedName != null
                ? view.obtainedName.transform.parent.GetComponent<Image>() : null;
            if (obtainedBoard != null && obtainedBoard.sprite == null) HouseUIUtil.ApplyPanelSkin(obtainedBoard);
            // 色块条（共用模板与交互，见 ColorSwatchStrip）：右侧选色行可点可悬停，获得弹窗那列只展示
            swatchStrip.Build(view.swatchRoot, new Vector2(26, 26), 34f);
            swatchStrip.Selected += index =>
            {
                var family = SelectedFamilyEntry();
                if (family == null) return;
                colorChoice[family.Key] = index;
                hoverVariant = -1;
                RefreshCards(); // 卡片缩略图跟随配色
                RefreshPreview();
            };
            swatchStrip.Previewed += index => { hoverVariant = index; RefreshPreview(); };
            obtainedStrip.Build(view.obtainedSwatchRoot, new Vector2(30, 30), 42f, vertical: true, interactive: false);

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

        /// <summary>
        /// 按**显式族**聚合当前分类的家具（保持表序；同类别相似物品天然排在一起）。
        ///
        /// 2026-08-15 族化：族键从「猜 id 前缀」改成读家具表的 <c>familyId</c> 列，族显示名读家具族表。
        /// 随之退役的还有 <c>FlatCategories</c>（盆栽平铺特例）——有了显式族，只有一个成员的族
        /// 自然就渲染成一张单卡，不需要为它开后门（家具族体系说明 §4.1、§6 待确认 #3）。
        /// </summary>
        private void BuildFamilies(int index)
        {
            listed.Clear();
            var byKey = new Dictionary<string, Family>();
            var table = GameManager.Instance.FurnitureTable;
            var families = GameManager.Instance.FurnitureFamilyTable;
            foreach (var entry in table.entries)
            {
                if (entry == null) continue;
                var category = string.IsNullOrEmpty(entry.category) ? "摆件" : entry.category;
                if (category != Categories[index]) continue;
                // 族 id 为空是配置事故（导表会 LogError 拦下），这里让它自成一族以免整类家具挤成一张卡；
                // 不换色的类目（盆栽）或指定家具族（床）逐件成族：它们是独立造型，不是同款配色。
                var flat = System.Array.IndexOf(NoColorCategories, category) >= 0 ||
                           System.Array.IndexOf(NoColorFamilyIds, entry.familyId) >= 0;
                var key = flat || string.IsNullOrEmpty(entry.familyId) ? entry.id : entry.familyId;
                if (!byKey.TryGetValue(key, out var family))
                {
                    family = new Family
                    {
                        Key = key,
                        DisplayName = flat ? entry.displayName
                            : families != null ? families.DisplayNameOf(key) : key,
                    };
                    byKey[key] = family;
                    listed.Add(family);
                }
                family.Variants.Add(entry);
            }
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

        /// <summary>
        /// 刷新全部卡片视觉：选中换框；声望未解禁呈「？」；已有存货的在角标显示「已有 n」。
        ///
        /// **不再有"已售罄"置灰**（家具库存说明 §5.8）：家具不限数量重复购买，买过不是拒绝理由。
        /// 角标的 n 取**当前选中配色**的库存，与缩略图/价格同一件——卡片展示的始终是那一个变体。
        /// </summary>
        private void RefreshCards()
        {
            for (var i = 0; i < cards.Count && i < listed.Count; i++)
            {
                var card = cards[i];
                var family = listed[i];
                var revealed = family.AnyRevealed(Economy);
                var selected = family.Key == selectedFamily;
                var showEntry = family.Variants[ChoiceOf(family)];
                var owned = Economy.OwnedCountOf(showEntry.id);

                if (card.frame != null)
                {
                    card.frame.sprite = selected && card.selectedSprite != null ? card.selectedSprite : card.normalSprite;
                    card.frame.color = Color.white;
                }
                if (card.button != null)
                {
                    // 悬停粉框（SpriteSwap）；选中时改由 normal 图表达，悬停仍可见反馈
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
                    SetThumb(card.thumb, revealed ? showEntry : null);
                    card.thumb.color = Color.white;
                }
                // 新设计图的价格样式：纯数字（货币符号画在卡面上），未解禁不显示
                if (card.priceLabel != null)
                    card.priceLabel.text = revealed ? $"{Economy.PriceOf(showEntry):N0}" : string.Empty;
                if (card.mark != null)
                {
                    card.mark.text = !revealed ? "？" : owned > 0 ? $"已有 {owned}" : string.Empty;
                    card.mark.color = MarkTint; // 2.0 商店主题蓝（Prefab 里烘的是 1.0 的酒红）
                }
                // 「已售罄」标签保留给未解禁项（家具本身不限量重复购买，§5.8）
                if (card.soldOutTag != null) card.soldOutTag.gameObject.SetActive(!revealed);
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

        /// <summary>一族配色的色块数据：内芯取家具表「色值」，已拥有的压暗。</summary>
        private List<ColorSwatchItem> SwatchItemsOf(Family family)
        {
            var items = new List<ColorSwatchItem>();
            if (family == null) return items;
            foreach (var variant in family.Variants)
                items.Add(new ColorSwatchItem(variant.swatchColor, Economy.IsFurnitureOwned(variant.id)));
            return items;
        }

        /// <summary>重建色块行：一族的每个配色一块（色值来自家具表，导表按素材平均色生成）。</summary>
        private void RefreshSwatches()
        {
            if (view.swatchRoot == null) return;
            var family = SelectedFamilyEntry();
            var show = family != null && family.Variants.Count > 1 && family.AnyRevealed(Economy);
            view.swatchRoot.gameObject.SetActive(show);
            if (!show) { swatchStrip.Clear(); return; }
            swatchStrip.Rebuild(SwatchItemsOf(family), ChoiceOf(family));
        }

        /// <summary>只刷视觉（选中态/已拥有压暗），色块数量没变时用。</summary>
        private void RefreshSwatchVisuals()
        {
            var family = SelectedFamilyEntry();
            if (family == null) return;
            swatchStrip.Refresh(SwatchItemsOf(family), ChoiceOf(family));
        }

        // ══════════ 预览与购买 ══════════

        private void RefreshPreview()
        {
            var family = SelectedFamilyEntry();
            var entry = CurrentVariant(family);
            var revealed = entry != null && Economy.IsFurnitureRevealed(entry);
            SetPreview(revealed ? entry : null);
            if (view.itemName != null)
                view.itemName.text = entry == null ? string.Empty : revealed ? entry.displayName : "？？？";
            if (view.itemDesc != null)
                view.itemDesc.text = entry == null ? string.Empty
                    : !revealed ? $"声望达到 {Economy.UnlockReputationOf(entry)} 后解禁（当前 {Economy.Data.Reputation}）"
                    : string.IsNullOrEmpty(entry.description) ? CategoryDesc(categoryIndex) : entry.description;

            var ownedCount = entry != null ? Economy.OwnedCountOf(entry.id) : 0;
            var affordable = entry != null && Economy.Data.Currency >= Economy.PriceOf(entry);
            // 价格牌**恒显示价格**（家具库存说明 §5.8）：原先已拥有时写「已拥有」，
            // 在可重复购买之后那是误导——买过照样能买。库存数量另外在描述区末尾说，
            // 不往这个纯数字节点里塞中文（版式会撑破）
            if (view.priceLabel != null)
                view.priceLabel.text = entry == null ? string.Empty
                    : revealed ? $"{Economy.PriceOf(entry):N0}" : "？";
            if (view.itemDesc != null && revealed && ownedCount > 0)
                view.itemDesc.text += $"\n<size=14>库存中已有 {ownedCount} 件</size>";
            if (view.buyButton != null)
            {
                // 设计稿 §7：代币不足置灰但可点（点了 toast 说明原因）。
                // **「已拥有」不再参与置灰判断**——它已经不是拒绝理由了
                view.buyButton.interactable = entry != null && revealed;
                if (view.buyButton.targetGraphic != null)
                    view.buyButton.targetGraphic.color = entry != null && revealed && affordable
                        ? Color.white : new Color(.6f, .58f, .62f, 1f);
            }

            // 键位提示：没有商品时不显示；X 只在有多配色时显示；购买键只随「解禁 + 买得起」置灰
            var hasProduct = entry != null;
            var multiColor = family != null && family.Variants.Count > 1;
            SetKeycapVisible(view.colorKeycap, view.colorKeycapLabel, hasProduct && multiColor, true);
            SetKeycapVisible(view.buyKeycap, view.buyKeycapLabel, hasProduct, revealed && affordable);
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
            // 「已拥有」不再是拒绝理由（2026-08-15 家具库存说明 §5.2）：家具可重复购买、不限数量。
            // 非卖品（售价 ≤ 0）仍要拦——那是漏配的信号，放行就是免费无限买
            if (Economy.PriceOf(entry) <= 0)
            {
                ui.ShowToast("这件家具不出售（商店表里没配价格）");
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
                case FurniturePurchaseResult.NotForSale:
                    ui.ShowToast("这件家具不出售（商店表里没配价格）");
                    break;
                default:
                    // AlreadyOwned 已废弃不再返回（家具库存说明 §5.2），所以这里只剩「不该发生」的兜底，
                    // 千万别再写成「已拥有」——那会在可重复购买之后变成误导
                    ui.ShowToast("购买没有成功，请查看 Console");
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
        /// <summary>
        /// 「X 改变颜色」原本只是一张提示图（没有 Button，所以既不能点也没有悬停，2026-08-18 反馈）：
        /// 运行时补一个 Button 并按 Prefab 烘的两态图做 SpriteSwap，点击等同按 X。只动运行时实例。
        /// </summary>
        private void BindColorKeycap()
        {
            if (view.colorKeycap == null) return;
            view.colorKeycap.raycastTarget = true;
            var button = view.colorKeycap.GetComponent<Button>();
            if (button == null) button = view.colorKeycap.gameObject.AddComponent<Button>();
            button.targetGraphic = view.colorKeycap;
            if (view.colorKeycapHover != null)
            {
                button.transition = Selectable.Transition.SpriteSwap;
                button.spriteState = new SpriteState
                {
                    highlightedSprite = view.colorKeycapHover,
                    pressedSprite = view.colorKeycapHover,
                    selectedSprite = view.colorKeycap.sprite,
                };
            }
            HouseUIUtil.BindButton(button, CycleColor);
        }

        /// <summary>把精灵填进缩略 RawImage 并按素材真实宽高比更新 AspectRatioFitter 的比例
        /// （比例随内容变必须代码喂；FitInParent 等模式属布局决策，以 Prefab/卡片模板上的配置为准）。</summary>
        /// <summary>
        /// 把右页大预览压到最底（紧贴背景之上）：它是整页最大的一张图，排在谁前面就会盖住谁——
        /// 价格面板、色块行、键位条都该压在它上面。只调运行时实例的层序，Prefab 不动。
        /// </summary>
        private void SinkPreview()
        {
            if (view.preview == null) return;
            var previewTransform = view.preview.transform;
            var siblings = previewTransform.parent;
            if (siblings == null) return;
            var floor = 0;
            if (view.background != null && view.background.transform.parent == siblings)
                floor = view.background.transform.GetSiblingIndex() + 1;
            if (previewTransform.GetSiblingIndex() > floor) previewTransform.SetSiblingIndex(floor);
        }

        /// <summary>商店预览采用统一虚拟画布，保留不同家具间的相对大小，同时让小摆件仍可辨认。</summary>
        private static readonly Vector2 StoreDesignCanvas = new Vector2(240f, 220f);

        /// <summary>各自适应图片的显示框上限（首次绑定时按实际渲染尺寸记下）。</summary>
        private readonly Dictionary<RawImage, Vector2> fitBoxes = new Dictionary<RawImage, Vector2>();

        /// <summary>
        /// 右页大预览：按家具表的商店专用宽高在统一虚拟画布中显示。
        /// 显示框取自 Prefab 手调的 Rect —— 只读不写，Prefab 布局仍是唯一真相源；
        /// 若 Prefab 上挂了 AspectRatioFitter 则关掉它，避免两套缩放逻辑互相打架。
        /// </summary>
        private void SetPreview(FurnitureEntry entry) => FitInBox(view.preview, entry);

        /// <summary>
        /// 将家具的商店专用尺寸映射进 Prefab 显示框。RawImage 只采样 Sprite 自己的 UV 区域，
        /// 不再把整张纹理或透明画布当成家具尺寸。
        /// </summary>
        private void FitInBox(RawImage image, FurnitureEntry entry)
        {
            if (image == null) return;
            var rect = image.rectTransform;
            if (!fitBoxes.TryGetValue(image, out var box))
            {
                var existing = image.GetComponent<AspectRatioFitter>();
                // 用**实际渲染尺寸**测框：Prefab 里若是 stretch 锚点，sizeDelta 表示的是边距而非尺寸
                var measured = rect.rect.size;
                box = measured.x > 1f && measured.y > 1f ? measured : rect.sizeDelta;
                // 卡片缩略图初始由 FitInParent 控制，此时自身 Rect 可能已被旧比例改成正方形；
                // 商店尺寸要使用完整 ThumbArea，所以在关闭旧组件前取父级显示框。
                if (existing != null && existing.aspectMode != AspectRatioFitter.AspectMode.None &&
                    rect.parent is RectTransform fittedParent && fittedParent.rect.width > 1f && fittedParent.rect.height > 1f)
                    box = fittedParent.rect.size;
                if ((box.x <= 1f || box.y <= 1f) && rect.parent is RectTransform parentRect)
                    box = parentRect.rect.size;
                if (box.x > 1f && box.y > 1f) fitBoxes[image] = box;
                // 关掉可能存在的比例组件与父级布局控制，避免两套逻辑打架把图拉回去
                if (existing != null) existing.enabled = false;
                if (rect.parent != null && rect.parent.GetComponent<LayoutGroup>() != null)
                {
                    var element = image.GetComponent<LayoutElement>();
                    if (element == null) element = image.gameObject.AddComponent<LayoutElement>();
                    element.ignoreLayout = true;
                }
            }
            var sprite = entry?.sprite;
            var has = sprite != null && sprite.texture != null;
            image.gameObject.SetActive(has);
            if (!has) return;
            image.texture = sprite.texture;
            image.uvRect = TightUvRect(sprite);

            var configured = new Vector2(
                Mathf.Max(1f, entry.storeDisplayWidth),
                Mathf.Max(1f, entry.storeDisplayHeight));
            if (box.x <= 0f || box.y <= 0f) return;
            var scale = Mathf.Min(box.x / StoreDesignCanvas.x, box.y / StoreDesignCanvas.y);
            var target = configured * scale;
            var safetyScale = Mathf.Min(1f, Mathf.Min(box.x / target.x, box.y / target.y));
            target *= safetyScale;
            // SetSizeWithCurrentAnchors 在固定锚点与 stretch 下都能得到正确的实际尺寸
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, target.x);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, target.y);
        }

        private static Rect TightUvRect(Sprite sprite)
        {
            var uv = sprite.uv;
            if (uv == null || uv.Length == 0) return new Rect(0f, 0f, 1f, 1f);
            var min = uv[0];
            var max = uv[0];
            for (var i = 1; i < uv.Length; i++)
            {
                min = Vector2.Min(min, uv[i]);
                max = Vector2.Max(max, uv[i]);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private void SetThumb(RawImage image, FurnitureEntry entry) => FitInBox(image, entry);

        // ══════════ 获得弹窗 ══════════

        private void ShowObtained(Family family, FurnitureEntry entry)
        {
            if (view.obtainedGroup == null) return;
            FitInBox(view.obtainedThumb, entry); // 弹窗缩略图与商品卡、详情共用商店专用比例
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
            if (view.obtainedSwatchRoot == null || family == null || family.Variants.Count <= 1)
            {
                obtainedStrip.Clear();
                return;
            }
            // 这列不表达「已拥有」（刚买完满屏压暗没有意义），只把买到的那个标成选中
            var items = new List<ColorSwatchItem>();
            var boughtIndex = 0;
            for (var i = 0; i < family.Variants.Count; i++)
            {
                items.Add(new ColorSwatchItem(family.Variants[i].swatchColor, false));
                if (family.Variants[i] == bought) boughtIndex = i;
            }
            obtainedStrip.Rebuild(items, boughtIndex);
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

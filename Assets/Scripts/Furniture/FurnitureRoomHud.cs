using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>家具模式收纳栏里一个槽位的展示状态。</summary>
    public enum FurnitureSlotState
    {
        /// <summary>还有余量，可拖出摆放。</summary>
        Available,
        /// <summary>
        /// **余量为 0**（全摆出去了 / 只买了这些）。2026-08-15 语义变更：
        /// 原先是「已经摆出去了」，家具改为可重复购买后改成按余量判（家具库存说明 §5.6）。
        /// </summary>
        Placed,
        /// <summary>声望已解禁但未购买，点击弹出购买确认。</summary>
        Locked,
        /// <summary>声望未达到解禁阈值，呈「？」。</summary>
        Unknown,
    }

    /// <summary>
    /// 家具模式 HUD：布局来自 FurnitureHudPage.prefab（2026-08-11 自运行时 uGUI 固化，§16.2），
    /// 槽位为 FurnitureSlot.prefab 模板实例化；本类只做画布生命周期、内容绑定与状态切换。
    /// 收纳栏按类型分页签（地面/桌面/壁挂），页签内 12 槽/页翻页。
    /// </summary>
    public sealed class FurnitureRoomHud
    {
        public event Action ExitClicked;
        /// <summary>「购买家具」：仓库只展示已拥有，购买走商店（控制器翻译成退出+开商店）。</summary>
        public event Action StoreClicked;
        public event Action GridToggleClicked;
        /// <summary>槽位被按下（左键 PointerDown，配合拖拽起手）。参数为家具 id。</summary>
        public event Action<string> SlotPressed;
        /// <summary>槽位被右键（半价出售的入口，家具库存说明 §5.5）。参数为家具 id。</summary>
        public event Action<string> SellPressed;
        /// <summary>购买确认。参数为家具 id。</summary>
        public event Action<string> PurchaseConfirmed;
        /// <summary>出售确认。参数为家具 id。</summary>
        public event Action<string> SellConfirmed;

        public bool PopupOpen { get; private set; }

        private const int SlotsPerPage = 12;
        private const float SlotGap = 8f;

        /// <summary>
        /// 页签顺序（下标对应 Prefab 的 tabButtons，与生成器里的图标一一对齐）。
        /// 取家具表的 category 值；表里没有的那一格会被 RefreshInventory 自动隐藏并左移，
        /// 所以「椅子」在策划把它加进表之前不占位（§16.6 加内容不改代码）。
        /// </summary>
        internal static readonly string[] TabCategories =
        {
            "摆件", "壁挂", "桌椅", "椅子", "灯具", "盆栽",
        };

        private GameObject root;
        private OutGameFurnitureHudView view;
        private GameObject slotTemplate;
        private readonly List<GameObject> slotInstances = new List<GameObject>();
        private FurnitureTable table;
        private Func<string, FurnitureSlotState> stateGetter;
        /// <summary>可摆余量读取口（跨房间统计只有 Controller 算得出，View 不自己算）。</summary>
        private Func<string, int> remainingGetter;
        /// <summary>售卖配置读取口（商店表，2026-08-13 拆表）：View 不摸表，由 Controller 注入（§11.4）。</summary>
        private Func<FurnitureEntry, int> priceGetter;
        private Func<FurnitureEntry, int> unlockGetter;
        /// <summary>回收额读取口（售价 × 回收比例）。</summary>
        private Func<FurnitureEntry, int> sellbackGetter;
        /// <summary>页签槽位（Prefab 里的三个位置）：有页签隐藏时后面的自动往前补位，不留空洞。</summary>
        private Vector2[] tabSlotPositions;
        private string currentTab = TabCategories[0];
        private int page;
        private bool chromeHidden;
        private Tween toastTween;
        private string popupFurnitureId;
        /// <summary>弹窗当前是「出售」还是「购买」：两者共用同一套 Prefab 节点，靠这一格分派确认动作。</summary>
        private bool popupIsSell;

        public void Build(FurnitureTable table, Func<string, FurnitureSlotState> stateGetter,
            Func<string, int> remainingGetter, Func<FurnitureEntry, int> priceGetter,
            Func<FurnitureEntry, int> unlockGetter, Func<FurnitureEntry, int> sellbackGetter)
        {
            this.table = table;
            this.stateGetter = stateGetter;
            this.remainingGetter = remainingGetter;
            this.priceGetter = priceGetter;
            this.unlockGetter = unlockGetter;
            this.sellbackGetter = sellbackGetter;

            // 画布生命周期归代码，布局归 Prefab（§16.2）
            root = new GameObject("FurnitureModeHud", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 600;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            // Expand（2026-08-18 跨平台修复）：画布尺寸永远 >= 1920×1080，宽高各自「只放大不缩小」。
            // 原来的 MatchWidthOrHeight .5 会在非 16:9 屏（Mac 常见的 16:10）上把画布缩成
            // 1822×1139 这类中间尺寸，于是所有按 1920×1080 写死的坐标横竖都对不上位。
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            var prefab = Resources.Load<GameObject>(OutGamePrefabResourcePaths.FurnitureHud);
            slotTemplate = Resources.Load<GameObject>(OutGamePrefabResourcePaths.FurnitureSlot);
            if (prefab == null || slotTemplate == null)
            {
                Debug.LogError("[Furniture] 家具模式 HUD Prefab 缺失，界面无法呈现（§16.2 不回退代码布局）：" +
                               OutGamePrefabResourcePaths.FurnitureHud + " / " + OutGamePrefabResourcePaths.FurnitureSlot);
                return;
            }
            var instance = UnityEngine.Object.Instantiate(prefab, root.transform, false);
            instance.name = "Hud";
            view = instance.GetComponent<OutGameFurnitureHudView>();
            if (view == null)
            {
                Debug.LogError("[Furniture] 家具模式 HUD Prefab 缺少视图组件：OutGameFurnitureHudView");
                return;
            }
            HouseUIUtil.ApplyFallbackFont(instance.transform);

            HouseUIUtil.BindButton(view.exitButton, () => ExitClicked?.Invoke());
            if (view.storeButton != null) HouseUIUtil.BindButton(view.storeButton, () => StoreClicked?.Invoke());
            HouseUIUtil.BindButton(view.gridToggleButton, () => GridToggleClicked?.Invoke());
            HouseUIUtil.BindButton(view.hideUiButton, () => SetChromeHidden(true));
            HouseUIUtil.BindButton(view.restoreButton, () => SetChromeHidden(false));
            HouseUIUtil.BindButton(view.prevPageButton, () => TurnPage(-1));
            HouseUIUtil.BindButton(view.nextPageButton, () => TurnPage(1));
            for (var i = 0; i < view.tabButtons.Length && i < TabCategories.Length; i++)
            {
                var category = TabCategories[i];
                HouseUIUtil.BindButton(view.tabButtons[i], () => SelectTab(category));
            }
            if (view.backButton != null)                        // 左下「ESC 返回」＝也退出摆放模式
                HouseUIUtil.BindButton(view.backButton, () => ExitClicked?.Invoke());
            if (view.purchaseScrimButton != null)
            {
                view.purchaseScrimButton.onClick.RemoveAllListeners();
                view.purchaseScrimButton.onClick.AddListener(CloseUnlockPopup);
            }
            HouseUIUtil.BindButton(view.purchaseCancelButton, CloseUnlockPopup);
            HouseUIUtil.BindButton(view.purchaseConfirmButton, () =>
            {
                var id = popupFurnitureId;
                var sell = popupIsSell;
                CloseUnlockPopup();
                if (string.IsNullOrEmpty(id)) return;
                if (sell) SellConfirmed?.Invoke(id);
                else PurchaseConfirmed?.Invoke(id);
            });
            // 购买弹窗面板换全局底图（Secondary-bg，9 宫格）。
            // 2.0 二次确认底板自带外观，别再往上盖（2026-08-20：盖了会变成黑底洋红）
            if (view.purchaseConfirmButton != null)
            {
                var confirmBoard = view.purchaseConfirmButton.transform.parent.GetComponent<Image>();
                if (confirmBoard != null && confirmBoard.sprite == null) HouseUIUtil.ApplyPanelSkin(confirmBoard);
            }

            EnsureTabAvailable();
            RefreshInventory();
        }

        // ── 页签与分页 ──

        private List<FurnitureEntry> EntriesOf(string category)
        {
            var result = new List<FurnitureEntry>();
            foreach (var entry in table.entries)
            {
                if (entry == null || entry.category != category) continue; // 按商店分类分页签（2026-08-20 设计图）
                // 仓库只展示已拥有的家具（2026-08-14）：购买一律走商店（家具模式里的「购买家具」按钮）
                var state = stateGetter(entry.id);
                if (state == FurnitureSlotState.Locked || state == FurnitureSlotState.Unknown) continue;
                result.Add(entry);
            }
            return result;
        }

        private void EnsureTabAvailable()
        {
            if (EntriesOf(currentTab).Count > 0) return;
            foreach (var category in TabCategories)
                if (EntriesOf(category).Count > 0) { currentTab = category; return; }
        }

        private void SelectTab(string category)
        {
            if (currentTab == category) return;
            currentTab = category;
            page = 0;
            RefreshInventory();
        }

        /// <summary>列表由 GridLayoutGroup 排版时（2.0 版式）一次铺完，不分页。</summary>
        private bool Scrolling => view != null && view.slotsRoot != null
                                  && view.slotsRoot.GetComponent<LayoutGroup>() != null;

        private int PageCount => Scrolling
            ? 1
            : Mathf.Max(1, Mathf.CeilToInt(EntriesOf(currentTab).Count / (float)SlotsPerPage));

        private void TurnPage(int direction)
        {
            var count = PageCount;
            page = (page + direction + count) % count; // 循环翻页
            RefreshInventory();
        }

        /// <summary>重建当前页签当前页的槽位（模板实例化，单页数量固定，直接重建最稳）。</summary>
        public void RefreshInventory()
        {
            if (view == null || view.slotsRoot == null || slotTemplate == null) return;
            foreach (var slot in slotInstances)
                if (slot != null) UnityEngine.Object.Destroy(slot);
            slotInstances.Clear();

            // 页签视觉：选中高亮；无内容的类型隐藏页签，后面的自动往前补位（槽位取自 Prefab 原始位置）
            if (tabSlotPositions == null)
            {
                tabSlotPositions = new Vector2[view.tabButtons.Length];
                for (var i = 0; i < view.tabButtons.Length; i++)
                    if (view.tabButtons[i] != null)
                        tabSlotPositions[i] = ((RectTransform)view.tabButtons[i].transform).anchoredPosition;
            }
            var visibleSlot = 0;
            for (var i = 0; i < view.tabButtons.Length && i < TabCategories.Length; i++)
            {
                var category = TabCategories[i];
                var hasAny = EntriesOf(category).Count > 0;
                if (view.tabButtons[i] != null)
                {
                    view.tabButtons[i].gameObject.SetActive(hasAny);
                    if (hasAny)
                        ((RectTransform)view.tabButtons[i].transform).anchoredPosition =
                            tabSlotPositions[Mathf.Min(visibleSlot++, tabSlotPositions.Length - 1)];
                }
                var selected = category == currentTab;
                // 图标页签：换图不涂色（涂色会把美术图整个染掉）；没有图标组件的老版式仍走配色
                var icon = view.tabButtons[i] != null
                    ? view.tabButtons[i].GetComponent<OutGameFurnitureTabIcon>() : null;
                if (icon != null) icon.SetSelected(selected);
                else if (view.tabBackgrounds[i] != null)
                    view.tabBackgrounds[i].color = selected
                        ? new Color(.32f, .06f, .18f, .95f) : new Color(.025f, .025f, .04f, .92f);
                if (view.tabLabels[i] != null)
                    view.tabLabels[i].color = selected ? HouseUIUtil.White : new Color(1, 1, 1, .55f);
            }

            var entries = EntriesOf(currentTab);
            var pageCount = PageCount;
            if (page >= pageCount) page = pageCount - 1;
            if (view.pageLabel != null) view.pageLabel.text = $"{page + 1} / {pageCount}";

            var slotSize = ((RectTransform)slotTemplate.transform).sizeDelta;
            var perPage = Scrolling ? entries.Count : SlotsPerPage;
            var start = Scrolling ? 0 : page * SlotsPerPage;
            var cursor = 16f;
            for (var i = start; i < entries.Count && i < start + perPage; i++)
            {
                BuildSlot(entries[i], cursor, slotSize);
                cursor += slotSize.x + SlotGap;
            }
        }

        private void BuildSlot(FurnitureEntry entry, float x, Vector2 slotSize)
        {
            var go = UnityEngine.Object.Instantiate(slotTemplate, view.slotsRoot, false);
            go.name = "Slot_" + entry.id;
            slotInstances.Add(go);
            if (!Scrolling)                                   // 网格版式下位置归 GridLayoutGroup 管
            {
                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = Vector2.zero;
                rect.anchoredPosition = new Vector2(x + slotSize.x / 2f, 14 + slotSize.y / 2f);
            }
            var slot = go.GetComponent<OutGameFurnitureSlotView>();
            if (slot == null) return;
            HouseUIUtil.ApplyFallbackFont(go.transform);

            var state = stateGetter(entry.id);
            if (slot.thumb != null)
            {
                slot.thumb.sprite = entry.sprite;
                slot.thumb.enabled = state != FurnitureSlotState.Unknown && entry.sprite != null;
                slot.thumb.color = state == FurnitureSlotState.Locked ? new Color(.45f, .45f, .5f, .85f)
                    : state == FurnitureSlotState.Placed ? new Color(1, 1, 1, .35f)
                    : Color.white;
            }
            if (slot.nameLabel != null) slot.nameLabel.text = entry.displayName;
            // 已摆完的卡略微压淡即可——纸卡皮肤下压到近乎透明会让整张卡消失（2026-08-20）
            if (slot.background != null)
                slot.background.color = state == FurnitureSlotState.Placed
                    ? new Color(1, 1, 1, .55f) : Color.white;
            // 余量角标：复用原来的「已摆放」节点（家具库存说明 §5.6，零 Prefab 改动）。
            // 有余量报数字、没余量说明白，两态都显示——留空的话玩家看不出自己到底有几件
            if (slot.placedLabel != null)
            {
                var showCount = state == FurnitureSlotState.Available || state == FurnitureSlotState.Placed;
                slot.placedLabel.gameObject.SetActive(showCount);
                if (showCount)
                {
                    var remaining = remainingGetter != null ? remainingGetter(entry.id) : 0;
                    slot.placedLabel.text = remaining > 0 ? $"×{remaining}" : "已摆完";
                }
            }
            if (slot.lockMask != null) slot.lockMask.SetActive(state == FurnitureSlotState.Locked);
            if (slot.priceLabel != null && state == FurnitureSlotState.Locked)
                slot.priceLabel.text = $"可购买\n<color=#D4A46B>◈ {priceGetter(entry)}</color>";
            if (slot.unknownMask != null) slot.unknownMask.SetActive(state == FurnitureSlotState.Unknown);
            if (slot.unknownRequirement != null && state == FurnitureSlotState.Unknown)
                slot.unknownRequirement.text = $"声望 {unlockGetter(entry)} 解禁";

            var trigger = go.AddComponent<EventTrigger>();
            var press = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            var id = entry.id;
            // 左键 = 拖拽起手 / 弹购买窗；右键 = 半价出售（§5.5）。
            // 右键起手落在 HUD 上时相机不平移，见 FurnitureCameraRig.HandlePan 的守卫
            press.callback.AddListener(data =>
            {
                if (data is PointerEventData pointer && pointer.button == PointerEventData.InputButton.Right)
                    SellPressed?.Invoke(id);
                else
                    SlotPressed?.Invoke(id);
            });
            trigger.triggers.Add(press);
        }

        // ── 顶部状态 ──

        public void ShowToast(string message)
        {
            if (view == null || view.toastLabel == null) return;
            view.toastLabel.text = message;
            toastTween?.Kill();
            view.toastGroup.alpha = 0f;
            toastTween = DOTween.Sequence()
                .Append(view.toastGroup.DOFade(1f, .18f))
                .AppendInterval(2f)
                .Append(view.toastGroup.DOFade(0f, .35f))
                .SetTarget(view.toastGroup);
        }

        /// <summary>
        /// 刷新顶部数值条。第一行是三个全局值（货币 / 声望 / 全局装饰分），
        /// 第二行是**本房**装饰分与它换来的小费加成——装修 → 赚钱这条因果链
        /// 玩家感知不到就等于不存在（家具库存说明 §6.3）。两行写进同一个 creditLabel，零 Prefab 改动。
        /// </summary>
        public void SetEconomy(int currency, int reputation, int decorationScore, int roomDecorScore, int tipBonus)
        {
            if (view == null || view.creditLabel == null) return;
            view.creditLabel.text =
                $"<color=#D4A46B>◈ {currency:N0}</color>    <color=#74D8D1>声望 {reputation}</color>    <color=#E22D76>装饰分 {decorationScore}</color>\n" +
                $"<size=15><color=#E22D76>本房装饰分 {roomDecorScore}</color>　<color=#D4A46B>完成服务小费 +{tipBonus}</color></size>";
        }

        public void SetGridToggle(bool on)
        {
            if (view == null) return;
            if (view.gridToggleLabel != null) view.gridToggleLabel.text = on ? "隐藏网格" : "显示网格";
            if (view.gridToggleButton != null && view.gridToggleButton.targetGraphic is Image image)
                image.color = on ? new Color(.32f, .06f, .18f, .9f) : new Color(.025f, .025f, .04f, .8f);
        }

        public void SetInventoryDropHint(bool on)
        {
            if (view != null && view.dropHint != null)
                view.dropHint.color = new Color(.89f, .4f, .56f, on ? .16f : 0f);
        }

        public bool IsPointerOverInventory(Vector2 screenPosition)
        {
            if (chromeHidden) return false; // 隐藏界面时收纳栏不可见，不吃拖拽落点判定
            return view != null && view.inventoryRect != null &&
                   RectTransformUtility.RectangleContainsScreenPoint(view.inventoryRect, screenPosition, null);
        }

        /// <summary>拖拽布置中：顶部 UI 淡出让位（收纳栏保留——拖回收纳需要它作为落点）。</summary>
        public void SetDragDimming(bool on)
        {
            if (chromeHidden || view == null || view.topGroup == null) return;
            view.topGroup.DOKill();
            view.topGroup.DOFade(on ? .1f : 1f, .2f).SetTarget(view.topGroup);
            view.topGroup.blocksRaycasts = !on;
            view.topGroup.interactable = !on;
        }

        /// <summary>「隐藏界面」：顶部与收纳栏整体隐藏，只留右上角「显示界面」入口；再点恢复。</summary>
        public void SetChromeHidden(bool hidden)
        {
            if (view == null) return;
            chromeHidden = hidden;
            FadeGroup(view.topGroup, hidden ? 0f : 1f, !hidden);
            FadeGroup(view.inventoryGroup, hidden ? 0f : 1f, !hidden);
            FadeGroup(view.restoreGroup, hidden ? 1f : 0f, hidden);
        }

        private static void FadeGroup(CanvasGroup group, float alpha, bool interactable)
        {
            if (group == null) return;
            group.DOKill();
            group.DOFade(alpha, .22f).SetTarget(group);
            group.blocksRaycasts = interactable;
            group.interactable = interactable;
        }

        // ── 购买弹窗 ──

        public void ShowPurchasePopup(FurnitureEntry entry, int currency)
        {
            if (view == null || view.purchaseGroup == null) return;
            PopupOpen = true;
            popupIsSell = false;
            popupFurnitureId = entry.id;
            var price = priceGetter(entry);
            var enough = currency >= price;
            if (view.purchaseTitle != null) view.purchaseTitle.text = $"购买「{entry.displayName}」";
            if (view.purchaseDesc != null)
                view.purchaseDesc.text = enough
                    ? $"花费 <color=#D4A46B>◈ {price}</color>（当前 ◈ {currency:N0}）"
                    : $"需要 <color=#D4A46B>◈ {price}</color>，当前只有 ◈ {currency:N0}";
            if (view.purchaseConfirmButton != null) view.purchaseConfirmButton.interactable = enough;
            FadeGroup(view.purchaseGroup, 1f, true);
        }

        /// <summary>
        /// 出售确认弹窗（家具库存说明 §5.5）。**复用购买弹窗那套 Prefab 节点**——
        /// 它自 2026-08-14「仓库只展示已拥有」之后就成了死代码，正好拿来用，零 Prefab 改动。
        /// </summary>
        public void ShowSellPopup(FurnitureEntry entry, int refund, int remaining)
        {
            if (view == null || view.purchaseGroup == null) return;
            PopupOpen = true;
            popupIsSell = true;
            popupFurnitureId = entry.id;
            if (view.purchaseTitle != null) view.purchaseTitle.text = $"出售「{entry.displayName}」";
            if (view.purchaseDesc != null)
                view.purchaseDesc.text =
                    $"回收 <color=#D4A46B>◈ {refund}</color>（原价的一半）\n<size=15>库存余量 {remaining} → {remaining - 1}</size>";
            if (view.purchaseConfirmButton != null) view.purchaseConfirmButton.interactable = true;
            FadeGroup(view.purchaseGroup, 1f, true);
        }

        public void CloseUnlockPopup()
        {
            PopupOpen = false;
            popupFurnitureId = null;
            popupIsSell = false;
            if (view != null) FadeGroup(view.purchaseGroup, 0f, false);
        }

        public void Destroy()
        {
            toastTween?.Kill();
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
            view = null;
        }
    }
}

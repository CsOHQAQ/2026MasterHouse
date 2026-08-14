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
        /// <summary>可拖出摆放。</summary>
        Available,
        /// <summary>已摆放在房间中。</summary>
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
        /// <summary>槽位被按下（PointerDown，配合拖拽起手）。参数为家具 id。</summary>
        public event Action<string> SlotPressed;
        /// <summary>购买确认。参数为家具 id。</summary>
        public event Action<string> PurchaseConfirmed;

        public bool PopupOpen { get; private set; }

        private const int SlotsPerPage = 12;
        private const float SlotGap = 8f;

        /// <summary>页签顺序（下标对应 Prefab 的 tabButtons）。</summary>
        private static readonly FurnitureSurfaceType[] TabSurfaces =
        {
            FurnitureSurfaceType.Floor, FurnitureSurfaceType.Table, FurnitureSurfaceType.Wall,
        };

        private GameObject root;
        private OutGameFurnitureHudView view;
        private GameObject slotTemplate;
        private readonly List<GameObject> slotInstances = new List<GameObject>();
        private FurnitureTable table;
        private Func<string, FurnitureSlotState> stateGetter;
        /// <summary>售卖配置读取口（商店表，2026-08-13 拆表）：View 不摸表，由 Controller 注入（§11.4）。</summary>
        private Func<FurnitureEntry, int> priceGetter;
        private Func<FurnitureEntry, int> unlockGetter;
        /// <summary>页签槽位（Prefab 里的三个位置）：有页签隐藏时后面的自动往前补位，不留空洞。</summary>
        private Vector2[] tabSlotPositions;
        private FurnitureSurfaceType currentTab = FurnitureSurfaceType.Floor;
        private int page;
        private bool chromeHidden;
        private Tween toastTween;
        private string popupFurnitureId;

        public void Build(FurnitureTable table, Func<string, FurnitureSlotState> stateGetter,
            Func<FurnitureEntry, int> priceGetter, Func<FurnitureEntry, int> unlockGetter)
        {
            this.table = table;
            this.stateGetter = stateGetter;
            this.priceGetter = priceGetter;
            this.unlockGetter = unlockGetter;

            // 画布生命周期归代码，布局归 Prefab（§16.2）
            root = new GameObject("FurnitureModeHud", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 600;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;

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
            for (var i = 0; i < view.tabButtons.Length && i < TabSurfaces.Length; i++)
            {
                var surface = TabSurfaces[i];
                HouseUIUtil.BindButton(view.tabButtons[i], () => SelectTab(surface));
            }
            if (view.purchaseScrimButton != null)
            {
                view.purchaseScrimButton.onClick.RemoveAllListeners();
                view.purchaseScrimButton.onClick.AddListener(CloseUnlockPopup);
            }
            HouseUIUtil.BindButton(view.purchaseCancelButton, CloseUnlockPopup);
            HouseUIUtil.BindButton(view.purchaseConfirmButton, () =>
            {
                var id = popupFurnitureId;
                CloseUnlockPopup();
                if (!string.IsNullOrEmpty(id)) PurchaseConfirmed?.Invoke(id);
            });
            // 购买弹窗面板换全局底图（Secondary-bg，9 宫格）
            if (view.purchaseConfirmButton != null)
                HouseUIUtil.ApplyPanelSkin(view.purchaseConfirmButton.transform.parent.GetComponent<Image>());

            EnsureTabAvailable();
            RefreshInventory();
        }

        // ── 页签与分页 ──

        private List<FurnitureEntry> EntriesOf(FurnitureSurfaceType surface)
        {
            var result = new List<FurnitureEntry>();
            foreach (var entry in table.entries)
            {
                if (entry == null || !entry.Supports(surface)) continue; // 多选表面：同一家具可出现在多个页签
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
            foreach (var surface in TabSurfaces)
                if (EntriesOf(surface).Count > 0) { currentTab = surface; return; }
        }

        private void SelectTab(FurnitureSurfaceType surface)
        {
            if (currentTab == surface) return;
            currentTab = surface;
            page = 0;
            RefreshInventory();
        }

        private int PageCount => Mathf.Max(1, Mathf.CeilToInt(EntriesOf(currentTab).Count / (float)SlotsPerPage));

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
            for (var i = 0; i < view.tabButtons.Length && i < TabSurfaces.Length; i++)
            {
                var surface = TabSurfaces[i];
                var hasAny = EntriesOf(surface).Count > 0;
                if (view.tabButtons[i] != null)
                {
                    view.tabButtons[i].gameObject.SetActive(hasAny);
                    if (hasAny)
                        ((RectTransform)view.tabButtons[i].transform).anchoredPosition =
                            tabSlotPositions[Mathf.Min(visibleSlot++, tabSlotPositions.Length - 1)];
                }
                var selected = surface == currentTab;
                if (view.tabBackgrounds[i] != null)
                    view.tabBackgrounds[i].color = selected ? new Color(.32f, .06f, .18f, .95f) : new Color(.025f, .025f, .04f, .92f);
                if (view.tabLabels[i] != null)
                    view.tabLabels[i].color = selected ? HouseUIUtil.White : new Color(1, 1, 1, .55f);
            }

            var entries = EntriesOf(currentTab);
            var pageCount = PageCount;
            if (page >= pageCount) page = pageCount - 1;
            if (view.pageLabel != null) view.pageLabel.text = $"{page + 1} / {pageCount}";

            var slotSize = ((RectTransform)slotTemplate.transform).sizeDelta;
            var start = page * SlotsPerPage;
            var cursor = 16f;
            for (var i = start; i < entries.Count && i < start + SlotsPerPage; i++)
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
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.anchoredPosition = new Vector2(x + slotSize.x / 2f, 14 + slotSize.y / 2f);
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
            if (slot.background != null && state == FurnitureSlotState.Placed)
                slot.background.color = new Color(1, 1, 1, .02f);
            if (slot.placedLabel != null) slot.placedLabel.gameObject.SetActive(state == FurnitureSlotState.Placed);
            if (slot.lockMask != null) slot.lockMask.SetActive(state == FurnitureSlotState.Locked);
            if (slot.priceLabel != null && state == FurnitureSlotState.Locked)
                slot.priceLabel.text = $"可购买\n<color=#D4A46B>◈ {priceGetter(entry)}</color>";
            if (slot.unknownMask != null) slot.unknownMask.SetActive(state == FurnitureSlotState.Unknown);
            if (slot.unknownRequirement != null && state == FurnitureSlotState.Unknown)
                slot.unknownRequirement.text = $"声望 {unlockGetter(entry)} 解禁";

            var trigger = go.AddComponent<EventTrigger>();
            var press = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            var id = entry.id;
            press.callback.AddListener(_ => SlotPressed?.Invoke(id));
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

        /// <summary>刷新三个流通数值的显示：货币 / 声望 / 装饰分。</summary>
        public void SetEconomy(int currency, int reputation, int decorationScore)
        {
            if (view != null && view.creditLabel != null)
                view.creditLabel.text =
                    $"<color=#D4A46B>◈ {currency:N0}</color>    <color=#74D8D1>声望 {reputation}</color>    <color=#E22D76>装饰分 {decorationScore}</color>";
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

        public void CloseUnlockPopup()
        {
            PopupOpen = false;
            popupFurnitureId = null;
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

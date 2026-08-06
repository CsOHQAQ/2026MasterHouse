using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using F = MasterPotion.OutGameUIFactory;

namespace MasterPotion
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
    /// 家具模式 HUD：顶部工具条 + 底部收纳栏 + 解锁弹窗 + 提示条。
    /// 原型阶段为运行时 uGUI；交互验收后再按项目规范固化为 Prefab。
    /// </summary>
    public sealed class FurnitureRoomHud
    {
        public event Action ExitClicked;
        public event Action GridToggleClicked;
        /// <summary>槽位被按下（PointerDown，配合拖拽起手）。参数为家具 id。</summary>
        public event Action<string> SlotPressed;
        /// <summary>购买确认。参数为家具 id。</summary>
        public event Action<string> PurchaseConfirmed;

        public bool PopupOpen { get; private set; }

        private GameObject root;
        private RectTransform inventoryRect;
        private RectTransform slotsRoot;
        private Text creditLabel;
        private Text gridToggleLabel;
        private Image gridToggleBackground;
        private Image inventoryHighlight;
        private RectTransform popupRoot;
        private Text toastLabel;
        private CanvasGroup toastGroup;
        private Tween toastTween;
        private FurnitureTable table;
        private Func<string, FurnitureSlotState> stateGetter;

        public void Build(FurnitureTable table, Func<string, FurnitureSlotState> stateGetter)
        {
            this.table = table;
            this.stateGetter = stateGetter;

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

            BuildTopBar();
            BuildInventory();
            BuildToast();
        }

        private void BuildTopBar()
        {
            var title = F.Panel(root.transform, "Title", new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(280, -74), new Vector2(500, 104), new Color(.03f, .03f, .05f, .8f));
            F.Outline(title.gameObject, new Color(.85f, .15f, .45f, .4f), new Vector2(1, -1));
            F.Label(title.transform, "Eyebrow", "FURNITURE MODE", 13, F.Rose,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(150, -22), new Vector2(280, 24));
            F.Label(title.transform, "Name", "家具摆放", 27, F.White,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(150, -52), new Vector2(280, 36), TextAnchor.MiddleLeft, FontStyle.Bold);
            F.Label(title.transform, "Hint", "拖拽摆放 · 拖回下方收纳 · 双击快速收纳 · ESC 退出", 14, new Color(1, 1, 1, .55f),
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(245, -84), new Vector2(470, 24));

            var creditPanel = F.Panel(root.transform, "Economy", new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-680, -60), new Vector2(460, 64), new Color(.03f, .03f, .05f, .8f));
            creditLabel = F.Label(creditPanel.transform, "Value", string.Empty, 20, F.White,
                TextAnchor.MiddleCenter, FontStyle.Bold);

            var toggle = F.Button(root.transform, "GridToggle", "显示网格", () => GridToggleClicked?.Invoke(),
                new Vector2(1, 1), new Vector2(1, 1), new Vector2(-360, -60), new Vector2(160, 64),
                new Color(.025f, .025f, .04f, .8f), F.White, 20);
            gridToggleBackground = toggle.targetGraphic as Image;
            gridToggleLabel = toggle.GetComponentInChildren<Text>();

            F.Button(root.transform, "Exit", "完成 · ESC", () => ExitClicked?.Invoke(),
                new Vector2(1, 1), new Vector2(1, 1), new Vector2(-160, -60), new Vector2(200, 64),
                new Color(.32f, .06f, .18f, .9f), F.White, 20);
        }

        private void BuildInventory()
        {
            var groups = new List<KeyValuePair<FurnitureSurfaceType, List<FurnitureEntry>>>();
            foreach (FurnitureSurfaceType surface in Enum.GetValues(typeof(FurnitureSurfaceType)))
            {
                var list = new List<FurnitureEntry>();
                foreach (var entry in table.entries)
                    if (entry != null && entry.surface == surface) list.Add(entry);
                if (list.Count > 0) groups.Add(new KeyValuePair<FurnitureSurfaceType, List<FurnitureEntry>>(surface, list));
            }

            const float slotWidth = 104f;
            const float slotGap = 8f;
            const float groupGap = 26f;
            var width = 32f;
            foreach (var group in groups) width += group.Value.Count * (slotWidth + slotGap) - slotGap + groupGap;
            width -= groupGap;

            var panel = F.Panel(root.transform, "Inventory", new Vector2(.5f, 0), new Vector2(.5f, 0),
                new Vector2(0, 96), new Vector2(width, 168), new Color(.03f, .03f, .05f, .84f));
            inventoryRect = panel.rectTransform;
            F.Outline(panel.gameObject, new Color(1, 1, 1, .12f), new Vector2(1, -1));
            inventoryHighlight = F.StretchPanel(panel.transform, "DropHint", new Color(.89f, .4f, .56f, 0f));
            inventoryHighlight.raycastTarget = false;
            slotsRoot = F.Stretch(panel.transform, "Slots");
            RefreshInventory();
        }

        /// <summary>重建收纳栏槽位（数量少，直接重建最稳）。</summary>
        public void RefreshInventory()
        {
            if (slotsRoot == null) return;
            for (var i = slotsRoot.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(slotsRoot.GetChild(i).gameObject);

            const float slotWidth = 104f;
            const float slotHeight = 122f;
            const float slotGap = 8f;
            const float groupGap = 26f;
            var cursor = 16f;
            foreach (FurnitureSurfaceType surface in Enum.GetValues(typeof(FurnitureSurfaceType)))
            {
                var entries = new List<FurnitureEntry>();
                foreach (var entry in table.entries)
                    if (entry != null && entry.surface == surface) entries.Add(entry);
                if (entries.Count == 0) continue;

                var groupWidth = entries.Count * (slotWidth + slotGap) - slotGap;
                F.Label(slotsRoot, "Group" + surface, SurfaceName(surface), 13, F.Rose,
                    new Vector2(0, 1), new Vector2(0, 1), new Vector2(cursor + groupWidth / 2f, -16), new Vector2(groupWidth, 22),
                    TextAnchor.MiddleLeft);
                for (var i = 0; i < entries.Count; i++)
                    BuildSlot(entries[i], cursor + i * (slotWidth + slotGap), slotWidth, slotHeight);
                cursor += groupWidth + groupGap;
            }
        }

        private void BuildSlot(FurnitureEntry entry, float x, float width, float height)
        {
            var state = stateGetter(entry.id);
            var slot = F.Panel(slotsRoot, "Slot_" + entry.id, new Vector2(0, 0), new Vector2(0, 0),
                new Vector2(x + width / 2f, 14 + height / 2f), new Vector2(width, height), new Color(1, 1, 1, .05f));
            F.Outline(slot.gameObject, new Color(1, 1, 1, .14f), new Vector2(1, -1));
            slot.raycastTarget = true;

            var thumb = F.Rect(slot.transform, "Thumb", new Vector2(.5f, 1), new Vector2(.5f, 1),
                new Vector2(0, -46), new Vector2(84, 76));
            var image = thumb.gameObject.AddComponent<Image>();
            image.sprite = entry.sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            if (entry.sprite == null) image.color = new Color(1, 1, 1, .1f);

            F.Label(slot.transform, "Name", entry.displayName, 15, F.White,
                new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 18), new Vector2(width - 8, 24), TextAnchor.MiddleCenter);

            if (state == FurnitureSlotState.Unknown)
            {
                // 声望未达到解禁阈值：按文档呈「？」未知态
                image.enabled = false;
                var mask = F.StretchPanel(slot.transform, "UnknownMask", new Color(.05f, .03f, .06f, .6f));
                mask.raycastTarget = false;
                F.Label(slot.transform, "Mark", "？", 38, new Color(1, 1, 1, .55f),
                    new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -44), new Vector2(width - 8, 52),
                    TextAnchor.MiddleCenter, FontStyle.Bold);
                F.Label(slot.transform, "Req", $"声望 {entry.unlockReputation} 解禁", 13, new Color(1, 1, 1, .55f),
                    new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 40), new Vector2(width - 6, 20), TextAnchor.MiddleCenter);
            }
            else if (state == FurnitureSlotState.Locked)
            {
                image.color = new Color(.45f, .45f, .5f, .85f);
                var mask = F.StretchPanel(slot.transform, "LockMask", new Color(.05f, .03f, .06f, .45f));
                mask.raycastTarget = false;
                F.Label(slot.transform, "Price", $"可购买\n<color=#D4A46B>◈ {entry.price}</color>", 15, F.White,
                    new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 10), new Vector2(width - 8, 48),
                    TextAnchor.MiddleCenter, FontStyle.Bold);
            }
            else if (state == FurnitureSlotState.Placed)
            {
                slot.color = new Color(1, 1, 1, .02f);
                image.color = new Color(1, 1, 1, .35f);
                F.Label(slot.transform, "State", "已摆放", 13, new Color(1, 1, 1, .5f),
                    new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -12), new Vector2(width - 8, 20), TextAnchor.MiddleCenter);
            }

            var trigger = slot.gameObject.AddComponent<EventTrigger>();
            var press = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            var id = entry.id;
            press.callback.AddListener(_ => SlotPressed?.Invoke(id));
            trigger.triggers.Add(press);
        }

        private void BuildToast()
        {
            var rect = F.Rect(root.transform, "Toast", new Vector2(.5f, 0), new Vector2(.5f, 0),
                new Vector2(0, 300), new Vector2(680, 52));
            var background = rect.gameObject.AddComponent<Image>();
            background.sprite = F.WhiteSprite;
            background.color = new Color(.05f, .04f, .07f, .92f);
            background.raycastTarget = false;
            F.Outline(rect.gameObject, new Color(.85f, .15f, .45f, .5f), new Vector2(1, -1));
            toastLabel = F.Label(rect, "Label", string.Empty, 18, F.White, TextAnchor.MiddleCenter);
            toastGroup = F.Group(rect.gameObject, 0f);
        }

        public void ShowToast(string message)
        {
            if (toastLabel == null) return;
            toastLabel.text = message;
            toastTween?.Kill();
            toastGroup.alpha = 0f;
            toastTween = DOTween.Sequence()
                .Append(toastGroup.DOFade(1f, .18f))
                .AppendInterval(2f)
                .Append(toastGroup.DOFade(0f, .35f))
                .SetTarget(toastGroup);
        }

        /// <summary>刷新三个流通数值的显示：货币 / 声望 / 装饰分。</summary>
        public void SetEconomy(int currency, int reputation, int decorationScore)
        {
            if (creditLabel != null)
                creditLabel.text =
                    $"<color=#D4A46B>◈ {currency:N0}</color>    <color=#74D8D1>声望 {reputation}</color>    <color=#E22D76>装饰分 {decorationScore}</color>";
        }

        public void SetGridToggle(bool on)
        {
            if (gridToggleLabel != null) gridToggleLabel.text = on ? "隐藏网格" : "显示网格";
            if (gridToggleBackground != null)
                gridToggleBackground.color = on ? new Color(.32f, .06f, .18f, .9f) : new Color(.025f, .025f, .04f, .8f);
        }

        public void SetInventoryDropHint(bool on)
        {
            if (inventoryHighlight != null)
                inventoryHighlight.color = new Color(.89f, .4f, .56f, on ? .16f : 0f);
        }

        public bool IsPointerOverInventory(Vector2 screenPosition)
        {
            return inventoryRect != null &&
                   RectTransformUtility.RectangleContainsScreenPoint(inventoryRect, screenPosition, null);
        }

        public void ShowPurchasePopup(FurnitureEntry entry, int currency)
        {
            CloseUnlockPopup();
            PopupOpen = true;
            var scrim = F.StretchPanel(root.transform, "PurchaseScrim", new Color(0, 0, 0, .45f));
            scrim.raycastTarget = true;
            popupRoot = scrim.rectTransform;
            var scrimButton = scrim.gameObject.AddComponent<Button>();
            scrimButton.transition = Selectable.Transition.None;
            scrimButton.onClick.AddListener(CloseUnlockPopup);

            var panel = F.Panel(scrim.transform, "Panel", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                new Vector2(0, 40), new Vector2(430, 240), new Color(.06f, .045f, .08f, .97f));
            F.Outline(panel.gameObject, new Color(.85f, .15f, .45f, .55f), new Vector2(1, -1));
            var enough = currency >= entry.price;
            F.Label(panel.transform, "Name", $"购买「{entry.displayName}」", 24, F.White,
                new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -46), new Vector2(390, 36),
                TextAnchor.MiddleCenter, FontStyle.Bold);
            F.Label(panel.transform, "Desc",
                enough ? $"花费 <color=#D4A46B>◈ {entry.price}</color>（当前 ◈ {currency:N0}）"
                       : $"需要 <color=#D4A46B>◈ {entry.price}</color>，当前只有 ◈ {currency:N0}",
                18, new Color(1, 1, 1, .72f),
                new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -96), new Vector2(390, 34), TextAnchor.MiddleCenter);
            var confirm = F.Button(panel.transform, "Confirm", "购买", () =>
                {
                    var id = entry.id;
                    CloseUnlockPopup();
                    PurchaseConfirmed?.Invoke(id);
                },
                new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(-95, 52), new Vector2(160, 58),
                new Color(.32f, .06f, .18f, .95f), F.White, 21);
            confirm.interactable = enough;
            F.Button(panel.transform, "Cancel", "取消", CloseUnlockPopup,
                new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(95, 52), new Vector2(160, 58),
                new Color(1, 1, 1, .08f), F.White, 21);
        }

        public void CloseUnlockPopup()
        {
            PopupOpen = false;
            if (popupRoot != null) UnityEngine.Object.Destroy(popupRoot.gameObject);
            popupRoot = null;
        }

        public void Destroy()
        {
            toastTween?.Kill();
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
        }

        private static string SurfaceName(FurnitureSurfaceType surface)
        {
            switch (surface)
            {
                case FurnitureSurfaceType.Floor: return "地面家具";
                case FurnitureSurfaceType.Table: return "桌面家具";
                default: return "壁挂家具";
            }
        }
    }
}

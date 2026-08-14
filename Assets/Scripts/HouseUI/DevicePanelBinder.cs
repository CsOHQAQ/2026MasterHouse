using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 家具图鉴面板绑定（2026-08-14 从原型设备图鉴改造）：按房间列出**当前实际摆放**的家具。
    /// 数据源 FurniturePlacementQuery（会话布局回落默认摆放，与需求判定同口径）+ FurnitureTable；
    /// 卡片走模板 Prefab 实例化（§16.2），详情区展示家具描述与装饰分，「前往摆放」直达家具模式。
    /// 面板内的房间选择只影响面板自身（打开时取 Hub 当前房间）。
    /// </summary>
    public static class DevicePanelBinder
    {
        private static int selectedRoom;
        private static int selectedIndex;

        public static void Bind(OutGameDevicePanelView view, HubPage page)
        {
            if (view == null) return;
            selectedRoom = page.RoomIndex;
            selectedIndex = 0;
            Refresh(view, page);
        }

        private static void Refresh(OutGameDevicePanelView view, HubPage page)
        {
            var codex = GameManager.Instance.CodexTable;
            var furnitureTable = GameManager.Instance.FurnitureTable;

            for (var i = 0; i < 4 && i < codex.rooms.Count; i++)
            {
                var index = i;
                var room = codex.rooms[i];
                if (view.roomLabels != null && i < view.roomLabels.Length && view.roomLabels[i] != null)
                    view.roomLabels[i].text = room.displayName +
                        $"\n<size=12>{FurniturePlacementQuery.FurnitureIdsIn(i).Count} 件家具</size>";
                if (view.roomBackgrounds != null && i < view.roomBackgrounds.Length && view.roomBackgrounds[i] != null)
                    view.roomBackgrounds[i].color = selectedRoom == i ? HouseUIUtil.Wine : new Color(1, 1, 1, .035f);
                if (view.roomButtons != null && i < view.roomButtons.Length && view.roomButtons[i] != null)
                    HouseUIUtil.BindButton(view.roomButtons[i], () =>
                    {
                        selectedRoom = index;
                        selectedIndex = 0;
                        Refresh(view, page);
                    });
            }

            // 选中房间当前摆放的家具（含桌面家具；同款多件如实重复列出）
            var placed = new List<FurnitureEntry>();
            foreach (var id in FurniturePlacementQuery.FurnitureIdsIn(selectedRoom))
            {
                var entry = furnitureTable != null ? furnitureTable.Find(id) : null;
                if (entry != null) placed.Add(entry);
            }

            if (view.deviceCardsRoot != null)
            {
                for (var i = view.deviceCardsRoot.childCount - 1; i >= 0; i--)
                    Object.Destroy(view.deviceCardsRoot.GetChild(i).gameObject);
                var template = Resources.Load<GameObject>(OutGamePrefabResourcePaths.DeviceCard);
                if (template == null)
                {
                    Debug.LogError("[HouseUI] 家具图鉴卡模板 Prefab 缺失（§16.2）：" + OutGamePrefabResourcePaths.DeviceCard);
                }
                else
                {
                    for (var i = 0; i < placed.Count; i++)
                    {
                        var index = i;
                        var entry = placed[i];
                        var instance = Object.Instantiate(template, view.deviceCardsRoot, false);
                        instance.name = "Furniture" + i;
                        var card = instance.GetComponent<DeviceCardView>();
                        if (card == null) continue;
                        var rect = (RectTransform)instance.transform;
                        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1);
                        // 3 列网格，超出往下换行
                        rect.anchoredPosition = new Vector2(-120 + i % 3 * 270, -155 - i / 3 * 290);
                        if (card.thumb != null)
                        {
                            var hasSprite = entry.sprite != null && entry.sprite.texture != null;
                            card.thumb.gameObject.SetActive(hasSprite);
                            if (hasSprite)
                            {
                                card.thumb.texture = entry.sprite.texture;
                                var fitter = card.thumb.GetComponent<UnityEngine.UI.AspectRatioFitter>();
                                if (fitter != null && entry.sprite.bounds.size.y > 0f)
                                    fitter.aspectRatio = entry.sprite.bounds.size.x / entry.sprite.bounds.size.y;
                            }
                        }
                        if (card.label != null)
                            card.label.text = $"{entry.displayName}\n<size=13>{entry.category} · 装饰分 {entry.decorationScore}</size>";
                        if (card.background != null)
                            card.background.color = selectedIndex == i ? new Color(.38f, .08f, .24f, .75f) : new Color(1, 1, 1, .045f);
                        HouseUIUtil.BindButton(card.button, () =>
                        {
                            selectedIndex = index;
                            Refresh(view, page);
                        });
                    }
                    HouseUIUtil.ApplyFallbackFont(view.deviceCardsRoot);
                }
            }

            if (placed.Count == 0)
            {
                if (view.recipeText != null)
                    view.recipeText.text = "<size=13>当前房间</size>\n<size=24>还没有摆放家具</size>\n去商店买点什么，再进家具模式摆出来吧。";
                if (view.makeButton != null)
                {
                    if (view.makeLabel != null) view.makeLabel.text = "前往摆放";
                    HouseUIUtil.BindButton(view.makeButton, page.GoFurnishFromPanel);
                    view.makeButton.interactable = true;
                }
                // 房间没家具时无可修之物，「修理」禁用置灰
                if (view.repairButton != null) view.repairButton.interactable = false;
                return;
            }

            var chosen = placed[Mathf.Clamp(selectedIndex, 0, placed.Count - 1)];
            if (view.recipeText != null)
                view.recipeText.text = $"<size=13>当前家具</size>\n<size=30>{chosen.displayName}</size>\n" +
                    $"{chosen.category} · 装饰分 {chosen.decorationScore}\n\n" +
                    (string.IsNullOrEmpty(chosen.description) ? "还没有人为它写下介绍……" : chosen.description);
            if (view.makeButton != null)
            {
                if (view.makeLabel != null) view.makeLabel.text = "前往摆放";
                var background = view.makeButton.targetGraphic as UnityEngine.UI.Image;
                if (background != null) background.color = HouseUIUtil.Wine;
                HouseUIUtil.BindButton(view.makeButton, page.GoFurnishFromPanel);
                view.makeButton.interactable = true;
            }
            // 「前往修理」（2026-08-14）：随所选家具刷新；家具修理业务未落地，先占位提示
            if (view.repairButton != null)
            {
                if (view.repairLabel != null) view.repairLabel.text = "前往修理";
                var chosenName = chosen.displayName;
                HouseUIUtil.BindButton(view.repairButton, () =>
                    page.Toast($"「{chosenName}」暂时不需要修理 · 修理功能开发中"));
                view.repairButton.interactable = true;
            }
        }
    }
}

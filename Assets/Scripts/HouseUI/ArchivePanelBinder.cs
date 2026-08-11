using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 档案面板绑定：叙事家具/世界与角色两 tab；档案卡用模板 Prefab 实例化（§16.2）。
    /// 详情区动作按钮（放入房间/迷雾半径/追踪）是选中项的上下文控件，运行时生成。
    /// </summary>
    public static class ArchivePanelBinder
    {
        private static bool showWorld;
        private static int selectedIndex;
        private static int fogRadius = 5;

        public static void Bind(OutGameArchivePanelView view, HubPage page)
        {
            if (view == null) return;
            for (var i = 0; i < 2; i++)
            {
                var toWorld = i == 1;
                if (view.tabButtons != null && i < view.tabButtons.Length && view.tabButtons[i] != null)
                    HouseUIUtil.BindButton(view.tabButtons[i], () =>
                    {
                        showWorld = toWorld;
                        selectedIndex = 0;
                        Refresh(view, page);
                    });
            }
            Refresh(view, page);
        }

        private static void Refresh(OutGameArchivePanelView view, HubPage page)
        {
            for (var i = 0; i < 2; i++)
            {
                if (view.tabBackgrounds != null && i < view.tabBackgrounds.Length && view.tabBackgrounds[i] != null)
                    view.tabBackgrounds[i].color = showWorld == (i == 1) ? HouseUIUtil.Wine : new Color(1, 1, 1, .04f);
            }

            var items = new List<CodexEntryDef>();
            GameManager.Instance.CodexTable.GetArchives(
                showWorld ? ECodexArchiveCategory.World : ECodexArchiveCategory.NarrativeFurniture, items);
            if (items.Count == 0) return;
            selectedIndex = Mathf.Clamp(selectedIndex, 0, items.Count - 1);

            if (view.gridRoot != null)
            {
                for (var i = view.gridRoot.childCount - 1; i >= 0; i--)
                    Object.Destroy(view.gridRoot.GetChild(i).gameObject);
                var template = Resources.Load<GameObject>(OutGamePrefabResourcePaths.ArchiveCard);
                if (template == null)
                {
                    Debug.LogError("[HouseUI] 档案卡模板 Prefab 缺失（§16.2）：" + OutGamePrefabResourcePaths.ArchiveCard);
                }
                else
                {
                    for (var i = 0; i < items.Count; i++)
                    {
                        var index = i;
                        var item = items[i];
                        var instance = Object.Instantiate(template, view.gridRoot, false);
                        instance.name = "Archive" + i;
                        var card = instance.GetComponent<ArchiveCardView>();
                        if (card == null) continue;
                        var rect = (RectTransform)instance.transform;
                        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
                        rect.anchoredPosition = new Vector2(135 + i % 2 * 235, -165 - i / 2 * 235);
                        if (card.label != null)
                            card.label.text = $"0{i + 1} / {item.type}\n{item.displayName}\n<size=13>{item.owner}</size>";
                        if (card.background != null)
                            card.background.color = selectedIndex == i ? new Color(.42f, .08f, .28f, .72f) : new Color(1, 1, 1, .04f);
                        if (card.art != null) card.art.texture = Resources.Load<Texture2D>(item.imagePath);
                        HouseUIUtil.BindButton(card.button, () =>
                        {
                            selectedIndex = index;
                            Refresh(view, page);
                        });
                    }
                    HouseUIUtil.ApplyFallbackFont(view.gridRoot);
                }
            }

            var selected = items[selectedIndex];
            if (view.detailPreview != null) view.detailPreview.texture = Resources.Load<Texture2D>(selected.imagePath);
            if (view.detailText != null)
                view.detailText.text = $"<size=13>{selected.type} · {selected.owner}</size>\n<size=32>{selected.displayName}</size>\n{(selected.id == "map" ? $"角色移动时，以当前位置为中心永久揭开迷雾。当前探索半径 {fogRadius} 米。" : selected.note)}";

            if (view.actionRoot == null) return;
            for (var i = view.actionRoot.childCount - 1; i >= 0; i--)
                Object.Destroy(view.actionRoot.GetChild(i).gameObject);
            if (!showWorld)
            {
                HouseUIRuntime.Button(view.actionRoot, "Place", "放入房间", () =>
                    {
                        HubPage.PlacedFurnitureId = selected.id;
                        page.Toast(selected.displayName + " 已加入访客房间快捷栏");
                    },
                    new Vector2(.5f, 0), new Vector2(0, 45), new Vector2(300, 62), HouseUIUtil.Wine, HouseUIUtil.White, 20);
            }
            else if (selected.id == "map")
            {
                for (var i = 0; i < 4; i++)
                {
                    var radius = (i + 1) * 5;
                    HouseUIRuntime.Button(view.actionRoot, "Radius" + radius, radius + "m", () =>
                        {
                            fogRadius = radius;
                            Refresh(view, page);
                        },
                        new Vector2(.5f, 0), new Vector2(-180 + i * 120, 45), new Vector2(105, 55),
                        fogRadius == radius ? HouseUIUtil.Wine : new Color(1, 1, 1, .04f), HouseUIUtil.White, 17);
                }
            }
            else
            {
                HouseUIRuntime.Button(view.actionRoot, "Track", "追踪这份资料",
                    () => page.Toast(selected.displayName + " 已设为追踪资料"),
                    new Vector2(.5f, 0), new Vector2(0, 45), new Vector2(300, 62), new Color(1, 1, 1, .04f), HouseUIUtil.White, 19);
            }
            HouseUIUtil.ApplyFallbackFont(view.actionRoot);
        }
    }
}

using UnityEngine;

namespace MasterHouse
{
    /// <summary>Hub 右侧功能 dock 绑定：四个面板入口（3.5c 接面板栈）+ 运行时追加的「家具摆放」入口。</summary>
    public sealed class HubRightDockBinder
    {
        public void Bind(OutGameHubRightDockView dock, HubPage page, Transform chromeRoot)
        {
            var icons = new[] { "器", "记", "录", "集" };
            var labels = new[] { "设备图鉴", "日记", "通讯录", "档案" };
            // 通讯录降级为占位页（§16.8），归 3.6；其余三个已迁移
            var panels = new EHousePanel?[] { EHousePanel.Device, EHousePanel.Journal, null, EHousePanel.Archive };
            for (var i = 0; i < dock.entries.Length && i < labels.Length; i++)
            {
                var label = labels[i];
                var panel = panels[i];
                dock.entries[i].icon.text = icons[i];
                dock.entries[i].label.text = label;
                HouseUIUtil.BindButton(dock.entries[i].button, () =>
                {
                    if (panel.HasValue) page.OpenPanel(panel.Value);
                    else page.OpenPanelPlaceholder(label);
                });
            }

            // 「家具摆放」入口：追加在 dock 下方的运行时按钮（不改动 Hub Prefab 既有布局，与旧壳一致）
            HouseUIRuntime.Button(chromeRoot, "FurnitureMode", "家    家具摆放", page.OpenFurnitureMode,
                new Vector2(1, .5f), new Vector2(-120, -262), new Vector2(205, 78),
                new Color(.32f, .06f, .18f, .86f), HouseUIUtil.White, 20, TextAnchor.MiddleLeft);
        }
    }
}

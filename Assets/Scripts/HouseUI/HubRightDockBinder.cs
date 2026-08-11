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
            // 通讯录为统一占位页（§16.8 明示豁免）
            var panels = new[] { EHousePanel.Device, EHousePanel.Journal, EHousePanel.Contacts, EHousePanel.Archive };
            for (var i = 0; i < dock.entries.Length && i < labels.Length; i++)
            {
                var panel = panels[i];
                dock.entries[i].icon.text = icons[i];
                dock.entries[i].label.text = labels[i];
                HouseUIUtil.BindButton(dock.entries[i].button, () => page.OpenPanel(panel));
            }

            // 「家具摆放」入口：追加在 dock 下方的运行时按钮（不改动 Hub Prefab 既有布局，与旧壳一致）
            HouseUIRuntime.Button(chromeRoot, "FurnitureMode", "家    家具摆放", page.OpenFurnitureMode,
                new Vector2(1, .5f), new Vector2(-120, -262), new Vector2(205, 78),
                new Color(.32f, .06f, .18f, .86f), HouseUIUtil.White, 20, TextAnchor.MiddleLeft);

            // 「结束今天」入口（§7 日结，周制退役）：常驻可点，可用性（场上无未处理访客）在 HubPage.TryEndDay 校验
            HouseUIRuntime.Button(chromeRoot, "EndDay", "结    结束今天", page.TryEndDay,
                new Vector2(1, .5f), new Vector2(-120, -350), new Vector2(205, 78),
                new Color(.06f, .18f, .32f, .86f), HouseUIUtil.White, 20, TextAnchor.MiddleLeft);
        }
    }
}

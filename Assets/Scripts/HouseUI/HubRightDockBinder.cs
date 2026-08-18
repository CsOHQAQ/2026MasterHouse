using UnityEngine;

namespace MasterHouse
{
    /// <summary>Hub 右侧功能 dock 绑定：四个面板入口 + 「家具摆放」「结束今天」两个动作入口。
    /// 动作入口已收编进 HubRightDock Prefab（2026-08-11，可在 Prefab 模式调整位置）；
    /// 旧版 Prefab 尚未经生成器修复时回退为运行时按钮，保证功能不缺。</summary>
    public sealed class HubRightDockBinder
    {
        public void Bind(OutGameHubRightDockView dock, HubPage page, Transform chromeRoot)
        {
            var icons = new[] { "器", "记", "录", "集" };
            var labels = new[] { "家具图鉴", "日记", "通讯录", "档案" };
            var panels = new[] { EHousePanel.Device, EHousePanel.Journal, EHousePanel.Contacts, EHousePanel.Archive };
            // 日记/通讯录暂未开放（2026-08-14）：置灰示意，点击提示开发中。
            // 档案于 2026-08-18 开放：直接进访客图鉴（旧的叙事资源档案面板内容未完工，先不外露）
            var developing = new[] { false, true, true, false };
            for (var i = 0; i < dock.entries.Length && i < labels.Length; i++)
            {
                var panel = panels[i];
                var locked = developing[i];
                var toCodex = panel == EHousePanel.Archive;
                dock.entries[i].icon.text = icons[i];
                dock.entries[i].label.text = locked ? labels[i] + "\n<size=11>功能开发中</size>" : labels[i];
                var tint = locked ? new Color(1, 1, 1, .38f) : Color.white;
                dock.entries[i].icon.color = tint;
                dock.entries[i].label.color = tint;
                if (locked)
                    HouseUIUtil.BindButton(dock.entries[i].button, () => page.Toast("功能开发中，敬请期待"), ESfx.None);
                else if (toCodex)
                    HouseUIUtil.BindButton(dock.entries[i].button, page.OpenCodex);
                else
                    HouseUIUtil.BindButton(dock.entries[i].button, () => page.OpenPanel(panel));
                HouseUIUtil.ApplyPanelSkin(dock.entries[i].background, locked ? .35f : .8f, 2.5f); // 置灰=更透的 common 框
            }

            if (dock.furnitureButton != null)
            {
                HouseUIUtil.BindButton(dock.furnitureButton, page.OpenFurnitureMode);
                HouseUIUtil.ApplyPanelSkin(dock.furnitureButton.targetGraphic as UnityEngine.UI.Image, .85f, 2.5f);
            }
            else
            {
                // 旧版 Prefab 兜底（生成器聚焦修复后即走上面的 Prefab 按钮）
                HouseUIRuntime.Button(chromeRoot, "FurnitureMode", "家    家具摆放", page.OpenFurnitureMode,
                    new Vector2(1, .5f), new Vector2(-120, -262), new Vector2(205, 78),
                    new Color(.32f, .06f, .18f, .86f), HouseUIUtil.White, 20, TextAnchor.MiddleLeft);
            }

            if (dock.endDayButton != null)
            {
                HouseUIUtil.BindButton(dock.endDayButton, page.TryEndDay);
                HouseUIUtil.ApplyPanelSkin(dock.endDayButton.targetGraphic as UnityEngine.UI.Image, .85f, 2.5f);
            }
            else
            {
                HouseUIRuntime.Button(chromeRoot, "EndDay", "结    结束今天", page.TryEndDay,
                    new Vector2(1, .5f), new Vector2(-120, -350), new Vector2(205, 78),
                    new Color(.06f, .18f, .32f, .86f), HouseUIUtil.White, 20, TextAnchor.MiddleLeft);
            }
        }
    }
}

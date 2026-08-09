using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 设备面板绑定：房间列表 + 设备卡（模板 Prefab 实例化，§16.2）+ 当前设备详情。
    /// 面板内的房间选择只影响面板自身（打开时取 Hub 当前房间；旧壳会顺带改 Hub 房间下标是历史 quirk，不保留）。
    /// </summary>
    public static class DevicePanelBinder
    {
        private static int selectedRoom;
        private static int selectedDevice;

        public static void Bind(OutGameDevicePanelView view, HubPage page)
        {
            if (view == null) return;
            selectedRoom = page.RoomIndex;
            selectedDevice = 0;
            Refresh(view, page);
        }

        private static void Refresh(OutGameDevicePanelView view, HubPage page)
        {
            var codex = GameManager.Instance.CodexTable;
            for (var i = 0; i < 4 && i < codex.rooms.Count; i++)
            {
                var index = i;
                var room = codex.rooms[i];
                if (view.roomLabels != null && i < view.roomLabels.Length && view.roomLabels[i] != null)
                    view.roomLabels[i].text = room.displayName + $"\n<size=12>{codex.CountDevicesOfRoom(room.id)} DEVICES</size>";
                if (view.roomBackgrounds != null && i < view.roomBackgrounds.Length && view.roomBackgrounds[i] != null)
                    view.roomBackgrounds[i].color = selectedRoom == i ? HouseUIUtil.Wine : new Color(1, 1, 1, .035f);
                if (view.roomButtons != null && i < view.roomButtons.Length && view.roomButtons[i] != null)
                    HouseUIUtil.BindButton(view.roomButtons[i], () =>
                    {
                        selectedRoom = index;
                        selectedDevice = 0;
                        Refresh(view, page);
                    });
            }

            var devices = new List<DeviceDef>();
            codex.GetDevicesOfRoom(codex.rooms[selectedRoom].id, devices);
            if (view.deviceCardsRoot != null)
            {
                for (var i = view.deviceCardsRoot.childCount - 1; i >= 0; i--)
                    Object.Destroy(view.deviceCardsRoot.GetChild(i).gameObject);
                var template = Resources.Load<GameObject>(OutGamePrefabResourcePaths.DeviceCard);
                if (template == null)
                {
                    Debug.LogError("[HouseUI] 设备卡模板 Prefab 缺失（§16.2）：" + OutGamePrefabResourcePaths.DeviceCard);
                }
                else
                {
                    for (var i = 0; i < devices.Count; i++)
                    {
                        var index = i;
                        var device = devices[i];
                        var instance = Object.Instantiate(template, view.deviceCardsRoot, false);
                        instance.name = "Device" + i;
                        var card = instance.GetComponent<DeviceCardView>();
                        if (card == null) continue;
                        var rect = (RectTransform)instance.transform;
                        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1);
                        rect.anchoredPosition = new Vector2(-120 + i * 270, -155);
                        if (card.label != null)
                            card.label.text = $"⚙\n<size=13>LV.{device.level} · {(device.owned ? "可使用" : "待修复")}</size>\n{device.displayName}\n<size=14>{device.effect}</size>";
                        if (card.background != null)
                            card.background.color = selectedDevice == i ? new Color(.38f, .08f, .24f, .75f) : new Color(1, 1, 1, .045f);
                        HouseUIUtil.BindButton(card.button, () =>
                        {
                            selectedDevice = index;
                            Refresh(view, page);
                        });
                    }
                    HouseUIUtil.ApplyFallbackFont(view.deviceCardsRoot);
                }
            }

            if (devices.Count == 0) return;
            var chosen = devices[Mathf.Clamp(selectedDevice, 0, devices.Count - 1)];
            if (view.recipeText != null)
                view.recipeText.text = $"<size=13>当前设备</size>\n<size=30>{chosen.displayName}</size>\n{chosen.effect}\n\n咖啡豆 ×2     温水 ×1";
            var ready = chosen.owned;
            if (view.makeButton != null)
            {
                if (view.makeLabel != null) view.makeLabel.text = ready ? "开始制作" : "需要修复";
                var background = view.makeButton.targetGraphic as UnityEngine.UI.Image;
                if (background != null) background.color = ready ? HouseUIUtil.Wine : HouseUIUtil.Hex("49434A");
                HouseUIUtil.BindButton(view.makeButton, () => page.Toast(chosen.displayName + " 已开始运作"));
                view.makeButton.interactable = ready;
            }
        }
    }
}

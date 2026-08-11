using UnityEngine;

namespace MasterHouse
{
    /// <summary>Hub 底部房间导航绑定：四个房间按钮 + 锁定的地下仓库；房间切换后 Refresh 重绑高亮。</summary>
    public sealed class HubRoomNavBinder
    {
        private OutGameHubRoomNavigationView view;
        private HubPage page;

        public void Bind(OutGameHubRoomNavigationView navigation, HubPage owner)
        {
            view = navigation;
            page = owner;
            Refresh();
        }

        public void Refresh()
        {
            if (view == null) return;
            var rooms = GameManager.Instance.CodexTable.rooms;
            for (var i = 0; i < view.rooms.Length && i < rooms.Count; i++)
            {
                var index = i;
                var room = rooms[i];
                var selected = page.RoomIndex == i;
                var item = view.rooms[i];
                item.code.text = room.code;
                item.icon.text = RoomIcon(i);
                item.roomName.text = room.displayName;
                item.state.text = selected ? "CURRENT" : string.Empty;
                // common 框换肤：选中/未选用透明度区分（选中不透明、未选更透）
                HouseUIUtil.ApplyPanelSkin(item.background, selected ? .95f : .45f, 2.5f);
                var color = selected ? HouseUIUtil.White : new Color(1, 1, 1, .72f);
                item.code.color = item.icon.color = item.roomName.color = color;
                HouseUIUtil.BindButton(item.button, () => page.SelectRoom(index));
            }
            var locked = view.lockedRoom;
            locked.code.text = "LOCKED";
            locked.icon.text = "▣";
            locked.roomName.text = "地下仓库";
            locked.state.text = string.Empty;
            HouseUIUtil.ApplyPanelSkin(locked.background, .25f, 2.5f);
            locked.code.color = locked.icon.color = locked.roomName.color = new Color(1, 1, 1, .3f);
            HouseUIUtil.BindButton(locked.button, () => page.Toast("仓库房间将在 House LV.04 解锁"));
        }

        private static string RoomIcon(int index) => index switch { 0 => "▰", 1 => "▱", 2 => "▦", _ => "▥" };
    }
}

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
                item.background.color = selected ? new Color(.45f, .08f, .3f, .77f) : new Color(1, 1, 1, .015f);
                var color = selected ? HouseUIUtil.White : new Color(1, 1, 1, .72f);
                item.code.color = item.icon.color = item.roomName.color = color;
                HouseUIUtil.BindButton(item.button, () => page.SelectRoom(index));
            }
            var locked = view.lockedRoom;
            locked.code.text = "LOCKED";
            locked.icon.text = "▣";
            locked.roomName.text = "地下仓库";
            locked.state.text = string.Empty;
            locked.background.color = Color.clear;
            locked.code.color = locked.icon.color = locked.roomName.color = new Color(1, 1, 1, .3f);
            HouseUIUtil.BindButton(locked.button, () => page.Toast("仓库房间将在 House LV.04 解锁"));
        }

        private static string RoomIcon(int index) => index switch { 0 => "▰", 1 => "▱", 2 => "▦", _ => "▥" };
    }
}

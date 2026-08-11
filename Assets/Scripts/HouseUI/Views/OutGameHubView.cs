using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>House 主界面的可编辑布局槽。</summary>
    public sealed class OutGameHubView : MonoBehaviour
    {
        public RectTransform sceneRoot;
        public RectTransform chromeRoot;
        public RectTransform modalRoot;
        public Text footer;
        public OutGameHubTopBarView topBar;
        public OutGameHubTaskCardView taskCard;
        public OutGameHubGuestRailView guestRail;
        public OutGameHubRightDockView rightDock;
        public OutGameHubRoomNavigationView roomNavigation;
        public OutGameHubSceneOverlayView sceneOverlay;
    }
}

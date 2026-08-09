using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    public sealed class OutGameHubRoomNavigationView : MonoBehaviour
    {
        public Image background;
        public Text title;
        public Text hint;
        public OutGameHubRoomButtonView[] rooms;
        public OutGameHubRoomButtonView lockedRoom;
    }
}

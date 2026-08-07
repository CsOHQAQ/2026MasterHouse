using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>「设备图鉴」面板内容的 Prefab 引用。设备卡数量随房间变化，运行时生成到 deviceCardsRoot。</summary>
    public sealed class OutGameDevicePanelView : MonoBehaviour
    {
        public Button[] roomButtons = new Button[4];
        public Image[] roomBackgrounds = new Image[4];
        public Text[] roomLabels = new Text[4];
        public RectTransform deviceCardsRoot;
        public Text recipeText;
        public Button makeButton;
        public Text makeLabel;
    }
}

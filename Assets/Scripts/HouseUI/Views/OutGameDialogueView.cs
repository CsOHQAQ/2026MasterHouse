using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>访客对话界面的 Prefab 引用。文本、选中态与事件由控制器运行时绑定。</summary>
    public sealed class OutGameDialogueView : MonoBehaviour
    {
        public RawImage sceneArt;
        public RectTransform characterCard;
        public RawImage portrait;
        public Text portraitTag;
        public Button closeButton;
        public Text weekTitle;
        public Button[] weekGuestButtons = new Button[4];
        public Image[] weekGuestBackgrounds = new Image[4];
        public Text[] weekGuestLabels = new Text[4];
        public RectTransform dialogueBox;
        public Text dialogueText;
        public Button needButton;
        public Button refuseButton;
        public Text refuseLabel;
        public Button serveButton;
        public Text serveLabel;
        public Text furnitureTitle;
        public Button[] furnitureButtons = new Button[5];
        public Image[] furnitureBackgrounds = new Image[5];
        public Text[] furnitureLabels = new Text[5];
        public Button endWeekButton;
    }
}

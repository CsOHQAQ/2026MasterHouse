using UnityEngine;
using UnityEngine.UI;

namespace MasterPotion
{
    /// <summary>「日记与成就」面板内容的 Prefab 引用。日记文章/成就列表随页签变化，运行时生成到 bodyRoot。</summary>
    public sealed class OutGameJournalPanelView : MonoBehaviour
    {
        public Button[] tabButtons = new Button[2];
        public Image[] tabBackgrounds = new Image[2];
        public Text[] tabLabels = new Text[2];
        public RectTransform bodyRoot;
    }
}

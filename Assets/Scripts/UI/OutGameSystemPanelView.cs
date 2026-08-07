using UnityEngine;
using UnityEngine.UI;

namespace MasterPotion
{
    /// <summary>右侧系统面板的公共外壳。</summary>
    public sealed class OutGameSystemPanelView : MonoBehaviour
    {
        public Image scrim;
        public Button scrimButton;
        public Image panel;
        public RectTransform headerRoot;
        public RectTransform contentRoot;
    }
}

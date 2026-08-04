using UnityEngine;
using UnityEngine.UI;

namespace MasterPotion
{
    /// <summary>纸张风格完整页面的公共序列化引用基类。</summary>
    public class OutGamePaperView : MonoBehaviour
    {
        public RawImage cover;
        public Image paper;
        public RectTransform frame;
        public Text eyebrow;
        public Text title;
        public Text description;
        public Button backButton;
        public RectTransform contentRoot;
        public RectTransform saveListRoot;
    }
}

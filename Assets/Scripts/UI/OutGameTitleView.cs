using UnityEngine;
using UnityEngine.UI;

namespace MasterPotion
{
    /// <summary>标题页 Prefab 的序列化引用；逻辑只绑定数据与交互。</summary>
    public sealed class OutGameTitleView : MonoBehaviour
    {
        public RawImage cover;
        public RawImage horizontalVignette;
        public RawImage verticalVignette;
        public RawImage menuGradient;
        public RawImage topRule;
        public RawImage bottomRule;
        public Text saveState;
        public Text hints;
        public Button[] menuButtons;
        public Text[] menuMainLabels;
        public Text[] menuSubtitles;
        public RawImage[] menuHoverImages;
    }
}

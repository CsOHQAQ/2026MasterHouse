using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>标题页 Prefab 的序列化引用；逻辑只绑定数据与交互。</summary>
    public sealed class OutGameTitleView : MonoBehaviour
    {
        public RawImage cover;
        [Tooltip("游戏名字画（2.0 登录页：标题与菜单都是单独的透明图，不再烘在封面上）")]
        public Image titleArt;
        [Tooltip("标题下的细分隔线")]
        public Image titleRule;
        [Tooltip("四项菜单的文字图（NEW GAME / LOAD GAME / OPTIONS / EXIT）")]
        public Image[] menuIcons;
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

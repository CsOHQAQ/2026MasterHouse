using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// ESC 系统菜单（2026-08-19 按 2.0 设计图新建）：整屏遮罩 + 纸面板 + 一列按钮。
    /// 条目顺序与 <see cref="EscMenuOverlay"/> 的绑定顺序一致，位置尺寸以 Prefab 为准（§16.2）。
    /// </summary>
    public sealed class OutGameEscMenuView : MonoBehaviour
    {
        [Tooltip("整屏遮罩：压暗底下的场景，同时挡住穿透点击")]
        public Image scrim;
        [Tooltip("纸面板底图")]
        public Image panel;

        [Header("条目（继续/存储/加载/选项/返回主菜单/退出）")]
        public Button[] buttons = new Button[6];
        public Image[] buttonFrames = new Image[6];
        public Text[] buttonLabels = new Text[6];

        [Header("条目三态（Prefab 烘焙引用，绑定层做 SpriteSwap）")]
        public Sprite itemNormal;
        public Sprite itemHover;
    }
}

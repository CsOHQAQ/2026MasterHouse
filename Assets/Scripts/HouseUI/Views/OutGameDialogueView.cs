using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 访客对话界面（GVN/视觉小说式）的 Prefab 引用：全屏场景 + 左下立绘 + 底部对话条 + 右侧选项列。
    /// 布局与美术引用烘在 Prefab 里（§16.2）；文本、选项内容与事件由 DialogueOverlay 运行时绑定。
    /// </summary>
    public sealed class OutGameDialogueView : MonoBehaviour
    {
        [Header("场景层")]
        [Tooltip("全屏场景底图（对话专用美术 bg.png）")] public RawImage sceneArt;
        [Tooltip("右侧撕边压暗层（rignt-bg.png）")] public Image rightShade;
        [Tooltip("左上 GUEST 标题")] public Text guestTitle;

        [Header("立绘与对话条")]
        [Tooltip("左下立绘（character/1.png 占位）")] public RawImage portrait;
        [Tooltip("底部对话条容器")] public RectTransform dialogueBar;
        [Tooltip("说话人名（粉）")] public Text speakerName;
        [Tooltip("名字下的笔刷分隔线（line.png）")] public Image nameLine;
        [Tooltip("对话正文")] public Text dialogueText;
        [Tooltip("正文尾部的继续箭头（arrow.png，纯装饰）")] public Image continueArrow;

        [Header("交互")]
        [Tooltip("左下 ESC·返回")] public Text escHint;
        public Button closeButton;
        [Tooltip("右侧选项列（Options 笔刷皮肤；数量按需启用，未用槽位隐藏）")]
        public Button[] optionButtons = new Button[7];
        public Image[] optionBackgrounds = new Image[7];
        public Text[] optionLabels = new Text[7];
    }
}

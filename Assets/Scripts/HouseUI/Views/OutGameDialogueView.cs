using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 访客对话界面（GVN/视觉小说式）的 Prefab 引用：全屏场景 + 左下立绘 + 底部对话条 + 右侧选项列。
    /// 布局与美术引用烘在 Prefab 里（§16.2）；文本、选项内容与事件由 DialogueOverlay 运行时绑定。
    ///
    /// 本 View 与 Prefab 是**对话系统落地时保留并扩展**的（架构设计 §16.9 修正记录）——
    /// 设计说明初稿说「整体退役新建纯对话框」，但这套 GVN 界面正是它要的形态，不重造。
    /// </summary>
    public sealed class OutGameDialogueView : MonoBehaviour
    {
        [Header("场景层")]
        [Tooltip("全屏场景底图（对话专用美术 bg.png）")] public RawImage sceneArt;
        [Tooltip("右侧撕边压暗层（rignt-bg.png）")] public Image rightShade;
        [Tooltip("左上 GUEST 标题")] public Text guestTitle;
        [Tooltip("整屏推进热区：点击推进台词 / 未显完时立即全文（§5.1）。压在场景之上、其余控件之下")]
        public Button advanceButton;

        [Header("立绘与对话条")]
        [Tooltip("左下立绘（按 EDialogueEmotion 取种族差分）")] public RawImage portrait;
        [Tooltip("底部对话条容器")] public RectTransform dialogueBar;
        [Tooltip("名字条整体：玩家句换配色、旁白句整条隐藏（§4.1 三种说话人样式）")] public RectTransform nameplate;
        [Tooltip("说话人名（粉）")] public Text speakerName;
        [Tooltip("名字下的笔刷分隔线（line.png）")] public Image nameLine;
        [Tooltip("对话正文")] public Text dialogueText;
        [Tooltip("正文尾部的继续箭头（arrow.png，纯装饰；停在分支上时隐藏）")] public Image continueArrow;

        [Header("旁白（§4.1：居中无框）")]
        [Tooltip("旁白专用文本：整屏居中、不显示对话条与立绘")] public Text narrationText;

        [Header("交互")]
        [Tooltip("左下 ESC·返回")] public Text escHint;
        public Button closeButton;

        [Header("选项列（模板 Prefab + 运行时实例化，§16.2）")]
        [Tooltip("选项容器：挂 VerticalLayoutGroup，运行时按分支选项数实例化 DialogueOption 模板")]
        public RectTransform optionsRoot;
    }
}
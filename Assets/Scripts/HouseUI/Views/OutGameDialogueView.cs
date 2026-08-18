using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 访客对话界面（GVN/视觉小说式）的 Prefab 引用：全屏场景 + 左下立绘 + 底部对白板 + 右侧选项列。
    /// 布局与美术引用烘在 Prefab 里（§16.2）；文本、选项内容与事件由 DialogueOverlay 运行时绑定。
    ///
    /// 本 View 与 Prefab 是**对话系统落地时保留并扩展**的（架构设计 §16.9 修正记录）——
    /// 设计说明初稿说「整体退役新建纯对话框」，但这套 GVN 界面正是它要的形态，不重造。
    ///
    /// 2026-08-19 按 2.0 设计图（Docs/待办工作流/新版对话UI样例.png）换代：底图换成明亮水彩外景，
    /// 对白条换成 `对白底板` 整张纸质素材（名字凸台与分隔线都烘在图里），配色从「暗底奶油字」
    /// 翻成「纸底墨蓝字」。同批退役的 1.0 元素：左上 GUEST 标题、正文尾部继续箭头、
    /// 右侧撕边压暗层、名字下的独立分隔线——2.0 设计图里都不存在，故字段一并删除。
    /// </summary>
    public sealed class OutGameDialogueView : MonoBehaviour
    {
        [Header("场景层")]
        [Tooltip("全屏场景底图（对话专用美术 PC ui 2.0/conversation/对话-底板）")] public RawImage sceneArt;
        [Tooltip("整屏推进热区：点击推进台词 / 未显完时立即全文（§5.1）。压在场景之上、其余控件之下")]
        public Button advanceButton;

        [Header("立绘与对白板")]
        [Tooltip("左下立绘（按台词的立绘ID 查 PortraitTable；留空的句子沿用上一句）。压在对白板之上")]
        public RawImage portrait;
        [Tooltip("底部对白板整体：旁白句不隐藏（2.0 起旁白复用本板、只是凸台留空、正文居中）")]
        public RectTransform dialogueBar;
        [Tooltip("说话人名：写在素材自带的左上凸台里。访客/玩家换配色，旁白留空（§4.1 三种说话人样式）")]
        public Text speakerName;
        [Tooltip("对话正文")] public Text dialogueText;

        [Header("交互")]
        [Tooltip("左下 ESC·返回（整图按钮，素材自带键名与文案）")] public Button closeButton;
        [Tooltip("右下 中键·切换选项（整图按钮）")] public Button cycleButton;
        [Tooltip("右下 space·确认（整图按钮）")] public Button confirmButton;

        [Header("选项列（Prefab 预摆槽位，§16.2）")]
        [Tooltip("选项容器：其下自下而上预摆若干 DialogueOptionView 槽位，运行时**底对齐**填充——" +
                 "选项列贴着对白板顶沿往上长（2.0 设计图口径）")]
        public RectTransform optionsRoot;
    }
}

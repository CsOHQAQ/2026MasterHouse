using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 需求交付页的 Prefab 引用（交付页落地说明 §3）：左访客、中交付框、右仓库。
    /// 布局与美术引用烘在 Prefab 里（§16.2 Prefab 是布局唯一真相源），
    /// 文案、仓库条目与三个出口按钮由 DeliveryOverlay 运行时绑定。
    ///
    /// **立绘在左**是硬要求（与对话框排布一致），不要镜像。
    /// </summary>
    public sealed class DeliveryPageView : MonoBehaviour
    {
        [Header("底层")]
        [Tooltip("整屏遮罩：点击 =「稍后再说」（与 ESC 同义），同时挡住下层 Hub 交互")]
        public Button scrimButton;

        [Header("访客（左）")]
        [Tooltip("访客立绘：按预览档位取种族差分（EDialogueEmotion）")] public RawImage portrait;
        [Tooltip("访客名")] public Text guestName;
        [Tooltip("程序化需求句（走 INeedPhraseBuilder，与对话里的 {需求} 同一个组装器）")] public Text needSentence;

        [Header("页内气泡（立绘下方，§7 待确认默认值）")]
        [Tooltip("气泡容器：交付框为空时隐藏，放入物品后显示交付预览单句")] public CanvasGroup bubbleGroup;
        [Tooltip("气泡正文（交付预览单句，绝不结算）")] public Text bubbleText;

        [Header("交付框（中）")]
        [Tooltip("交付框落点：拖拽命中判定用这个 RectTransform")] public RectTransform dropZone;
        [Tooltip("框内物品图标；ItemDef.icon 为空时按 DisplayColor 显示占位色块")] public Image dropItemIcon;
        [Tooltip("框内物品名")] public Text dropItemName;
        [Tooltip("空框提示语（「把物品拖到这里」），放入后隐藏")] public Text dropHint;

        [Header("仓库（右）")]
        [Tooltip("仓库列表滚动视图（滚轮/滚动条滚动；条目本身的拖拽用于交付，不参与滚动）")]
        public ScrollRect cargoScroll;
        [Tooltip("仓库条目容器：运行时按模板 Prefab 实例化（§16.2 动态列表项）")] public RectTransform cargoContent;
        [Tooltip("仓库为空时的提示")] public Text cargoEmptyLabel;

        [Header("奖励预期与出口")]
        [Tooltip("奖励预期：档名 + 货币 + 声望；框空时显示「——」")] public Text rewardPreview;
        [Tooltip("确认交付（框空时禁用）")] public Button confirmButton;
        public Text confirmLabel;
        [Tooltip("拒绝交付：按钮上直接写明扣多少声望（§3.1）")] public Button rejectButton;
        public Text rejectLabel;
        [Tooltip("稍后再说：什么都不发生，访客保持「服务中」")] public Button laterButton;
        public Text laterLabel;

        [Header("拖拽")]
        [Tooltip("拖拽幽灵的父节点（须在所有内容之上，否则幽灵会被列表遮住）")] public RectTransform dragLayer;
    }
}

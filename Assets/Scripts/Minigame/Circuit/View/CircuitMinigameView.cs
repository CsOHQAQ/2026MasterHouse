using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 「修理电路」页面的序列化引用容器（纯字段袋，无逻辑；与局外 OutGame*View 同例）。
    ///
    /// **Prefab 是布局唯一真相源**（架构 §16.2）：这里只登记引用，不写任何布局代码，
    /// 也不做缺失时的代码兜底——缺引用是 LogError，不是回退。
    /// 配色也放在这里而不是硬编码在逻辑里：无美术阶段用占位色，美术进场改 Inspector 即可。
    /// </summary>
    public sealed class CircuitMinigameView : MonoBehaviour
    {
        [Header("棋盘")]
        [Tooltip("棋盘可用区：格子大小按它与画布行列数自动算，居中摆放")]
        public RectTransform boardArea;

        [Tooltip("格子层。pivot 必须是 (0,0)——坐标换算按左下角原点做")]
        public RectTransform gridRoot;

        [Tooltip("节点层（电源/电池/中转件）")]
        public RectTransform nodeRoot;

        [Tooltip("已成线的导线层")]
        public RectTransform linkRoot;

        [Tooltip("描线与幽灵预览层，压在最上面")]
        public RectTransform previewRoot;

        [Header("顶部预算条")]
        public Text linkBudgetLabel;
        public Text pieceBudgetLabel;
        public Text litLabel;

        [Header("左侧件库")]
        public RectTransform paletteRoot;

        [Tooltip("件库条目模板（§16.2 动态列表项 = 模板 Prefab + 运行时实例化）。运行时会被隐藏并克隆")]
        public CircuitPaletteItemView paletteItemTemplate;

        [Header("按钮")]
        public Button finishButton;
        public Button abortButton;

        [Header("提示（操作失败原因，须在界面可见）")]
        public Text messageLabel;

        [Header("占位配色")]
        public Color cellColor = new Color(1f, 1f, 1f, 0.06f);
        public Color sourceColor = new Color(0.35f, 0.72f, 0.40f, 0.95f);
        public Color batteryColor = new Color(0.34f, 0.52f, 0.85f, 0.95f);
        public Color batteryLitColor = new Color(0.98f, 0.83f, 0.30f, 1f);
        public Color transitColor = new Color(0.62f, 0.45f, 0.80f, 0.95f);
        public Color wireColor = new Color(0.85f, 0.88f, 0.92f, 0.95f);
        public Color wireDeadColor = new Color(0.45f, 0.47f, 0.52f, 0.85f);
        public Color previewColor = new Color(0.95f, 0.90f, 0.45f, 0.75f);
        public Color legalColor = new Color(0.40f, 0.90f, 0.45f, 0.55f);
        public Color illegalColor = new Color(0.92f, 0.35f, 0.32f, 0.55f);
        public Color budgetWarnColor = new Color(0.95f, 0.36f, 0.33f, 1f);
        public Color budgetNormalColor = Color.white;
    }
}

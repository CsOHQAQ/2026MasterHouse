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

        [Header("全局视觉样式")]
        [Tooltip("所有节点共用的状态图标等表现资产")]
        public CircuitVisualStyleConfig visualStyle;

        [Tooltip("Prefab 固定 UI 的主题资产（件库、按钮等；不含节点功能图标）")]
        public CircuitUIStyleConfig uiStyle;

        [Header("关卡背景")]
        [Tooltip("全屏背景图。Sprite 由当前 LevelDef.BackgroundSprite 驱动。")]
        public Image levelBackground;

        [Header("顶部预算条")]
        public CircuitTopStatusBarView topStatusBar;

        [Header("左侧件库")]
        [Tooltip("件库整体背景。Sprite 与染色由 uiStyle 驱动。")]
        public Image palettePanelBackground;

        public RectTransform paletteRoot;

        [Tooltip("件库条目模板（§16.2 动态列表项 = 模板 Prefab + 运行时实例化）。运行时会被隐藏并克隆")]
        public CircuitPaletteItemView paletteItemTemplate;

        [Header("按钮")]
        public Button finishButton;
        public Button abortButton;

        [Tooltip("【完成】按钮上的文案。课程包模式下会被改写成「下一关」/「交卷」")]
        public Text finishButtonLabel;

        [Header("提示（操作失败原因，须在界面可见）")]
        public Text messageLabel;

        // ══════════ 以下只在课程包模式（CircuitLessonPackDef）下用 ══════════
        // 单关模式一律隐藏，且**不参与 ValidateView 的必需件校验**——
        // 否则手调过的旧 Prefab 会因为缺这些新控件而连单关都开不了（那是本轮不该有的回归）。

        [Header("课程包·教学栏")]
        [Tooltip("整条教学栏的根节点。单关模式下整体隐藏")]
        public GameObject lessonPanel;

        [Tooltip("课程标题")]
        public Text lessonTitleLabel;

        [Tooltip("教学说明（多行）")]
        public Text lessonBriefLabel;

        [Header("课程包·关卡导航")]
        public Button prevLessonButton;
        public Button retryLessonButton;

        [Header("课程包·过关小结")]
        [Tooltip("小结面板根节点，默认关闭。开着时棋盘不接受输入")]
        public GameObject summaryPanel;

        public Text summaryTitleLabel;
        public Text summaryBodyLabel;

        [Tooltip("小结面板上的主按钮：进入下一关 / 交卷")]
        public Button summaryContinueButton;

        [Tooltip("主按钮的文案。会被改写成「下一关」/「交卷」")]
        public Text summaryContinueLabel;

        [Tooltip("关掉小结、留在本关继续调整")]
        public Button summaryStayButton;

        [Header("占位配色")]
        public Color cellColor = new Color(1f, 1f, 1f, 0.06f);
        public Color sourceColor = new Color(0.35f, 0.72f, 0.40f, 0.95f);
        public Color batteryColor = new Color(0.34f, 0.52f, 0.85f, 0.95f);
        public Color batteryLitColor = new Color(0.98f, 0.83f, 0.30f, 1f);
        public Color transitColor = new Color(0.62f, 0.45f, 0.80f, 0.95f);
        public Color legalColor = new Color(0.40f, 0.90f, 0.45f, 0.55f);
        public Color illegalColor = new Color(0.92f, 0.35f, 0.32f, 0.55f);
        [Tooltip("预算超出上限时的数字颜色（只有导线会超；中转件被 CanBuild 硬拦，到不了这一档）")]
        public Color budgetWarnColor = new Color(0.95f, 0.36f, 0.33f, 1f);

        [Tooltip("预算正好用满时的数字颜色。用满是合法甚至最优解，不该报红，但要看得见")]
        public Color budgetFullColor = new Color(0.98f, 0.78f, 0.35f, 1f);

        public Color budgetNormalColor = Color.white;
    }
}

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
        [Tooltip("最底层全屏背景图。Sprite 由 CircuitVisualStyleConfig.levelBackgroundSprite 驱动。")]
        public Image levelBackground;

        [Tooltip("介于底层背景与节点层之间的装饰图。Sprite 由当前 LevelDef.BackgroundSprite 驱动。")]
        public Image levelDecorativeBackground;

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

        // ══════════ 音效（2026-08-20）══════════
        // 剪辑直配在这里、不进音效表：本小游戏的专属音，配在就近的地方，换音不牵动全局通用音
        //（口径与制作咖啡一致，见 Docs/音效系统说明.md §3.5）。留空 = 该处静音，是配置手段不是缺件。

        [Header("音效（剪辑直配在这里，不进音效表）")]
        [Tooltip("描线音：描格途中每往前延伸一格响一声。\n" +
                 "退格截断不响（只有「往前画」才出声）；鼠标快扫一帧能吃进十几格，限一帧最多一声。\n" +
                 "默认 2_Pickup_260813_1；留空 = 不响")]
        public AudioClip drawStepClip;

        [Tooltip("描线音的音量倍率，乘在设置页「音效」音量之上")]
        [Range(0f, 2f)] public float drawStepVolume = 1f;

        [Tooltip("落件音：中转件成功摆下（点选落子与件库拖放两条路径），以及挪动已摆好的件成功落位。\n" +
                 "「这里放不下」「这种件已经用完了」这类失败不响——界面上已经有文字说明。\n" +
                 "默认 2_Putdown_260812；留空 = 不响")]
        public AudioClip nodePlaceClip;

        [Tooltip("落件音的音量倍率，乘在设置页「音效」音量之上")]
        [Range(0f, 2f)] public float nodePlaceVolume = 1f;

        [Tooltip("接线音：从节点接口拉出线（按下那一刻）、以及成功接到另一个接口时，各响一次。\n" +
                 "没描到接口的静默作废、超预算被拒等失败一律不响。\n" +
                 "默认 1_Button_260812；留空 = 不响")]
        public AudioClip linkConnectClip;

        [Tooltip("接线音的音量倍率，乘在设置页「音效」音量之上")]
        [Range(0f, 2f)] public float linkConnectVolume = 1f;

        [Tooltip("电池满足音：有电池从「不满足」翻成「满足」的那一刻响一次。\n" +
                 "默认 4_ScoreGain_260812；留空 = 不响")]
        public AudioClip batteryLitClip;

        [Tooltip("电池满足音的音量倍率，乘在设置页「音效」音量之上")]
        [Range(0f, 2f)] public float batteryLitVolume = 1f;

        [Tooltip("电池失去满足音：有电池从「满足」翻回「不满足」时响一次（拆线、挪件、删件导致）。\n" +
                 "搭建途中「还没满足」不响——只有由满足变回不满足这一个瞬间才出声。\n" +
                 "默认 4_ScoreLose_260813_1；留空 = 不响")]
        public AudioClip batteryUnlitClip;

        [Tooltip("电池失去满足音的音量倍率，乘在设置页「音效」音量之上")]
        [Range(0f, 2f)] public float batteryUnlitVolume = 1f;

        // ══════════ 以下只在课程包模式（CircuitLessonPackDef）下用 ══════════
        // 单关模式一律隐藏，且**不参与 ValidateView 的必需件校验**——
        // 否则手调过的旧 Prefab 会因为缺这些新控件而连单关都开不了（那是本轮不该有的回归）。

        [Header("课程包·教学栏")]
        [Tooltip("整条教学栏的根节点。单关模式下整体隐藏")]
        public GameObject lessonPanel;

        [Tooltip("教学栏整体背景。Sprite 与染色由 uiStyle 驱动。")]
        public Image lessonPanelBackground;

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
        public Color cellColor = new Color(0.25f, 0.25f, 0.25f, 0.20f);
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

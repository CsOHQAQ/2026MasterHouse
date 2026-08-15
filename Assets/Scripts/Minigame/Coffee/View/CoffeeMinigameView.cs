using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 「制作咖啡」页面的序列化引用容器（纯字段袋，无逻辑；与 CircuitMinigameView 同例）。
    ///
    /// **Prefab 是布局唯一真相源**（架构 §16.2）：这里只登记引用，不写任何布局代码，
    /// 也不做缺失时的代码兜底——缺引用是 LogError，不是回退。
    ///
    /// 字段的归属划分：**影响判定的手感参数在 CoffeeLevelDef**（策划按关调），
    /// 纯表现（环半径比例、配色、提示时长）在本类（美术/整体调，一处生效所有关）。
    /// </summary>
    public sealed class CoffeeMinigameView : MonoBehaviour
    {
        [Header("阶段根（Launch 时开磨豆关冲泡，环节切换时互换）")]
        public RectTransform grindRoot;
        public RectTransform pourRoot;

        [Header("磨豆子")]
        [Tooltip("圆环所在的方形区域，环半径按它的短边 × 比例算")]
        public RectTransform grindArea;

        [Tooltip("运行时生成物（环轮廓点/障碍点）都挂这里，锚在区域中心")]
        public RectTransform grindContentRoot;

        [Tooltip("点模板：运行时克隆出环轮廓与障碍弧段（§16.2 动态列表项 = 模板 + 运行时实例化）")]
        public Image grindDotTemplate;

        public RectTransform pointer;
        public Image pointerImage;

        [Header("冲咖啡")]
        public RectTransform cupArea;
        public Image cupImage;

        [Header("HUD")]
        public Text phaseLabel;
        public Text scoreLabel;

        [Tooltip("进度条填充：代码驱动 anchorMax.x（0~1）")]
        public RectTransform progressFill;

        [Tooltip("提示与撞击反馈（操作反馈须在界面可见）")]
        public Text messageLabel;

        [Tooltip("手感调参用的实时数据（冲咖啡的均速/方差/样本数）。美术进场后可整个隐藏")]
        public Text tuningLabel;

        public Button abortButton;

        [Header("表现参数（不影响判定）")]
        [Range(0.1f, 0.5f)] public float outerRingRadiusFraction = 0.42f;
        [Range(0.05f, 0.45f)] public float innerRingRadiusFraction = 0.27f;
        public int ringDotCount = 72;
        public float ringDotSize = 8f;
        public float obstacleDotSize = 16f;

        [Tooltip("撞击提示在提示栏停留的秒数，之后恢复环节说明")]
        public float hitMessageSeconds = 1.2f;

        [Tooltip("结算展示（研磨 X + 冲泡 Y = Z 分）停留秒数，之后才真正 onFinish")]
        public float settleShowSeconds = 1.6f;

        [Header("占位配色")]
        public Color ringColor = new Color(1f, 1f, 1f, 0.18f);
        public Color obstacleColor = new Color(0.92f, 0.35f, 0.32f, 0.95f);
        public Color pointerColor = new Color(0.98f, 0.83f, 0.30f, 1f);
        public Color pointerStunColor = new Color(0.92f, 0.35f, 0.32f, 1f);
        public Color cupIdleColor = new Color(1f, 1f, 1f, 0.08f);
        public Color cupActiveColor = new Color(0.42f, 0.68f, 0.94f, 0.28f);
        public Color messageNormalColor = new Color(0.94f, 0.94f, 0.96f, 1f);
        public Color messageWarnColor = new Color(1f, 0.72f, 0.35f, 1f);
    }
}

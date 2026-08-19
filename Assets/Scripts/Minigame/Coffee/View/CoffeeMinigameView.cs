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

        [Tooltip("杯内水面：材质由根组件在 Launch 时用 UIWater shader 创建（Prefab 不挂材质资产），水位=冲泡进度")]
        public Image waterImage;

        [Header("HUD")]
        public Text phaseLabel;
        public Text scoreLabel;

        [Tooltip("进度条填充：代码驱动 anchorMax.x（0~1）")]
        public RectTransform progressFill;

        [Tooltip("提示与撞击反馈（操作反馈须在界面可见）")]
        public Text messageLabel;

        [Tooltip("手感调参用的实时数据（冲咖啡的均速/方差/样本数）。Prefab 里默认隐藏，" +
                 "只由测试场景的 CoffeeLevelTestBootstrap 打开——正式局不显示")]
        public Text tuningLabel;

        public Button abortButton;

        [Header("音效（循环音：剪辑直配在这里，不进音效表）")]
        [Tooltip("研磨循环音：磨豆环节持续播放，撞障碍的硬直期间停（磨盘停转磨豆声也停）。\n" +
                 "留空 = 本环节静音。硬起硬停，不做淡入淡出；再次响起从头播")]
        public AudioClip grindLoopClip;

        [Tooltip("研磨循环音的音量倍率，乘在设置页「音效」音量之上")]
        [Range(0f, 2f)] public float grindLoopVolume = 1f;

        [Tooltip("冲泡循环音：按住左键且鼠标在杯内时持续播放，松手或滑出杯即停（与进度增长同一条件）。\n" +
                 "留空 = 本环节静音")]
        public AudioClip pourLoopClip;

        [Tooltip("冲泡循环音的音量倍率，乘在设置页「音效」音量之上")]
        [Range(0f, 2f)] public float pourLoopVolume = 1f;

        [Tooltip("本小游戏进行期间把 BGM 压低到原音量的这个倍率——让研磨/冲泡声站到前面来。\n" +
                 "1 = 不压低，0 = 期间全哑。结算走完 / 放弃 / 页面被销毁都会恢复。\n" +
                 "与设置页「背景音乐」是相乘关系：压低期间玩家改设置照样生效")]
        [Range(0f, 1f)] public float bgmDuckFactor = 0.35f;

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

        [Header("水面表现（不影响判定；俯视：液面从杯心扩展，倒水点高频冒波元叠出尾迹）")]
        [Tooltip("边缘晃动速度下限（弧度/秒；滑窗方差=0 时，2026-08-17 访谈：速度由方差线性归一驱动）")]
        public float waterWobbleSpeedMin = 1.5f;

        [Tooltip("边缘晃动速度上限（弧度/秒；滑窗方差≥归一上限时）")]
        public float waterWobbleSpeedMax = 8f;

        [Tooltip("方差线性归一的分母，单位(杯径/秒)²：滑窗方差除以它 clamp 到 0~1 后映射晃动速度。\n" +
                 "纯表现常数，不与关卡阈值挂钩（2026-08-17 访谈拍板）")]
        public float waterVarianceNormalizer = 0.1f;

        [Tooltip("方差滑动窗口宽度（秒）：只看最近这段的表现，反馈跟手")]
        public float waterVarianceWindowSeconds = 1f;

        [Tooltip("倒水时边缘晃动幅度（0~1，乘材质的 _EdgeWobble 基准值）")]
        [Range(0f, 1f)] public float waterWobbleAmpPouring = 1f;

        [Tooltip("停手时边缘微晃幅度（余环飘完后画面不死全靠它）")]
        [Range(0f, 1f)] public float waterWobbleAmpIdle = 0.35f;

        [Tooltip("晃动幅度趋向目标的响应速度（越大切换越干脆）")]
        public float waterWaveDamping = 5f;

        [Tooltip("倒水时波元生成间隔（秒）：要密（≈每帧半），单个波元才隐进包络里。\n" +
                 "槽位共 32 个，间隔 × 32 ≥ 寿命才不会提前顶掉活着的波元")]
        public float waterWakeSpawnInterval = 0.03f;

        [Tooltip("单个波元从出生到消散的寿命（秒）：尾迹拖多长")]
        public float waterWakeLifetime = 0.9f;

        [Tooltip("波元扩散速度（uv/秒；杯直径=1）。要小于正常拖动的速度，V 形尾迹才成形——\n" +
                 "拖得比波快，波才会被甩在身后（开尔文尾迹的成因）")]
        public float waterWakeWaveSpeed = 0.22f;

        [Tooltip("单个波元的出生强度（0~1）：要弱，亮度只该在波元扎堆的包络处积累出来")]
        [Range(0f, 1f)] public float waterWakeStrength = 0.12f;

        [Tooltip("按下瞬间落水水花的出生强度（可超 1，与波元叠加后在 shader 里饱和）")]
        public float waterSplashStrength = 1.4f;

        [Header("占位配色")]
        public Color ringColor = new Color(1f, 1f, 1f, 0.18f);
        public Color obstacleColor = new Color(0.92f, 0.35f, 0.32f, 0.95f);
        public Color pointerColor = new Color(0.98f, 0.83f, 0.30f, 1f);
        public Color pointerStunColor = new Color(0.92f, 0.35f, 0.32f, 1f);
        public Color cupIdleColor = new Color(1f, 1f, 1f, 0.08f);
        public Color cupActiveColor = new Color(0.42f, 0.68f, 0.94f, 0.28f);

        [Tooltip("咖啡液色（UIWater 材质的水体色）")]
        public Color waterColor = new Color(0.42f, 0.27f, 0.16f, 0.85f);

        [Tooltip("波纹亮纹色（读作高光/咖啡油脂）")]
        public Color waterRippleColor = new Color(0.85f, 0.68f, 0.45f, 0.95f);
        public Color messageNormalColor = new Color(0.94f, 0.94f, 0.96f, 1f);
        public Color messageWarnColor = new Color(1f, 0.72f, 0.35f, 1f);
    }
}

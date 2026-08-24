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
        [Tooltip("整屏底图（PC ui 2.0/咖啡研磨/底图.png）：磨盘与两道轨道细线都画在里面。\n" +
                 "与冲泡同例，走 AspectRatioFitter 的 EnvelopeParent 填满裁切")]
        public Image grindBackground;

        [Tooltip("两条轨道所在的方形区域，轨道半径按它的短边 × 比例算。\n" +
                 "它是底图节点的子级、用锚点比例定位——底图被裁切放大时轨道跟着放大，\n" +
                 "始终压在画出来的那两道细线上")]
        public RectTransform grindArea;

        [Tooltip("运行时生成的障碍珠都挂这里，锚在区域中心")]
        public RectTransform grindContentRoot;

        [Tooltip("障碍珠模板（红色珠子）：运行时克隆成一段弧（§16.2 动态列表项 = 模板 + 运行时实例化）。\n" +
                 "珠子多大以这个模板的 RectTransform 为准——代码不改克隆件的尺寸，\n" +
                 "排布间距也是照它的宽度算出来的")]
        public Image obstacleBeadTemplate;

        [Tooltip("指针（把手）。两道轨道已经画在底图上，所以运行时只生成障碍珠，不再画环轮廓")]
        public RectTransform pointer;
        public Image pointerImage;

        [Header("冲咖啡")]
        [Tooltip("整屏底图（PC ui 2.0/咖啡/image 341.png）：满杯的咖啡已经画在图里。\n" +
                 "走 AspectRatioFitter 的 EnvelopeParent 填满裁切——非 16:9 时宁可切掉水彩边缘也不留黑边")]
        public Image pourBackground;

        [Tooltip("判定区：判定取它的内切圆，对齐到底图上画出来的咖啡液面。\n" +
                 "它是底图节点的子级、用锚点比例定位——底图填满裁切放大时判定圆跟着放大，\n" +
                 "视觉与判定在任何屏幕比例下都咬合。1920×1080 下换算：半径 210，圆心在屏幕中心右 12、上 25")]
        public RectTransform cupArea;

        [Tooltip("杯内液面：材质由根组件在 Launch 时用 UIWater shader 创建（Prefab 不挂材质资产）。\n" +
                 "底图已经是满杯，所以液面半径**常驻满**，这一层只画搅动波纹与边缘晃动——\n" +
                 "进度不再由水位表达，改看 HUD 的进度条（2026-08-20 拍板）")]
        public Image waterImage;

        [Header("HUD")]
        [Tooltip("左上角空白底卡：装阶段名、上一环节得分与进度条")]
        public Image hudCard;

        public Text phaseLabel;
        public Text scoreLabel;

        [Tooltip("进度条填充：代码驱动 anchorMax.x（0~1）")]
        public RectTransform progressFill;

        [Tooltip("提示与撞击反馈（操作反馈须在界面可见）")]
        public Text messageLabel;

        [Tooltip("手感调参用的实时数据（冲咖啡的均速/方差/样本数）。Prefab 里默认隐藏，" +
                 "只由测试场景的 CoffeeLevelTestBootstrap 打开——正式局不显示")]
        public Text tuningLabel;

        [Header("暂停（页面级：磨豆与冲泡共用同一颗按钮、同一个弹窗）")]
        [Tooltip("左下角的「ESC 暂停」整图键位按钮（文案烘在素材里，不另挂 Text），两态走 SpriteSwap")]
        public Button escButton;

        [Tooltip("暂停弹窗根：默认隐藏。打开期间整局逻辑全冻——指针停转、进度停积累、两路循环音都停")]
        public RectTransform pauseRoot;

        [Tooltip("暂停弹窗的【继续】")]
        public Button resumeButton;

        [Tooltip("暂停弹窗的【放弃】：不结算，访客保持「服务中」。\n" +
                 "页面上不再单独摆一颗放弃按钮——设计图里只有左下角那颗 ESC（2026-08-20）")]
        public Button abortButton;

        [Header("通关结算弹窗（2026-08-20，版式见 Docs/待办工作流/小游戏结算参考.png）")]
        [Tooltip("结算弹窗根：默认隐藏。冲泡灌满当帧由代码打开（带入场动画），\n" +
                 "点【ESC 返回】或按 ESC 键才真正 onFinish——通关不再自动退出")]
        public RectTransform settleRoot;

        [Tooltip("结算弹窗整体的 CanvasGroup（挂在弹窗根上）：入场淡入用，代码只推 alpha")]
        public CanvasGroup settleGroup;

        [Tooltip("结算底板节点：入场时连同【ESC 返回】一起从下方浮上来")]
        public RectTransform settleBoard;

        [Tooltip("入场动画时长（秒）：整体淡入 + 底板与按钮上浮，与二次确认弹窗同观感")]
        public float settleIntroSeconds = 0.3f;

        [Tooltip("入场上浮的距离（px）")]
        public float settleIntroRise = 28f;

        [Tooltip("弹窗底部的【ESC 返回】：分已到手，点击 = 结算退出（走 onFinish，不是放弃）")]
        public Button settleReturnButton;

        [Tooltip("标题下那行结算点评：按总分从 settleFlavorLines 里挑一档，文案由代码填。\n" +
                 "得分明细在下面三栏统计里已经有了，这行只放调侃")]
        public Text settleDetailLabel;

        [Tooltip("结算点评文案表（按总分分档，改文案 = 改这里，不用碰代码，见架构 §16.6）：\n" +
                 "每次取「下限 ≤ 总分」里下限最大的一条，顺序随便填，代码自己挑。\n" +
                 "表空着或全都够不着 = 那行退回显示「研磨 X ＋ 冲泡 Y ＝ Z 分」。\n" +
                 "单行不换行（Detail 宽 600px、字号 24），一条控制在 20 个汉字以内")]
        public CoffeeSettleFlavor[] settleFlavorLines =
        {
            new CoffeeSettleFlavor { minScore = 90, text = "香得理直气壮，今天这杯有底气。" },
            new CoffeeSettleFlavor { minScore = 75, text = "挑不出毛病的一杯，就是有点太乖。" },
            new CoffeeSettleFlavor { minScore = 60, text = "味道还行，客人应该不会说什么。" },
            new CoffeeSettleFlavor { minScore = 40, text = "有点涩，配块饼干应该没人发现。" },
            new CoffeeSettleFlavor { minScore = 0,  text = "你冲泡出了一杯苦苦的咖啡，提神管够。" },
        };

        [Tooltip("统计面板三栏之一：研磨得分")]
        public Text settleGrindValue;

        [Tooltip("统计面板三栏之二：冲泡得分")]
        public Text settlePourValue;

        [Tooltip("统计面板三栏之三：评级（冲泡档位名：优秀/良好/普通）")]
        public Text settleGradeValue;

        [Tooltip("三颗星，从左到右。素材只有亮星没有灰星——没点亮的用同一张图按 settleStarDimColor 压暗占位")]
        public Image[] settleStars;

        [Tooltip("总分 ≥ 这个值亮 3 颗星")]
        public int settleThreeStarScore = 90;

        [Tooltip("总分 ≥ 这个值亮 2 颗星；不足也给 1 颗——没有失败条件，通关就至少一颗")]
        public int settleTwoStarScore = 60;

        [Tooltip("没点亮的星的乘法染色（同一张亮星素材压暗）")]
        public Color settleStarDimColor = new Color(0.55f, 0.52f, 0.48f, 0.85f);

        [Header("音效（剪辑直配在这里，不进音效表）")]
        [Tooltip("阶段通关音（一次性）：磨豆磨满、冲泡灌满时各响一次。\n" +
                 "默认复用全局的正向提示音 4_ScoreGain；留空 = 不响")]
        public AudioClip stageClearClip;

        [Tooltip("阶段通关音的音量倍率，乘在设置页「音效」音量之上")]
        [Range(0f, 2f)] public float stageClearVolume = 1f;

        [Tooltip("撞障碍音（一次性）：磨豆撞上红珠时响一次。\n" +
                 "**默认留空 = 不响**——2026-08-20 素材还没选好，先把接口留在这儿，\n" +
                 "将来把剪辑拖进来就自动生效，不用改代码")]
        public AudioClip grindHitClip;

        [Tooltip("撞障碍音的音量倍率，乘在设置页「音效」音量之上")]
        [Range(0f, 2f)] public float grindHitVolume = 1f;

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
        [Tooltip("外轨半径 = 区域短边 × 这个比例。区域本身就是按外轨直径摆的，所以取 0.5（= 外接圆）")]
        [Range(0.1f, 0.5f)] public float outerRingRadiusFraction = 0.5f;

        [Tooltip("内轨同理。底图上两道细线的半径实测是 800 : 982，所以取 0.5 × 800/982")]
        [Range(0.05f, 0.5f)] public float innerRingRadiusFraction = 0.5f * 800f / 982f;

        [Tooltip("障碍珠沿弧排布的间距 = 珠子直径 × 这个系数。小于 1 才会首尾略叠、串成一条实心弧")]
        [Range(0.3f, 1f)] public float obstacleBeadSpacing = 0.7f;

        [Tooltip("撞击提示在提示栏停留的秒数，之后恢复环节说明")]
        public float hitMessageSeconds = 1.2f;

        [Header("环节过场（磨豆 → 冲泡）")]
        [Tooltip("过场幕布根：整屏米白纸色 + 居中的环节名。默认隐藏，只在换环节那一下放。\n" +
                 "它压在 HUD 之上、暂停弹窗之下——换环节时连 HUD 一起盖住才干净")]
        public RectTransform transitionRoot;

        [Tooltip("幕布的 CanvasGroup：代码只推这一个 alpha，纸色与环节名一起进退")]
        public CanvasGroup transitionGroup;

        [Tooltip("幕布正中的环节名（「② 冲咖啡」），文案由代码写")]
        public Text transitionLabel;

        [Tooltip("幕布淡入秒数。**环节是在幕布全满那一帧换的**，玩家看不到硬切")]
        public float transitionInSeconds = 0.35f;

        [Tooltip("幕布全满的停留秒数：留给玩家读那行环节名（2026-08-20 反馈从 0.35 延到 2）")]
        public float transitionHoldSeconds = 2f;

        [Tooltip("幕布淡出秒数。出场比入场慢一点，新画面是「揭开」而不是「闪现」")]
        public float transitionOutSeconds = 0.5f;

        [Header("研磨开局倒计时（2026-08-22 一轮测试改进 #13）")]
        [Tooltip("倒计时遮罩根：整屏灰色半透 + 居中大字。默认隐藏，开局播一遍 3/2/1/开始！。\n" +
                 "它压在 HUD 之上、结算与暂停弹窗之下——倒计时期间页面读作「预备」，但暂停仍要能盖住它")]
        public RectTransform countdownRoot;

        [Tooltip("倒计时的 CanvasGroup：结尾整体淡出用，灰底与大字一起退")]
        public CanvasGroup countdownGroup;

        [Tooltip("倒计时正中的大字（3 / 2 / 1 / 开始！），文案由代码写")]
        public Text countdownLabel;

        [Tooltip("每个数字的停留秒数")]
        public float countdownStepSeconds = 0.8f;

        [Tooltip("「开始！」的停留秒数，之后遮罩淡出、研磨才接管输入")]
        public float countdownGoSeconds = 0.6f;

        [Tooltip("结尾遮罩淡出秒数（淡出播完才真正开局，这段仍不吃输入）")]
        public float countdownFadeOutSeconds = 0.25f;

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
        // 2026-08-20 反馈「水波不够明显」后上调（原 0.12）：
        // 静止搅动时，同一像素上同时处在环带里的波元只有约 2 个（环带厚 0.018uv ÷ 扩散 0.22uv/s
        // ≈ 0.08s 的时间窗，除以 0.03s 的生成间隔）。0.12 × 2 = 0.24，远没到饱和，所以看着很淡。
        // 0.35 × 2 ≈ 0.7，包络才亮得起来；再高就会连成一片糊掉，失去尾迹的形状
        [Range(0f, 1f)] public float waterWakeStrength = 0.35f;

        [Tooltip("按下瞬间落水水花的出生强度（可超 1，与波元叠加后在 shader 里饱和）")]
        public float waterSplashStrength = 2f;

        [Tooltip("波纹环带厚度（uv）：越厚，相邻波元越容易叠在一起、包络越亮。\n" +
                 "和「波元出生强度」一起决定水波的明显程度，调这两个即可")]
        [Range(0.001f, 0.06f)] public float waterRingThickness = 0.018f;

        [Tooltip("液面边缘晃动的基准幅度（uv，再乘晃动幅度）：读作咖啡在杯里晃荡。\n" +
                 "0.03 × 杯径 420 ≈ 13px 的进出，太小就看不出来")]
        [Range(0f, 0.08f)] public float waterEdgeWobble = 0.03f;

        [Header("磨豆的指针染色")]
        [Tooltip("常态：白 = 不染色，显示把手贴图的本色")]
        public Color pointerColor = Color.white;

        [Tooltip("撞障碍硬直期间：把把手压暗一档（乘法染色）。\n" +
                 "别改成红的——把手是张木头贴图，整体染红会很脏（2026-08-20 拍板）")]
        public Color pointerStunColor = new Color(0.55f, 0.50f, 0.48f, 1f);

        [Tooltip("液面底色：底图已经画了满杯咖啡，这里只压很薄的一层。\n" +
                 "它存在的意义是让晃动的液面边沿看得出来（读作咖啡在杯里晃）——太浓会把底图的水彩笔触糊掉")]
        public Color waterColor = new Color(0.24f, 0.16f, 0.10f, 0.28f);

        [Tooltip("波纹亮纹色（读作高光/咖啡油脂）")]
        public Color waterRippleColor = new Color(0.96f, 0.90f, 0.76f, 0.85f);

        [Tooltip("进度环色（2026-08-20 加）：贴杯壁内侧、自 12 点顺时针合拢，读作咖啡油脂圈围起来。\n" +
                 "进度条原本只在左上底卡里，而玩家全程盯着杯子——这一圈是给焦点看的")]
        public Color waterProgressColor = new Color(0.96f, 0.86f, 0.66f, 0.9f);

        [Tooltip("进度环的带宽（uv，杯径 = 1）。0.035 × 杯径 420 ≈ 15px")]
        [Range(0f, 0.2f)] public float waterProgressWidth = 0.035f;

        [Tooltip("进度环离杯壁的内缩（uv）：别贴死在边上，留一点余地")]
        [Range(0f, 0.2f)] public float waterProgressInset = 0.02f;

        [Tooltip("还没走到那一段的浅槽透明度（相对环色 alpha）：留道槽，玩家才知道这圈要绕多远。\n" +
                 "0 = 不画槽，进度环凭空长出来")]
        [Range(0f, 1f)] public float waterProgressTrackAlpha = 0.22f;

        [Tooltip("提示文字常态色：设计图取色 #5676A5，与 ESC 键位条上的蓝是同一个")]
        public Color messageNormalColor = new Color(0.337f, 0.463f, 0.647f, 1f);

        [Tooltip("撞障碍时的警示色")]
        public Color messageWarnColor = new Color(0.72f, 0.35f, 0.30f, 1f);
    }

    /// <summary>
    /// 结算弹窗那行点评的一档：总分下限 + 文案（CoffeeMinigameView.settleFlavorLines 的表行）。
    ///
    /// 内容进资产、不进代码（架构 §16.6）：加档 = 加一行，改口吻 = 改 Inspector。
    /// 判定用的是**总分**（研磨 0~50 ＋ 冲泡 20/30/50，封顶 100），与星数阈值各算各的——
    /// 想让点评跟着星走，把下限填成 settleTwoStarScore / settleThreeStarScore 一样的值即可。
    /// </summary>
    [System.Serializable]
    public sealed class CoffeeSettleFlavor
    {
        [Tooltip("总分下限（含）。挑的是「够得着的档里下限最大的那条」，所以最低那档填 0 兜底")]
        public int minScore;

        [Tooltip("这一档显示的点评文案")]
        public string text;
    }
}

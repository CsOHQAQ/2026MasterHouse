using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 全局入口（§9）：注册/初始化所有 Manager；驱动全局固定 tick；承接 Manager 间通信。
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public LevelManager LevelManager { get; private set; }
        public LinkManager LinkManager { get; private set; }
        public PlayerCargoData PlayerCargo { get; private set; }
        public HouseClockManager HouseClockManager { get; private set; }
        public EconomyManager EconomyManager { get; private set; }

        /// <summary>局外内容表（Model，运行时只读，§16.6）。缺失是报错不是回退。</summary>
        public VisitorTable VisitorTable { get; private set; }
        public CodexTable CodexTable { get; private set; }

        [Tooltip("启动时自动加载的小关（可空，便于搭测试场景）")]
        [SerializeField] private LevelDef startLevel;

        /// <summary>真实时间累积器。仅存在于驱动壳层，逻辑内部禁止接触真实时间（§3.1）。</summary>
        private float tickAccumulator;

        /// <summary>单帧最多补的累积时长（秒）：卡帧后丢弃超出部分，防追帧螺旋。仅驱动壳层防御，tick 本身仍逐个完整执行。</summary>
        private const float MaxCatchUpSeconds = 1f;

        // ── 时间控制（调试面板能力清单；只作用于驱动壳层，不触碰 tick 内部逻辑）──

        /// <summary>是否暂停逻辑推进（表现层照常运行）。</summary>
        public bool IsPaused { get; private set; }

        /// <summary>倍速系数（面板用 0.5x/1x/2x/4x/8x），乘在时间累积上。</summary>
        public float TimeScale { get; private set; } = 1f;

        /// <summary>暂停/恢复。切换时清零累积器，避免恢复瞬间连补一串 tick。</summary>
        public void SetPaused(bool paused)
        {
            IsPaused = paused;
            tickAccumulator = 0f;
        }

        public void SetTimeScale(float scale)
        {
            TimeScale = Mathf.Clamp(scale, 0.1f, 16f);
        }

        /// <summary>单步：自动进入暂停并直接喂一个 tick（绕过累积器）。</summary>
        public void StepOneTick()
        {
            SetPaused(true);
            RunTick();
        }

        /// <summary>推进一个全局 tick：局内局外共用同一心跳（§16.4）。两侧测试场景当前隔离（待定 #19），推进顺序暂无耦合。</summary>
        private void RunTick()
        {
            LevelManager.TickAll();
            HouseClockManager.Tick();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            PlayerCargo = new PlayerCargoData();
            LinkManager = new LinkManager();
            LevelManager = new LevelManager(LinkManager, PlayerCargo);
            VisitorTable = Resources.Load<VisitorTable>("OutGameUI/VisitorTable");
            CodexTable = Resources.Load<CodexTable>("OutGameUI/CodexTable");
            if (VisitorTable == null || CodexTable == null)
                Debug.LogError("局外内容表缺失（Resources/OutGameUI/VisitorTable|CodexTable）：请执行菜单 MasterHouse → 局外内容 → 生成内容表");

            HouseClockManager = new HouseClockManager();
            EconomyManager = new EconomyManager(CodexTable); // 纯事件驱动，不进 RunTick（§16.4）；Codex 供装饰分数量统计（§16.7）
        }

        private void Start()
        {
            if (startLevel != null)
                LevelManager.LoadLevel(startLevel);
        }

        private void Update()
        {
            // 驱动壳层：整个逻辑层唯一允许接触真实时间的位置——
            // 把流逝的真实时间折算成固定步长 tick 次数（§3.1）。
            // tick 内部一律以 TickCount 计时，禁止 Time.deltaTime / DateTime.Now / 无种子随机（§11.1）。
            if (IsPaused) return;

            var config = GameConfig.Instance;
            if (config == null) return;

            float tickInterval = 1f / Mathf.Max(1, config.TicksPerSecond); // 待定 #5：暂按 10 tick/秒
            tickAccumulator += Time.deltaTime * TimeScale;
            if (tickAccumulator > MaxCatchUpSeconds)
                tickAccumulator = MaxCatchUpSeconds;
            while (tickAccumulator >= tickInterval)
            {
                tickAccumulator -= tickInterval;
                RunTick();
            }
        }
    }
}
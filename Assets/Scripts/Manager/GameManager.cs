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

        [Tooltip("启动时自动加载的小关（可空，便于搭测试场景）")]
        [SerializeField] private LevelDef startLevel;

        /// <summary>真实时间累积器。仅存在于驱动壳层，逻辑内部禁止接触真实时间（§3.1）。</summary>
        private float tickAccumulator;

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
            var config = GameConfig.Instance;
            if (config == null) return;

            float tickInterval = 1f / Mathf.Max(1, config.TicksPerSecond); // 待定 #5：暂按 10 tick/秒
            tickAccumulator += Time.deltaTime;
            while (tickAccumulator >= tickInterval)
            {
                tickAccumulator -= tickInterval;
                LevelManager.TickAll();
            }
        }
    }
}
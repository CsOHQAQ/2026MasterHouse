using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 全局入口（§9）：注册/初始化所有 Manager；驱动全局固定 tick；承接 Manager 间通信。
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        // 局内节点玩法（LevelManager / LinkManager / PlayerCargo）已随小游戏框架落地退役：
        // 「修理电路」是自包含的小游戏，自己持有 LevelManager/LinkManager，
        // **不认识任何 Manager**（小游戏说明 §3.1 硬约束），因此这里不再注册它们。

        public HouseClockManager HouseClockManager { get; private set; }
        public EconomyManager EconomyManager { get; private set; }
        public VisitorManager VisitorManager { get; private set; }

        /// <summary>
        /// 自研对话系统本体（§16.9，待定 #17 已定案）。同时作为访客侧的 IDialogueService 实现——
        /// 访客模块只认接口，不反向依赖对话模块。
        /// </summary>
        public DialogueManager DialogueManager { get; private set; }

        /// <summary>局外内容表（Model，运行时只读，§16.6）。缺失是报错不是回退。</summary>
        public VisitorScheduleTable VisitorSchedule { get; private set; }
        public VisitorTuningConfig VisitorTuning { get; private set; }
        public DialogueTuningConfig DialogueTuning { get; private set; }

        /// <summary>对话整表（2026-08-14 重构）：全部对话组与池挂载，由 Excel 导表整表重建。</summary>
        public DialogueTable DialogueTable { get; private set; }

        /// <summary>立绘索引表（2026-08-14 立绘 ID 化）：立绘ID → Resources 路径，对话与 Hub 小卡共用。</summary>
        public PortraitTable PortraitTable { get; private set; }

        public CodexTable CodexTable { get; private set; }

        /// <summary>家具配置表（Model，§16.7 并入 Def 体系：统一由此加载，消费方不再散落 Resources.Load）。</summary>
        public FurnitureTable FurnitureTable { get; private set; }
        /// <summary>
        /// 家具族表：同款家具的换色变体归为一族。族级**数值**已在导表时展开进 FurnitureTable 每一行，
        /// 所以运行时只用它取**族显示名**（商城卡片标题 / 收纳栏槽位名），不用它查数值。
        /// </summary>
        public FurnitureFamilyTable FurnitureFamilyTable { get; private set; }
        public FurnitureRoomTable FurnitureRoomTable { get; private set; }
        /// <summary>商店表：家具售卖配置（2026-08-13 从家具表拆出）；读取一律经 EconomyManager。</summary>
        public StoreTable StoreTable { get; private set; }

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

        /// <summary>
        /// 推进一个全局 tick。局内节点产线已随小游戏框架退役，本方法现在只驱动局外：
        /// 时钟先走、访客后判（用刚推进的时间做整数比较）。
        /// 小游戏**完全不在 tick 内**——它自治计时且期间闸门是关的（§3.3）。
        /// </summary>
        private void RunTick()
        {
            HouseClockManager.Tick();
            VisitorManager.Tick();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            VisitorSchedule = Resources.Load<VisitorScheduleTable>("OutGameUI/VisitorScheduleTable");
            VisitorTuning = Resources.Load<VisitorTuningConfig>("OutGameUI/VisitorTuningConfig");
            DialogueTuning = Resources.Load<DialogueTuningConfig>("OutGameUI/DialogueTuningConfig");
            DialogueTable = Resources.Load<DialogueTable>("OutGameUI/DialogueTable");
            PortraitTable = Resources.Load<PortraitTable>("OutGameUI/PortraitTable");
            if (PortraitTable == null)
                Debug.LogError("立绘索引表缺失（Resources/OutGameUI/PortraitTable）：对话与访客小卡将一张立绘都显示不出来；" +
                               "请编辑 Excel/立绘表.xlsx 后运行 Tools/导表/export_config.bat，" +
                               "或执行菜单 MasterHouse → 对话系统 → 从 CSV 导入立绘");
            CodexTable = Resources.Load<CodexTable>("OutGameUI/CodexTable");
            if (VisitorSchedule == null || VisitorTuning == null || CodexTable == null)
                Debug.LogError("局外内容表缺失或损坏（Resources/OutGameUI/VisitorScheduleTable|VisitorTuningConfig|CodexTable）：" +
                               "内容资产是权威数据源，缺失请执行菜单 MasterHouse → 访客系统 → 创建示例资产（补齐缺失）或从版本库恢复；" +
                               "若资产存在却加载不到，检查其 m_Script 引用是否指向同名 .cs");
            FurnitureTable = Resources.Load<FurnitureTable>("OutGameUI/FurnitureTable");
            FurnitureFamilyTable = Resources.Load<FurnitureFamilyTable>("OutGameUI/FurnitureFamilyTable");
            FurnitureRoomTable = Resources.Load<FurnitureRoomTable>("OutGameUI/FurnitureRoomTable");
            StoreTable = Resources.Load<StoreTable>("OutGameUI/StoreTable");
            if (FurnitureTable == null || FurnitureRoomTable == null)
                Debug.LogError("家具配置表缺失（Resources/OutGameUI/FurnitureTable|FurnitureRoomTable）：请执行菜单 MasterHouse → 家具系统 → 创建配置表");
            if (FurnitureFamilyTable == null)
                Debug.LogError("家具族表缺失（Resources/OutGameUI/FurnitureFamilyTable）：商城与收纳栏将退化成一件一卡、" +
                               "槽位名显示族 id；请执行菜单 MasterHouse → 家具系统 → 从 CSV 导入家具四表");
            if (StoreTable == null)
                Debug.LogError("商店表缺失（Resources/OutGameUI/StoreTable）：全部家具将按非卖品（价格 0）处理；" +
                               "请执行菜单 MasterHouse → 家具系统 → 从 CSV 导入家具四表");

            HouseClockManager = new HouseClockManager(VisitorTuning); // 营业时段迁入 VisitorTuningConfig（§4.5）
            // Economy 纯事件驱动，不进 RunTick（§16.4）；Codex 供装饰分数量统计（§16.7 毒点①），家具两表供所有权与初始摆放分
            EconomyManager = new EconomyManager(CodexTable, FurnitureTable, FurnitureRoomTable, StoreTable);

            // 对话与访客的**两阶段初始化**：VisitorManager 的构造需要 IDialogueService，
            // DialogueManager 又需要 VisitorManager——构造期循环依赖，靠先造后 Bind 解开。
            // 顺序不能颠倒，Bind 也不能漏（漏了对话里的事件与条件全部拿不到上下文）。
            DialogueManager = new DialogueManager(DialogueTuning, DialogueTable);
            // runSeed 由 VisitorManager 内部注入固定默认常量（§6.1，待定 #9），GM 面板可改写
            VisitorManager = new VisitorManager(VisitorSchedule, VisitorTuning, HouseClockManager,
                EconomyManager, DialogueManager);
            DialogueManager.Bind(VisitorManager, EconomyManager, HouseClockManager);
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
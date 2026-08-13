namespace MasterHouse
{
    /// <summary>
    /// Pin 运行时数据。只能由 Manager 与 CircuitSolver 修改。
    /// </summary>
    public class PinData
    {
        public readonly PinDef Def;

        public readonly NodeData Owner;

        /// <summary>在节点 Pin 列表中的索引（稳定排序键）。</summary>
        public readonly int IndexInNode;

        /// <summary>
        /// 接在本 Pin 上的链接；null = 空口。
        /// **至多一条**（§4.2 修订版）——建线时由 LinkManager 拦截，不是靠约定。
        /// </summary>
        public LinkData Link;

        /// <summary>
        /// 生效方向：Def.Direction 非 None 时恒等于它；
        /// 为 None（十字件）时由第一条接上的线确定、断线后还原（LinkManager 负责）。
        /// </summary>
        public EPinDirection RuntimeDirection;

        // ── 以下两个字段是 CircuitSolver 每次求解的记忆化缓存，不是持久状态。
        //    每次 Solve 开头统一清零；除求解器外任何人不得写入。 ──

        /// <summary>本 Pin 作为输出口时送出的电量（求解缓存）。</summary>
        public int OutPower;

        /// <summary>OutPower 本轮是否已算过（求解缓存）。</summary>
        public bool OutPowerResolved;

        public PinLayout Layout => Owner.Def.Pins[IndexInNode];

        /// <summary>所属中转分组号；-1 = 不分组（§4.7）。</summary>
        public int Group => Def.PinGroup;

        public PinData(NodeData owner, int indexInNode)
        {
            Owner = owner;
            IndexInNode = indexInNode;
            Def = owner.Def.Pins[indexInNode].Pin;
            RuntimeDirection = Def.Direction;
        }
    }
}

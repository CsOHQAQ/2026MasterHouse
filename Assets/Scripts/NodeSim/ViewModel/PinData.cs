using System.Collections.Generic;

namespace MasterHouse
{
    /// <summary>
    /// Pin 运行时数据（§10）。只能由 Manager 修改。
    /// </summary>
    public class PinData
    {
        public readonly PinDef Def;

        public readonly NodeData Owner;

        /// <summary>在节点 Pin 列表中的索引（稳定排序键）。</summary>
        public readonly int IndexInNode;

        /// <summary>已连接的链接。按 LinkId 升序维护（创建即追加，LinkId 自增，天然有序）。</summary>
        public readonly List<LinkData> Links = new List<LinkData>();

        /// <summary>轮询指针（§3.3）：上次服务到的 LinkId。随存档序列化（§11.5）。</summary>
        public long RoundRobinPointer = -1;

        /// <summary>生效物资类型：普通 Pin = Def.ItemType；中转配对 Pin 运行时随连接同步（§6.3）。</summary>
        public ItemDef RuntimeItemType;

        /// <summary>生效方向：中转配对 Pin 运行时随连接同步（§6.3）。</summary>
        public EPinDirection RuntimeDirection;

        public PinLayout Layout => Owner.Def.Pins[IndexInNode];

        /// <summary>配对 Pin（仅中转节点，§6.3）；无配对返回 null。</summary>
        public PinData PairedPin =>
            Def.PairedPinIndex >= 0 && Def.PairedPinIndex < Owner.Pins.Count
                ? Owner.Pins[Def.PairedPinIndex]
                : null;

        public PinData(NodeData owner, int indexInNode)
        {
            Owner = owner;
            IndexInNode = indexInNode;
            Def = owner.Def.Pins[indexInNode].Pin;
            RuntimeItemType = Def.ItemType;
            RuntimeDirection = Def.Direction;
        }
    }
}
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 中转型节点（§6.3、§7）：无配方的加工节点，只转运不加工。
    /// 配对关系配置在 Pins 各条目的 PinDef.PairedPinIndex 中；
    /// 位置由策划在关卡（LevelDef.PresetNodes）中预置。
    /// </summary>
    [CreateAssetMenu(fileName = "中转节点", menuName = "MasterHouse/节点/中转型", order = 23)]
    public class TransitNodeDef : NodeDef
    {
        public override ENodeType NodeType => ENodeType.Transit;

        [Tooltip("待定 #6：内部暂存容量，暂按每类型小容量（如 = Pin 最大速率）")]
        public int StorageCapPerItem = 1;
    }
}
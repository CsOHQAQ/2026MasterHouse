using UnityEngine;

namespace MasterHouse
{
    /// <summary>加工型节点（§7）：消耗输入暂存推进配方，产出写入输出暂存。</summary>
    [CreateAssetMenu(fileName = "加工节点", menuName = "MasterHouse/节点/加工型", order = 21)]
    public class ProcessorNodeDef : NodeDef
    {
        public override ENodeType NodeType => ENodeType.Processor;

        [Tooltip("待定 #3：先按「策划配单条配方」实现；加工时长配在 RecipeDef.WorkTicks")]
        public RecipeDef Recipe;

        [Tooltip("输入暂存上限（v1 简化：每种物资统一一个值）")]
        public int InputStorageCapPerItem = 10;

        [Tooltip("输出暂存上限（v1 简化：每种物资统一一个值）")]
        public int OutputStorageCapPerItem = 10;
    }
}
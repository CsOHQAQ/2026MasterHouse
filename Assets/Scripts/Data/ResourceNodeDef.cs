using UnityEngine;

namespace MasterHouse
{
    /// <summary>资源型节点（§7）：无输入，按速率生产；自身暂存满则停产。</summary>
    [CreateAssetMenu(fileName = "资源节点", menuName = "MasterHouse/节点/资源型", order = 20)]
    public class ResourceNodeDef : NodeDef
    {
        public override ENodeType NodeType => ENodeType.Resource;

        public ItemDef OutputItem;

        [Tooltip("每 N tick 生产一次（速率一律以 tick 为单位 §3.1）")]
        public int TicksPerProduction = 10;

        [Tooltip("每次生产的数量")]
        public int AmountPerProduction = 1;

        [Tooltip("自身暂存上限，满则停产（§7）")]
        public int StorageCap = 10;
    }
}
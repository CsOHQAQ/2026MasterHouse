using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 仓库型节点（§7）：漏斗而非容器——收到即从本关经济中消失，计入全局 PlayerCargo。
    /// 无存储上限；v1 只进不出（输出待定 #15）。
    /// </summary>
    [CreateAssetMenu(fileName = "仓库节点", menuName = "MasterHouse/节点/仓库型", order = 22)]
    public class StorageNodeDef : NodeDef
    {
        public override ENodeType NodeType => ENodeType.Storage;

        [Tooltip("可接收的物资白名单；空列表 = 任意物资都收。建线时校验")]
        public List<ItemDef> Whitelist = new List<ItemDef>();
    }
}
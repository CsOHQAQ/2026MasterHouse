using System.Collections.Generic;
using UnityEngine;

namespace MasterPotion
{
    /// <summary>存储节点：可接收并转发配置内的资源（每种资源同时生成一对输入/输出端口），容量无上限。</summary>
    [CreateAssetMenu(menuName = "MasterPotion/Node/Storage Node", fileName = "StorageNode")]
    public class StorageNodeDef : NodeDef
    {
        public List<ResourceDef> resources = new();
    }
}

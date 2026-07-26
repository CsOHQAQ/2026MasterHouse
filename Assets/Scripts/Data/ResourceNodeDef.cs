using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterPotion
{
    [Serializable]
    public class ProductionEntry
    {
        public ResourceDef resource;
        [Tooltip("每隔多少秒产出 1 件")]
        public float interval = 2f;
        [Tooltip("输出缓存上限，堆满后停产")]
        public int maxBuffer = 5;
    }

    /// <summary>固定资源产出节点：按间隔向输出缓存生产资源。</summary>
    [CreateAssetMenu(menuName = "MasterPotion/Node/Resource Node", fileName = "ResourceNode")]
    public class ResourceNodeDef : NodeDef
    {
        public List<ProductionEntry> productions = new();
    }
}

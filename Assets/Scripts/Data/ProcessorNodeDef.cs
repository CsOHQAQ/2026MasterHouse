using System.Collections.Generic;
using UnityEngine;

namespace MasterPotion
{
    /// <summary>加工节点：输入/输出端口由全部配方的输入/输出资源并集决定。</summary>
    [CreateAssetMenu(menuName = "MasterPotion/Node/Processor Node", fileName = "ProcessorNode")]
    public class ProcessorNodeDef : NodeDef
    {
        public List<RecipeDef> recipes = new();
        [Tooltip("每种输入资源的缓存上限")]
        public int inputBufferCap = 5;
        [Tooltip("每种输出资源的缓存上限，堆满后暂停加工")]
        public int outputBufferCap = 5;
    }
}

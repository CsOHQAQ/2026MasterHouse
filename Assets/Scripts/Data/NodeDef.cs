using UnityEngine;

namespace MasterHouse
{
    public enum ENodeType
    {
        None,   //出现这个说明你忘了配置
        Resource,
        Processor,
        Collector,
    }

    
    public class NodeDef : ScriptableObject
    {
        public readonly ENodeType NodeType = ENodeType.None;
    }


    public class ResourceNodeDef : NodeDef
    {
        public readonly ENodeType NodeType = ENodeType.Resource;
        public readonly string NodeName;
    }
}
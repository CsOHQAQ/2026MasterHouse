using System.Collections.Generic;
using UnityEngine;

namespace MasterPotion
{
    public enum PortDirection { Input, Output }

    /// <summary>卡片边缘的资源端口（类似 UE 蓝图的 pin），链接的资源类型由端口决定。</summary>
    public class Port : MonoBehaviour
    {
        public NodeBase Node { get; private set; }
        public ResourceDef Resource { get; private set; }
        public PortDirection Direction { get; private set; }

        public readonly List<Link> Links = new();

        public bool IsConnected => Links.Count > 0;

        public void Init(NodeBase node, ResourceDef resource, PortDirection direction)
        {
            Node = node;
            Resource = resource;
            Direction = direction;
        }
    }
}

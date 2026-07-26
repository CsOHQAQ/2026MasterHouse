using System.Collections.Generic;
using UnityEngine;

namespace MasterPotion
{
    /// <summary>统一驱动所有节点与链接的模拟心跳。节点/链接在 OnEnable/OnDisable 中自行注册。</summary>
    public class SimulationManager : MonoBehaviour
    {
        public static readonly List<NodeBase> Nodes = new();
        public static readonly List<Link> Links = new();

        private void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < Nodes.Count; i++) Nodes[i].SimTick(dt);
            for (int i = 0; i < Links.Count; i++) Links[i].SimTick(dt);
        }
    }
}

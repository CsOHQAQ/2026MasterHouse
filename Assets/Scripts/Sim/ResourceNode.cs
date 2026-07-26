using System.Collections.Generic;

namespace MasterPotion
{
    /// <summary>固定资源产出节点：只出不进。</summary>
    public class ResourceNode : NodeBase
    {
        private ResourceNodeDef RDef => (ResourceNodeDef)Def;
        private readonly List<float> timers = new();

        public override void Init(NodeDef def)
        {
            base.Init(def);
            for (int i = 0; i < ((ResourceNodeDef)def).productions.Count; i++)
                timers.Add(0f);
        }

        public override bool CanAcceptInput(ResourceDef r) => false;

        public override void SimTick(float dt)
        {
            var prods = RDef.productions;
            for (int i = 0; i < prods.Count; i++)
            {
                var p = prods[i];
                if (p.resource == null || p.interval <= 0f) continue;

                timers[i] += dt;
                if (timers[i] < p.interval) continue;

                if (outputBuffer.Get(p.resource) < p.maxBuffer)
                {
                    outputBuffer.Add(p.resource);
                    timers[i] -= p.interval;
                }
                else
                {
                    timers[i] = p.interval; // 缓存满：暂停，但腾出空间后立即产出
                }
            }
        }

        protected override string BuildInfoText() => "库存: " + outputBuffer.ToDisplayString();
    }
}

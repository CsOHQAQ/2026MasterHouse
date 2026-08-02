namespace MasterPotion
{
    /// <summary>存储节点：outputBuffer 即库存，收进来的资源可直接被出链取走（中转站），容量无上限。</summary>
    public class StorageNode : NodeBase
    {
        private StorageNodeDef SDef => (StorageNodeDef)Def;

        public override bool CanAcceptInput(ResourceDef r) => SDef.resources.Contains(r);

        public override void ReceiveInput(ResourceDef r) => outputBuffer.Add(r);

        public override void SimTick(float dt) { }

        protected override string BuildInfoText() => "存量: " + outputBuffer.ToDisplayString();
    }
}

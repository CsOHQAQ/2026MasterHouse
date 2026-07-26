using System.Collections.Generic;
using UnityEngine;

namespace MasterPotion
{
    /// <summary>所有节点的运行时基类。视觉与端口由 NodeFactory 程序化构建。</summary>
    public abstract class NodeBase : MonoBehaviour
    {
        public NodeDef Def { get; private set; }

        public readonly List<Port> InputPorts = new();
        public readonly List<Port> OutputPorts = new();

        protected readonly ResourceBuffer outputBuffer = new();
        private TextMesh infoText;

        public virtual void Init(NodeDef def) => Def = def;

        public void SetInfoText(TextMesh text) => infoText = text;

        public void RegisterPort(Port port) =>
            (port.Direction == PortDirection.Input ? InputPorts : OutputPorts).Add(port);

        /// <summary>目标侧：当前是否愿意接收 1 件该资源（不满足需求 = 链接停止传输）。</summary>
        public abstract bool CanAcceptInput(ResourceDef r);

        /// <summary>目标侧：实际收下 1 件该资源。</summary>
        public virtual void ReceiveInput(ResourceDef r) { }

        /// <summary>源侧：输出缓存里是否有现货。</summary>
        public virtual bool HasOutput(ResourceDef r) => outputBuffer.Get(r) > 0;

        /// <summary>源侧：取走 1 件。</summary>
        public virtual void TakeOutput(ResourceDef r) => outputBuffer.TryRemove(r);

        /// <summary>本节点相关链接增删后由 LinkManager 调用。</summary>
        public virtual void OnConnectionsChanged() { }

        /// <summary>由 SimulationManager 每帧驱动。</summary>
        public abstract void SimTick(float dt);

        protected abstract string BuildInfoText();

        protected virtual void Update()
        {
            if (infoText != null) infoText.text = BuildInfoText();
        }

        protected virtual void OnEnable() => SimulationManager.Nodes.Add(this);
        protected virtual void OnDisable() => SimulationManager.Nodes.Remove(this);
    }
}

using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 节点表现层（§10 View 类）：节点表现 + 挂载 UI。
    /// View 只读：仅读取绑定的 NodeData 渲染，写操作必须经 Manager（§2）。
    /// </summary>
    public class NodeGO : MonoBehaviour
    {
        public NodeData Data { get; private set; }

        public void Bind(NodeData data)
        {
            Data = data;
            // TODO：按 Def.Shape 生成占格表现、按 Def.Pins 生成 Pin 表现
        }

        private void Update()
        {
            if (Data == null) return;

            // TODO（待定 #10：数据变化感知机制未定，v1 暂定每帧轮询）：
            // - 世界坐标 = Origin × GameConfig.GridSize
            // - 暂存量 / 配方进度显示（进度条插值 §3.1）
            // - IsIllegal：非法临时态提示表现（待定 #14）
        }
    }
}
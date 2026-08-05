using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 链接表现层（§10 View 类）：折线渲染 + 状态配色。View 只读（§2）。
    /// 状态配色参考 §6.5：空闲/在途=正常色，阻塞=灰色，断线/类型失效=红色（类型失效另加警示图标）。
    /// </summary>
    public class LinkGO : MonoBehaviour
    {
        public LinkData Data { get; private set; }

        public LineRenderer LineRenderer;

        public void Bind(LinkData data)
        {
            Data = data;
            // TODO：按 PathCells 生成折线顶点（世界坐标 = 格坐标 × GameConfig.GridSize）
        }

        private void Update()
        {
            if (Data == null) return;

            // TODO（待定 #10：v1 暂定每帧轮询）：
            // - PathCells 变化（玩家理线）时重建折线
            // - 按 State 切换配色（§6.5 表格）
        }
    }
}
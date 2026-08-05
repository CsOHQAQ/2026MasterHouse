using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 画布格显示（§10 View 类）。View 只读：不写任何数据（§2）。
    /// </summary>
    public class GridGO : MonoBehaviour
    {
        /// <summary>本格的全局格坐标。</summary>
        public Vector2Int Cell { get; private set; }

        public SpriteRenderer SpriteRenderer;

        public void Bind(Vector2Int cell)
        {
            Cell = cell;
            // TODO：世界坐标 = cell × GameConfig.GridSize；按格子类型着色
        }
    }
}
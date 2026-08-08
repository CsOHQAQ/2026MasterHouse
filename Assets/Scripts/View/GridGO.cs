using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 画布格显示（§10 View 类）。View 只读：不写任何数据（§2）。
    /// </summary>
    public class GridGO : MonoBehaviour
    {
        /// <summary>占位底格色（EGridType 目前仅 Default，策划细化格子类型后按类型着色）。</summary>
        private static readonly Color CellColor = new Color(0.22f, 0.22f, 0.25f);

        /// <summary>本格的全局格坐标。</summary>
        public Vector2Int Cell { get; private set; }

        public SpriteRenderer SpriteRenderer;

        public void Bind(Vector2Int cell)
        {
            Cell = cell;
            transform.position = ViewUtil.CellCenter(cell);

            // prefab 已带视觉时只定位；否则纯代码生成占位视觉（零素材依赖）
            if (SpriteRenderer == null)
            {
                SpriteRenderer = gameObject.AddComponent<SpriteRenderer>();
                SpriteRenderer.sprite = VisualAssets.WhiteSprite;
                SpriteRenderer.sharedMaterial = VisualAssets.UnlitMaterial;
                SpriteRenderer.sortingOrder = SortingOrders.Grid;
                SpriteRenderer.color = CellColor;
                float s = ViewUtil.GridSize;
                transform.localScale = new Vector3(s * 0.94f, s * 0.94f, 1f); // 留缝呈现格线
            }
        }
    }
}

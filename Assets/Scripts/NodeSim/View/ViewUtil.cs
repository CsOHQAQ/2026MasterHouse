using UnityEngine;

namespace MasterHouse
{
    /// <summary>View 层坐标换算工具。逻辑层只用格坐标，世界坐标仅在表现/交互层出现（§2）。</summary>
    public static class ViewUtil
    {
        /// <summary>一格的世界尺寸。</summary>
        public static float GridSize =>
            GameConfig.Instance != null ? GameConfig.Instance.GridSize : 1f;

        /// <summary>格中心世界坐标 =（格坐标 + 0.5）× GridSize——View 层统一坐标约定。</summary>
        public static Vector3 CellCenter(Vector2Int cell)
        {
            float s = GridSize;
            return new Vector3((cell.x + 0.5f) * s, (cell.y + 0.5f) * s, 0f);
        }

        /// <summary>格左下角世界坐标（节点根物体定位用）。</summary>
        public static Vector3 CellCorner(Vector2Int cell)
        {
            float s = GridSize;
            return new Vector3(cell.x * s, cell.y * s, 0f);
        }
    }
}

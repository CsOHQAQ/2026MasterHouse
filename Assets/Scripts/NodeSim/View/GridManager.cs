using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// View 层画布格管理：为已加载小关生成/回收 GridGO（§10 GridGO 的工厂与容器）。
    /// View 只读（§2）；小关 Load/Unload 时由 ViewManager 调用刷新。
    /// </summary>
    public class GridManager : MonoBehaviour
    {
        [Tooltip("可选：画布格 prefab；留空则纯代码生成占位格")]
        public GameObject gridPrefab;

        /// <summary>各关画布容器。仅键查询，不依赖枚举顺序。</summary>
        private readonly Dictionary<LevelData, Transform> containers =
            new Dictionary<LevelData, Transform>();

        public void ShowCanvas(LevelData level)
        {
            if (containers.ContainsKey(level)) return;

            var container = new GameObject($"画布_{level.Def.name}").transform;
            container.SetParent(transform, false);
            containers.Add(level, container);

            foreach (var cell in level.Def.Canvas.CellsAt(level.Def.WorldOrigin))
            {
                GameObject go;
                if (gridPrefab != null)
                {
                    go = Instantiate(gridPrefab, container);
                }
                else
                {
                    go = new GameObject($"格({cell.x},{cell.y})");
                    go.transform.SetParent(container, false);
                }

                var grid = go.GetComponent<GridGO>();
                if (grid == null)
                    grid = go.AddComponent<GridGO>();
                grid.Bind(cell);
            }
        }

        public void HideCanvas(LevelData level)
        {
            if (!containers.TryGetValue(level, out var container)) return;
            containers.Remove(level);
            Destroy(container.gameObject);
        }
    }
}

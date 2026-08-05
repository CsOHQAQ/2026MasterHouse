using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// View 层画布格管理：为聚焦小关生成/回收 GridGO（§10 GridGO 的工厂与容器）。
    /// View 只读（§2）；小关 Load/Unload 时由外部调用刷新。
    /// </summary>
    public class GridManager : MonoBehaviour
    {
        public GameObject gridPrefab;

        public void ShowCanvas(LevelData level)
        {
            // TODO：按 level.Def.Canvas.CellsAt(WorldOrigin) 逐格实例化 GridGO 并 Bind
        }

        public void HideCanvas()
        {
            // TODO：回收全部 GridGO（配合 Unload §8.4）
        }
    }
}
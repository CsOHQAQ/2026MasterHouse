using UnityEngine;
using UnityEngine.EventSystems;

namespace MasterHouse
{
    /// <summary>
    /// 玩家世界内交互 Controller（§2/§9）：拖拽、悬浮、理线。
    /// 只把输入翻译成对 Manager 的调用，不直接修改任何数据类（§2）。
    /// </summary>
    public class InteractionController : MonoBehaviour
    {
        private Camera cam;

        private void Awake()
        {
            cam = Camera.main;
        }

        private void Update()
        {
            // TODO：
            // - 从 Pin 拖出创建链接 → LinkManager.TryCreateLink
            // - 拖拽节点移动 → LevelManager.MoveNode（非法临时态交互提示待定 #14）
            // - 抓住线段手动拖排理线（§5）→ 校验后写回 PathCells（经 LinkManager）
            // - 删除链接 → LinkManager.DeleteLink
        }

        public static bool IsPointerOverUI()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }
    }
}
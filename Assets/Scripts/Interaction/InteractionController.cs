using UnityEngine;
using UnityEngine.EventSystems;

namespace MasterPotion
{
    /// <summary>
    /// 世界内的鼠标交互（统一用 Physics2D 点选，优先级：端口 > 卡片 > 链接）：
    /// - 左键从端口拖出：创建链接（拖到相反方向、同资源的端口上松开）
    /// - 左键拖拽卡片：按画布单元格吸附移动节点（只落在合法位置）
    /// - 双击链接：删除
    /// </summary>
    public class InteractionController : MonoBehaviour
    {
        private const float LinkClickDist = 0.2f;
        private const float DoubleClickWindow = 0.35f;

        private Camera cam;

        private Port dragPort;
        private LineRenderer dragLine;

        private NodeBase dragNode;
        private Vector3 dragNodeOffset;

        private Link lastClickedLink;
        private float lastLinkClickTime;

        private void Awake() => cam = Camera.main;

        private void Update()
        {
            if (PlacementController.Instance != null &&
                (PlacementController.Instance.IsPlacing ||
                 PlacementController.JustPlacedFrame == Time.frameCount))
            {
                CancelDrags();
                return;
            }

            if (BoardEditController.Instance != null && BoardEditController.Instance.IsEditing)
            {
                CancelDrags();
                return;
            }

            Vector2 world = cam.ScreenToWorldPoint(Input.mousePosition);

            if (Input.GetMouseButtonDown(0) && !IsPointerOverUI()) OnPress(world);
            if (Input.GetMouseButton(0)) OnHold(world);
            if (Input.GetMouseButtonUp(0)) OnRelease(world);
        }

        private void OnPress(Vector2 world)
        {
            var (port, node) = PickAt(world);

            if (port != null)
            {
                dragPort = port;
                dragLine = CreateDragLine(port);
                return;
            }

            if (node != null)
            {
                dragNode = node;
                dragNodeOffset = node.transform.position - (Vector3)world;
                return;
            }

            var link = LinkManager.Instance.FindLinkNear(world, LinkClickDist);
            if (link != null)
            {
                if (link == lastClickedLink &&
                    Time.unscaledTime - lastLinkClickTime < DoubleClickWindow)
                {
                    LinkManager.Instance.DeleteLink(link);
                    lastClickedLink = null;
                }
                else
                {
                    lastClickedLink = link;
                    lastLinkClickTime = Time.unscaledTime;
                }
            }
        }

        private void OnHold(Vector2 world)
        {
            if (dragPort != null && dragLine != null)
            {
                dragLine.SetPosition(0, dragPort.transform.position);
                dragLine.SetPosition(1, world);
            }
            else if (dragNode != null)
            {
                // 按格吸附移动：仅当目标区域完整在画布内且不与其他节点重叠时才落位
                var desiredCenter = (Vector3)world + dragNodeOffset;
                var origin = BoardGrid.SnapOrigin(desiredCenter, dragNode.Def.gridSize);
                if (origin != dragNode.GridOrigin &&
                    BoardGrid.Instance != null &&
                    BoardGrid.Instance.CanPlace(origin, dragNode.Def.gridSize, ignore: dragNode))
                {
                    dragNode.SetGridPlacement(origin);
                }
            }
        }

        private void OnRelease(Vector2 world)
        {
            if (dragPort != null)
            {
                var (targetPort, _) = PickAt(world);
                if (targetPort != null && LinkManager.Instance.CanConnect(dragPort, targetPort))
                    LinkManager.Instance.CreateLink(dragPort, targetPort);
                CancelDrags();
            }
            dragNode = null;
        }

        private void CancelDrags()
        {
            if (dragLine != null) Destroy(dragLine.gameObject);
            dragLine = null;
            dragPort = null;
            dragNode = null;
        }

        private static (Port, NodeBase) PickAt(Vector2 world)
        {
            var hits = Physics2D.OverlapPointAll(world);
            Port port = null;
            NodeBase node = null;
            foreach (var h in hits)
            {
                if (port == null) port = h.GetComponent<Port>();
                if (node == null) node = h.GetComponent<NodeBase>();
            }
            return (port, node);
        }

        private static LineRenderer CreateDragLine(Port port)
        {
            var go = new GameObject("DragLine");
            var line = go.AddComponent<LineRenderer>();
            LinkManager.SetupLine(line, 0.05f, SortingOrders.DragLine);
            var c = port.Resource != null ? port.Resource.color : Color.white;
            line.startColor = c;
            line.endColor = c;
            line.SetPosition(0, port.transform.position);
            line.SetPosition(1, port.transform.position);
            return line;
        }

        public static bool IsPointerOverUI() =>
            EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }
}

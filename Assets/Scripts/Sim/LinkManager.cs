using System.Linq;
using UnityEngine;

namespace MasterPotion
{
    /// <summary>链接的创建 / 删除 / 校验 / 点选查询。</summary>
    public class LinkManager : MonoBehaviour
    {
        public static LinkManager Instance { get; private set; }

        [Tooltip("每条链接的运送间隔（秒），启动时被 GameConfig 覆盖")]
        public float transferInterval = 1f;

        private void Awake() => Instance = this;

        /// <summary>确定两个端口中谁是输出、谁是输入。方向相同则失败。</summary>
        public static bool TryOrient(Port a, Port b, out Port from, out Port to)
        {
            from = null;
            to = null;
            if (a == null || b == null || a == b) return false;
            if (a.Direction == b.Direction) return false;
            from = a.Direction == PortDirection.Output ? a : b;
            to = a.Direction == PortDirection.Output ? b : a;
            return true;
        }

        public bool CanConnect(Port a, Port b)
        {
            if (!TryOrient(a, b, out var from, out var to)) return false;
            if (from.Node == to.Node) return false;          // 不允许自连
            if (from.Resource != to.Resource) return false;  // 端口资源类型必须一致
            if (SimulationManager.Links.Any(l => l.From == from && l.To == to)) return false; // 去重
            if (LinkRouter.Route(from, to) == null) return false; // 画布单元格内必须存在可行走线
            return true;
        }

        public Link CreateLink(Port a, Port b)
        {
            if (!CanConnect(a, b)) return null;
            TryOrient(a, b, out var from, out var to);

            var go = new GameObject($"Link_{from.Resource.name}");
            var line = go.AddComponent<LineRenderer>();
            SetupLine(line, 0.06f, SortingOrders.Link);

            var link = go.AddComponent<Link>();
            link.Init(from, to, transferInterval);
            from.Links.Add(link);
            to.Links.Add(link);
            from.Node.OnConnectionsChanged();
            to.Node.OnConnectionsChanged();
            return link;
        }

        public void DeleteLink(Link link)
        {
            if (link == null) return;
            var from = link.From;
            var to = link.To;
            from.Links.Remove(link);
            to.Links.Remove(link);
            Destroy(link.gameObject);
            from.Node.OnConnectionsChanged();
            to.Node.OnConnectionsChanged();
        }

        /// <summary>找出距离点击位置最近且在阈值内的链接（用于双击删除）。</summary>
        public Link FindLinkNear(Vector2 point, float maxDist)
        {
            Link best = null;
            float bestDist = maxDist;
            foreach (var l in SimulationManager.Links)
            {
                if (l.From == null || l.To == null) continue;
                float d = l.DistanceTo(point);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = l;
                }
            }
            return best;
        }

        public static void SetupLine(LineRenderer line, float width, int sortingOrder)
        {
            line.material = VisualAssets.UnlitMaterial;
            line.positionCount = 2;
            line.startWidth = width;
            line.endWidth = width;
            line.useWorldSpace = true;
            line.sortingOrder = sortingOrder;
        }
    }
}

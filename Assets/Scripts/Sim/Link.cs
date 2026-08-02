using System.Collections.Generic;
using UnityEngine;

namespace MasterPotion
{
    /// <summary>
    /// 一条从输出端口到输入端口的链接，资源类型即起始端口的资源类型。
    /// 走线由 LinkRouter 在画布单元格上寻路得到；画布内无可行路径时链接变红并停止传输。
    /// 离散传输：每隔 interval 秒运送 1 件；源无货或目标不收时阻塞（变灰）。
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class Link : MonoBehaviour
    {
        private static readonly Color BlockedColor = new Color(0.45f, 0.45f, 0.45f);
        private static readonly Color NoPathColor = new Color(0.75f, 0.25f, 0.25f);

        public Port From { get; private set; }
        public Port To { get; private set; }
        public ResourceDef Resource => From.Resource;

        /// <summary>画布内是否存在可行走线。</summary>
        public bool HasPath { get; private set; }

        private readonly List<Vector3> path = new();
        private LineRenderer line;
        private float interval = 1f;
        private float timer;
        private bool blocked;

        private int cachedBoardVersion = -1;
        private Vector3 cachedFromPos, cachedToPos;

        public void Init(Port from, Port to, float transferInterval)
        {
            From = from;
            To = to;
            interval = Mathf.Max(0.05f, transferInterval);
            line = GetComponent<LineRenderer>();
            RefreshPath();
        }

        public void SimTick(float dt)
        {
            if (From == null || To == null) return;
            if (!HasPath) return; // 无走线：不传输

            timer += dt;
            if (timer < interval) return;

            if (From.Node.HasOutput(Resource) && To.Node.CanAcceptInput(Resource))
            {
                From.Node.TakeOutput(Resource);
                To.Node.ReceiveInput(Resource);
                timer -= interval;
                blocked = false;
                TransferPulse.Spawn(new List<Vector3>(path), Resource.color);
            }
            else
            {
                timer = interval; // 保持蓄势，条件一满足立即运送
                blocked = true;
            }
        }

        /// <summary>点到走线折线的最近距离（用于点选）。</summary>
        public float DistanceTo(Vector2 point)
        {
            float best = float.MaxValue;
            for (int i = 0; i < path.Count - 1; i++)
                best = Mathf.Min(best, DistanceToSegment(point, path[i], path[i + 1]));
            return best;
        }

        private void RefreshPathIfNeeded()
        {
            if (cachedBoardVersion == BoardGrid.Version &&
                cachedFromPos == From.transform.position &&
                cachedToPos == To.transform.position) return;
            RefreshPath();
        }

        private void RefreshPath()
        {
            cachedBoardVersion = BoardGrid.Version;
            cachedFromPos = From.transform.position;
            cachedToPos = To.transform.position;

            var routed = LinkRouter.Route(From, To);
            path.Clear();
            if (routed != null)
            {
                HasPath = true;
                path.AddRange(routed);
            }
            else
            {
                // 无可行走线：画一条直线提示，但停止传输
                HasPath = false;
                path.Add(cachedFromPos);
                path.Add(cachedToPos);
            }
        }

        private void LateUpdate()
        {
            if (line == null || From == null || To == null) return;
            RefreshPathIfNeeded();

            line.positionCount = path.Count;
            for (int i = 0; i < path.Count; i++) line.SetPosition(i, path[i]);

            var c = !HasPath ? NoPathColor : blocked ? BlockedColor : Resource.color;
            line.startColor = c;
            line.endColor = c;
        }

        private void OnEnable() => SimulationManager.Links.Add(this);
        private void OnDisable() => SimulationManager.Links.Remove(this);

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-6f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return Vector2.Distance(p, a + ab * t);
        }
    }
}

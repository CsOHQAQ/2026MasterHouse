using UnityEngine;

namespace MasterPotion
{
    /// <summary>
    /// 一条从输出端口到输入端口的链接，资源类型即起始端口的资源类型。
    /// 离散传输：每隔 interval 秒运送 1 件；源无货或目标不收时阻塞（变灰）。
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class Link : MonoBehaviour
    {
        public Port From { get; private set; }
        public Port To { get; private set; }
        public ResourceDef Resource => From.Resource;

        private LineRenderer line;
        private float interval = 1f;
        private float timer;
        private bool blocked;

        public void Init(Port from, Port to, float transferInterval)
        {
            From = from;
            To = to;
            interval = Mathf.Max(0.05f, transferInterval);
            line = GetComponent<LineRenderer>();
        }

        public void SimTick(float dt)
        {
            if (From == null || To == null) return;

            timer += dt;
            if (timer < interval) return;

            if (From.Node.HasOutput(Resource) && To.Node.CanAcceptInput(Resource))
            {
                From.Node.TakeOutput(Resource);
                To.Node.ReceiveInput(Resource);
                timer -= interval;
                blocked = false;
                TransferPulse.Spawn(From.transform.position, To.transform.position, Resource.color);
            }
            else
            {
                timer = interval; // 保持蓄势，条件一满足立即运送
                blocked = true;
            }
        }

        private void LateUpdate()
        {
            if (line == null || From == null || To == null) return;
            line.SetPosition(0, From.transform.position);
            line.SetPosition(1, To.transform.position);
            var c = blocked ? new Color(0.45f, 0.45f, 0.45f) : Resource.color;
            line.startColor = c;
            line.endColor = c;
        }

        private void OnEnable() => SimulationManager.Links.Add(this);
        private void OnDisable() => SimulationManager.Links.Remove(this);
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 线上脉冲（§10 View 类）：由链接槽位数据直接驱动，无需额外逻辑（§6.4）。
    /// View 只读；连续感全部由视觉插值实现，逻辑层只给 tick 计数（§3.1）。
    /// </summary>
    public class TransferPulseGO : MonoBehaviour
    {
        public LinkData Data { get; private set; }

        private SpriteRenderer sprite;

        /// <summary>视觉进度 0~1：帧间向「下一 tick 到达的进度」平滑推进，纯表现不回写数据。</summary>
        private float visualT;

        public void Bind(LinkData data)
        {
            Data = data;
            sprite = VisualAssets.CreateSpriteSquare(transform, "脉冲块",
                Vector3.zero, 0.3f * ViewUtil.GridSize, Color.white, SortingOrders.Pulse);
            visualT = 0f;
        }

        private void Update()
        {
            if (Data == null || sprite == null) return;

            // 槽空 / 断线 / 类型失效：隐藏（§6.5）
            bool visible = Data.SlotCount > 0 &&
                (Data.State == ELinkState.InTransit || Data.State == ELinkState.Blocked);
            sprite.enabled = visible;
            if (!visible)
            {
                visualT = 0f;
                return;
            }

            transform.position = SamplePath(Data.PathCells, ComputeVisualT());
            sprite.color = Data.SlotItem != null ? Data.SlotItem.DisplayColor : Color.white;
        }

        private float ComputeVisualT()
        {
            // 阻塞：停驻在折线末端（目标门口），玩家一眼看出堵点（§6.4）
            if (Data.State == ELinkState.Blocked || Data.TransitTicks <= 0)
            {
                visualT = 1f;
                return visualT;
            }

            float baseT = Mathf.Clamp01((float)Data.TransitCounter / Data.TransitTicks);
            float capT = Mathf.Clamp01((Data.TransitCounter + 1f) / Data.TransitTicks);
            if (visualT < baseT)
                visualT = baseT; // 视觉落后于逻辑（掉帧等）时直接追上

            // 帧间平滑速度 = 逻辑推进速率（tick/秒 ÷ 在途总 tick），随倍速缩放、暂停停住
            var config = GameConfig.Instance;
            float ticksPerSecond = config != null ? Mathf.Max(1, config.TicksPerSecond) : 10f;
            var gm = GameManager.Instance;
            float timeScale = gm != null ? (gm.IsPaused ? 0f : gm.TimeScale) : 1f;
            float speed = ticksPerSecond * timeScale / Data.TransitTicks;

            // 上限压到 capT：新一趟运输开始（计时归零）时 capT 回落，视觉自动折返起点
            visualT = Mathf.Min(visualT + speed * Time.deltaTime, capT);
            return visualT;
        }

        /// <summary>按弧长比例沿折线取点。途径格均相邻，每段等长，直接按段插值。</summary>
        private static Vector3 SamplePath(List<Vector2Int> path, float t)
        {
            if (path == null || path.Count == 0) return Vector3.zero;
            if (path.Count == 1 || t >= 1f) return ViewUtil.CellCenter(path[path.Count - 1]);

            float f = Mathf.Clamp01(t) * (path.Count - 1);
            int i = Mathf.Min((int)f, path.Count - 2);
            return Vector3.Lerp(
                ViewUtil.CellCenter(path[i]),
                ViewUtil.CellCenter(path[i + 1]),
                f - i);
        }
    }
}

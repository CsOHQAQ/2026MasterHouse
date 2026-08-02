using System.Collections.Generic;
using UnityEngine;

namespace MasterPotion
{
    /// <summary>每完成一次运送，沿链接走线飞行一个小方块作为反馈。</summary>
    public class TransferPulse : MonoBehaviour
    {
        private List<Vector3> points;
        private float[] cumLength;
        private float totalLength;
        private float duration;
        private float t;

        /// <summary>沿折线飞行（至少 2 个点）。</summary>
        public static void Spawn(List<Vector3> pathPoints, Color color)
        {
            if (pathPoints == null || pathPoints.Count < 2) return;

            var go = new GameObject("Pulse");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = VisualAssets.WhiteSprite;
            sr.sharedMaterial = VisualAssets.UnlitMaterial;
            sr.color = color;
            sr.sortingOrder = SortingOrders.Pulse;
            go.transform.localScale = Vector3.one * 0.18f;
            go.transform.position = pathPoints[0];

            var pulse = go.AddComponent<TransferPulse>();
            pulse.points = pathPoints;
            pulse.cumLength = new float[pathPoints.Count];
            for (int i = 1; i < pathPoints.Count; i++)
                pulse.cumLength[i] = pulse.cumLength[i - 1] +
                    Vector3.Distance(pathPoints[i - 1], pathPoints[i]);
            pulse.totalLength = pulse.cumLength[pathPoints.Count - 1];
            // 速度大致恒定：路径越长飞得越久
            pulse.duration = Mathf.Clamp(pulse.totalLength * 0.08f, 0.25f, 1f);
        }

        public static void Spawn(Vector3 from, Vector3 to, Color color) =>
            Spawn(new List<Vector3> { from, to }, color);

        private void Update()
        {
            t += Time.deltaTime / duration;
            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }

            float dist = t * totalLength;
            for (int i = 1; i < points.Count; i++)
            {
                if (dist > cumLength[i]) continue;
                float seg = cumLength[i] - cumLength[i - 1];
                float k = seg < 1e-5f ? 0f : (dist - cumLength[i - 1]) / seg;
                transform.position = Vector3.Lerp(points[i - 1], points[i], k);
                return;
            }
            transform.position = points[points.Count - 1];
        }
    }
}

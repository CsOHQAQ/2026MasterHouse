using UnityEngine;

namespace MasterPotion
{
    /// <summary>每完成一次运送，沿链接飞行一个小方块作为反馈。</summary>
    public class TransferPulse : MonoBehaviour
    {
        private const float Duration = 0.3f;

        private Vector3 from;
        private Vector3 to;
        private float t;

        public static void Spawn(Vector3 from, Vector3 to, Color color)
        {
            var go = new GameObject("Pulse");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = VisualAssets.WhiteSprite;
            sr.sharedMaterial = VisualAssets.UnlitMaterial;
            sr.color = color;
            sr.sortingOrder = SortingOrders.Pulse;
            go.transform.localScale = Vector3.one * 0.18f;
            go.transform.position = from;

            var pulse = go.AddComponent<TransferPulse>();
            pulse.from = from;
            pulse.to = to;
        }

        private void Update()
        {
            t += Time.deltaTime / Duration;
            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }
            transform.position = Vector3.Lerp(from, to, t);
        }
    }
}

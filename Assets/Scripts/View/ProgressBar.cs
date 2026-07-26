using UnityEngine;

namespace MasterPotion
{
    /// <summary>加工节点卡片上的加工进度条（左对齐填充）。</summary>
    public class ProgressBar : MonoBehaviour
    {
        public ProcessorNode target;
        public Transform fill;
        public float width = 1.6f;

        private void LateUpdate()
        {
            if (target == null || fill == null) return;
            float t = Mathf.Clamp01(target.Progress01);
            var scale = fill.localScale;
            fill.localScale = new Vector3(Mathf.Max(0.0001f, width * t), scale.y, 1f);
            var pos = fill.localPosition;
            fill.localPosition = new Vector3(-width * (1f - t) * 0.5f, pos.y, pos.z);
        }
    }
}

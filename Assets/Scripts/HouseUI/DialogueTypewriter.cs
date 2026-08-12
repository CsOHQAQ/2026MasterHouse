using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 台词逐字显现（设计说明 §5.1）。
    ///
    /// **这是表现层计时，允许用 deltaTime**——模态对话框期间 tick 本来就停了（§8 闸门），
    /// 打字机不进逻辑层，不违反 §11.1「逻辑层禁止 Time.deltaTime」。
    /// 用 unscaledDeltaTime：调试面板把 GameManager 暂停时，对话仍要能读下去。
    ///
    /// 由 DialogueOverlay 在运行时挂到对话层实例上（不是布局件，不进 Prefab）。
    /// </summary>
    public sealed class DialogueTypewriter : MonoBehaviour
    {
        private Text target;
        private string full = string.Empty;
        private float charsPerSecond = 30f;
        private float shown;
        private int voicedCount; // 已发过逐字音的字符数：只有新字符出现才请求发声（音效需求 #8）

        /// <summary>全文是否已显完（点击语义分岔点：未显完 ⇒ 立即全文；已显完 ⇒ 下一步）。</summary>
        public bool IsComplete => target == null || shown >= full.Length;

        public void Play(Text label, string text, float speed)
        {
            target = label;
            full = text ?? string.Empty;
            charsPerSecond = speed;
            shown = 0f;
            voicedCount = 0;
            if (target == null) return;
            // 速度为无穷大（配置里关了打字机）时一步到位
            if (float.IsInfinity(charsPerSecond) || charsPerSecond <= 0f)
            {
                SkipToEnd();
                return;
            }
            target.text = string.Empty;
        }

        /// <summary>立即显满。跳全文不补发逐字音（一次点击不该带出一串打字声）。</summary>
        public void SkipToEnd()
        {
            shown = full.Length;
            voicedCount = full.Length;
            if (target != null) target.text = full;
        }

        /// <summary>换内容前收手，避免上一句的残留继续往新 Text 里写。</summary>
        public void Stop()
        {
            target = null;
            full = string.Empty;
            shown = 0f;
            voicedCount = 0;
        }

        private void Update()
        {
            if (IsComplete) return;
            shown += Time.unscaledDeltaTime * charsPerSecond;
            var count = Mathf.Clamp(Mathf.FloorToInt(shown), 0, full.Length);
            // 逐字音（音效需求 #8）：每有新字符落上屏请求一次，节奏由音效表 DialogueTyping 的 minInterval 节流
            if (count > voicedCount)
            {
                voicedCount = count;
                SfxManager.Play(ESfx.DialogueTyping);
            }
            // 注意：按字符截断，台词里若写了富文本标签会在标签中间被切开。
            // 现阶段台词是纯文本，需要富文本时再在这里做标签感知的截断。
            target.text = full.Substring(0, count);
        }
    }
}
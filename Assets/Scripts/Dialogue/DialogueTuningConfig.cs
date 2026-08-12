using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 对话全局调参（Model 层，运行时只读；设计说明 §3/§12）。
    /// 【务必独占本文件】ScriptableObject 必须与文件同名，否则 .asset 的脚本引用会损坏（见 RETRO）。
    /// </summary>
    [CreateAssetMenu(menuName = "MasterHouse/对话调参配置", fileName = "DialogueTuningConfig")]
    public sealed class DialogueTuningConfig : ScriptableObject
    {
        [Header("打字机（§5.1）")]

        [Tooltip("每秒显现字数。打字机是**表现层计时**、允许用 deltaTime——\n" +
                 "模态对话框期间 tick 本来就停了，它不进逻辑层，不违反 §11.1")]
        public float typewriterCharsPerSecond = 30f;

        [Tooltip("勾上则跳过逐字显现，台词一次性显满（无障碍/调试用）")]
        public bool skipTypewriter;

        [Header("去重（§4.6 / §6）")]

        [Tooltip("recent 环长度 N：同一 (种族, 触发分类) 最近 N 次抽中的组不再抽。\n" +
                 "候选被排空时自动清空该键的环重新筛——保证永远有话可说。\n" +
                 "【待确认，§12】默认 3")]
        public int recentRingLength = 3;

        // 气泡停留时长**不配在这里**：闲逛冒泡的调度器在访客侧（VisitorManager.ScheduleNextBubble），
        // 沿用 VisitorTuningConfig.bubbleIntervalTicks / bubbleJitterTicks / bubbleHoldTicks。
        // 一个数值只能有一个家（§4.3 的原则同样适用于跨模块配置）。

        /// <summary>安全取值：配置写了非正数时回落 1，避免除零与死循环。</summary>
        public int RecentRingLengthSafe => Mathf.Max(1, recentRingLength);

        /// <summary>安全取值：非正速度视为「不打字、直接显满」。</summary>
        public float TypewriterCharsPerSecondSafe =>
            skipTypewriter || typewriterCharsPerSecond <= 0f ? float.MaxValue : typewriterCharsPerSecond;
    }
}

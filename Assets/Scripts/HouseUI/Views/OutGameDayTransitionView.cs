using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>开始新一天的过场层 Prefab 引用（黑夜→白天，2026-08-14）。
    /// Prefab 里存的是**入夜时刻**的静态状态（夜空色 + 地平线微光），破晓的颜色推移全在 DayTransitionFx 里做。</summary>
    public sealed class OutGameDayTransitionView : MonoBehaviour
    {
        [Tooltip("整屏夜空底色；过场期间挡住全部点击")]
        public Image sky;
        [Tooltip("地平线光晕（软椭圆渐变）：夜里是幽蓝月光，破晓时染成暖橙并上升")]
        public Image glow;
        public Text dayLabel;
        public Text subLabel;
        /// <summary>当日结算正文（2026-08-14 结算并入过场）：夜幕阶段显示，破晓前淡出。</summary>
        public Text bodyLabel;
        /// <summary>「点击任意处 · 开始新的一天」提示。</summary>
        public Text hintLabel;
        /// <summary>日夜交替分帧画布（2026-08-14）：过场期间循环播放绘本风分帧序列；无帧素材时保持隐藏走纯色夜空。</summary>
        public RawImage cycleFrames;
    }
}

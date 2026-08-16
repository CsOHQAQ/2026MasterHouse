using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>设置页滑条行模板（音量类 0~100，2026-08-16 设置页重做）。取值与回调由绑定器接。</summary>
    public sealed class OutGameSettingsSliderRow : MonoBehaviour
    {
        public Image background;
        public Text label;
        public Slider slider;
        public Text value;
    }
}

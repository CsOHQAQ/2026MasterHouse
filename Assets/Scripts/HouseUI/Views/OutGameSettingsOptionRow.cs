using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>设置页左右切换行模板（开关/枚举选项，2026-08-16 设置页重做）。选项集与回调由绑定器接。</summary>
    public sealed class OutGameSettingsOptionRow : MonoBehaviour
    {
        public Image background;
        public Text label;
        public Text value;
        public Button left;
        public Button right;
        public Image indicator;
    }
}

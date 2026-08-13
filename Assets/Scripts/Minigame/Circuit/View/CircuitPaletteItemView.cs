using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>件库里的一种中转件（动态列表项模板，架构 §16.2）。纯字段袋。</summary>
    public sealed class CircuitPaletteItemView : MonoBehaviour
    {
        public Button button;
        public Image background;
        public Text label;

        [Tooltip("剩余数量，形如「2/3」")]
        public Text count;
    }
}

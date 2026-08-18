using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace MasterHouse
{
    /// <summary>件库里的一种中转件（动态列表项模板，架构 §16.2）。纯字段袋。</summary>
    public sealed class CircuitPaletteItemView : MonoBehaviour
    {
        public Button button;

        [Tooltip("通用九宫格节点卡底图。由 CircuitUIStyleConfig 配置。")]
        public Image background;

        [Tooltip("节点自身的功能图标，来自 NodeDef.FunctionIconSprite。")]
        public Image functionIcon;

        [Tooltip("条目高度随节点外接长宽动态调整。")]
        public LayoutElement layoutElement;

        [Tooltip("右上角的可摆数量数字容器。")]
        public RectTransform countDigitRoot;

        [Tooltip("数字图片模板；运行时按位克隆，模板自身保持隐藏。")]
        public Image countDigitTemplate;

        [System.NonSerialized] public readonly List<Image> countDigitInstances = new List<Image>();

        public Text label;

        [Tooltip("剩余数量，形如「2/3」")]
        public Text count;
    }
}

using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>当日结算面板的 Prefab 引用（访客交付说明 §7 日结）。文本与事件由 DaySettleOverlay 运行时绑定。</summary>
    public sealed class OutGameDaySettleView : MonoBehaviour
    {
        public Image scrim;
        public RectTransform panel;
        public Text title;
        public Text body;
        public Button confirmButton;
        public Text confirmLabel;
    }
}

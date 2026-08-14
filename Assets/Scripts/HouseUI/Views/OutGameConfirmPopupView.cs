using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>通用确认弹窗的 Prefab 引用（首个用例：结束今天，2026-08-14）。文本与事件由 ConfirmOverlay 运行时绑定。</summary>
    public sealed class OutGameConfirmPopupView : MonoBehaviour
    {
        public Image scrim;
        public RectTransform panel;
        public Text title;
        public Text body;
        public Button confirmButton;
        public Text confirmLabel;
        public Button cancelButton;
        public Text cancelLabel;
    }
}

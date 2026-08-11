using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    public sealed class OutGameHubRightDockView : MonoBehaviour
    {
        public Text title;
        public OutGameHubDockButtonView[] entries;
        [Tooltip("「家具摆放」入口（2026-08-11 自运行时按钮收编进 Prefab，可在 Prefab 模式调整）")]
        public Button furnitureButton;
        [Tooltip("「结束今天」入口（§7 日结；同上收编进 Prefab）")]
        public Button endDayButton;
    }
}

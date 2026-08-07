using UnityEngine.UI;

namespace MasterPotion
{
    /// <summary>完整标题设置页面。</summary>
    public sealed class OutGameSettingsPageView : OutGamePaperView
    {
        public Text dataSummary;
        public Button saveButton;
        public Button loadButton;
        public Toggle autoDialogueToggle;
        public Toggle hintToggle;
        public Toggle cameraShakeToggle;
    }
}

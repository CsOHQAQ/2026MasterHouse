using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    public sealed class OutGameHubTopBarView : MonoBehaviour
    {
        public Button timeButton;
        public Text weekDatePhase;
        public Text phaseRange;
        public Text clock;
        public Button creditButton;
        public Text creditLabel;
        public Button brandButton;
        public Text brandLabel;
        public Text welcomeLabel;
        public Button optionsButton;
        public Text optionsLabel;
        [Tooltip("声望/装饰分数值条（2026-08-11 自运行时动态件收编进 Prefab，可在 Prefab 模式调整）")]
        public Text economyChipLabel;
    }
}

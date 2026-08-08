using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>「今日委托」面板内容的 Prefab 引用。</summary>
    public sealed class OutGameTasksPanelView : MonoBehaviour
    {
        public Text focusText;
        public Button[] taskButtons = new Button[3];
        public Text[] taskLabels = new Text[3];
        public Text progressText;
    }
}

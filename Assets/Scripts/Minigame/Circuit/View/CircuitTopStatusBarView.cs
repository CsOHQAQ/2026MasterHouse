using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>顶部状态条的 Prefab 引用容器。实时文案由 CircuitMinigame 写入，样式由 CircuitUIStyleConfig 提供。</summary>
    public sealed class CircuitTopStatusBarView : MonoBehaviour
    {
        public Image background;
        public Text progressLabel;
        public Text linkBudgetLabel;
        public Text pieceBudgetLabel;
        public Text litLabel;
    }
}

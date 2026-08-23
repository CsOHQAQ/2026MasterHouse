using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>顶部状态条的 Prefab 引用容器。实时文案由 CircuitMinigame 写入，样式由 CircuitUIStyleConfig 提供。</summary>
    public sealed class CircuitTopStatusBarView : MonoBehaviour
    {
        public Image background;
        public Image icon;
        public Text titleLabel;
        public Text subtitleLabel;
        public Text progressLabel;
        public Image linkBudgetBackground;
        public Text linkBudgetUsedValue;
        public Text linkBudgetTotalValue;
        [Tooltip("导线已用格数超出预算时的颜色；正常颜色直接使用 LinkBudgetUsedValue 的 Text Color。")]
        public Color linkBudgetUsedOverflowColor = new Color(0.95f, 0.36f, 0.33f, 1f);
        public Text linkBudgetLabel;
        public Text pieceBudgetLabel;
        public Text litLabel;
    }
}

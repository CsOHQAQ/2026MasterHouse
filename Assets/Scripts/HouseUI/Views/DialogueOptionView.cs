using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 分支选项槽位的字段袋。槽位在 DialogueView Prefab 里预摆（阶梯排布可逐个手调，§16.2 布局真相源），
    /// DialogueOverlay 运行时按分支选项数绑定或隐藏；选项数超出槽位数时克隆最后一个槽位向下延伸。
    /// </summary>
    public sealed class DialogueOptionView : MonoBehaviour
    {
        public Button button;

        [Tooltip("笔刷底图（Options-default / Options-hover 做 SpriteSwap）")]
        public Image background;

        public Text label;
    }
}
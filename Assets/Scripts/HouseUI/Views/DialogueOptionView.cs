using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 分支选项按钮模板的 Prefab 引用（纯字段袋）。
    /// 选项数量随对话内容变化，所以走「模板 Prefab + 运行时实例化」（§16.2 硬约定），
    /// 不再是旧版那种固定 7 个槽位、按需隐藏的做法——那样有上限、且选项少时不会重排居中。
    /// </summary>
    public sealed class DialogueOptionView : MonoBehaviour
    {
        public Button button;

        [Tooltip("笔刷底图（Options-default / Options-hover 做 SpriteSwap）")]
        public Image background;

        public Text label;
    }
}
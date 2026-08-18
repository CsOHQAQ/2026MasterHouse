using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 分支选项槽位的字段袋。槽位在 DialogueView Prefab 里预摆（可逐个手调，§16.2 布局真相源），
    /// DialogueOverlay 运行时按分支选项数**底对齐**绑定或隐藏；超出槽位数时克隆最上一个槽位向上延伸。
    ///
    /// 2026-08-19 二图叠放（2.0 设计图）：`选项-默认`(1598×218) 与 `选项-悬停`(1946×290) 两张素材
    /// 主体几乎一样大（1536×176 vs 1622×180），悬停多出来的那一大块全是右侧那条尖尾。
    /// 塞进同一个 rect 做 SpriteSwap 会把蓝条主体缩掉 13%、尾巴压扁，所以改成两张 Image 各按
    /// 自己的原始比例定尺寸、按**主体中心对齐**叠放，切状态只切显隐（见 SetSelected）。
    /// </summary>
    public sealed class DialogueOptionView : MonoBehaviour
    {
        public Button button;

        [Tooltip("默认底图（选项-默认，米白纸条）")]
        public Image background;

        [Tooltip("选中/悬停底图（选项-悬停，蓝水彩带尖尾）。尺寸与偏移在 Prefab 里按素材原始比例摆好")]
        public Image hoverBackground;

        public Text label;

        /// <summary>
        /// 切换选中态：只切两张底图的显隐，不改任何尺寸（尺寸是 Prefab 的真相）。
        /// 两张都关掉射线、点击由槽位根节点那张透明 Image 接（否则切显隐会把按钮的
        /// targetGraphic 一起关掉、选项变成点不动的死条）。
        /// </summary>
        public void SetSelected(bool selected)
        {
            if (background != null) background.enabled = !selected;
            if (hoverBackground != null) hoverBackground.enabled = selected;
        }
    }
}

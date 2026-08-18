using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 商店页（STORE，按美术示意图重做）的 Prefab 引用：全屏底图 + 分类页签（Q/E 切换）+
    /// 左侧卡片滚动网格 + 右侧大预览与描述 + 价格/购买 + 获得弹窗。
    /// 布局与美术烘在 Prefab（§16.2）；内容与事件由 StoreOverlay 运行时绑定。
    /// </summary>
    public sealed class OutGameStorePageView : MonoBehaviour
    {
        [Header("底与顶栏")]
        public RawImage background;
        public Text title;
        public Text tokenLabel;

        [Header("分类页签")]
        public Image categoryIcon;
        public Text categoryName;
        public Text categoryDesc;
        public Button prevCategory;
        public Button nextCategory;
        [Tooltip("五个分类圆标（盆栽/摆件/桌椅/壁挂/灯具，对应素材 1~5.png）")]
        public Sprite[] categorySprites = new Sprite[5];

        [Header("卡片滚动网格（模板实例化，§16.2）")]
        public ScrollRect scroll;
        public RectTransform gridContent;
        public Text emptyLabel;

        [Header("右侧预览")]
        public RawImage preview;
        public Text itemName;
        public Text itemDesc;
        public Text priceLabel;
        public Button buyButton;

        [Header("交互")]
        public Button closeButton;

        [Header("选色（设计稿 §5，2026-08-14）")]
        [Tooltip("预览下方的色块行容器：色块按当前家具族的配色变体数运行时实例化")]
        public RectTransform swatchRoot;

        [Header("键位提示（设计稿 §7：无商品时隐藏）")]
        public Image colorKeycap;
        [Tooltip("「X 改变颜色」的悬停图（绑定层运行时补 Button 并做 SpriteSwap）")]
        public Sprite colorKeycapHover;
        public Text colorKeycapLabel;
        public Image buyKeycap;
        public Text buyKeycapLabel;

        [Header("获得弹窗（NEW ITEM OBTAINED）")]
        public CanvasGroup obtainedGroup;
        public RawImage obtainedThumb;
        public Text obtainedName;
        public Text obtainedDesc;
        public Button obtainedClose;
        [Tooltip("弹窗左缘的配色列（设计稿获得弹窗：展示该族全部配色，高亮买到的那个）")]
        public RectTransform obtainedSwatchRoot;
    }
}

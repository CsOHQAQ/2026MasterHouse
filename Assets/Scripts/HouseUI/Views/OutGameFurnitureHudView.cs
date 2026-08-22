using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 家具模式 HUD 的 Prefab 引用（原型期运行时 uGUI 固化为 Prefab，§16.2）。
    /// 槽位为模板实例化（FurnitureSlot.prefab）；文本、状态与事件由 FurnitureRoomHud 运行时绑定。
    /// </summary>
    public sealed class OutGameFurnitureHudView : MonoBehaviour
    {
        [Header("放置模式房间背景")]
        [Tooltip("按房间表顺序：起居室、卧室、厨房、书房。仅在进入家具放置模式后使用，不影响主宅总览。")]
        public Sprite[] roomBackgrounds = new Sprite[4];

        [Header("顶部（整体可淡出/隐藏）")]
        [Tooltip("顶部容器：拖拽布置时淡出、「隐藏界面」时隐藏")] public CanvasGroup topGroup;
        public Text creditLabel;
        public Button hideUiButton;
        public Button gridToggleButton;
        public Text gridToggleLabel;
        public Button exitButton;
        [Tooltip("左下「ESC 返回」：和「完成放置」同样退出摆放模式（2026-08-20 设计图）")]
        public Button backButton;
        [Tooltip("购买家具：退出摆放模式并打开商店（仓库只展示已拥有，购买一律走商店）")]
        public Button storeButton;

        [Header("「显示界面」小按钮（隐藏态唯一入口）")]
        public CanvasGroup restoreGroup;
        public Button restoreButton;

        [Header("收纳栏")]
        [Tooltip("收纳栏面板（拖回收纳的命中区域）")] public RectTransform inventoryRect;
        public CanvasGroup inventoryGroup;
        [Tooltip("拖拽悬停时的落点高亮层")] public Image dropHint;
        [Tooltip("分类页签：图标三态图，下标对应 FurnitureRoomHud.TabCategories；表里没有的分类运行时隐藏")]
        public Button[] tabButtons = new Button[6];
        public Image[] tabBackgrounds = new Image[6];
        public Text[] tabLabels = new Text[6];
        public Button prevPageButton;
        public Button nextPageButton;
        public Text pageLabel;
        [Tooltip("槽位实例化容器")] public RectTransform slotsRoot;

        [Header("提示条")]
        public CanvasGroup toastGroup;
        public Text toastLabel;

        [Header("购买确认弹窗（默认隐藏）")]
        public CanvasGroup purchaseGroup;
        public Button purchaseScrimButton;
        public Text purchaseTitle;
        public Text purchaseDesc;
        public Button purchaseConfirmButton;
        public Button purchaseCancelButton;
    }
}

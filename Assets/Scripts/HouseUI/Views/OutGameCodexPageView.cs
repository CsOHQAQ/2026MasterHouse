using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 访客图鉴页（2026-08-18 按 2.0 设计图新建）：一排档案卡横向铺开，正中一张是焦点卡。
    /// 卡面素材是整图（每个种族一张「显示」+ 一张「不显示」剪影），故这里只存槽位与素材引用，
    /// 卡面由 <see cref="CodexOverlay"/> 按当前焦点与解锁态填。位置尺寸一律以 Prefab 为准（§16.2）。
    /// </summary>
    public sealed class OutGameCodexPageView : MonoBehaviour
    {
        [Header("底图与标题")]
        public RawImage background;
        public Text title;

        [Header("卡位（左→右，正中那个是焦点位）")]
        [Tooltip("卡片图；数量决定一屏铺几张，正中一张为焦点（数量应为奇数）")]
        public Image[] cardSlots;
        public Button[] cardButtons;

        [Header("图鉴条目（三个数组一一对应，按种族表行序烘在 Prefab 上）")]
        [Tooltip("收录的种族；顺序即翻页顺序")]
        public VisitorRaceDef[] races;
        [Tooltip("已查看：彩色档案卡")]
        public Sprite[] revealedCards;
        [Tooltip("未查看/未解锁：蓝色剪影卡")]
        public Sprite[] hiddenCards;

        [Header("键位条")]
        public Button backButton;
        [Tooltip("中键：切换选项（切到下一张卡）")]
        public Button switchButton;
        [Tooltip("空格：查看（把焦点卡翻成彩色）")]
        public Button viewButton;

        [Header("焦点卡编号（卡面素材把 NO.001 画死了，用一块纸色补丁盖掉再写真实编号）")]
        [Tooltip("补丁与编号的共同父节点：位置压在焦点卡的 NO. 那一行上，整体带卡面的倾角")]
        public RectTransform focusNumberRoot;
        public Text focusNumber;

        [Header("焦点卡说明（可空：设计图上没有，留作后续扩展的接缝）")]
        public Text focusName;
        public Text focusNote;
    }
}

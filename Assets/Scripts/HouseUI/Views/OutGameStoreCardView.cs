using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 商店卡片模板（§16.2 动态列表项 = 模板 Prefab + 运行时实例化）。
    /// 三态框素材烘在模板上：默认 defaul / 悬停 hover（SpriteSwap）/ 选中 selected（绑定层置换 normal 图）。
    /// </summary>
    public sealed class OutGameStoreCardView : MonoBehaviour
    {
        public Button button;
        public Image frame;
        public RawImage thumb;
        public Text priceLabel;
        [Tooltip("状态角标：已售罄 / ？（声望未解禁）")]
        public Text mark;

        [Header("三态框（Prefab 烘焙引用，绑定层切换）")]
        public Sprite normalSprite;
        public Sprite hoverSprite;
        public Sprite selectedSprite;
    }
}

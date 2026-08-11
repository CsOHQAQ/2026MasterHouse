using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>商城货架卡模板的序列化引用（§16.2 动态列表项 = Prefab 模板 + 运行时实例化）。</summary>
    public sealed class MarketCardView : MonoBehaviour
    {
        public Image background;
        public Button button;
        public Text label;
        public Image thumb;
    }
}

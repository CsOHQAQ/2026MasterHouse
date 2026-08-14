using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>家具图鉴卡列表项模板的序列化引用（§16.2 动态列表项 = Prefab 模板 + 运行时实例化）。</summary>
    public sealed class DeviceCardView : MonoBehaviour
    {
        public Image background;
        public Button button;
        public Text label;
        [Tooltip("家具缩略图（在 ThumbArea 容器内保比例自适应；2026-08-14 图鉴改列真实摆放家具）")]
        public RawImage thumb;
    }
}

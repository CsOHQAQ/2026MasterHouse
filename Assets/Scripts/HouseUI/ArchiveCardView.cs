using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>档案卡列表项模板的序列化引用（§16.2 动态列表项 = Prefab 模板 + 运行时实例化）。</summary>
    public sealed class ArchiveCardView : MonoBehaviour
    {
        public Image background;
        public Button button;
        public Text label;
        public RawImage art;
    }
}

using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>成就行模板的序列化引用（§16.2 动态列表项 = Prefab 模板 + 运行时实例化）。</summary>
    public sealed class AchievementRowView : MonoBehaviour
    {
        public Image background;
        public Text label;
    }
}

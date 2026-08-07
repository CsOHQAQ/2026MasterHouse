using UnityEngine;
using UnityEngine.UI;

namespace MasterPotion
{
    /// <summary>单个存档位模板；数据由控制器填充，排版留在 Prefab。</summary>
    public sealed class OutGameSaveSlotView : MonoBehaviour
    {
        public Button button;
        public Image mark;
        public Text slotNumber;
        public Text eyebrow;
        public Text information;
        public Text actionLabel;
    }
}

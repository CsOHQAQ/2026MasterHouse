using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 家具收纳栏槽位模板（§16.2 动态列表项 = 模板 Prefab + 运行时实例化）。
    /// 四种状态的元素全部烘在模板上，由 FurnitureRoomHud 按状态显隐：
    /// 可拖出（缩略图+名称）/ 已摆放（置灰+角标）/ 可购买（价格遮罩）/ 未解禁（？遮罩）。
    /// </summary>
    public sealed class OutGameFurnitureSlotView : MonoBehaviour
    {
        public Image background;
        public Image thumb;
        public Text nameLabel;

        [Header("已摆放态")]
        public Text placedLabel;

        [Header("可购买态")]
        public GameObject lockMask;
        public Text priceLabel;

        [Header("未解禁态（？）")]
        public GameObject unknownMask;
        public Text unknownMark;
        public Text unknownRequirement;
    }
}

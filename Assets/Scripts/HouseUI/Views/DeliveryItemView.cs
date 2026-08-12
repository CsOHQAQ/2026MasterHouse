using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 仓库条目模板的字段袋（交付页落地说明 §3：条目走「模板 Prefab + 运行时实例化」，§16.2 硬约定）。
    /// 一条 = 图标 + 名字 + 数量；拖拽行为在同物体上的 DeliveryDragSource。
    /// </summary>
    public sealed class DeliveryItemView : MonoBehaviour
    {
        [Tooltip("条目底框（选中态换色：指示这件正在交付框里）")] public Image background;
        [Tooltip("物品图标；ItemDef.icon 为空时按 DisplayColor 显示占位色块（美术未接入的过渡态）")] public Image icon;
        public Text itemName;
        [Tooltip("数量（只列 > 0 的条目）")] public Text count;
    }
}

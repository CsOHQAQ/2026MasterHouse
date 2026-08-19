using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 收纳格纸卡的悬停换图（2026-08-20 放置页设计图）。
    /// 槽位靠 EventTrigger 起手拖拽、不是 Button，用不上 SpriteSwap，只能自己听进出事件。
    /// 纯表现，不碰任何状态。
    /// </summary>
    [RequireComponent(typeof(Image))]
    public sealed class OutGameFurnitureSlotHover : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler
    {
        public Sprite normal;
        public Sprite hover;

        private Image target;

        private void Awake() => target = GetComponent<Image>();

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (target != null && hover != null) target.sprite = hover;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (target != null && normal != null) target.sprite = normal;
        }
    }
}

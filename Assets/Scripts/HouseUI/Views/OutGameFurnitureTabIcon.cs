using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 放置页分类图标的三态换图（2026-08-20 设计图）。
    /// 选中态由 FurnitureRoomHud 调 <see cref="SetSelected"/> 指定——Button 自带的
    /// SpriteSwap 只认指针状态，认不出「当前页签是哪个」。悬停态在这里自己听。
    /// 纯表现，不碰任何状态。
    /// </summary>
    [RequireComponent(typeof(Image))]
    public sealed class OutGameFurnitureTabIcon : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler
    {
        public Sprite normal;
        public Sprite hover;
        public Sprite selected;

        private Image target;
        private bool isSelected;
        private bool isHovering;

        private void Awake() => target = GetComponent<Image>();

        public void SetSelected(bool value)
        {
            isSelected = value;
            Apply();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            isHovering = true;
            Apply();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            isHovering = false;
            Apply();
        }

        private void Apply()
        {
            if (target == null) target = GetComponent<Image>();
            if (target == null) return;
            // 选中优先于悬停：选中的那一格鼠标划过去也不该退回悬停态
            var sprite = isSelected ? selected : isHovering ? hover : normal;
            if (sprite != null) target.sprite = sprite;
        }
    }
}

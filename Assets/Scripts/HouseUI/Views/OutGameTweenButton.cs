using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>网页按钮 hover/press 的 DOTween 手感；兼任按钮点击音的统一发声点（音效需求 #1）。</summary>
    public sealed class OutGameTweenButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, IPointerClickHandler, ISelectHandler, IDeselectHandler
    {
        public float hoverScale = 1.025f;
        public Graphic hoverGraphic;

        [Tooltip("点击音：默认基础点击；被更具体的动作音取代时设为 None（如访客卡、对话推进按钮）")]
        public ESfx clickSfx = ESfx.UiClick;

        private RectTransform rectTransform;
        private Vector3 baseScale = Vector3.one;
        private Selectable selectable;

        private void Awake()
        {
            CacheTransform();
        }

        private void OnEnable()
        {
            CacheTransform();
        }

        private void CacheTransform()
        {
            if (rectTransform != null) return;
            rectTransform = transform as RectTransform;
            if (rectTransform != null) baseScale = rectTransform.localScale;
        }

        private void OnDisable()
        {
            CacheTransform();
            if (rectTransform == null) return;
            rectTransform.DOKill();
            rectTransform.localScale = baseScale;
            if (hoverGraphic != null)
                hoverGraphic.color = new Color(hoverGraphic.color.r, hoverGraphic.color.g, hoverGraphic.color.b, 0);
        }

        public void OnPointerEnter(PointerEventData eventData) => SetHighlighted(true);

        public void OnPointerExit(PointerEventData eventData)
        {
            var selected = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;
            if (!selected) SetHighlighted(false);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            CacheTransform();
            if (rectTransform == null) return;
            rectTransform.DOKill();
            rectTransform.DOScale(baseScale * .97f, .08f).SetEase(Ease.OutQuad).SetUpdate(true);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            CacheTransform();
            if (rectTransform == null) return;
            rectTransform.DOKill();
            rectTransform.DOScale(baseScale * hoverScale, .1f).SetEase(Ease.OutQuad).SetUpdate(true);
            FadeHover(1, .1f);
        }

        /// <summary>点击音走本组件而非各处 onClick：Prefab 上挂了本组件的按钮零改动全覆盖。</summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (selectable == null) selectable = GetComponent<Selectable>();
            if (selectable != null && !selectable.IsInteractable()) return; // 置灰按钮点了不响
            SfxManager.Play(clickSfx);
        }

        public void OnSelect(BaseEventData eventData) => SetHighlighted(true);
        public void OnDeselect(BaseEventData eventData) => SetHighlighted(false);

        private void SetHighlighted(bool highlighted)
        {
            CacheTransform();
            if (rectTransform == null) return;
            rectTransform.DOKill();
            rectTransform.DOScale(highlighted ? baseScale * hoverScale : baseScale, .16f)
                .SetEase(Ease.OutCubic).SetUpdate(true);
            FadeHover(highlighted ? 1 : 0, .16f);
        }

        private void FadeHover(float alpha, float duration)
        {
            if (hoverGraphic == null) return;
            hoverGraphic.DOKill();
            hoverGraphic.DOFade(alpha, duration).SetEase(Ease.OutCubic).SetUpdate(true);
        }
    }
}

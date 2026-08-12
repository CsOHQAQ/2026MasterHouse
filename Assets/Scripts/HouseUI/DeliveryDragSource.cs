using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 仓库条目的拖拽源（交付页落地说明 §5.4）：拖起 → 跟随 → 命中判定 → 落空回弹。
    ///
    /// 项目此前的 HouseUI 是纯点击交互（家具模式那套拖拽是世界空间的独立模式，不通用），
    /// 所以这套 UGUI 拖拽是新造的。刻意做得很薄：
    ///   · 幽灵 = 条目自身的克隆体，不需要额外 Prefab，视觉天然一致；
    ///   · 命中判定 = 交付框 Rect 的屏幕点包含测试，不用 IDropHandler
    ///     （落点只有一个，为它铺一套 drop 目标接口是过度设计）；
    ///   · 拖拽**不预扣库存**——库存只在 VisitorManager.Submit 里扣，所以取消拖拽无需任何回滚。
    ///
    /// 已知取舍：条目消费掉拖拽事件后，列表**不能靠拖动滚动**（滚轮与滚动条照常可用）。
    /// 竖直拖动转发给 ScrollRect 的写法要引入方向阈值与两套手势的边界情况，
    /// 现阶段一屏放得下几种物资，不值当。
    /// </summary>
    public sealed class DeliveryDragSource : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private RectTransform dragLayer;
        private RectTransform dropZone;
        private Camera uiCamera;
        private Action onDroppedInZone;

        private RectTransform ghost;

        /// <summary>由 DeliveryOverlay 在实例化条目后绑定。dropZone 命中时回调 onDropped。</summary>
        public void Bind(RectTransform dragLayer, RectTransform dropZone, Camera uiCamera, Action onDropped)
        {
            this.dragLayer = dragLayer;
            this.dropZone = dropZone;
            this.uiCamera = uiCamera;
            onDroppedInZone = onDropped;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (dragLayer == null) return;
            var self = (RectTransform)transform;

            // 幽灵 = 自己的克隆：连同图标/文字/底框一起，拖起来的就是玩家看到的那一条
            var clone = Instantiate(gameObject, dragLayer, false);
            clone.name = "DragGhost";
            var cloneSource = clone.GetComponent<DeliveryDragSource>();
            if (cloneSource != null) Destroy(cloneSource); // 幽灵不能再触发拖拽
            var layout = clone.GetComponent<LayoutElement>();
            if (layout != null) Destroy(layout);

            ghost = (RectTransform)clone.transform;
            ghost.anchorMin = ghost.anchorMax = new Vector2(.5f, .5f);
            ghost.pivot = new Vector2(.5f, .5f);
            ghost.sizeDelta = self.rect.size;
            ghost.localScale = Vector3.one;

            var group = clone.GetComponent<CanvasGroup>();
            if (group == null) group = clone.AddComponent<CanvasGroup>();
            group.alpha = .88f;
            group.blocksRaycasts = false; // 否则幽灵会挡住自己下面的交付框，命中判定永远落空

            MoveGhost(eventData);
        }

        public void OnDrag(PointerEventData eventData) => MoveGhost(eventData);

        public void OnEndDrag(PointerEventData eventData)
        {
            if (ghost == null) return;

            var hit = dropZone != null &&
                      RectTransformUtility.RectangleContainsScreenPoint(dropZone, eventData.position, uiCamera);
            if (hit)
            {
                DestroyGhost();
                onDroppedInZone?.Invoke();
                return;
            }

            // 落空回弹：飞回条目原位再销毁，给玩家一个「没放进去」的明确反馈
            var target = WorldToDragLayer(((RectTransform)transform).position);
            var flying = ghost;
            ghost = null;
            flying.DOAnchorPos(target, .18f).SetEase(Ease.OutCubic).SetUpdate(true)
                .OnComplete(() => { if (flying != null) Destroy(flying.gameObject); });
        }

        private void MoveGhost(PointerEventData eventData)
        {
            if (ghost == null) return;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    dragLayer, eventData.position, uiCamera, out var local))
                ghost.anchoredPosition = local;
        }

        private Vector2 WorldToDragLayer(Vector3 worldPosition)
        {
            var screen = RectTransformUtility.WorldToScreenPoint(uiCamera, worldPosition);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(dragLayer, screen, uiCamera, out var local);
            return local;
        }

        private void DestroyGhost()
        {
            if (ghost == null) return;
            Destroy(ghost.gameObject);
            ghost = null;
        }

        private void OnDestroy()
        {
            // 条目随页面销毁时幽灵可能还挂在拖拽层上（拖到一半关页面）
            if (ghost != null) Destroy(ghost.gameObject);
        }
    }
}

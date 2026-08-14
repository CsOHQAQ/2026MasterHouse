using System;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 商店页键位（设计稿 §7）：Q/E 切分类、X 改变颜色、**空格购买**、空格/ESC 关获得弹窗（ESC 由壳统一处理）。
    /// 由 StoreOverlay 打开时挂到页面根上，随页销毁；语义全部回调给 Overlay，本类不碰业务。
    /// </summary>
    public sealed class StoreHotkeys : MonoBehaviour
    {
        private Action prevCategory;
        private Action nextCategory;
        private Action cycleColor;
        private Action buy;
        private Action closePopup;
        private Func<bool> popupOpen;

        /// <summary>挂载帧号：吞掉打开当帧的按键，避免上一层界面的输入泄漏。</summary>
        private int spawnFrame;

        public void Bind(Action onPrev, Action onNext, Action onCycleColor, Action onBuy,
            Action onClosePopup, Func<bool> isPopupOpen)
        {
            prevCategory = onPrev;
            nextCategory = onNext;
            cycleColor = onCycleColor;
            buy = onBuy;
            closePopup = onClosePopup;
            popupOpen = isPopupOpen;
            spawnFrame = Time.frameCount;
        }

        private void Update()
        {
            if (Time.frameCount <= spawnFrame) return;
            var popup = popupOpen != null && popupOpen();
            if (popup)
            {
                // 弹窗态：空格只负责收下弹窗（设计稿：空格&ESC 都能关）
                if (Input.GetKeyDown(KeyCode.Space)) closePopup?.Invoke();
                return;
            }
            if (Input.GetKeyDown(KeyCode.Q)) prevCategory?.Invoke();
            if (Input.GetKeyDown(KeyCode.E)) nextCategory?.Invoke();
            if (Input.GetKeyDown(KeyCode.X)) cycleColor?.Invoke();
            if (Input.GetKeyDown(KeyCode.Space)) buy?.Invoke(); // 购买键是空格（不是回车）
        }
    }
}

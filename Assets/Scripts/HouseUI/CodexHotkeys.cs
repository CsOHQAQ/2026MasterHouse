using System;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 图鉴页键位（非布局件，运行时挂，口径同 <see cref="StoreHotkeys"/>）：
    /// 中键/←→/QE 切换选项、空格查看。ESC 走 HouseUIManager 的叠加层返回，不在这里收。
    /// </summary>
    public sealed class CodexHotkeys : MonoBehaviour
    {
        private Action prev;
        private Action next;
        private Action view;

        /// <summary>挂载帧号：吞掉打开当帧的按键，避免上一层界面的输入泄漏。</summary>
        private int spawnFrame;

        public void Bind(Action onPrev, Action onNext, Action onView)
        {
            prev = onPrev;
            next = onNext;
            view = onView;
            spawnFrame = Time.frameCount;
        }

        private void Update()
        {
            if (Time.frameCount <= spawnFrame) return;
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.Q)) prev?.Invoke();
            // 中键 = 设计图上的「切换选项」，按一次往后翻一张
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.E) ||
                Input.GetMouseButtonDown(2)) next?.Invoke();
            if (Input.GetKeyDown(KeyCode.Space)) view?.Invoke();
            var scroll = Input.mouseScrollDelta.y;
            if (scroll > .01f) prev?.Invoke();
            else if (scroll < -.01f) next?.Invoke();
        }
    }
}

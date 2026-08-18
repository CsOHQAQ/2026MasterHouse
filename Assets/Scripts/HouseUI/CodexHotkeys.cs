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

        /// <summary>攒够一档才翻一张（各家鼠标一次拨动给的 delta 差别很大）。</summary>
        private const float ScrollStep = 1f;
        /// <summary>停手多久就把没攒满的零头忘掉。</summary>
        private const float ScrollForgetSeconds = .35f;
        private float scrollAccum;
        private float idleTimer;

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
            HandleScroll();
        }

        /// <summary>
        /// 滚轮翻页（2026-08-18 反馈「一格一格的有些僵硬」）：不再来一个 delta 就翻一张。
        /// 各家鼠标/触控板一次拨动给的 delta 差别很大（有的一格给 1、有的连给三次 0.1），
        /// 这里先累加、够一档才翻，翻完扣掉一档而不是清零——连续滚起来节奏才是匀的。
        /// 反向拨动立刻清账，免得攒着的正向量把回滚吃掉。
        /// </summary>
        private void HandleScroll()
        {
            var scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > .001f && Mathf.Sign(scroll) != Mathf.Sign(scrollAccum)) scrollAccum = 0f;
            scrollAccum += scroll;
            // 一段时间没动就把零头忘掉，避免上一次的余量攒到下一次里
            if (Mathf.Approximately(scroll, 0f))
            {
                idleTimer += Time.unscaledDeltaTime;
                if (idleTimer > ScrollForgetSeconds) scrollAccum = 0f;
                return;
            }
            idleTimer = 0f;
            while (scrollAccum >= ScrollStep) { scrollAccum -= ScrollStep; prev?.Invoke(); }
            while (scrollAccum <= -ScrollStep) { scrollAccum += ScrollStep; next?.Invoke(); }
        }
    }
}

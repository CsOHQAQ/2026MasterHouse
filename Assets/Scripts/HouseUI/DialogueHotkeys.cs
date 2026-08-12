using System;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 对话层键盘/滚轮输入（对白演出规格）：空格/回车推进对白或确认选项、滚轮切换选项。
    /// 由 DialogueOverlay 打开时挂到对话层根上，随层销毁；语义全部回调给 Overlay，本类不碰业务。
    /// 运行时挂组件属于动态表现件（§16.2 例外口径同 DialogueTypewriter）。
    /// </summary>
    public sealed class DialogueHotkeys : MonoBehaviour
    {
        private Action<int> cycle;
        private Action confirm;

        /// <summary>挂载帧号：吞掉打开当帧的按键，避免上一层界面的确认输入泄漏进来连击。</summary>
        private int spawnFrame;

        public void Bind(Action<int> onCycle, Action onConfirm)
        {
            cycle = onCycle;
            confirm = onConfirm;
            spawnFrame = Time.frameCount;
        }

        private void Update()
        {
            if (Time.frameCount <= spawnFrame) return;

            var scroll = Input.GetAxis("Mouse ScrollWheel");
            if (cycle != null && Mathf.Abs(scroll) > .01f)
                cycle(scroll < 0 ? 1 : -1); // 向下滚 = 往下一项

            if (confirm != null &&
                (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) ||
                 Input.GetKeyDown(KeyCode.KeypadEnter)))
                confirm();
        }
    }
}

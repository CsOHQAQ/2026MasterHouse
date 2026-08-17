using System;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 确认弹窗键位（2026-08-17）：空格 = 确认（ESC 取消由壳的叠加层弹栈天然处理，不在这里管）。
    /// 只在本弹窗处于栈顶时生效，避免与下层界面的空格语义打架。
    /// </summary>
    public sealed class ConfirmHotkeys : MonoBehaviour
    {
        private Button confirm;
        private Func<bool> isTop;

        public void Init(Button confirmButton, Func<bool> isTopOverlay)
        {
            confirm = confirmButton;
            isTop = isTopOverlay;
        }

        private void Update()
        {
            if (confirm == null || (isTop != null && !isTop())) return;
            if (Input.GetKeyDown(KeyCode.Space)) confirm.onClick.Invoke();
        }
    }
}

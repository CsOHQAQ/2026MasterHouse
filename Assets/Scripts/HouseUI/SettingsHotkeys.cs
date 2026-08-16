using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// Hub 设置叠加层的键位转发（2026-08-16）：叠加层打开时页面 HandleInput 被壳拦下，
    /// R 重置 / 回车应用由本组件每帧转发给 SettingsPageBinder（ESC 走壳的叠加层弹栈，不在这里管）。
    /// </summary>
    public sealed class SettingsHotkeys : MonoBehaviour
    {
        private SettingsPageBinder binder;

        public void Init(SettingsPageBinder target) => binder = target;

        private void Update() => binder?.HandleHotkeys();
    }
}

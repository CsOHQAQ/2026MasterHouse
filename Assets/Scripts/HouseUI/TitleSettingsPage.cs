namespace MasterHouse
{
    /// <summary>
    /// 标题设置页（2026-08-16 按新美术重做）：整页 Prefab（左分页 + 右内容行 + 底部键位栏），
    /// 内容绑定在 SettingsPageBinder（与 Hub 设置叠加层共用，§16.8）。
    /// 键位：ESC 返回（丢弃未应用改动）、R 重置修改、回车应用落盘。
    /// </summary>
    public sealed class TitleSettingsPage : HousePage
    {
        protected override string PrefabPath => OutGamePrefabResourcePaths.SettingsPage;

        private readonly SettingsPageBinder binder = new SettingsPageBinder();

        protected override void OnEnter()
        {
            var view = Root != null ? Root.GetComponent<OutGameSettingsPageView>() : null;
            if (view == null)
            {
                UnityEngine.Debug.LogError("[HouseUI] 设置页 Prefab 缺少视图组件：OutGameSettingsPageView");
                return;
            }
            binder.Bind(view, UI, Back);
        }

        public override void HandleInput() => binder.HandleHotkeys();

        public override bool OnEscape()
        {
            Back();
            return true;
        }

        /// <summary>返回标题页（ESC 或点底部「返回」）：丢弃未应用的改动。</summary>
        private void Back()
        {
            binder.DiscardUnsaved();
            UI.ShowPage(new TitlePage());
        }
    }
}

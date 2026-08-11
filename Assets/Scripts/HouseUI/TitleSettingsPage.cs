namespace MasterHouse
{
    /// <summary>
    /// 标题设置页：游戏性开关落 HouseSettings 全局配置文件（§16.5，改动即写盘）。
    /// 存档相关入口在存档移除期间占位（§16.5 明示豁免）。内容绑定与 Hub 设置叠加层共用（§16.8）。
    /// </summary>
    public sealed class TitleSettingsPage : PaperPage<OutGameSettingsPageView>
    {
        protected override string PrefabPath => OutGamePrefabResourcePaths.SettingsPage;

        protected override void OnBind()
        {
            BindContent(View, UI);
            // 标题页语境：读取按钮进存档占位页（Hub 叠加层语境维持 BindContent 的占位提示）
            View.loadButton.onClick.RemoveAllListeners();
            View.loadButton.onClick.AddListener(() => UI.ShowPage(new SavePlaceholderPage()));
        }

        /// <summary>设置内容绑定（标题设置页与 Hub 设置叠加层共用，§16.8 复用同一 Prefab）。</summary>
        internal static void BindContent(OutGameSettingsPageView view, HouseUIManager ui)
        {
            view.dataSummary.text = "界面切换       沉浸式\n\n存档系统       重构中（待定 #9）";
            view.saveButton.onClick.RemoveAllListeners();
            view.saveButton.onClick.AddListener(() => ui.ShowToast("存档功能重构中：统一存档定案后回归（待定 #9）"));
            view.loadButton.onClick.RemoveAllListeners();
            view.loadButton.onClick.AddListener(() => ui.ShowToast("存档功能重构中：统一存档定案后回归（待定 #9）"));

            var settings = HouseSettings.Data;
            HouseUIUtil.BindToggle(view.autoDialogueToggle, settings.autoDialogue, value =>
            {
                settings.autoDialogue = value;
                HouseSettings.Save();
            });
            HouseUIUtil.BindToggle(view.hintToggle, settings.showInteractionHints, value =>
            {
                settings.showInteractionHints = value;
                HouseSettings.Save();
            });
            HouseUIUtil.BindToggle(view.cameraShakeToggle, settings.cameraShake, value =>
            {
                settings.cameraShake = value;
                HouseSettings.Save();
            });
        }
    }
}

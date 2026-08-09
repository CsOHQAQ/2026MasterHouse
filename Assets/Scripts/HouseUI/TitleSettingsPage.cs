namespace MasterHouse
{
    /// <summary>
    /// 标题设置页：游戏性开关落 HouseSettings 全局配置文件（§16.5，改动即写盘）。
    /// 存档相关入口在存档移除期间占位（§16.5 明示豁免）：保存按钮出提示，读取按钮进占位页。
    /// </summary>
    public sealed class TitleSettingsPage : PaperPage<OutGameSettingsPageView>
    {
        protected override string PrefabPath => OutGamePrefabResourcePaths.SettingsPage;

        protected override void OnBind()
        {
            View.dataSummary.text = "界面切换       沉浸式\n\n存档系统       重构中（待定 #9）";
            View.saveButton.onClick.RemoveAllListeners();
            View.saveButton.onClick.AddListener(() => UI.ShowToast("存档功能重构中：统一存档定案后回归（待定 #9）"));
            View.loadButton.onClick.RemoveAllListeners();
            View.loadButton.onClick.AddListener(() => UI.ShowPage(new SavePlaceholderPage()));

            var settings = HouseSettings.Data;
            HouseUIUtil.BindToggle(View.autoDialogueToggle, settings.autoDialogue, value =>
            {
                settings.autoDialogue = value;
                HouseSettings.Save();
            });
            HouseUIUtil.BindToggle(View.hintToggle, settings.showInteractionHints, value =>
            {
                settings.showInteractionHints = value;
                HouseSettings.Save();
            });
            HouseUIUtil.BindToggle(View.cameraShakeToggle, settings.cameraShake, value =>
            {
                settings.cameraShake = value;
                HouseSettings.Save();
            });
        }
    }
}

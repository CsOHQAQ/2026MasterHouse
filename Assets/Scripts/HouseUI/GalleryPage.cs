using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 画廊页：游戏日志 / 成就两个 tab，两套布局都保存在 Prefab 内，这里只切换显隐与 tab 状态。
    /// tab 选择跨次进入保留（与旧壳一致，会话级）。
    /// </summary>
    public sealed class GalleryPage : PaperPage<OutGameGalleryPageView>
    {
        protected override string PrefabPath => OutGamePrefabResourcePaths.GalleryPage;

        private static bool showAchievements;

        protected override void OnBind()
        {
            View.logTab.onClick.RemoveAllListeners();
            View.logTab.onClick.AddListener(() => { showAchievements = false; Refresh(); });
            View.achievementTab.onClick.RemoveAllListeners();
            View.achievementTab.onClick.AddListener(() => { showAchievements = true; Refresh(); });
            Refresh();
        }

        private void Refresh()
        {
            View.logRoot.gameObject.SetActive(!showAchievements);
            View.achievementRoot.gameObject.SetActive(showAchievements);
            SetTabState(View.logTab, !showAchievements);
            SetTabState(View.achievementTab, showAchievements);
        }

        private static void SetTabState(Button button, bool active)
        {
            if (button == null || button.targetGraphic == null) return;
            button.targetGraphic.color = active ? HouseUIUtil.Wine : new UnityEngine.Color(1, 1, 1, .12f);
            var label = button.GetComponentInChildren<Text>();
            if (label != null) label.color = active ? HouseUIUtil.White : HouseUIUtil.Wine;
        }
    }
}

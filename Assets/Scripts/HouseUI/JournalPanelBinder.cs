using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>日记面板绑定：日记/成就两 tab；文章与成就行用模板 Prefab 实例化（§16.2），内容读 CodexTable。</summary>
    public static class JournalPanelBinder
    {
        private static bool showAchievements;

        public static void Bind(OutGameJournalPanelView view, HubPage page)
        {
            if (view == null) return;
            for (var i = 0; i < 2; i++)
            {
                var toAchievements = i == 1;
                if (view.tabButtons != null && i < view.tabButtons.Length && view.tabButtons[i] != null)
                    HouseUIUtil.BindButton(view.tabButtons[i], () =>
                    {
                        showAchievements = toAchievements;
                        Refresh(view);
                    });
            }
            Refresh(view);
        }

        private static void Refresh(OutGameJournalPanelView view)
        {
            for (var i = 0; i < 2; i++)
            {
                if (view.tabBackgrounds != null && i < view.tabBackgrounds.Length && view.tabBackgrounds[i] != null)
                    view.tabBackgrounds[i].color = showAchievements == (i == 1) ? HouseUIUtil.Wine : new Color(1, 1, 1, .04f);
            }
            if (view.bodyRoot == null) return;
            for (var i = view.bodyRoot.childCount - 1; i >= 0; i--)
                Object.Destroy(view.bodyRoot.GetChild(i).gameObject);

            var codex = GameManager.Instance.CodexTable;
            if (!showAchievements)
            {
                var template = Resources.Load<GameObject>(OutGamePrefabResourcePaths.JournalArticle);
                if (template == null)
                {
                    Debug.LogError("[HouseUI] 日记文章模板 Prefab 缺失（§16.2）：" + OutGamePrefabResourcePaths.JournalArticle);
                    return;
                }
                for (var i = 0; i < codex.journalEntries.Count; i++)
                {
                    var entry = codex.journalEntries[i];
                    var instance = Object.Instantiate(template, view.bodyRoot, false);
                    instance.name = "Article" + i;
                    var article = instance.GetComponent<JournalArticleView>();
                    if (article == null) continue;
                    var rect = (RectTransform)instance.transform;
                    rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
                    rect.anchoredPosition = new Vector2(i % 2 == 0 ? -280 : 300, 90 - i / 2 * 560);
                    if (article.text != null)
                        article.text.text = $"<color=#E22D76><size=14>{entry.dateText}</size></color>\n<size=29>{entry.title}</size>\n\n{entry.body}";
                }
            }
            else
            {
                var template = Resources.Load<GameObject>(OutGamePrefabResourcePaths.AchievementRow);
                if (template == null)
                {
                    Debug.LogError("[HouseUI] 成就行模板 Prefab 缺失（§16.2）：" + OutGamePrefabResourcePaths.AchievementRow);
                    return;
                }
                for (var i = 0; i < codex.achievements.Count; i++)
                {
                    var achievement = codex.achievements[i];
                    var done = i < 2; // 原型假状态：完成态是运行时数据，成就系统未实现前保持「前两项 ✓」
                    var instance = Object.Instantiate(template, view.bodyRoot, false);
                    instance.name = "Achievement" + i;
                    var row = instance.GetComponent<AchievementRowView>();
                    if (row == null) continue;
                    var rect = (RectTransform)instance.transform;
                    rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
                    rect.anchoredPosition = new Vector2(i % 2 == 0 ? -280 : 300, 150 - i / 2 * 210);
                    if (row.label != null)
                        row.label.text = $"{(done ? "✓" : (i + 1).ToString())}     {achievement.displayName}\n<size=15>          {achievement.note}</size>";
                    if (row.background != null)
                        row.background.color = done ? new Color(.4f, .08f, .25f, .6f) : new Color(1, 1, 1, .035f);
                }
            }
            HouseUIUtil.ApplyFallbackFont(view.bodyRoot);
        }
    }
}

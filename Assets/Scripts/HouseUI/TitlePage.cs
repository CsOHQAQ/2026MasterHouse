using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 标题页：主菜单绑定与键盘导航（↑↓/Enter）。
    /// 存档功能重写期间移除（§16.5 明示豁免）：「继续游戏」禁用占位，「读取存档」进占位页；
    /// 「新游戏」直接开始新进度（无槽位概念，待定 #9 统一存档定案后回归）。
    /// </summary>
    public sealed class TitlePage : HousePage
    {
        protected override string PrefabPath => OutGamePrefabResourcePaths.Title;

        private Button[] menuButtons;
        private int menuIndex;

        private readonly struct MenuItem
        {
            public readonly string Cn;
            public readonly string En;
            public readonly Action Action;
            public readonly bool Enabled;

            public MenuItem(string cn, string en, Action action, bool enabled)
            {
                Cn = cn;
                En = en;
                Action = action;
                Enabled = enabled;
            }
        }

        protected override void OnEnter()
        {
            var view = Root != null ? Root.GetComponent<OutGameTitleView>() : null;
            if (view == null)
            {
                Debug.LogError("[HouseUI] 标题页 Prefab 缺少视图组件：OutGameTitleView");
                return;
            }

            EnsureTitleTextures();
            if (view.cover != null)
            {
                if (view.cover.texture == null) view.cover.texture = Resources.Load<Texture2D>("OutGameUI/og-meros");
                ConfigureCover(view.cover);
                view.cover.color = new Color(1, 1, 1, 0);
                view.cover.rectTransform.localScale = Vector3.one * 1.035f;
                view.cover.DOFade(1, 1.1f).SetEase(Ease.OutCubic).SetUpdate(true);
                view.cover.rectTransform.DOScale(Vector3.one, 1.1f).SetEase(Ease.OutCubic).SetUpdate(true);
            }
            if (view.horizontalVignette != null) view.horizontalVignette.texture = titleHorizontalVignette;
            if (view.verticalVignette != null) view.verticalVignette.texture = titleVerticalVignette;
            if (view.menuGradient != null) view.menuGradient.texture = titleMenuGradient;
            if (view.topRule != null) view.topRule.texture = titleRuleGradient;
            if (view.bottomRule != null) view.bottomRule.texture = titleRuleGradient;

            if (view.saveState != null)
                view.saveState.text = "等待第一位住客";

            var items = new[]
            {
                // §16.5：存档移除期间「继续游戏」禁用占位（待定 #9 统一存档接入后回归）
                new MenuItem("继续游戏", "存档系统重构中", null, false),
                new MenuItem("新游戏", "NEW STORY", StartNewGame, true),
                new MenuItem("读取存档", "LOAD GAME", () => UI.ShowPage(new SavePlaceholderPage()), true),
                new MenuItem("画廊", "LOG & ACHIEVEMENT", () => UI.ShowPage(new GalleryPage()), true),
                new MenuItem("设置", "OPTIONS", () => UI.ShowPage(new TitleSettingsPage()), true),
                new MenuItem("退出游戏", "QUIT", () => UI.ShowPage(new ExitPage()), true),
            };

            menuButtons = view.menuButtons;
            for (var i = 0; i < items.Length && i < menuButtons.Length; i++)
            {
                var item = items[i];
                var button = menuButtons[i];
                button.onClick.RemoveAllListeners();
                if (item.Action != null) button.onClick.AddListener(() => item.Action());
                button.interactable = item.Enabled;
                if (i < view.menuMainLabels.Length && view.menuMainLabels[i] != null)
                {
                    view.menuMainLabels[i].text = item.Cn;
                    view.menuMainLabels[i].color = i == 1 ? HouseUIUtil.Hex("F0A080") : HouseUIUtil.Hex("DBC9BD");
                    HouseUIUtil.EnsureLetterSpacing(view.menuMainLabels[i], 3.2f);
                }
                if (i < view.menuSubtitles.Length && view.menuSubtitles[i] != null)
                {
                    view.menuSubtitles[i].text = item.En;
                    HouseUIUtil.EnsureLetterSpacing(view.menuSubtitles[i], 1.5f);
                }
                if (i < view.menuHoverImages.Length && view.menuHoverImages[i] != null)
                {
                    view.menuHoverImages[i].texture = titleHoverGradient;
                    // Prefab 中的 hover 图可能保存为可见状态，绑定时强制归零，默认不显示
                    var hoverColor = view.menuHoverImages[i].color;
                    view.menuHoverImages[i].color = new Color(hoverColor.r, hoverColor.g, hoverColor.b, 0f);
                }
                var feedback = button.GetComponent<OutGameTweenButton>();
                if (feedback == null) feedback = button.gameObject.AddComponent<OutGameTweenButton>();
                feedback.hoverScale = 1.055f;
                if (i < view.menuHoverImages.Length) feedback.hoverGraphic = view.menuHoverImages[i];
                var group = HouseUIUtil.Group(button.gameObject, 0);
                var targetAlpha = item.Enabled ? 1f : .34f;
                // 错峰淡入以 CanvasGroup 为目标：不能挂在 button.transform 上——
                // OutGameTweenButton 的 hover/选中逻辑会按 transform 目标 DOKill，会误杀进场动画（不可见但可点击的 bug）
                group.DOFade(targetAlpha, .42f).SetEase(Ease.OutCubic).SetUpdate(true)
                    .SetDelay(.08f + i * .055f);
            }
            HouseUIUtil.EnsureLetterSpacing(view.saveState, .65f);
            HouseUIUtil.EnsureLetterSpacing(view.hints, .8f);
            HouseUIUtil.ApplyFallbackFont(Root);
            // 默认不选中任何菜单项：橙色 hover 渐变只在鼠标悬停或键盘导航后出现（无存档概念，默认落在「新游戏」）
            menuIndex = 1;
        }

        public override void HandleInput()
        {
            if (menuButtons == null || menuButtons.Length == 0) return;
            if (Input.GetKeyDown(KeyCode.UpArrow)) MoveSelection(-1);
            if (Input.GetKeyDown(KeyCode.DownArrow)) MoveSelection(1);
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                var selected = menuButtons[menuIndex];
                if (selected != null && selected.interactable) selected.onClick.Invoke();
            }
        }

        private void MoveSelection(int direction)
        {
            for (var step = 0; step < menuButtons.Length; step++)
            {
                menuIndex = (menuIndex + direction + menuButtons.Length) % menuButtons.Length;
                var candidate = menuButtons[menuIndex];
                if (candidate == null || !candidate.interactable) continue;
                candidate.Select();
                return;
            }
        }

        private void StartNewGame()
        {
            // §16.5：存档移除期间无槽位概念，新游戏直接重置局外进度进屋（统一存档接入后回归槽位选择，待定 #9）
            var gm = GameManager.Instance;
            gm.VisitorManager.ResetNew();
            gm.EconomyManager.ResetToDefaults();
            gm.HouseClockManager.ResetNew();
            FurnitureRoomController.ResetSession();
            FurnitureSceneComposer.ClearBaked();
            UI.ShowPage(new OpeningPage());
        }

        // ── 标题页程序化贴图（复刻网页版渐变/晕影；随本页使用，非布局兜底）──

        private static Texture2D titleHorizontalVignette;
        private static Texture2D titleVerticalVignette;
        private static Texture2D titleMenuGradient;
        private static Texture2D titleRuleGradient;
        private static Texture2D titleHoverGradient;

        /// <summary>复刻网页 title-cover 的 object-fit:cover，避免非 16:9 Game View 拉伸背景。</summary>
        private static void ConfigureCover(RawImage image)
        {
            if (image == null || image.texture == null) return;
            var fitter = image.GetComponent<AspectRatioFitter>();
            if (fitter == null) fitter = image.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = (float)image.texture.width / image.texture.height;
        }

        private static void EnsureTitleTextures()
        {
            if (titleHorizontalVignette != null && titleVerticalVignette != null &&
                titleMenuGradient != null && titleRuleGradient != null && titleHoverGradient != null) return;

            titleHorizontalVignette = NewTitleTexture("TitleHorizontalVignette", 512, 2);
            for (var x = 0; x < titleHorizontalVignette.width; x++)
            {
                var t = x / (titleHorizontalVignette.width - 1f);
                var alpha = t <= .43f
                    ? Mathf.Lerp(.72f, .18f, t / .43f)
                    : t <= .67f ? Mathf.Lerp(.18f, 0, (t - .43f) / .24f) : 0;
                SetColumn(titleHorizontalVignette, x, new Color(2f / 255, 5f / 255, 10f / 255, alpha));
            }
            titleHorizontalVignette.Apply(false, true);

            titleVerticalVignette = NewTitleTexture("TitleVerticalVignette", 2, 512);
            for (var y = 0; y < titleVerticalVignette.height; y++)
            {
                var t = y / (titleVerticalVignette.height - 1f);
                var alpha = t <= .37f
                    ? Mathf.Lerp(.74f, 0, t / .37f)
                    : Mathf.Lerp(0, .12f, (t - .37f) / .63f);
                SetRow(titleVerticalVignette, y, new Color(1f / 255, 3f / 255, 7f / 255, alpha));
            }
            titleVerticalVignette.Apply(false, true);

            titleMenuGradient = NewTitleTexture("TitleMenuGradient", 512, 2);
            for (var x = 0; x < titleMenuGradient.width; x++)
            {
                var t = x / (titleMenuGradient.width - 1f);
                float alpha;
                if (t <= .17f) alpha = Mathf.Lerp(0, .82f, t / .17f);
                else if (t <= .5f) alpha = Mathf.Lerp(.82f, .9f, (t - .17f) / .33f);
                else if (t <= .83f) alpha = Mathf.Lerp(.9f, .82f, (t - .5f) / .33f);
                else alpha = Mathf.Lerp(.82f, 0, (t - .83f) / .17f);
                SetColumn(titleMenuGradient, x, new Color(3f / 255, 6f / 255, 11f / 255, alpha));
            }
            titleMenuGradient.Apply(false, true);

            titleRuleGradient = NewTitleTexture("TitleRuleGradient", 256, 2);
            for (var x = 0; x < titleRuleGradient.width; x++)
            {
                var t = x / (titleRuleGradient.width - 1f);
                var alpha = Mathf.Clamp01(1 - Mathf.Abs(t - .5f) * 2) * .72f;
                SetColumn(titleRuleGradient, x, new Color(233f / 255, 137f / 255, 104f / 255, alpha));
            }
            titleRuleGradient.Apply(false, true);

            titleHoverGradient = NewTitleTexture("TitleHoverGradient", 256, 64);
            for (var y = 0; y < titleHoverGradient.height; y++)
            for (var x = 0; x < titleHoverGradient.width; x++)
            {
                var nx = (x / (titleHoverGradient.width - 1f) - .5f) * 2;
                var ny = (y / (titleHoverGradient.height - 1f) - .5f) * 2;
                var radius = Mathf.Sqrt(nx * nx + ny * ny);
                var alpha = .44f * Mathf.Clamp01(1 - radius / .68f);
                titleHoverGradient.SetPixel(x, y, new Color(150f / 255, 53f / 255, 52f / 255, alpha));
            }
            titleHoverGradient.Apply(false, true);
        }

        private static Texture2D NewTitleTexture(string name, int width, int height)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        private static void SetColumn(Texture2D texture, int x, Color color)
        {
            for (var y = 0; y < texture.height; y++) texture.SetPixel(x, y, color);
        }

        private static void SetRow(Texture2D texture, int y, Color color)
        {
            for (var x = 0; x < texture.width; x++) texture.SetPixel(x, y, color);
        }
    }
}

using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// HouseUI 模块的小工具集：字体/颜色/字距/CanvasGroup/Tween 清理。
    /// 只服务「Prefab 绑定」，不承担任何界面搭建职责（§16.2：Prefab 是布局唯一真相源）。
    /// </summary>
    public static class HouseUIUtil
    {
        public static readonly Color Wine = Hex("6E243E");
        public static readonly Color White = Hex("F3E8DD");

        private static Font font;

        /// <summary>
        /// 项目 UI 字体：思源黑体（美术定稿，源文件在 Assets/PC ui/Fonts，Resources 下放运行时加载副本）。
        /// 资产缺失时回退旧的系统字体链，保证文字始终可显示。
        /// </summary>
        public static Font Font
        {
            get
            {
                if (font != null) return font;
                font = Resources.Load<Font>("Fonts/SourceHanSansOLD-Normal-2");
                if (font == null)
                {
                    Debug.LogWarning("[HouseUI] 项目字体缺失（Resources/Fonts/SourceHanSansOLD-Normal-2），回退系统字体");
                    string[] preferred = { "Georgia", "Times New Roman", "STKaiti", "KaiTi", "Microsoft YaHei", "SimHei" };
                    font = Font.CreateDynamicFontFromOSFont(preferred, 32);
                }
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        /// <summary>
        /// 全局面板底图（PC ui/common/Secondary-bg，黑底粉边，9 宫格切片）：
        /// 各类弹窗/面板/卡片统一经此换肤；素材缺失时保持原有底色不变。
        /// alpha 控制整体透明度；borderScale 越大边框越细（小按钮传 2~3，避免切片边框比按钮还厚）。
        /// </summary>
        public static void ApplyPanelSkin(Image panel, float alpha = 1f, float borderScale = 1f)
        {
            if (panel == null) return;
            var skin = Resources.Load<Sprite>("OutGameUI/common/Secondary-bg");
            if (skin == null) return;
            panel.sprite = skin;
            panel.color = new Color(1f, 1f, 1f, alpha);
            panel.type = Image.Type.Sliced;
            panel.pixelsPerUnitMultiplier = Mathf.Max(.01f, borderScale);
        }

        public static Color Hex(string value, float alpha = 1f)
        {
            if (!value.StartsWith("#")) value = "#" + value;
            ColorUtility.TryParseHtmlString(value, out var color);
            color.a = alpha;
            return color;
        }

        /// <summary>Prefab 里未指定字体（或落在内置默认字体）的文本统一替换为项目字体。</summary>
        public static void ApplyFallbackFont(Transform root)
        {
            if (root == null) return;
            var legacyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            foreach (var label in root.GetComponentsInChildren<Text>(true))
            {
                if (label.font == null || label.font == legacyFont || label.font.name == "Arial" ||
                    label.font.name == "LegacyRuntime")
                    label.font = Font;
            }
        }

        public static void EnsureLetterSpacing(Text label, float spacing)
        {
            if (label == null) return;
            var effect = label.GetComponent<OutGameLetterSpacing>();
            if (effect == null) effect = label.gameObject.AddComponent<OutGameLetterSpacing>();
            effect.spacing = spacing;
            label.SetVerticesDirty();
        }

        public static CanvasGroup Group(GameObject go, float alpha = 1f)
        {
            var group = go.GetComponent<CanvasGroup>();
            if (group == null) group = go.AddComponent<CanvasGroup>();
            group.alpha = alpha;
            return group;
        }

        /// <summary>Prefab 按钮统一绑定：清旧监听、挂新回调、补 hover 手感组件（兼点击音，音效需求 #1）。
        /// clickSfx 传 None 表示这颗按钮的声音由回调内更具体的动作音承担（如访客卡、对话推进）。</summary>
        public static void BindButton(Button button, UnityEngine.Events.UnityAction action, ESfx clickSfx = ESfx.UiClick)
        {
            if (button == null) return;
            var feedback = button.GetComponent<OutGameTweenButton>();
            if (feedback == null) feedback = button.gameObject.AddComponent<OutGameTweenButton>();
            feedback.hoverScale = 1.025f;
            feedback.clickSfx = clickSfx;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        public static void BindToggle(Toggle toggle, bool value, UnityEngine.Events.UnityAction<bool> action)
        {
            if (toggle == null) return;
            toggle.onValueChanged.RemoveAllListeners();
            toggle.isOn = value; // 先设值再挂监听：初始化赋值不触发回调、也不该响点击音
            var feedback = toggle.GetComponent<OutGameTweenButton>();
            toggle.onValueChanged.AddListener(v =>
            {
                // Toggle 没有走 BindButton 的手感组件；没挂 OutGameTweenButton 时由这里补点击音，挂了则由组件发声不重复
                if (feedback == null) SfxManager.Play(ESfx.UiClick);
                action(v);
            });
        }

        /// <summary>销毁页面/叠加层前停掉其层级下所有 Tween，防止 DOTween 在本帧末尾写入失效对象。</summary>
        public static void KillTweensUnder(Transform root)
        {
            if (root == null) return;
            foreach (var target in root.GetComponentsInChildren<Transform>(true))
                DOTween.Kill(target);
            foreach (var target in root.GetComponentsInChildren<CanvasGroup>(true))
                DOTween.Kill(target);
            foreach (var target in root.GetComponentsInChildren<Graphic>(true))
                DOTween.Kill(target);
        }
    }
}

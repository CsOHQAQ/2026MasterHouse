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

        /// <summary>与旧局外 UI 同源的字体选择：拉丁字符优先 Georgia，中文回退楷体。</summary>
        public static Font Font
        {
            get
            {
                if (font != null) return font;
                string[] preferred = { "Georgia", "Times New Roman", "STKaiti", "KaiTi", "Microsoft YaHei", "SimHei" };
                font = Font.CreateDynamicFontFromOSFont(preferred, 32);
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
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

        public static void BindToggle(Toggle toggle, bool value, UnityEngine.Events.UnityAction<bool> action)
        {
            if (toggle == null) return;
            toggle.onValueChanged.RemoveAllListeners();
            toggle.isOn = value;
            toggle.onValueChanged.AddListener(action);
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

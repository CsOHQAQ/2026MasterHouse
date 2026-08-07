using System;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>局外 UI 的运行时控件工厂。所有尺寸都以 1920×1080 为设计基准。</summary>
    public static class OutGameUIFactory
    {
        public static readonly Color Ink = Hex("17131B");
        public static readonly Color Paper = Hex("D7C9B8");
        public static readonly Color PaperLight = Hex("F1E5D1");
        public static readonly Color Wine = Hex("6E243E");
        public static readonly Color Rose = Hex("E22D76");
        public static readonly Color RoseSoft = Hex("B23B68");
        public static readonly Color Cyan = Hex("74D8D1");
        public static readonly Color Gold = Hex("D4A46B");
        public static readonly Color White = Hex("F3E8DD");

        private static Font font;
        private static Sprite whiteSprite;

        public static Font Font
        {
            get
            {
                if (font != null) return font;
                // 对齐网页：拉丁字符优先 Georgia，中文回退到楷体。
                string[] preferred = { "Georgia", "Times New Roman", "STKaiti", "KaiTi", "Microsoft YaHei", "SimHei" };
                font = Font.CreateDynamicFontFromOSFont(preferred, 32);
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        public static Sprite WhiteSprite
        {
            get
            {
                if (whiteSprite != null) return whiteSprite;
                var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                texture.name = "OutGameUI_White";
                texture.SetPixel(0, 0, Color.white);
                texture.Apply();
                whiteSprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f), 100f);
                return whiteSprite;
            }
        }

        public static Color Hex(string value, float alpha = 1f)
        {
            if (!value.StartsWith("#")) value = "#" + value;
            ColorUtility.TryParseHtmlString(value, out var color);
            color.a = alpha;
            return color;
        }

        public static RectTransform Rect(Transform parent, string name, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(.5f, .5f);
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(Transform parent, string name)
        {
            return Rect(parent, name, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }

        public static Image Panel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 anchoredPosition, Vector2 size, Color color)
        {
            var rt = Rect(parent, name, anchorMin, anchorMax, anchoredPosition, size);
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = WhiteSprite;
            image.color = color;
            return image;
        }

        public static Image StretchPanel(Transform parent, string name, Color color)
        {
            var rt = Stretch(parent, name);
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = WhiteSprite;
            image.color = color;
            return image;
        }

        public static RawImage Texture(Transform parent, string name, string resourcePath,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size, Color? tint = null)
        {
            var rt = Rect(parent, name, anchorMin, anchorMax, anchoredPosition, size);
            var image = rt.gameObject.AddComponent<RawImage>();
            image.texture = Resources.Load<Texture2D>(resourcePath);
            image.color = tint ?? Color.white;
            return image;
        }

        public static RawImage StretchTexture(Transform parent, string name, string resourcePath, Color? tint = null)
        {
            var rt = Stretch(parent, name);
            var image = rt.gameObject.AddComponent<RawImage>();
            image.texture = Resources.Load<Texture2D>(resourcePath);
            image.color = tint ?? Color.white;
            return image;
        }

        public static Text Label(Transform parent, string name, string value, int size, Color color,
            TextAnchor alignment = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var rt = Stretch(parent, name);
            var text = rt.gameObject.AddComponent<Text>();
            text.font = Font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.supportRichText = true;
            text.raycastTarget = false;
            text.text = value;
            return text;
        }

        public static Text Label(Transform parent, string name, string value, int size, Color color,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 dimensions,
            TextAnchor alignment = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect(parent, name, anchorMin, anchorMax, anchoredPosition, dimensions);
            var text = rt.gameObject.AddComponent<Text>();
            text.font = Font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.supportRichText = true;
            text.raycastTarget = false;
            text.text = value;
            return text;
        }

        public static Button Button(Transform parent, string name, string caption, Action action,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size,
            Color background, Color foreground, int fontSize = 26, TextAnchor alignment = TextAnchor.MiddleCenter,
            bool feedback = true)
        {
            var image = Panel(parent, name, anchorMin, anchorMax, anchoredPosition, size, background);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            colors.pressedColor = new Color(.82f, .82f, .82f, 1f);
            colors.disabledColor = new Color(.42f, .42f, .42f, .6f);
            colors.fadeDuration = .08f;
            button.colors = colors;
            if (action != null) button.onClick.AddListener(() => action());
            var label = Label(image.transform, "Label", caption, fontSize, foreground, alignment, FontStyle.Bold);
            label.rectTransform.offsetMin = new Vector2(14, 8);
            label.rectTransform.offsetMax = new Vector2(-14, -8);
            if (feedback) image.gameObject.AddComponent<OutGameTweenButton>();
            return button;
        }

        public static CanvasGroup Group(GameObject go, float alpha = 1f)
        {
            // UnityEngine.Object 使用“伪 null”；不能用 ?? 判断缺失组件。
            var group = go.GetComponent<CanvasGroup>();
            if (group == null) group = go.AddComponent<CanvasGroup>();
            group.alpha = alpha;
            return group;
        }

        public static void Outline(GameObject go, Color color, Vector2 distance)
        {
            var outline = go.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = distance;
            outline.useGraphicAlpha = true;
        }

        public static void Shadow(GameObject go, Color color, Vector2 distance)
        {
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = color;
            shadow.effectDistance = distance;
            shadow.useGraphicAlpha = true;
        }
    }

}

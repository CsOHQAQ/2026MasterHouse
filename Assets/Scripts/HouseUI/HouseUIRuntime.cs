using System;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 动态表现件的最小运行时构建器：Toast/开门过场/房间切换门扇/家具热点/经济数值条/家具入口这类
    /// 非布局、非列表项的临时动态内容（§16.2 认可的运行时生成范围）。
    /// 【硬约束】禁止用于页面布局兜底或动态列表项——页面走 Prefab（缺失报错），列表项走模板 Prefab 实例化。
    /// 【豁免记录（§16.7，3.8）】原型期未 Prefab 化的家具 HUD 与 GM 调试面板亦由本类服务
    /// （双锚点重载即为此对齐旧工厂签名）；二轮家具 HUD Prefab 化时收紧回上述范围。
    /// </summary>
    public static class HouseUIRuntime
    {
        public static readonly Color Rose = HouseUIUtil.Hex("E22D76");
        public static readonly Color Cyan = HouseUIUtil.Hex("74D8D1");
        public static readonly Color White = HouseUIUtil.White;

        private static Sprite whiteSprite;

        public static Sprite WhiteSprite
        {
            get
            {
                if (whiteSprite != null) return whiteSprite;
                var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "HouseUI_White" };
                texture.SetPixel(0, 0, Color.white);
                texture.Apply();
                whiteSprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f), 100f);
                return whiteSprite;
            }
        }

        public static RectTransform Rect(Transform parent, string name, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            return rect;
        }

        public static RectTransform Stretch(Transform parent, string name)
        {
            var rect = Rect(parent, name, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        public static Image Panel(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, Color color)
        {
            var rect = Rect(parent, name, anchor, anchor, position, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = WhiteSprite;
            image.type = Image.Type.Sliced;
            image.color = color;
            return image;
        }

        public static Image StretchPanel(Transform parent, string name, Color color)
        {
            var rect = Stretch(parent, name);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = WhiteSprite;
            image.color = color;
            return image;
        }

        public static Text Label(Transform parent, string name, string content, int fontSize, Color color,
            Vector2 anchor, Vector2 position, Vector2 size,
            TextAnchor alignment = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal)
        {
            var rect = Rect(parent, name, anchor, anchor, position, size);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = HouseUIUtil.Font;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = color;
            text.text = content;
            text.raycastTarget = false;
            return text;
        }

        public static Text StretchLabel(Transform parent, string name, string content, int fontSize, Color color,
            TextAnchor alignment = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal)
        {
            var rect = Stretch(parent, name);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = HouseUIUtil.Font;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = color;
            text.text = content;
            text.raycastTarget = false;
            return text;
        }

        public static Button Button(Transform parent, string name, string caption,
            UnityEngine.Events.UnityAction action, Vector2 anchor, Vector2 position, Vector2 size,
            Color background, Color foreground, int fontSize, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var image = Panel(parent, name, anchor, position, size, background);
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            if (action != null) button.onClick.AddListener(action);
            var feedback = image.gameObject.AddComponent<OutGameTweenButton>();
            feedback.hoverScale = 1.025f;
            if (!string.IsNullOrEmpty(caption))
                StretchLabel(image.transform, "Label", caption, fontSize, foreground, alignment, FontStyle.Bold);
            return button;
        }

        public static RawImage StretchTexture(Transform parent, string name, string resourcePath, Color? tint = null)
        {
            var rect = Stretch(parent, name);
            var image = rect.gameObject.AddComponent<RawImage>();
            if (!string.IsNullOrEmpty(resourcePath)) image.texture = Resources.Load<Texture2D>(resourcePath);
            image.color = tint ?? Color.white;
            return image;
        }

        // ── 以下为对齐旧 OutGameUIFactory 签名的重载（家具 HUD/GM 面板豁免使用，行为逐参数复刻）──

        public static Image Panel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 position, Vector2 size, Color color)
        {
            var rect = Rect(parent, name, anchorMin, anchorMax, position, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = WhiteSprite;
            image.type = Image.Type.Sliced;
            image.color = color;
            return image;
        }

        /// <summary>拉伸满父级的文本（默认 MiddleLeft，与旧工厂一致）。</summary>
        public static Text Label(Transform parent, string name, string value, int size, Color color,
            TextAnchor alignment = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            return TextOn(Stretch(parent, name), value, size, color, alignment, style);
        }

        public static Text Label(Transform parent, string name, string value, int size, Color color,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 dimensions,
            TextAnchor alignment = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            return TextOn(Rect(parent, name, anchorMin, anchorMax, anchoredPosition, dimensions),
                value, size, color, alignment, style);
        }

        /// <summary>旧工厂全语义按钮：tint 状态色 + 标签内边距 + 可关闭 hover 反馈。</summary>
        public static Button Button(Transform parent, string name, string caption, Action action,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size,
            Color background, Color foreground, int fontSize = 26, TextAnchor alignment = TextAnchor.MiddleCenter,
            bool feedback = true)
        {
            var image = Panel(parent, name, anchorMin, anchorMax, anchoredPosition, size, background);
            image.raycastTarget = true;
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

        public static void Outline(GameObject go, Color color, Vector2 distance)
        {
            var outline = go.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = distance;
            outline.useGraphicAlpha = true;
        }

        public static CanvasGroup Group(GameObject go, float alpha = 1f) => HouseUIUtil.Group(go, alpha);

        private static Text TextOn(RectTransform rect, string value, int size, Color color,
            TextAnchor alignment, FontStyle style)
        {
            var text = rect.gameObject.AddComponent<Text>();
            text.font = HouseUIUtil.Font;
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
    }
}

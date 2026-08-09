using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 动态表现件的最小运行时构建器：Toast/开门过场/房间切换门扇/家具热点/经济数值条/家具入口这类
    /// 非布局、非列表项的临时动态内容（§16.2 认可的运行时生成范围）。
    /// 【硬约束】禁止用于页面布局兜底或动态列表项——页面走 Prefab（缺失报错），列表项走模板 Prefab 实例化。
    /// </summary>
    public static class HouseUIRuntime
    {
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
    }
}

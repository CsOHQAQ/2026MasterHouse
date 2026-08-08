using UnityEngine;

namespace MasterHouse
{
    /// <summary>渲染排序统一定义。</summary>
    public static class SortingOrders
    {
        public const int Grid = 0;
        public const int Link = 5;
        public const int Pulse = 6;
        public const int Card = 10;
        public const int CardDecor = 12;
        public const int Pin = 13;
        public const int Text = 15;
        public const int DragLine = 20;
    }

    /// <summary>运行时生成并缓存的共享视觉资源（无需任何美术素材）。</summary>
    public static class VisualAssets
    {
        private static Sprite whiteSprite;
        /// <summary>1x1 白色方块 Sprite，配合缩放与着色画出所有矩形。</summary>
        public static Sprite WhiteSprite
        {
            get
            {
                if (whiteSprite == null)
                    whiteSprite = Sprite.Create(
                        Texture2D.whiteTexture, new Rect(0, 0, 1, 1),
                        new Vector2(0.5f, 0.5f), pixelsPerUnit: 1f);
                return whiteSprite;
            }
        }

        private static Material unlitMaterial;
        /// <summary>无光照 Sprite 材质，保证在 URP 2D Renderer 下没有灯光也能正常显示。</summary>
        public static Material UnlitMaterial
        {
            get
            {
                if (unlitMaterial == null)
                    unlitMaterial = new Material(Shader.Find("Sprites/Default"));
                return unlitMaterial;
            }
        }

        private static Font defaultFont;
        /// <summary>内置动态字体，可直接渲染中文。</summary>
        public static Font DefaultFont
        {
            get
            {
                if (defaultFont == null)
                    defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return defaultFont;
            }
        }

        /// <summary>创建一个方形色块（白 Sprite + 缩放 + 着色），占位视觉的通用积木。</summary>
        public static SpriteRenderer CreateSpriteSquare(Transform parent, string name,
            Vector3 localPos, float worldSize, Color color, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = new Vector3(worldSize, worldSize, 1f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = WhiteSprite;
            sr.sharedMaterial = UnlitMaterial;
            sr.color = color;
            sr.sortingOrder = sortingOrder;
            return sr;
        }

        /// <summary>创建一个世界空间文字。worldSize 为单行文字的近似世界高度。</summary>
        public static TextMesh CreateWorldText(Transform parent, string name, Vector3 localPos,
            string text, float worldSize, TextAnchor anchor, Color color, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;

            var tm = go.AddComponent<TextMesh>();
            tm.font = DefaultFont;
            tm.fontSize = 64;
            tm.characterSize = worldSize * 10f / 64f;
            tm.anchor = anchor;
            tm.alignment = ToAlignment(anchor);
            tm.color = color;
            tm.text = text;

            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = tm.font.material;
            mr.sortingOrder = sortingOrder;
            return tm;
        }

        private static TextAlignment ToAlignment(TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.UpperLeft:
                case TextAnchor.MiddleLeft:
                case TextAnchor.LowerLeft:
                    return TextAlignment.Left;
                case TextAnchor.UpperRight:
                case TextAnchor.MiddleRight:
                case TextAnchor.LowerRight:
                    return TextAlignment.Right;
                default:
                    return TextAlignment.Center;
            }
        }
    }
}

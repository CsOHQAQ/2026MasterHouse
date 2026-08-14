using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 标题页「相片火烧」转场（2026-08-14）：帧末截下整屏快照盖在最上层，
    /// 下面先把主背景页就位，然后快照从点击处起火向外烧穿（UIBurn shader），露出新页面；烧完自毁。
    /// 纯表现件（快照 + 材质推进度），运行时生成，不涉及布局。
    /// </summary>
    public static class TitleBurnFx
    {
        public static void Play(HouseUIManager ui, Vector2 originScreen, System.Action showNext)
        {
            ui.StartCoroutine(Run(ui, originScreen, showNext));
        }

        private static IEnumerator Run(HouseUIManager ui, Vector2 originScreen, System.Action showNext)
        {
            yield return new WaitForEndOfFrame(); // 截屏必须在帧末
            var shader = Resources.Load<Shader>("Shaders/UIBurn");
            if (shader == null)
            {
                Debug.LogError("[HouseUI] 火烧转场 shader 缺失（Resources/Shaders/UIBurn），退化为直接切页");
                showNext();
                yield break;
            }
            // 帧末直读后备缓冲截整屏（等价 ScreenCapture，但不额外依赖 ScreenCaptureModule）
            var shot = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            shot.Apply(false, false);

            var go = new GameObject("TitleBurnLayer", typeof(RectTransform), typeof(RawImage));
            go.layer = 5;
            var rect = (RectTransform)go.transform;
            rect.SetParent(ui.PageRoot, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var raw = go.GetComponent<RawImage>();
            raw.texture = shot;
            raw.raycastTarget = true; // 烧完之前挡住下层输入

            var material = new Material(shader);
            material.SetTexture("_NoiseTex", NoiseTexture());
            material.SetVector("_Origin", new Vector4(
                Mathf.Clamp01(originScreen.x / Mathf.Max(Screen.width, 1)),
                Mathf.Clamp01(originScreen.y / Mathf.Max(Screen.height, 1)), 0, 0));
            material.SetFloat("_Aspect", Screen.width / (float)Mathf.Max(Screen.height, 1));
            material.SetFloat("_Progress", 0);
            raw.material = material;

            showNext();               // 主背景页在快照底下就位
            rect.SetAsLastSibling();  // 快照压回最上层

            DOTween.To(() => 0f, p => material.SetFloat("_Progress", p), 1.25f, 1.5f)
                .SetEase(Ease.InSine).SetUpdate(true).SetLink(go)
                .OnComplete(() =>
                {
                    Object.Destroy(go);
                    Object.Destroy(material);
                    Object.Destroy(shot);
                });
        }

        private static Texture2D noiseTexture;

        /// <summary>叠层 Perlin 噪声（火烧毛边用），首次生成后缓存复用。</summary>
        private static Texture2D NoiseTexture()
        {
            if (noiseTexture != null) return noiseTexture;
            const int size = 256;
            noiseTexture = new Texture2D(size, size, TextureFormat.R8, false) { wrapMode = TextureWrapMode.Repeat };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var u = x / (float)size;
                var v = y / (float)size;
                var value = Mathf.PerlinNoise(u * 5.13f + 11.7f, v * 5.13f + 3.9f) * .6f
                          + Mathf.PerlinNoise(u * 13.7f + 27.2f, v * 13.7f + 51.6f) * .3f
                          + Mathf.PerlinNoise(u * 29.3f + 5.4f, v * 29.3f + 17.8f) * .1f;
                var b = (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);
                pixels[y * size + x] = new Color32(b, b, b, 255);
            }
            noiseTexture.SetPixels32(pixels);
            noiseTexture.Apply(false, true);
            return noiseTexture;
        }
    }
}

using System;
using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 新游戏开场（2026-08-16 登录页重做）两段式：
    ///   ①登录页整屏快照从点击处**火烧**揭开，露出底下的外景图（无渐变，UIBurn shader）；
    ///   ②外景推镜：缩放/平移到「房屋与主楼剖面对齐」的取景，随后整层直接淡出，
    ///     底下就是 Hub 总览（主楼剖面）——对齐保证淡出瞬间两栋楼重合（主楼图不再进动画，用户定案 2026-08-16）。
    /// 纯表现件，运行时生成；播放期间挡住全部输入，播完自毁。
    /// </summary>
    public static class OpeningZoomFx
    {
        /// <summary>推镜缩放锚点（外景房屋附近，只影响运动轨迹、不影响终点取景）。</summary>
        private static readonly Vector2 ZoomPivot = new Vector2(.42f, .52f);

        /// <summary>外景 → 主楼的对齐变换（ORB 特征匹配实测，2026-08-16）：把外景缩放 AlignScale、
        /// 平移 AlignOffset（归一化，y 向上）后，两张图的房屋逐窗对齐。渐变瞬间若有错位改这两个值。
        /// HubSceneBinder 的「缩小到外景」层级复用同一变换（反向），保持进出取景一致。</summary>
        public const float AlignScale = 1.339f;
        public static readonly Vector2 AlignOffset = new Vector2(-.235f, -.172f);

        public static void Play(HouseUIManager ui, Action showNext)
        {
            ui.StartCoroutine(Run(ui, showNext));
        }

        private static IEnumerator Run(HouseUIManager ui, Action showNext)
        {
            // 外景取**当前时刻的延时帧**（2026-08-17 用户定案）：新游戏落在清晨，开场就用那一帧的晨光，
            // 于是推镜落地时与 Hub 的天色完全同一张画面，不再出现「开场蓝天、进屋晨曦」的色差。
            var minute = GameManager.Instance.HouseClockManager.Data.MinuteOfDayF;
            Texture exterior = SkyCycle.Exterior.Sample(minute, out var frame, out _, out _)
                ? frame
                : Resources.Load<Texture2D>("OutGameUI/house-exterior");
            var burnShader = Resources.Load<Shader>("Shaders/UIBurn");
            if (exterior == null)
            {
                Debug.LogError("[HouseUI] 开场推镜素材缺失（延时帧与 house-exterior 都没有），退化为直接切页");
                showNext();
                yield break;
            }

            // 火烧要用的整屏快照必须在帧末截
            yield return new WaitForEndOfFrame();
            var shot = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            shot.Apply(false, false);

            var layer = new GameObject("OpeningZoomLayer", typeof(RectTransform));
            layer.layer = 5;
            var layerRect = (RectTransform)layer.transform;
            layerRect.SetParent(ui.PageRoot, false);
            layerRect.anchorMin = Vector2.zero;
            layerRect.anchorMax = Vector2.one;
            layerRect.offsetMin = layerRect.offsetMax = Vector2.zero;
            layerRect.SetAsLastSibling();

            // ②外景：推近终点 = 房屋对齐主楼剖面的取景（淡出后与 Hub 总览重合）。
            // 帧本身已带天色（延时序列），所以**不再叠昼夜调色**——叠了就与 Hub 那层对不上（2026-08-17）
            var extRect = FullScreenImage(layerRect, "Exterior", exterior, ZoomPivot);
            var extImage = extRect.GetComponent<RawImage>();
            extImage.color = Color.white;
            // ①登录页快照：压最上，火烧揭开
            var burnRect = FullScreenImage(layerRect, "TitleShot", shot, new Vector2(.5f, .5f));
            var burnImage = burnRect.GetComponent<RawImage>();
            Material burnMaterial = null;
            if (burnShader != null)
            {
                burnMaterial = new Material(burnShader);
                burnMaterial.SetTexture("_NoiseTex", NoiseTexture());
                burnMaterial.SetVector("_Origin", new Vector4(
                    Mathf.Clamp01(Input.mousePosition.x / Mathf.Max(Screen.width, 1)),
                    Mathf.Clamp01(Input.mousePosition.y / Mathf.Max(Screen.height, 1)), 0, 0));
                burnMaterial.SetFloat("_Aspect", Screen.width / (float)Mathf.Max(Screen.height, 1));
                burnMaterial.SetFloat("_Progress", 0);
                burnImage.material = burnMaterial;
            }

            var group = layer.AddComponent<CanvasGroup>();
            group.blocksRaycasts = true;
            SfxManager.Play(ESfx.PageTransition);

            // 房屋对齐（外景 → 主楼）：终点位置由对齐变换推出——
            // 目标：外景任意点 q 的屏幕位置 = AlignScale·q + AlignOffset（即与主楼图逐点重合）
            // 绕 pivot P 缩放 S 并平移 O 时屏幕位置 = (q-P)·S + P + O/视口 → O = 视口·(AlignOffset - P·(1-S))
            var parentSize = ((RectTransform)ui.PageRoot).rect.size;
            if (parentSize.x < 1f) parentSize = new Vector2(1920f, 1080f);
            var targetScale = AlignScale;
            var targetOffset = Vector2.Scale(AlignOffset - ZoomPivot * (1f - AlignScale), parentSize);

            // 时间轴：火烧 [0, 1.1] → 外景推近对齐 [0.9, 2.4] → 换出 Hub 后整层淡出 [2.4, 2.95]
            var seq = DOTween.Sequence().SetUpdate(true).SetLink(layer);
            // 推镜期间时钟仍停着（开门时刻），帧不用换；天色已在帧里，无需再逐帧调色
            if (burnMaterial != null)
            {
                var material = burnMaterial;
                seq.Insert(0f, DOTween.To(() => 0f, p => material.SetFloat("_Progress", p), 1.3f, 1.1f)
                    .SetEase(Ease.InSine)
                    .OnComplete(() => { if (burnRect != null) UnityEngine.Object.Destroy(burnRect.gameObject); }));
            }
            else
            {
                // shader 缺失时的兜底：快照直接淡出
                seq.Insert(0f, burnImage.DOFade(0f, .6f)
                    .OnComplete(() => { if (burnRect != null) UnityEngine.Object.Destroy(burnRect.gameObject); }));
            }
            seq.Insert(.9f, extRect.DOScale(targetScale, 1.5f).SetEase(Ease.InOutCubic));
            seq.Insert(.9f, extRect.DOAnchorPos(targetOffset, 1.5f).SetEase(Ease.InOutCubic));
            seq.InsertCallback(2.4f, () =>
            {
                showNext();                   // Hub 页在推镜层底下就位（初始相机即主楼总览）
                layerRect.SetAsLastSibling(); // 新页面可能压到上面，把推镜层压回最上
            });
            seq.Insert(2.45f, group.DOFade(0f, .55f).SetEase(Ease.InOutSine));
            seq.OnComplete(() =>
            {
                if (layer != null) UnityEngine.Object.Destroy(layer);
                if (burnMaterial != null) UnityEngine.Object.Destroy(burnMaterial);
                if (shot != null) UnityEngine.Object.Destroy(shot);
            });
        }

        /// <summary>全屏 RawImage（指定缩放锚点）。</summary>
        private static RectTransform FullScreenImage(RectTransform parent, string name, Texture texture, Vector2 pivot)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.layer = 5;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.pivot = pivot; // 先设缩放锚点再铺满，避免 pivot 变更挪动矩形
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = go.GetComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = true; // 挡输入
            return rect;
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

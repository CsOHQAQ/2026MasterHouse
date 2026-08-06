using System;
using UnityEngine;
using UnityEngine.UI;

namespace MasterPotion
{
    /// <summary>
    /// 访客序列帧图集描述（与 CatVsDog 导出的 *_sheet.json 字段一致，JsonUtility 解析）。
    /// PNG 入库时可能被缩放，因此帧定位只依赖 columns/rows/frameCount，宽高只用于长宽比。
    /// </summary>
    [Serializable]
    internal sealed class OutGameVisitorSheet
    {
        public int frameWidth;
        public int frameHeight;
        public int columns;
        public int rows;
        public int frameCount;

        public float Aspect => frameHeight > 0 ? (float)frameWidth / frameHeight : 1f;

        /// <summary>从 Resources 同名加载 PNG（Texture2D）与 JSON（TextAsset）。缺任意一个返回 null。</summary>
        public static OutGameVisitorSheet Load(string resourcePath, out Texture2D texture)
        {
            texture = Resources.Load<Texture2D>(resourcePath);
            var json = Resources.Load<TextAsset>(resourcePath);
            if (texture == null || json == null) return null;
            var sheet = JsonUtility.FromJson<OutGameVisitorSheet>(json.text);
            return sheet != null && sheet.columns > 0 && sheet.rows > 0 && sheet.frameCount > 0 ? sheet : null;
        }
    }

    /// <summary>
    /// 用 RawImage 的 uvRect 播放序列帧图集：不切片、不生成 Sprite，直接按行列换算每帧 UV。
    /// 帧序为从左到右、从上到下；frameCount 可以小于 columns×rows（尾部空格子跳过）。
    /// </summary>
    internal sealed class OutGameVisitorSheetAnimator : MonoBehaviour
    {
        private RawImage image;
        private OutGameVisitorSheet sheet;
        private float fps = 12f;
        private bool loop = true;
        private bool playing;
        private float timer;
        private int frame;
        private Action onComplete;

        /// <summary>切换到一段图集动画并从第 0 帧开始播放。onComplete 仅在非循环播放结束时回调一次。</summary>
        public void Play(Texture2D texture, OutGameVisitorSheet meta, float framesPerSecond, bool looping, Action completed = null)
        {
            if (image == null) image = GetComponent<RawImage>();
            if (image == null || texture == null || meta == null) return;
            image.texture = texture;
            sheet = meta;
            fps = Mathf.Max(1f, framesPerSecond);
            loop = looping;
            onComplete = completed;
            timer = 0f;
            frame = 0;
            playing = true;
            ApplyFrame();
        }

        public bool IsPlaying => playing;

        /// <summary>当前图集的单帧长宽比（不同动作的帧尺寸可能不同）。</summary>
        public float CurrentAspect => sheet != null ? sheet.Aspect : 1f;

        private void Update()
        {
            if (!playing || sheet == null) return;
            // 局外 UI 全部按不受 timeScale 影响的节奏运行（与 DOTween SetUpdate(true) 一致）
            timer += Time.unscaledDeltaTime;
            var step = 1f / fps;
            while (timer >= step)
            {
                timer -= step;
                frame++;
                if (frame >= sheet.frameCount)
                {
                    if (loop)
                    {
                        frame = 0;
                    }
                    else
                    {
                        frame = sheet.frameCount - 1;
                        playing = false;
                        var callback = onComplete;
                        onComplete = null;
                        callback?.Invoke();
                        break;
                    }
                }
            }
            ApplyFrame();
        }

        private void ApplyFrame()
        {
            if (image == null || sheet == null) return;
            var col = frame % sheet.columns;
            var row = frame / sheet.columns; // 0 = 最上面一行
            var w = 1f / sheet.columns;
            var h = 1f / sheet.rows;
            image.uvRect = new Rect(col * w, 1f - (row + 1) * h, w, h);
        }
    }
}

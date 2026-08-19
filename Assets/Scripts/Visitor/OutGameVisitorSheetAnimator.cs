using System;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 访客序列帧图集描述（与 CatVsDog 导出的 *_sheet.json 字段一致，JsonUtility 解析）。
    /// PNG 入库时可能被缩放，因此帧定位只依赖 columns/rows/frameCount，宽高只用于长宽比。
    /// </summary>
    [Serializable]
    // public 而非 internal：编辑器工具（测量脚底留白）在 Assembly-CSharp-Editor 里，
    // internal 跨不过程序集边界（2026-08-18 编译报错 CS0122）
    public sealed class OutGameVisitorSheet
    {
        public int frameWidth;
        public int frameHeight;
        public int columns;
        public int rows;
        public int frameCount;
        /// <summary>
        /// 每帧底部透明留白占帧高的比例（2026-08-18 反馈「访客还是有些高」）：
        /// 立绘四周带留白，脚底并不在帧的下边缘——直接把帧底压在地面坐标上，人就浮起来了。
        /// 演员用它当 pivot.y，可见的脚底才落在地面点上。各张图差别很大（0 ~ 0.14），
        /// 所以按图存，值由菜单「测量访客立绘脚底留白」量出来写进同名 JSON。
        /// </summary>
        public float footPadding;
        /// <summary>
        /// 每帧顶部透明留白占帧高的比例（2026-08-18 反馈「名牌离得太远」）：
        /// 立绘头顶上方也有一大段留白（猫那张占 32%），名牌与气泡挂在演员矩形的上边缘，
        /// 于是它们离真正的头顶隔着这一整段空气。挂点按它下压，名牌才贴着头。
        /// </summary>
        public float headPadding;
        /// <summary>该图集的建议播放帧率；旧资源未配置时仍按 12 FPS 播放。</summary>
        public float framesPerSecond;
        /// <summary>起身/坐下等非移动段的建议帧率；未配置时沿用主体动画帧率。</summary>
        public float transitionFramesPerSecond;
        /// <summary>完整动作中允许产生位移的帧区间；区间外只播放起身/坐下，不改变位置。</summary>
        public bool hasMovementWindow;
        public int moveStartFrame;
        public int moveEndFrame;
        /// <summary>首次迈步后真正开始循环的帧；用于避开“入步帧 → 循环尾帧”之间的姿态断点。</summary>
        public int walkLoopStartFrame;
        /// <summary>仅该动作素材的默认朝向与工程约定相反时翻转。</summary>
        public bool invertFacing;
        /// <summary>没有收尾帧时，停止移动后倒放起步前置帧，平滑回到待机姿态。</summary>
        public bool reverseIntroOnStop;
        /// <summary>切回待机后仍沿用 walk 素材的朝向约定，避免站定瞬间左右翻面。</summary>
        public bool keepWalkFacingWhenIdle;
        /// <summary>到达目标时立即结束行走循环并进入收尾段，避免在终点原地踏步。</summary>
        public bool stopImmediatelyAtTarget;

        public float Aspect => frameHeight > 0 ? (float)frameWidth / frameHeight : 1f;
        public float PlaybackFps => framesPerSecond > 0f ? framesPerSecond : 12f;
        public float TransitionPlaybackFps => transitionFramesPerSecond > 0f
            ? transitionFramesPerSecond
            : PlaybackFps;
        public bool HasMovementWindow => hasMovementWindow &&
            moveStartFrame >= 0 && moveEndFrame >= moveStartFrame && moveEndFrame < frameCount;
        public int WalkLoopStartFrame => walkLoopStartFrame > moveStartFrame && walkLoopStartFrame <= moveEndFrame
            ? walkLoopStartFrame
            : moveStartFrame;

        /// <summary>
        /// 从 Resources 同名加载 PNG（Texture2D）与 JSON（TextAsset）。PNG 缺失返回 null。
        ///
        /// **JSON 可以没有**：那就是一张单帧定格图（2026-08-16 入库的 QQ 小人就是这种），
        /// 按 1×1×1 的图集处理——uvRect 铺满整张图，播放逻辑一行都不用分叉。
        /// 序列帧素材（CatVsDog 导出的那批）必须带 JSON，缺了会当成单帧而不是报错，
        /// 这是有意的：动图退化成定格图是能看的表现，比整只访客不出现好。
        /// </summary>
        public static OutGameVisitorSheet Load(string resourcePath, out Texture2D texture)
        {
            texture = Resources.Load<Texture2D>(resourcePath);
            if (texture == null) return null;
            var json = Resources.Load<TextAsset>(resourcePath);
            if (json == null)
                return new OutGameVisitorSheet
                {
                    frameWidth = texture.width,
                    frameHeight = texture.height,
                    columns = 1,
                    rows = 1,
                    frameCount = 1,
                };
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
        /// <summary>单帧定格图当「一次性动作」播时的停留时长（秒）。按 fps 算只有 1/14 秒，眼睛跟不上。</summary>
        private const float StaticHoldSeconds = 1.1f;

        private RawImage image;
        private OutGameVisitorSheet sheet;
        private float fps = 12f;
        private bool loop = true;
        private bool playing;
        private float timer;
        private int frame;
        private int firstFrame;
        private int lastFrame;
        private int frameStep = 1;
        private Action onComplete;

        /// <summary>切换到一段图集动画并从第 0 帧开始播放。onComplete 仅在非循环播放结束时回调一次。</summary>
        public void Play(Texture2D texture, OutGameVisitorSheet meta, float framesPerSecond, bool looping, Action completed = null)
        {
            PlayRange(texture, meta, framesPerSecond, 0, meta != null ? meta.frameCount - 1 : 0, looping, completed);
        }

        /// <summary>播放同一图集中的闭区间帧段，用于“起身一次 → 行走循环 → 坐下一次”。</summary>
        public void PlayRange(Texture2D texture, OutGameVisitorSheet meta, float framesPerSecond,
            int rangeStart, int rangeEnd, bool looping, Action completed = null)
        {
            if (image == null) image = GetComponent<RawImage>();
            if (image == null || texture == null || meta == null) return;
            image.texture = texture;
            sheet = meta;
            fps = Mathf.Max(1f, framesPerSecond);
            loop = looping;
            onComplete = completed;
            timer = 0f;
            firstFrame = Mathf.Clamp(rangeStart, 0, meta.frameCount - 1);
            lastFrame = Mathf.Clamp(rangeEnd, 0, meta.frameCount - 1);
            frameStep = lastFrame >= firstFrame ? 1 : -1;
            frame = firstFrame;
            CompletedLoops = 0;
            playing = true;
            ApplyFrame();
        }

        public bool IsPlaying => playing;
        public int CurrentFrame => frame;
        public int CompletedLoops { get; private set; }

        /// <summary>当前图集的单帧长宽比（不同动作的帧尺寸可能不同）。</summary>
        public float CurrentAspect => sheet != null ? sheet.Aspect : 1f;

        private void Update()
        {
            if (!playing || sheet == null) return;
            // 单帧定格图循环播 = 一张不动的立牌，没有帧要推
            if (sheet.frameCount <= 1 && loop) return;
            // 局外 UI 全部按不受 timeScale 影响的节奏运行（与 DOTween SetUpdate(true) 一致）
            timer += Time.unscaledDeltaTime;
            // 单帧一次性动作按固定时长停留，否则 1/fps 秒就切回待机、根本看不见
            var step = sheet.frameCount <= 1 ? StaticHoldSeconds : 1f / fps;
            while (timer >= step)
            {
                timer -= step;
                frame += frameStep;
                var passedEnd = frameStep > 0 ? frame > lastFrame : frame < lastFrame;
                if (passedEnd)
                {
                    if (loop)
                    {
                        frame = firstFrame;
                        CompletedLoops++;
                    }
                    else
                    {
                        frame = lastFrame;
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

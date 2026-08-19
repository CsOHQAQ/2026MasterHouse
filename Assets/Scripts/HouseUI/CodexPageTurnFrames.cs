using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 翻书分帧序列（2026-08-19）：把美术给的翻书动画视频抽成一圈帧，
    /// 翻页时整幅底图直接播它——纸的卷曲、投影、落页全是手绘的，比代码模拟的缩放像得多。
    ///
    /// 帧在 <c>Resources/OutGameUI/PageTurn/turn_00..</c>，一圈 = 一次「从右往左翻一页」。
    /// **往后翻正放、往前翻倒放**，所以只需要这一圈。
    /// 视频与详情页底图是同一套美术、同为 16:9，实测边缘匹配正好落在原位，不需要缩放校正。
    /// </summary>
    public static class CodexPageTurnFrames
    {
        private const string PathPrefix = "OutGameUI/PageTurn/turn_";
        private static Texture2D[] frames;

        /// <summary>
        /// 每一帧里**纸的前缘**落在画面横向的什么位置（0=左边缘，1=右边缘）。
        /// 从分帧自己量出来的：只看书页内框、和摊平那帧作差、取变化列的最左端，
        /// 再压成单调并做三点平滑（帧 6 的阴影会让原始值跳 0.17）。
        /// 有了它，页面内容就能被纸的前缘一点点切掉，露出底下的空白书页。
        /// </summary>
        private static readonly float[] Front =
        {
            1.000f, 0.886f, 0.804f, 0.752f, 0.702f, 0.638f, 0.571f, 0.525f, 0.507f, 0.501f,
            0.492f, 0.474f, 0.446f, 0.411f, 0.371f, 0.334f, 0.307f, 0.291f, 0.279f, 0.264f,
            0.244f, 0.218f, 0.191f, 0.164f, 0.142f, 0.128f, 0.120f, 0.120f, 0.120f, 0.120f,
        };

        /// <summary>帧数；0 = 素材缺失（调用方退回代码模拟的翻页）。</summary>
        public static int Count => Load().Length;

        public static Texture2D At(int index)
        {
            var set = Load();
            return set.Length == 0 ? null : set[Mathf.Clamp(index, 0, set.Length - 1)];
        }

        /// <summary>按进度取帧。<paramref name="reversed"/> = 倒放（往前翻一页）。</summary>
        public static Texture2D Sample(float t01, bool reversed)
        {
            var set = Load();
            if (set.Length == 0) return null;
            var t = Mathf.Clamp01(reversed ? 1f - t01 : t01);
            return set[Mathf.Min(set.Length - 1, Mathf.FloorToInt(t * set.Length))];
        }

        /// <summary>
        /// 按进度取「这一刻还该露着内容的那一段占多宽」（0~1）。
        /// 正放时纸从右往左扫，留下的是左边一段 [0, f]；倒放时纸从左往右扫，留下的是右边一段 [f, 1]。
        /// 两种情况都返回**可见段的宽度占比**，裁哪一侧由调用方按方向决定。
        /// </summary>
        public static float FrontAt(float t01, bool reversed)
        {
            var t = Mathf.Clamp01(reversed ? 1f - t01 : t01);
            var at = t * (Front.Length - 1);
            var lo = Mathf.FloorToInt(at);
            var edge = Mathf.Lerp(Front[lo], Front[Mathf.Min(Front.Length - 1, lo + 1)], at - lo);
            return reversed ? 1f - edge : edge;
        }

        private static Texture2D[] Load()
        {
            if (frames != null) return frames;
            var list = new System.Collections.Generic.List<Texture2D>();
            for (var i = 0; i < 128; i++)
            {
                var texture = Resources.Load<Texture2D>(PathPrefix + i.ToString("00"));
                if (texture == null) break;
                list.Add(texture);
            }
            if (list.Count == 0)
                Debug.LogWarning("[Codex] 翻书分帧缺失（Resources/" + PathPrefix + "00），翻页退回代码模拟");
            frames = list.ToArray();
            return frames;
        }
    }
}

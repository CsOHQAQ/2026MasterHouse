using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 翻书分帧序列（2026-08-19）：把美术给的翻书动画视频抽成一圈帧，
    /// 再用 BiRefNet 把**书**从每帧里抠出来（背景已去掉、带透明通道）。
    /// 翻页时这本抠出来的书盖在常规底图上播——外圈云纹始终是详情页自己的底图，
    /// 不会因为视频背景画得不一样而整屏跳变。
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
        /// 值已归一到 [0,1]（原始量程 0.12~1.0 是书页内框的左右边），两个方向收尾都干净。
        /// </summary>
        private static readonly float[] Front =
        {
            1.000f, 0.870f, 0.777f, 0.718f, 0.661f, 0.589f, 0.513f, 0.460f, 0.440f, 0.433f,
            0.423f, 0.402f, 0.370f, 0.331f, 0.285f, 0.243f, 0.213f, 0.194f, 0.181f, 0.164f,
            0.141f, 0.111f, 0.081f, 0.050f, 0.025f, 0.009f, 0.000f, 0.000f, 0.000f, 0.000f,
        };

        /// <summary>纸的前缘扫到书脊时，这条曲线的取值：过了这一刻，被翻走的那页就完全立起来了。</summary>
        public const float SpineAt = .432f;

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

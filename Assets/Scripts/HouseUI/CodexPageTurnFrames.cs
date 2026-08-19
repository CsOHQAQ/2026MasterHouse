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

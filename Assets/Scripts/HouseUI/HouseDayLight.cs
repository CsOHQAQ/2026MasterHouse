using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 局外昼夜光照色带（2026-08-14）：按局内时钟给场景/封面调色。表现参数，放代码（同 DayTransitionFx 破晓配色）。
    /// tint 走乘法调色（RawImage/Image.color）——白 = 正午原图、偏暖 = 清晨/黄昏、压暗偏蓝 = 夜里，对比度不丢；
    /// veil 是顶层罩色，只在深夜启用（把不吃 tint 的元素一起压进夜色），白天恒为透明，避免「盖雾」发灰。
    /// Hub 四宫格与标题页封面共用本色带，进出游戏时灯光衔接。
    /// </summary>
    public static class HouseDayLight
    {
        private static readonly (float minute, Color tint, Color veil)[] Keys =
        {
            (0f, new Color(.4f, .45f, .68f), new Color(.05f, .08f, .22f, .35f)),        // 深夜
            (5f * 60f, new Color(.4f, .45f, .68f), new Color(.05f, .08f, .22f, .35f)),  // 5:00 仍是深夜
            (7f * 60f, new Color(1f, .8f, .64f), Color.clear),                          // 7:00 破晓橙
            (9f * 60f, new Color(1f, .93f, .84f), Color.clear),                         // 9:00 清晨暖白
            (11.5f * 60f, Color.white, Color.clear),                                    // 11:30 起正午烈日（原图即基准）
            (14f * 60f, Color.white, Color.clear),                                      // 14:00 止
            (17f * 60f, new Color(1f, .9f, .74f), Color.clear),                         // 17:00 午后金
            (19f * 60f, new Color(1f, .72f, .54f), Color.clear),                        // 19:00 日落橙红
            (21f * 60f, new Color(.62f, .55f, .76f), new Color(.05f, .08f, .22f, .12f)),// 21:00 暮色紫
            (22.5f * 60f, new Color(.44f, .48f, .7f), new Color(.05f, .08f, .22f, .3f)),// 22:30 入夜蓝
            (24f * 60f, new Color(.4f, .45f, .68f), new Color(.05f, .08f, .22f, .35f)), // 24:00 回到深夜
        };

        /// <summary>取给定时刻的光照（关键帧间线性插值）。</summary>
        public static (Color tint, Color veil) At(float minuteOfDay)
        {
            var minute = Mathf.Clamp(minuteOfDay, 0f, 24f * 60f);
            for (var i = 1; i < Keys.Length; i++)
            {
                if (minute > Keys[i].minute) continue;
                var from = Keys[i - 1];
                var to = Keys[i];
                var t = Mathf.InverseLerp(from.minute, to.minute, minute);
                return (Color.Lerp(from.tint, to.tint, t), Color.Lerp(from.veil, to.veil, t));
            }
            var last = Keys[Keys.Length - 1];
            return (last.tint, last.veil);
        }

        /// <summary>当前游戏时刻的光照。</summary>
        public static (Color tint, Color veil) Now()
            => At(GameManager.Instance.HouseClockManager.Data.MinuteOfDayF);
    }
}

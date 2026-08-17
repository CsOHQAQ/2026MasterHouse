using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 场景昼夜循环（2026-08-17）：外景与主楼剖面各有一套延时分帧序列，按局内时钟播放——
    /// 日月升落、云层变换、星空、窗户亮灯、室内光影全在帧里，不再各自单独实现。
    ///
    /// 两套序列各自标定「时刻 → 帧号」的关键点，映射到同一时钟因而天然对齐；
    /// 相邻两帧交叉淡化，所以时间流速再慢也是平滑推进而非跳帧。
    /// 主楼序列的素材不含日出段，关键点让帧号先减后增（乒乓），黎明段即倒放的暮色。
    /// </summary>
    public sealed class SkyCycle
    {
        // 关键点密度按**营业时段**（08:00–22:00 玩家在场）倾斜：那段每帧只代表几分钟，天色推进最细腻；
        // 打烊后到次日开门玩家看不到（「结束今天」直接跳到次日开门），序列在那段快速走完并回到起点。

        // ⚠ 关键点表必须**声明在两个外景实例之前**：静态字段按声明顺序初始化，
        // 放在后面的话构造时拿到的是 null（曾因此在开场推镜里空引用崩溃）。
        private static readonly Vector2[] ExteriorKeys =
        {
            new Vector2(0f, 0f),        // 07:00 日出
            new Vector2(60f, 5f),       // 08:00 开门
            new Vector2(300f, 30f),     // 12:00 正午
            new Vector2(660f, 78f),     // 18:00 日落
            new Vector2(750f, 90f),     // 19:30 夜幕合拢
            new Vector2(810f, 100f),    // 20:30 入夜
            new Vector2(900f, 118f),    // 22:00 打烊 = 深夜星空（时钟在此停走，这就是玩家看到的终局夜景）
            new Vector2(1260f, 122f),   // 次日 04:00
            new Vector2(1440f, 124f),   // 次日 07:00（= 帧 0，循环）
        };

        /// <summary>
        /// 纯天空版外景（房子抹掉、只留天空/日月/星空）：主楼层可见时铺在完整外景之上——
        /// 主楼遮罩的透明区若露出外景那栋楼，旗杆与招牌就会成双（两套素材的建筑差十几像素）。
        /// 帧号与 Exterior 一一对应，共用同一张关键点表。
        /// </summary>
        public static readonly SkyCycle SkyOnly = new SkyCycle("OutGameUI/SkyOnly", ExteriorKeys);

        /// <summary>外景（124 帧）：帧 0 = 日出，30 = 正午，78 = 日落，118 = 深夜星空。</summary>
        public static readonly SkyCycle Exterior = new SkyCycle("OutGameUI/SkyCycle", ExteriorKeys);

        /// <summary>主楼剖面（181 帧）：帧 0 = 正午，60 = 日落，90 = 夜幕，125 起室内灯陆续亮，145+ 灯火通明。
        /// 素材没有日出段，黎明由深夜倒放回暮色帧（82）填补，且压在玩家看不到的凌晨。</summary>
        public static readonly SkyCycle House = new SkyCycle("OutGameUI/HouseCycle", new[]
        {
            new Vector2(0f, 82f),       // 07:00 朝霞（倒放段的暮色）
            new Vector2(60f, 45f),      // 08:00 开门，晨光
            new Vector2(300f, 0f),      // 12:00 正午
            new Vector2(660f, 60f),     // 18:00 日落晚霞
            new Vector2(750f, 90f),     // 19:30 夜幕合拢（室内最暗）
            new Vector2(810f, 120f),    // 20:30 灯陆续亮起 + 栈桥灯串
            new Vector2(900f, 178f),    // 22:00 打烊 = 灯火通明的深夜（时钟停走，终局画面必须是亮灯的）
            new Vector2(1260f, 180f),   // 次日 04:00
            new Vector2(1440f, 82f),    // 次日 07:00（回到起点，循环闭合）
        });

        private readonly string folder;
        private readonly Vector2[] keys;
        private Texture2D[] frames;

        private SkyCycle(string resourceFolder, Vector2[] keyframes)
        {
            folder = resourceFolder;
            keys = keyframes;
        }

        /// <summary>帧序列（按名排序，惰性加载）；目录为空返回 null。</summary>
        public Texture2D[] Frames
        {
            get
            {
                if (frames != null && frames.Length > 0) return frames;
                var loaded = Resources.LoadAll<Texture2D>(folder);
                if (loaded == null || loaded.Length == 0) return null;
                System.Array.Sort(loaded, (a, b) => string.CompareOrdinal(a.name, b.name));
                frames = loaded;
                return frames;
            }
        }

        /// <summary>取当前时刻的两帧与混合权重：结果 = Lerp(前帧, 后帧, blend)。无素材返回 false。</summary>
        public bool Sample(float minuteOfDay, out Texture2D from, out Texture2D to, out float blend)
        {
            from = to = null;
            blend = 0f;
            var all = Frames;
            if (all == null || keys == null || keys.Length < 2) return false;

            var t = Mathf.Repeat(minuteOfDay - 420f, 1440f); // 以 07:00 为循环起点
            var frame = keys[keys.Length - 1].y;
            for (var i = 1; i < keys.Length; i++)
            {
                if (t > keys[i].x) continue;
                frame = Mathf.Lerp(keys[i - 1].y, keys[i].y, Mathf.InverseLerp(keys[i - 1].x, keys[i].x, t));
                break;
            }
            frame = Mathf.Clamp(frame, 0f, all.Length - 1.0001f);
            var lower = Mathf.FloorToInt(frame);
            from = all[lower];
            to = all[Mathf.Min(lower + 1, all.Length - 1)];
            blend = frame - lower;
            return true;
        }
    }
}

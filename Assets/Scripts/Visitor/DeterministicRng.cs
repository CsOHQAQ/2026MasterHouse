namespace MasterHouse
{
    /// <summary>
    /// 逻辑层确定性随机（访客交付说明 §6.1）：SplitMix64 派生种子 + xorshift64* 随机流。
    /// 禁止用 UnityEngine.Random（全局状态、跨平台不保证一致，§11.1）。
    /// 结构体持有全部状态（State 可序列化），同一种子重放必得同一序列——读档刷需求的路子被堵死。
    /// 概率一律用整数百分比（Chance），避免 float 跨平台误差。
    /// </summary>
    public struct DeterministicRng
    {
        private ulong state;

        /// <summary>随机流内部状态（存档接缝用，待定 #9）。</summary>
        public ulong State
        {
            get => state;
            set => state = value != 0 ? value : 0x9E3779B97F4A7C15UL;
        }

        public DeterministicRng(long seed)
        {
            state = Mix((ulong)seed);
            if (state == 0) state = 0x9E3779B97F4A7C15UL;
        }

        /// <summary>派生种子（§6.1）：rollSeed = Hash(runSeed, scheduleDay, scheduleIndex)。无状态、不依赖调用顺序。</summary>
        public static long Hash(long runSeed, int a, int b)
        {
            var h = (ulong)runSeed;
            h = Mix(h ^ ((ulong)(uint)a * 0x9E3779B97F4A7C15UL));
            h = Mix(h ^ ((ulong)(uint)b * 0xBF58476D1CE4E5B9UL));
            return (long)h;
        }

        /// <summary>
        /// 三元派生种子：对话选取用（对话设计说明 §6）——
        /// 种子 = Hash(runSeed, 访客实例Id, 触发分类, 本次请求序号)。
        /// 与两元重载同样无状态、不依赖调用顺序。
        /// </summary>
        public static long Hash(long runSeed, int a, int b, int c)
        {
            var h = (ulong)Hash(runSeed, a, b);
            h = Mix(h ^ ((ulong)(uint)c * 0x94D049BB133111EBUL));
            return (long)h;
        }

        /// <summary>
        /// 字符串稳定哈希（FNV-1a 32 位）：把资产名/id 这类字符串键喂进派生种子用。
        /// **不要用 string.GetHashCode()**——.NET 不保证它跨进程/跨版本稳定，
        /// 那会让「同一 runSeed 重进游戏结果一致」这条验收项在某次升级后悄悄失效。
        /// </summary>
        public static int HashString(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            unchecked
            {
                var hash = 2166136261u;
                foreach (var c in value)
                {
                    hash ^= c;
                    hash *= 16777619u;
                }
                return (int)hash;
            }
        }

        /// <summary>SplitMix64 终混（雪崩充分，适合把结构化输入打散成种子）。</summary>
        private static ulong Mix(ulong z)
        {
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>xorshift64* 下一随机数。</summary>
        public ulong NextULong()
        {
            var x = state;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            state = x;
            return x * 0x2545F4914F6CDD1DUL;
        }

        /// <summary>[minInclusive, maxExclusive) 的整数；区间非法时返回 minInclusive。</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            var span = (ulong)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextULong() % span);
        }

        /// <summary>整数百分比概率（0~100）。</summary>
        public bool Chance(int percent)
        {
            if (percent <= 0) return false;
            if (percent >= 100) return true;
            return Range(0, 100) < percent;
        }
    }
}

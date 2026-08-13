using System;
using System.Collections.Generic;

namespace MasterHouse
{
    /// <summary>
    /// 条件节点的运行时状态：每条需求维护一条滑动窗口内的到货记录。
    /// 只能由 Manager 修改（§2）；View 只读。
    ///
    /// 到货是稀疏的（链接按节拍成批送达），故用「到货记录队列」而非每 tick 一格的
    /// 环形数组——窗口配到几百 tick 也不会撑大存档。
    /// 时间轴用所属关卡的 TickCount：关卡未打开时它不推进，窗口内容天然冻结，
    /// 玩家再进来仍是离开时的达标状态（锁存是自然结果，无需额外字段）。
    /// </summary>
    public class ConditionState
    {
        /// <summary>一条到货记录：某 tick 收到多少。</summary>
        public struct Arrival
        {
            public long Tick;
            public int Amount;
        }

        /// <summary>一条需求的运行时轨道。</summary>
        public class Track
        {
            /// <summary>对应的策划配置（Model 层，只读）。</summary>
            public readonly ConditionEntry Entry;

            /// <summary>窗口内的到货记录，按 tick 升序（追加即有序）。</summary>
            public readonly List<Arrival> Arrivals = new List<Arrival>();

            /// <summary>窗口内累计量（Arrivals 之和，增量维护，避免每帧重算）。</summary>
            public int WindowAmount;

            public bool Satisfied;

            public int Required => Math.Max(1, Entry.RequiredAmount);

            public int WindowTicks => Math.Max(1, Entry.WindowTicks);

            public Track(ConditionEntry entry)
            {
                Entry = entry;
            }
        }

        /// <summary>按 Def.Conditions 的顺序排列，顺序稳定（§11.2）。</summary>
        public readonly List<Track> Tracks = new List<Track>();

        /// <summary>全部条件均达标。每 tick 由 Manager 在节点阶段重算。</summary>
        public bool Satisfied { get; private set; }

        public ConditionState(ConditionNodeDef def)
        {
            foreach (var entry in def.Conditions)
                if (entry != null && entry.Item != null)
                    Tracks.Add(new Track(entry));
            // 未配条件的条件节点视为恒达标（与「关卡没有条件节点 = 恒生效」同口径）
            Satisfied = Tracks.Count == 0;
        }

        /// <summary>
        /// 记一次到货（投递阶段调用）。不在需求列表里的物资直接蒸发、不记账。
        /// 同一物资配了多条需求（不同窗口）时各自记录。
        /// </summary>
        public void Record(ItemDef item, int amount, long tick)
        {
            if (item == null || amount <= 0) return;
            foreach (var track in Tracks)
            {
                if (track.Entry.Item != item) continue;

                int last = track.Arrivals.Count - 1;
                if (last >= 0 && track.Arrivals[last].Tick == tick)
                {
                    // 同 tick 的多次到货并入一条，队列更短
                    var record = track.Arrivals[last];
                    record.Amount += amount;
                    track.Arrivals[last] = record;
                }
                else
                {
                    track.Arrivals.Add(new Arrival { Tick = tick, Amount = amount });
                }
                track.WindowAmount += amount;
            }
        }

        /// <summary>
        /// 推进窗口：弹出过期记录并重算达标（节点阶段每 tick 调用）。
        /// 窗口口径为最近 WindowTicks 个 tick，即 Tick ∈ (tick - W, tick]。
        /// </summary>
        public void Advance(long tick)
        {
            bool all = true;
            foreach (var track in Tracks)
            {
                long expireAt = tick - track.WindowTicks; // Tick <= expireAt 的记录已出窗
                int expired = 0;
                while (expired < track.Arrivals.Count && track.Arrivals[expired].Tick <= expireAt)
                {
                    track.WindowAmount -= track.Arrivals[expired].Amount;
                    expired++;
                }
                if (expired > 0)
                    track.Arrivals.RemoveRange(0, expired);

                track.Satisfied = track.WindowAmount >= track.Required;
                if (!track.Satisfied) all = false;
            }
            Satisfied = all;
        }
    }
}

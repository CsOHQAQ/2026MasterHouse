using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>串门邻居（纯表现氛围 NPC，随机轮换进场、门口排队，无业务字段）。原挂 VisitorTable（已退役），迁入调参配置。</summary>
    [Serializable]
    public sealed class AmbientVisitorDef
    {
        [Tooltip("稳定键（图集文件名），演员命名为 neighbor_ + id")]
        public string id;
        public string displayName;
        [Tooltip("序列帧前缀，如 OutGameUI/Visitors/laoda")]
        public string sheetPath;
    }

    /// <summary>
    /// 访客全局表现与营业参数（Model 层，运行时只读；访客交付说明 §4.5）。
    /// 旧 HouseClockManager.DayStartMinute（const 8*60）迁入本配置的开门时刻——代码里不再留营业时间魔数。
    /// 【务必独占本文件】ScriptableObject 必须与文件同名，否则 .asset 的脚本引用会损坏（见 RETRO）。
    /// </summary>
    [CreateAssetMenu(menuName = "MasterHouse/访客调参配置", fileName = "VisitorTuningConfig")]
    public sealed class VisitorTuningConfig : ScriptableObject
    {
        [Header("营业时段（当天分钟数，整数比较，§16.4）")]
        [Tooltip("开门时刻（分钟）：每天从这一刻开始；日结后时间跳到次日此刻")]
        public int openMinute = 8 * 60;
        [Tooltip("打烊时刻（分钟）：到点后一切 tick 业务统一冻结（§7 打烊闸门）")]
        public int closeMinute = 22 * 60;

        [Header("场景表现")]
        [Range(.2f, 1.2f)]
        [Tooltip("访客演员的基础世界缩放；还会叠加脚底深度带来的轻微透视缩放")]
        public float actorWorldScale = .6f;

        [Header("需求示意（tick，2026-08-14 对话重构）")]
        [Tooltip("入住后到「开口示意」的最短间隔（tick）：客人先安顿一会儿才会有话说")]
        public int needPromptMinTicks = 60;
        [Tooltip("入住后到「开口示意」的最长间隔（tick）：实际值在 [最短, 最长] 之间由实例随机流取（读档不刷）。\n" +
                 "这段时间里点访客只会得到一句提示，不播对话；**服务超时也从示意那一刻才开始倒计时**")]
        public int needPromptMaxTicks = 180;

        [Header("闲逛节奏（tick，§4.5/§5）")]
        [Tooltip("冒泡间隔（tick）：闲逛访客每隔约这么久自动抽一句闲聊台词")]
        public int bubbleIntervalTicks = 120;
        [Tooltip("冒泡间隔抖动（tick）：实际间隔 = 间隔 ± 抖动（确定性随机流取值）")]
        public int bubbleJitterTicks = 40;
        [Tooltip("气泡停留时长（tick）：闲逛台词气泡展示这么久后收起（表现层换算秒数）")]
        public int bubbleHoldTicks = 40;

        [Header("氛围邻居（纯表现，随机轮换进场）")]
        public List<AmbientVisitorDef> ambientVisitors = new List<AmbientVisitorDef>();
    }
}

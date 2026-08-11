using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>日程条目（访客交付说明 §4.4）：第几天 / 出现时刻 / 种族 / 具名覆写（可空）。</summary>
    [Serializable]
    public sealed class VisitorScheduleEntry
    {
        [Tooltip("第几天（从 1 开始）")] public int day = 1;
        [Tooltip("出现时刻：当天分钟数 0~1439")] public int appearMinute = 9 * 60;
        public VisitorRaceDef race;
        [Tooltip("具名剧情客人覆写（可空；结构占位，运行时暂不消费，§4.4）")] public NamedVisitorDef namedOverride;
    }

    /// <summary>
    /// 访客日程表（Model 层，运行时只读；访客交付说明 §4.4）。单资产多行。
    /// 零随机、零上限：谁在第几天几点出现完全由策划配；不做生成器、不做同时在场上限。
    /// 消费时按 (day, 出现时刻, 下标) 稳定排序（§11.2）；本表下标即派生种子的 scheduleIndex（§6.1），
    /// **重排/插入既有条目会改变对应访客的需求 roll**，加内容请追加在表尾。
    /// 【务必独占本文件】ScriptableObject 必须与文件同名，否则 .asset 的脚本引用会损坏（见 RETRO）。
    /// </summary>
    [CreateAssetMenu(menuName = "MasterHouse/访客日程表", fileName = "VisitorScheduleTable")]
    public sealed class VisitorScheduleTable : ScriptableObject
    {
        public List<VisitorScheduleEntry> entries = new List<VisitorScheduleEntry>();
    }
}

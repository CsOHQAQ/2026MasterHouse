using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>日程条目（访客交付说明 §4.4 + 需求重做说明 §4.2）：第几天 / 出现时刻 / 种族 / 需求 / 具名覆写（可空）。</summary>
    [Serializable]
    public sealed class VisitorScheduleEntry
    {
        [Tooltip("第几天（从 1 开始）")] public int day = 1;
        [Tooltip("出现时刻：当天**最早**出现时刻（分钟数 0~1439）。" +
                 "前台满员或客房住满时投放会卡住等待，所以实际到场可能晚于此值（需求重做说明 §5.4）")]
        public int appearMinute = 9 * 60;
        public VisitorRaceDef race;
        [Tooltip("本次拜访的需求（必填）。为空的条目会 LogError 并跳过投放——" +
                 "新模型下没有需求的访客无事可做（需求重做说明 §4.2）")]
        public NeedDef need;
        [Tooltip("具名剧情客人覆写（可空；结构占位，运行时暂不消费，§4.4）")] public NamedVisitorDef namedOverride;
    }

    /// <summary>
    /// 访客日程表（Model 层，运行时只读；访客交付说明 §4.4 + 需求重做说明 §4.2）。单资产多行。
    /// 零随机、零上限：谁在第几天几点带什么需求出现完全由策划配；不做生成器、不做同时在场上限。
    /// 消费时按 (day, 出现时刻, 下标) 稳定排序（§11.2）；本表下标即派生种子的 scheduleIndex（§6.1），
    /// **重排/插入既有条目会改变对话抽取结果**（需求已改为配死、不再受种子影响），加内容请追加在表尾。
    /// 【务必独占本文件】ScriptableObject 必须与文件同名，否则 .asset 的脚本引用会损坏（见 RETRO）。
    /// </summary>
    [CreateAssetMenu(menuName = "MasterHouse/访客日程表", fileName = "VisitorScheduleTable")]
    public sealed class VisitorScheduleTable : ScriptableObject
    {
        public List<VisitorScheduleEntry> entries = new List<VisitorScheduleEntry>();
    }
}

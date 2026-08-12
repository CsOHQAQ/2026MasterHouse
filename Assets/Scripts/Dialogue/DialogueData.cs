using System;
using System.Collections.Generic;

namespace MasterHouse
{
    /// <summary>
    /// 对话运行时数据（ViewModel，设计说明 §4.6）。只能由 DialogueManager 修改（§11.4）。
    ///
    /// 用 Dictionary 做键查询**不违反 §11.2**——那条禁的是「依赖枚举顺序」，
    /// 这里只做查询与入队出队，从不按枚举顺序遍历（Capture 需要遍历，那里显式排序）。
    /// </summary>
    public sealed class DialogueData
    {
        /// <summary>
        /// recent 去重环：分类键 → 最近抽中过的对话组，定长 N（配在 DialogueTuningConfig）。
        /// 列表头是最旧的，超长从头丢。
        /// </summary>
        private readonly Dictionary<string, List<DialogueGroupDef>> recent =
            new Dictionary<string, List<DialogueGroupDef>>();

        /// <summary>
        /// 请求序号：派生种子的第三个分量（§6「种子 = Hash(runSeed, 访客实例Id, 触发分类, 本次请求序号)」）。
        /// 没有它，同一访客同一触发点的多次请求（典型是闲逛冒泡）会永远抽到同一条。
        /// 随存档序列化，读档后继续递增。
        /// </summary>
        public int RequestSerial;

        /// <summary>
        /// 分类键（§4.5 的八分类）。非「完成服务」触发点不带满意度档——
        /// 否则同一个分类会被拆成四份环，去重形同虚设。
        /// </summary>
        public static string CategoryKey(string raceId, EVisitorDialogueTrigger trigger, EServeSatisfaction satisfaction)
        {
            var tier = trigger == EVisitorDialogueTrigger.ServiceDone ? (int)satisfaction : 0;
            return $"{raceId}|{(int)trigger}|{tier}";
        }

        /// <summary>该分类最近是否抽过这个组。</summary>
        public bool WasRecentlyPlayed(string key, DialogueGroupDef group)
        {
            if (group == null) return false;
            return recent.TryGetValue(key, out var ring) && ring.Contains(group);
        }

        /// <summary>
        /// 记入 recent 环（**只在正常播完时调用**；中断视为这段对话没被播放，不写入，§5.2）。
        /// 环长变化（策划改配置）时按新长度裁掉最旧的。
        /// </summary>
        public void MarkPlayed(string key, DialogueGroupDef group, int ringLength)
        {
            if (group == null || ringLength <= 0) return;
            if (!recent.TryGetValue(key, out var ring))
            {
                ring = new List<DialogueGroupDef>(ringLength);
                recent[key] = ring;
            }
            ring.Remove(group); // 已在环里则挪到最新位置，避免同一组占两格
            ring.Add(group);
            while (ring.Count > ringLength) ring.RemoveAt(0);
        }

        /// <summary>清空某分类的环（§6：候选被排空时清环重筛，保证永远有话可说）。</summary>
        public void ClearRecent(string key)
        {
            if (recent.TryGetValue(key, out var ring)) ring.Clear();
        }

        /// <summary>新游戏 / GM 重置。</summary>
        public void Reset()
        {
            recent.Clear();
            RequestSerial = 0;
        }

        // ── 存档接缝占位（§4.6，待定 #9）：与 EconomyManager / VisitorManager 现有做法一致，当前无调用方 ──

        /// <summary>
        /// 导出快照（无调用方，待定 #9）。对话组按 id 存而不是资产引用——JSON 存档存不了对象引用。
        /// 遍历前按键排序：Dictionary 的枚举顺序不稳定，直接遍历会让同一状态导出不同字节（§11.2）。
        /// </summary>
        public DialogueSaveData Capture()
        {
            var data = new DialogueSaveData { requestSerial = RequestSerial };
            var keys = new List<string>(recent.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var key in keys)
            {
                var ring = recent[key];
                if (ring == null || ring.Count == 0) continue;
                var entry = new DialogueRecentSaveData { key = key };
                foreach (var group in ring)
                    if (group != null)
                        entry.groupIds.Add(group.DisplayId);
                data.recent.Add(entry);
            }
            return data;
        }

        /// <summary>
        /// 从快照恢复（无调用方，待定 #9）。
        /// resolve 负责把对话组 id 换回资产（由 DialogueManager 用已知的全部对话池构建）；
        /// 传 null 或解析不到时跳过该条——recent 环只是防重复的润色，丢了不影响正确性。
        /// </summary>
        public void Restore(DialogueSaveData data, Func<string, DialogueGroupDef> resolve)
        {
            Reset();
            if (data == null) return;
            RequestSerial = data.requestSerial;
            if (resolve == null) return;
            foreach (var entry in data.recent)
            {
                if (entry == null || string.IsNullOrEmpty(entry.key)) continue;
                var ring = new List<DialogueGroupDef>();
                foreach (var id in entry.groupIds)
                {
                    var group = resolve(id);
                    if (group != null) ring.Add(group);
                }
                if (ring.Count > 0) recent[entry.key] = ring;
            }
        }
    }

    /// <summary>对话存档快照（存档接缝占位，无调用方，待定 #9）。</summary>
    [Serializable]
    public sealed class DialogueSaveData
    {
        public int requestSerial;
        public List<DialogueRecentSaveData> recent = new List<DialogueRecentSaveData>();
    }

    /// <summary>单个分类的 recent 环快照（待定 #9）。</summary>
    [Serializable]
    public sealed class DialogueRecentSaveData
    {
        public string key;
        public List<string> groupIds = new List<string>();
    }
}

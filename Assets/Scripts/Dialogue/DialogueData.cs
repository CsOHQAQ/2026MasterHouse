using System;
using System.Collections.Generic;

namespace MasterHouse
{
    /// <summary>
    /// 对话运行时数据（ViewModel）。只能由 DialogueManager 修改（§11.4）。
    ///
    /// 用 Dictionary 做键查询**不违反 §11.2**——那条禁的是「依赖枚举顺序」，
    /// 这里只做查询与入队出队，从不按枚举顺序遍历（Capture 需要遍历，那里显式排序）。
    /// </summary>
    public sealed class DialogueData
    {
        /// <summary>
        /// recent 去重环：分类键 → 最近抽中过的对话组 id，定长 N（配在 DialogueTuningConfig）。
        /// 列表头是最旧的，超长从头丢。
        /// </summary>
        private readonly Dictionary<string, List<int>> recent = new Dictionary<string, List<int>>();

        /// <summary>
        /// 请求序号：派生种子的第三个分量。没有它，同一访客同一分类的多次请求
        /// （典型是闲聊冒泡）会永远抽到同一条。随存档序列化，读档后继续递增。
        /// </summary>
        public int RequestSerial;

        /// <summary>
        /// 分类键：种族 + 分类。**不带需求ID**——专属组与通用组的候选集本来就互不相交，
        /// 混在一个环里不会互相误伤，多一维只会让环变得稀疏、去重形同虚设。
        /// </summary>
        public static string CategoryKey(string raceId, EDialogueCategory category) =>
            $"{raceId}|{(int)category}";

        /// <summary>该分类最近是否抽过这个组。</summary>
        public bool WasRecentlyPlayed(string key, int groupId) =>
            recent.TryGetValue(key, out var ring) && ring.Contains(groupId);

        /// <summary>
        /// 记入 recent 环（**只在正常播完时调用**；中断视为这段对话没被播放，不写入）。
        /// 环长变化（策划改配置）时按新长度裁掉最旧的。
        /// </summary>
        public void MarkPlayed(string key, int groupId, int ringLength)
        {
            if (string.IsNullOrEmpty(key) || ringLength <= 0) return;
            if (!recent.TryGetValue(key, out var ring))
            {
                ring = new List<int>(ringLength);
                recent[key] = ring;
            }
            ring.Remove(groupId); // 已在环里则挪到最新位置，避免同一组占两格
            ring.Add(groupId);
            while (ring.Count > ringLength) ring.RemoveAt(0);
        }

        /// <summary>清空某分类的环（候选被排空时清环重筛，保证永远有话可说）。</summary>
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

        // ── 存档接缝占位（待定 #9）：与 EconomyManager / VisitorManager 现有做法一致，当前无调用方 ──

        /// <summary>
        /// 导出快照（无调用方，待定 #9）。对话组存 id（int），JSON 存档天然装得下。
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
                entry.groupIds.AddRange(ring);
                data.recent.Add(entry);
            }
            return data;
        }

        /// <summary>从快照恢复（无调用方，待定 #9）。</summary>
        public void Restore(DialogueSaveData data)
        {
            Reset();
            if (data == null) return;
            RequestSerial = data.requestSerial;
            foreach (var entry in data.recent)
            {
                if (entry == null || string.IsNullOrEmpty(entry.key) || entry.groupIds.Count == 0) continue;
                recent[entry.key] = new List<int>(entry.groupIds);
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
        public List<int> groupIds = new List<int>();
    }
}

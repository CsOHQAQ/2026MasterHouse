using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>一个对话组：一串按顺序播放的步骤（Excel 第二页里同一个「对话组ID」的所有行）。</summary>
    [Serializable]
    public sealed class DialogueGroup
    {
        [Tooltip("对话组ID（Excel 第二页的主键）。建议分段：1xxxx 初次见面 / 2xxxx 等待接待 / " +
                 "3xxxx 需求对话 / 4xxxx 需求反馈 / 5xxxx 闲聊——只是约定，导入器不强制")]
        public int id;

        [Tooltip("按顺序播放的步骤")]
        public List<DialogueStep> steps = new List<DialogueStep>();

        [Tooltip("来源 Excel 行号（第二页里这一组的首行），校验器报错时指路用；不参与玩法")]
        public int sourceRow;

        public string DisplayId => id.ToString();
    }

    /// <summary>
    /// 对话池的一条挂载（Excel 第一页的一行）：把一个对话组挂到「某种族 · 某分类 · 某需求」下。
    ///
    /// 同一个组可以出现多行（挂进多个分类、或分给多个种族）——第一页里写多行即可。
    /// 「种族 = 通用」在导入期就展开成每个种族一行，运行时不再有通配逻辑。
    /// </summary>
    [Serializable]
    public sealed class DialoguePoolEntry
    {
        public int groupId;

        [Tooltip("种族 id（VisitorRaceDef.raceId）。Excel 里写 `通用` 或 `/` 多选时，导入期已展开成逐个种族")]
        public string raceId;

        [Tooltip("需求资产名（如 Need_修理电路）。空 = 不挑需求。\n" +
                 "【需求对话】必填；四档反馈选填（填了的专属优先，没有专属才用留空的通用组）；其余分类应留空")]
        public string needId;

        public EDialogueCategory category;

        [Tooltip("进入候选的条件，多条 AND。留空 = 无条件参与抽取")]
        public List<DialogueCall> conditions = new List<DialogueCall>();

        [Tooltip("来源 Excel 行号（第一页），校验器报错时指路用；不参与玩法")]
        public int sourceRow;
    }

    /// <summary>
    /// 对话整表（Model 层，运行时只读）。**唯一数据源是 Excel/对话表.xlsx**：
    /// 导表 → Assets/Configs/对话组表.csv + 对话内容表.csv → DialogueCsvImporter 整表重建本资产。
    /// Inspector 里的手改会在下次导表时被覆盖，别在这里改内容。
    ///
    /// 2026-08-14 重构：从「一个对话组一个 SO 资产 + 一个种族一个对话池资产」改成这一张整表。
    /// 原来选散资产的三条理由（diff 友好、多人并行不冲突、可拖拽引用与查找引用）在
    /// 「Excel 成唯一源、对话编辑器退役」之后全部失效，而散资产要付的代价是实打实的：
    /// Excel 删一行就留一个孤儿 .asset、改一次 ID 就多一份垃圾、上百个组就是上百对 .asset/.meta。
    /// 整表重建把这些问题一次性消掉，也和家具表/商店表/音效表统一了心智模型。
    ///
    /// 【务必独占本文件】ScriptableObject 必须与文件同名，否则 .asset 的脚本引用会损坏（见 RETRO）。
    /// </summary>
    [CreateAssetMenu(menuName = "MasterHouse/对话整表", fileName = "DialogueTable")]
    public sealed class DialogueTable : ScriptableObject
    {
        [Tooltip("全部对话组（来自 Excel 第二页「对话内容」）")]
        public List<DialogueGroup> groups = new List<DialogueGroup>();

        [Tooltip("全部池挂载（来自 Excel 第一页「对话组」，通用种族已展开）")]
        public List<DialoguePoolEntry> entries = new List<DialoguePoolEntry>();

        // ── 运行时索引（首次访问时惰性构建；导表重建资产后靠 OnEnable 清空重来）──
        //
        // 字典只做 key 查询、从不按枚举顺序遍历（候选列表的顺序直接来自 entries 的资产顺序 = Excel 行序，
        // 稳定可复现），不违反确定性守则 §11.2。

        [NonSerialized] private Dictionary<int, DialogueGroup> groupById;
        [NonSerialized] private Dictionary<string, List<DialoguePoolEntry>> entriesByKey;

        private void OnEnable() => InvalidateIndex();

        /// <summary>导入器整表重建之后调用一次，丢弃旧索引。</summary>
        public void InvalidateIndex()
        {
            groupById = null;
            entriesByKey = null;
        }

        /// <summary>按 id 取对话组；不存在返回 null。</summary>
        public DialogueGroup GroupOf(int id)
        {
            BuildIndex();
            return groupById.TryGetValue(id, out var group) ? group : null;
        }

        /// <summary>
        /// 取某种族某分类下的全部挂载（顺序 = Excel 行序）。需求筛选与条件筛选由 DialogueManager 做，
        /// 因为那两步需要运行时上下文。没有内容时返回空列表而不是 null。
        /// </summary>
        public List<DialoguePoolEntry> EntriesOf(string raceId, EDialogueCategory category)
        {
            BuildIndex();
            return entriesByKey.TryGetValue(IndexKey(raceId, category), out var list) ? list : Empty;
        }

        private static readonly List<DialoguePoolEntry> Empty = new List<DialoguePoolEntry>();

        private static string IndexKey(string raceId, EDialogueCategory category) =>
            $"{raceId}|{(int)category}";

        private void BuildIndex()
        {
            if (groupById != null) return;

            groupById = new Dictionary<int, DialogueGroup>();
            foreach (var group in groups)
            {
                if (group == null) continue;
                if (groupById.ContainsKey(group.id))
                {
                    Debug.LogError($"[对话表] 对话组ID {group.id} 重复，后一条被忽略；" +
                                   "请在 Excel 第二页里改掉重复的 ID", this);
                    continue;
                }
                groupById[group.id] = group;
            }

            entriesByKey = new Dictionary<string, List<DialoguePoolEntry>>();
            foreach (var entry in entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.raceId)) continue;
                var key = IndexKey(entry.raceId, entry.category);
                if (!entriesByKey.TryGetValue(key, out var list))
                {
                    list = new List<DialoguePoolEntry>();
                    entriesByKey[key] = list;
                }
                list.Add(entry);
            }
        }
    }
}

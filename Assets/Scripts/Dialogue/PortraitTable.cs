using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>立绘索引表的一行（Excel/立绘表.xlsx 的一行）：立绘ID → 资源路径。</summary>
    [Serializable]
    public sealed class PortraitEntry
    {
        [Tooltip("立绘ID（主键）。对话表第二页的「立绘ID」列、访客种族表的「默认立绘ID」列填的就是它。\n" +
                 "不强制命名规范——差分本来就归不了类，这正是 2026-08-14 撤掉表情枚举的原因。\n" +
                 "唯一的硬约束是不能带逗号（CSV 分隔符）")]
        public string portraitId;

        [Tooltip("Resources 相对路径（不带扩展名），如 OutGameUI/Guests/fox。\n" +
                 "**素材必须位于某个 Resources 目录下**，运行时靠 Resources.Load<Texture2D> 取图")]
        public string path;

        [Tooltip("备注，仅供策划阅读；导入器原样存下，不参与玩法")]
        public string note;

        [Tooltip("来源 Excel 行号，报错时指路用；不参与玩法")]
        public int sourceRow;
    }

    /// <summary>
    /// 立绘索引整表（Model 层，运行时只读）。**唯一数据源是 Excel/立绘表.xlsx**：
    /// 导表 → Assets/Configs/立绘表.csv → PortraitCsvImporter 整表重建本资产。
    /// Inspector 里的手改会在下次导表时被覆盖。
    ///
    /// 2026-08-14 立绘 ID 化：原先立绘住在 VisitorRaceDef.portraits 上，是一列
    /// 「表情=路径/表情=路径」的双层分隔字符串，索引键是硬编码的 EDialogueEmotion 五项枚举。
    /// 美术要求「更多差分、且不太好归类」之后，枚举这条路走不通了——归不了类的东西不该有枚举。
    /// 现在退回最朴素的形式：一张 ID → 路径的索引表，谁都能引用它，加差分 = 加一行。
    ///
    /// 表里**不区分角色**：`fox_平静` 和 `老板娘_惊讶` 在这张表里是平等的两行。
    /// 「谁默认长什么样」是访客种族表的事（默认立绘ID 列），「这句话配哪张脸」是对话表的事。
    ///
    /// 【务必独占本文件】ScriptableObject 必须与文件同名，否则 .asset 的脚本引用会损坏（见 RETRO）。
    /// </summary>
    [CreateAssetMenu(menuName = "MasterHouse/立绘索引表", fileName = "PortraitTable")]
    public sealed class PortraitTable : ScriptableObject
    {
        [Tooltip("全部立绘（来自 Excel/立绘表.xlsx，行序 = Excel 行序）")]
        public List<PortraitEntry> entries = new List<PortraitEntry>();

        // 惰性索引；导表重建资产后靠 OnEnable 清空重来（口径与 DialogueTable 一致）
        [NonSerialized] private Dictionary<string, PortraitEntry> byId;

        private void OnEnable() => InvalidateIndex();

        /// <summary>导入器整表重建之后调用一次，丢弃旧索引。</summary>
        public void InvalidateIndex() => byId = null;

        /// <summary>立绘ID 是否存在（导表期校验用）。</summary>
        public bool Contains(string portraitId)
        {
            BuildIndex();
            return !string.IsNullOrEmpty(portraitId) && byId.ContainsKey(portraitId);
        }

        /// <summary>
        /// 取立绘的 Resources 路径；ID 为空或查不到时返回空串。
        ///
        /// **查不到不打日志**：导表期已经硬校验过「对话表引用的立绘ID 必须在本表里」，
        /// 运行时还能查不到只剩两种情况——表没导（那 Console 里早有导表错误）、
        /// 或调用方传了空串（那是「这句不换立绘」的正常表达）。两种都不该再刷屏。
        /// </summary>
        public string PathOf(string portraitId)
        {
            BuildIndex();
            if (string.IsNullOrEmpty(portraitId)) return string.Empty;
            return byId.TryGetValue(portraitId, out var entry) ? entry.path : string.Empty;
        }

        /// <summary>取立绘贴图；ID 为空或查不到时返回 null，由调用方决定怎么表现（通常是不显示立绘）。</summary>
        public Texture2D TextureOf(string portraitId)
        {
            var path = PathOf(portraitId);
            return string.IsNullOrEmpty(path) ? null : Resources.Load<Texture2D>(path);
        }

        private void BuildIndex()
        {
            if (byId != null) return;
            var map = new Dictionary<string, PortraitEntry>();
            if (entries != null)
                foreach (var entry in entries)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.portraitId)) continue;
                    if (map.ContainsKey(entry.portraitId))
                    {
                        Debug.LogError($"[立绘表] 立绘ID「{entry.portraitId}」重复，后一条被忽略；" +
                                       "请在 Excel/立绘表.xlsx 里改掉重复的 ID", this);
                        continue;
                    }
                    map[entry.portraitId] = entry;
                }
            byId = map;
        }
    }
}

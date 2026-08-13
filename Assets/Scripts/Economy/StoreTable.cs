using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>商店表中的一行：一件家具的售卖配置。</summary>
    [Serializable]
    public sealed class StoreEntry
    {
        [Tooltip("对应 FurnitureTable 的家具 id")] public string furnitureId;
        [Tooltip("购买价格（货币）；0 = 初始拥有")] public int price;
        [Tooltip("解禁所需声望；声望不足时在商城/收纳栏呈「？」")] public int unlockReputation;
    }

    /// <summary>
    /// 商店表（2026-08-13 从家具表拆出）：家具的**售卖配置**独立成表，
    /// 家具表只管物理与展示属性，改价格/解禁不动家具表（§16.6 改内容 = 改资产）。
    ///
    /// 按 furnitureId 关联家具表。**不在本表里的家具 = 非卖品**，等价于价格 0 / 解禁 0，
    /// 即「初始就拥有、不需要买」——这条兜底让漏配不会把家具锁死。
    /// 读取一律经 EconomyManager.PriceOf / UnlockReputationOf（View 不直接摸表，§11.4）。
    /// 【务必独占本文件】ScriptableObject 必须与文件同名，否则 .asset 的脚本引用会损坏（见 RETRO）。
    /// </summary>
    [CreateAssetMenu(menuName = "MasterHouse/商店表", fileName = "StoreTable")]
    public sealed class StoreTable : ScriptableObject
    {
        public List<StoreEntry> entries = new List<StoreEntry>();

        public StoreEntry Find(string furnitureId)
        {
            if (string.IsNullOrEmpty(furnitureId)) return null;
            for (var i = 0; i < entries.Count; i++)
                if (entries[i] != null && entries[i].furnitureId == furnitureId) return entries[i];
            return null;
        }

        /// <summary>售价；不在表里（非卖品）按 0 处理。</summary>
        public int PriceOf(string furnitureId)
        {
            var entry = Find(furnitureId);
            return entry != null ? entry.price : 0;
        }

        /// <summary>解禁声望；不在表里（非卖品）按 0 处理（即始终可见）。</summary>
        public int UnlockReputationOf(string furnitureId)
        {
            var entry = Find(furnitureId);
            return entry != null ? entry.unlockReputation : 0;
        }
    }
}

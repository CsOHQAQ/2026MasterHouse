using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterPotion
{
    /// <summary>家具购买结果。</summary>
    public enum FurniturePurchaseResult
    {
        Success,
        AlreadyOwned,
        /// <summary>声望未达到解禁阈值（商城/图鉴中呈「？」）。</summary>
        ReputationLocked,
        NotEnoughCurrency,
    }

    /// <summary>流通数值的存档快照（随局外存档槽位一起序列化）。</summary>
    [Serializable]
    public sealed class HouseEconomySaveData
    {
        public int currency;
        public int reputation;
        public int gmDecorationBonus;
        public List<string> ownedFurniture = new List<string>();
    }

    /// <summary>
    /// 流通数值服务（单一数据源）：货币、玩家声望、House 装饰分，以及装饰品所有权。
    /// 商城面板与家具摆放模式都从这里读写；存档系统通过 Capture/Restore 快照接入。
    /// </summary>
    public static class HouseEconomy
    {
        private const string ConfigPath = "OutGameUI/HouseEconomyConfig";
        private const string FurnitureTablePath = "OutGameUI/FurnitureTable";
        private const string RoomTablePath = "OutGameUI/FurnitureRoomTable";

        private static HouseEconomyConfig config;
        private static bool initialized;
        private static int currency;
        private static int reputation;
        private static int furnitureDecorScore;
        private static int gmDecorationBonus;
        private static int roomCount;
        private static int deviceCount;
        private static readonly HashSet<string> ownedFurniture = new HashSet<string>();

        /// <summary>任一数值变化后触发（UI 刷新用）。</summary>
        public static event Action Changed;

        private static void EnsureInit()
        {
            if (initialized) return;
            initialized = true;
            config = Resources.Load<HouseEconomyConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<HouseEconomyConfig>();
                Debug.LogWarning("[HouseEconomy] 流通数值配置缺失，使用内置默认值。请执行菜单 MasterPotion → 家具系统 → 创建配置表。");
            }
            currency = config.startCurrency;
            reputation = config.startReputation;
            roomCount = OutGameUIData.Rooms.Length;
            deviceCount = CountOwnedDevices();

            var table = Resources.Load<FurnitureTable>(FurnitureTablePath);
            if (table != null)
                foreach (var entry in table.entries)
                    if (entry != null && entry.price <= 0) ownedFurniture.Add(entry.id);
            furnitureDecorScore = ComputeInitialFurnitureDecor(table);
        }

        private static int CountOwnedDevices()
        {
            var count = 0;
            foreach (var room in OutGameUIData.Devices)
                foreach (var device in room)
                {
                    var parts = device.Split('|');
                    if (parts.Length > 3 && parts[3] == "1") count++;
                }
            return count;
        }

        /// <summary>家具模式尚未打开时，用房间表初始摆放估算装饰品得分基线。</summary>
        private static int ComputeInitialFurnitureDecor(FurnitureTable table)
        {
            var rooms = Resources.Load<FurnitureRoomTable>(RoomTablePath);
            if (table == null || rooms == null || rooms.rooms.Count == 0 || rooms.rooms[0] == null) return 0;
            var sum = 0;
            foreach (var placement in rooms.rooms[0].initialPlacements)
            {
                var entry = placement == null ? null : table.Find(placement.furnitureId);
                if (entry != null) sum += entry.decorationScore;
            }
            return sum;
        }

        public static int Currency { get { EnsureInit(); return currency; } }
        public static int Reputation { get { EnsureInit(); return reputation; } }

        /// <summary>House 装饰分 = 房间数量 × 权重 + 设备 × 权重 + 已摆放装饰品得分。无去处。</summary>
        public static int DecorationScore
        {
            get
            {
                EnsureInit();
                return roomCount * config.decorScorePerRoom + deviceCount * config.decorScorePerDevice
                       + furnitureDecorScore + gmDecorationBonus;
            }
        }

        public static int ServiceCurrencyReward { get { EnsureInit(); return config.serviceCurrencyReward; } }
        public static int ServiceReputationReward { get { EnsureInit(); return config.serviceReputationReward; } }
        public static int RefuseReputationPenalty { get { EnsureInit(); return config.refuseReputationPenalty; } }
        public static int FailReputationPenalty { get { EnsureInit(); return config.failReputationPenalty; } }

        /// <summary>货币+声望来源：完成一次客人服务。</summary>
        public static void CompleteGuestService()
        {
            EnsureInit();
            currency += config.serviceCurrencyReward;
            reputation += config.serviceReputationReward;
            RaiseChanged();
        }

        /// <summary>声望去处：拒绝服务客人。</summary>
        public static void RefuseGuestService()
        {
            EnsureInit();
            reputation = Mathf.Max(0, reputation - config.refuseReputationPenalty);
            RaiseChanged();
        }

        /// <summary>声望去处：周结算时未完成的客人服务。</summary>
        public static void FailGuestServices(int count)
        {
            if (count <= 0) return;
            EnsureInit();
            reputation = Mathf.Max(0, reputation - config.failReputationPenalty * count);
            RaiseChanged();
        }

        public static bool IsFurnitureOwned(string furnitureId)
        {
            EnsureInit();
            return ownedFurniture.Contains(furnitureId);
        }

        /// <summary>声望是否已达到 Item 的解禁阈值（未达到时商城/收纳栏呈「？」）。</summary>
        public static bool IsFurnitureRevealed(FurnitureEntry entry)
        {
            EnsureInit();
            return entry != null && reputation >= entry.unlockReputation;
        }

        /// <summary>货币去处：购买装饰品。解禁（声望）与购买（货币）是两道独立的门。</summary>
        public static FurniturePurchaseResult TryPurchaseFurniture(FurnitureEntry entry)
        {
            EnsureInit();
            if (entry == null || ownedFurniture.Contains(entry.id)) return FurniturePurchaseResult.AlreadyOwned;
            if (reputation < entry.unlockReputation) return FurniturePurchaseResult.ReputationLocked;
            if (currency < entry.price) return FurniturePurchaseResult.NotEnoughCurrency;
            currency -= entry.price;
            ownedFurniture.Add(entry.id);
            RaiseChanged();
            return FurniturePurchaseResult.Success;
        }

        /// <summary>装饰分来源：家具摆放变化后由家具模式回写当前摆放的装饰品得分总和。</summary>
        public static void SetFurnitureDecorationScore(int score)
        {
            EnsureInit();
            if (furnitureDecorScore == score) return;
            furnitureDecorScore = score;
            RaiseChanged();
        }

        /// <summary>导出存档快照。</summary>
        public static HouseEconomySaveData Capture()
        {
            EnsureInit();
            var data = new HouseEconomySaveData
            {
                currency = currency,
                reputation = reputation,
                gmDecorationBonus = gmDecorationBonus,
            };
            data.ownedFurniture.AddRange(ownedFurniture);
            return data;
        }

        /// <summary>从存档快照恢复；data 为 null 时重置为配置表默认值。</summary>
        public static void Restore(HouseEconomySaveData data)
        {
            if (data == null)
            {
                ResetToDefaults();
                return;
            }
            EnsureInit();
            currency = Mathf.Max(0, data.currency);
            reputation = Mathf.Max(0, data.reputation);
            gmDecorationBonus = Mathf.Max(0, data.gmDecorationBonus);
            ownedFurniture.Clear();
            if (data.ownedFurniture != null)
                foreach (var id in data.ownedFurniture)
                    if (!string.IsNullOrEmpty(id)) ownedFurniture.Add(id);
            // price<=0 的基础家具始终视为拥有，防止改表或旧档缺失后丢家具
            var table = Resources.Load<FurnitureTable>(FurnitureTablePath);
            if (table != null)
                foreach (var entry in table.entries)
                    if (entry != null && entry.price <= 0) ownedFurniture.Add(entry.id);
            RaiseChanged();
        }

        /// <summary>重置为配置表初始状态（新游戏 / 读旧版本存档时调用）。</summary>
        public static void ResetToDefaults()
        {
            initialized = false;
            ownedFurniture.Clear();
            gmDecorationBonus = 0;
            furnitureDecorScore = 0;
            EnsureInit();
            RaiseChanged();
        }

        /// <summary>GM：增减货币（下限 0）。</summary>
        public static void GmAddCurrency(int amount)
        {
            EnsureInit();
            currency = Mathf.Max(0, currency + amount);
            RaiseChanged();
        }

        /// <summary>GM：增减声望（下限 0）。声望变化会实时影响 Item 解禁状态。</summary>
        public static void GmAddReputation(int amount)
        {
            EnsureInit();
            reputation = Mathf.Max(0, reputation + amount);
            RaiseChanged();
        }

        /// <summary>GM：增减装饰分加成项（装饰分本身是派生值，GM 只操作独立加成，下限 0）。</summary>
        public static void GmAddDecorationBonus(int amount)
        {
            EnsureInit();
            gmDecorationBonus = Mathf.Max(0, gmDecorationBonus + amount);
            RaiseChanged();
        }

        private static void RaiseChanged() => Changed?.Invoke();
    }
}

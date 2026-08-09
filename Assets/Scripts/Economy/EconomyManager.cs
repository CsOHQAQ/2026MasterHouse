using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 流通数值逻辑（§16.3，旧 HouseEconomy 静态服务平移）：货币/声望/装饰分与家具所有权的唯一修改入口，
    /// 商城面板与家具摆放模式都从这里读写。纯事件驱动、不挂 tick（§16.4 只要求时钟与访客业务上 tick）。
    /// 与旧实现的唯一结构差异：不再反向读 OutGameUIData（§16.7 毒点①），房间/设备数量由内容侧推入。
    /// 家具表/房间表仍在此加载（SO 配置属 Model 层，方向正常；并入 Def 体系是 3.8）。
    /// </summary>
    public class EconomyManager
    {
        private const string ConfigPath = "OutGameUI/HouseEconomyConfig";
        private const string FurnitureTablePath = "OutGameUI/FurnitureTable";
        private const string RoomTablePath = "OutGameUI/FurnitureRoomTable";

        private readonly EconomyConfig config;

        public EconomyData Data { get; } = new EconomyData();

        /// <summary>任一数值变化后触发（§2.1：玩家操作产生的离散变化由 Manager 广播，UI 刷新用）。</summary>
        public event Action Changed;

        public EconomyManager()
        {
            var loaded = Resources.Load<EconomyConfig>(ConfigPath);
            if (loaded == null)
            {
                loaded = ScriptableObject.CreateInstance<EconomyConfig>();
                Debug.LogWarning("[EconomyManager] 流通数值配置缺失，使用内置默认值。请执行菜单 MasterHouse → 家具系统 → 创建配置表。");
            }
            config = loaded;
            ApplyDefaults();
        }

        /// <summary>House 装饰分 = 房间数量 × 权重 + 设备 × 权重 + 已摆放装饰品得分 + GM 加成。纯展示派生值，无去处。</summary>
        public int DecorationScore =>
            Data.RoomCount * config.decorScorePerRoom + Data.DeviceCount * config.decorScorePerDevice
            + Data.FurnitureDecorScore + Data.GmDecorationBonus;

        public int ServiceCurrencyReward => config.serviceCurrencyReward;
        public int ServiceReputationReward => config.serviceReputationReward;
        public int RefuseReputationPenalty => config.refuseReputationPenalty;
        public int FailReputationPenalty => config.failReputationPenalty;

        /// <summary>装饰分构成项的数量来源（§16.7 毒点①已断）：由内容侧推入，3.3 内容 Def 化后改由 Def 资产统计。</summary>
        public void SetDecorSourceCounts(int roomCount, int deviceCount)
        {
            if (Data.RoomCount == roomCount && Data.DeviceCount == deviceCount) return;
            Data.RoomCount = roomCount;
            Data.DeviceCount = deviceCount;
            RaiseChanged();
        }

        /// <summary>货币+声望来源：完成一次客人服务。</summary>
        public void CompleteGuestService()
        {
            Data.Currency += config.serviceCurrencyReward;
            Data.Reputation += config.serviceReputationReward;
            RaiseChanged();
        }

        /// <summary>声望去处：拒绝服务客人。</summary>
        public void RefuseGuestService()
        {
            Data.Reputation = Mathf.Max(0, Data.Reputation - config.refuseReputationPenalty);
            RaiseChanged();
        }

        /// <summary>声望去处：周结算时未完成的客人服务。</summary>
        public void FailGuestServices(int count)
        {
            if (count <= 0) return;
            Data.Reputation = Mathf.Max(0, Data.Reputation - config.failReputationPenalty * count);
            RaiseChanged();
        }

        public bool IsFurnitureOwned(string furnitureId) => Data.OwnedFurniture.Contains(furnitureId);

        /// <summary>声望是否已达到 Item 的解禁阈值（未达到时商城/收纳栏呈「？」）。</summary>
        public bool IsFurnitureRevealed(FurnitureEntry entry) => entry != null && Data.Reputation >= entry.unlockReputation;

        /// <summary>货币去处：购买装饰品。解禁（声望）与购买（货币）是两道独立的门。</summary>
        public FurniturePurchaseResult TryPurchaseFurniture(FurnitureEntry entry)
        {
            if (entry == null || Data.OwnedFurniture.Contains(entry.id)) return FurniturePurchaseResult.AlreadyOwned;
            if (Data.Reputation < entry.unlockReputation) return FurniturePurchaseResult.ReputationLocked;
            if (Data.Currency < entry.price) return FurniturePurchaseResult.NotEnoughCurrency;
            Data.Currency -= entry.price;
            Data.OwnedFurniture.Add(entry.id);
            RaiseChanged();
            return FurniturePurchaseResult.Success;
        }

        /// <summary>装饰分来源：家具摆放变化后由家具模式回写当前摆放的装饰品得分总和。</summary>
        public void SetFurnitureDecorationScore(int score)
        {
            if (Data.FurnitureDecorScore == score) return;
            Data.FurnitureDecorScore = score;
            RaiseChanged();
        }

        /// <summary>导出存档快照（过渡：旧存档 v3 经济段，待定 #9）。列表按 id 排序，序列化结果稳定（§11.2）。</summary>
        public EconomySaveData Capture()
        {
            var data = new EconomySaveData
            {
                currency = Data.Currency,
                reputation = Data.Reputation,
                gmDecorationBonus = Data.GmDecorationBonus,
            };
            data.ownedFurniture.AddRange(Data.OwnedFurniture);
            data.ownedFurniture.Sort(string.CompareOrdinal);
            return data;
        }

        /// <summary>从存档快照恢复；data 为 null 时重置为配置表默认值。</summary>
        public void Restore(EconomySaveData data)
        {
            if (data == null)
            {
                ResetToDefaults();
                return;
            }
            Data.Currency = Mathf.Max(0, data.currency);
            Data.Reputation = Mathf.Max(0, data.reputation);
            Data.GmDecorationBonus = Mathf.Max(0, data.gmDecorationBonus);
            Data.OwnedFurniture.Clear();
            if (data.ownedFurniture != null)
                foreach (var id in data.ownedFurniture)
                    if (!string.IsNullOrEmpty(id)) Data.OwnedFurniture.Add(id);
            // price<=0 的基础家具始终视为拥有，防止改表或旧档缺失后丢家具
            AddFreeFurniture();
            RaiseChanged();
        }

        /// <summary>重置为配置表初始状态（新游戏 / 读旧版本存档 / GM 重置）。</summary>
        public void ResetToDefaults()
        {
            ApplyDefaults();
            RaiseChanged();
        }

        /// <summary>GM：增减货币（下限 0）。</summary>
        public void GmAddCurrency(int amount)
        {
            Data.Currency = Mathf.Max(0, Data.Currency + amount);
            RaiseChanged();
        }

        /// <summary>GM：增减声望（下限 0）。声望变化会实时影响 Item 解禁状态。</summary>
        public void GmAddReputation(int amount)
        {
            Data.Reputation = Mathf.Max(0, Data.Reputation + amount);
            RaiseChanged();
        }

        /// <summary>GM：增减装饰分加成项（装饰分本身是派生值，GM 只操作独立加成，下限 0）。</summary>
        public void GmAddDecorationBonus(int amount)
        {
            Data.GmDecorationBonus = Mathf.Max(0, Data.GmDecorationBonus + amount);
            RaiseChanged();
        }

        /// <summary>回配置表初始状态。房间/设备数量是内容统计而非游玩进度，保留已推入的值不清。</summary>
        private void ApplyDefaults()
        {
            Data.Currency = config.startCurrency;
            Data.Reputation = config.startReputation;
            Data.GmDecorationBonus = 0;
            Data.OwnedFurniture.Clear();
            var table = AddFreeFurniture();
            Data.FurnitureDecorScore = ComputeInitialFurnitureDecor(table);
        }

        /// <summary>把 price<=0 的基础家具补进所有权集合，返回加载到的家具表（可能为 null）。</summary>
        private FurnitureTable AddFreeFurniture()
        {
            var table = Resources.Load<FurnitureTable>(FurnitureTablePath);
            if (table != null)
                foreach (var entry in table.entries)
                    if (entry != null && entry.price <= 0) Data.OwnedFurniture.Add(entry.id);
            return table;
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

        private void RaiseChanged() => Changed?.Invoke();
    }
}
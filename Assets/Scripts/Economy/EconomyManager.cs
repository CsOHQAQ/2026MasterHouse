using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>玩法收支的正负向语义（音效需求 #4）：表现层据此挑正向/负向提示音，本枚举不进存档。</summary>
    public enum EEconomyFeedback
    {
        CurrencyGain,
        CurrencyLoss,
        ReputationGain,
        ReputationLoss,
    }

    /// <summary>
    /// 流通数值逻辑（§16.3，旧 HouseEconomy 静态服务平移）：货币/声望/装饰分与家具所有权的唯一修改入口，
    /// 商城面板与家具摆放模式都从这里读写。纯事件驱动、不挂 tick（§16.4 只要求时钟与访客业务上 tick）。
    /// 与旧实现的唯一结构差异：不再反向读 OutGameUIData（§16.7 毒点①），房间/设备数量改由 CodexTable（Def 资产）统计。
    /// 家具表/商店表/房间表仍在此加载（SO 配置属 Model 层，方向正常；并入 Def 体系是 3.8）。
    /// </summary>
    public class EconomyManager
    {
        private const string ConfigPath = "OutGameUI/HouseEconomyConfig";

        private readonly EconomyConfig config;
        private readonly FurnitureTable furnitureTable;
        private readonly FurnitureRoomTable roomTable;
        /// <summary>售卖配置（价格/解禁声望，2026-08-13 从家具表拆出）；缺失时全部按非卖品处理。</summary>
        private readonly StoreTable storeTable;

        public EconomyData Data { get; } = new EconomyData();

        /// <summary>任一数值变化后触发（§2.1：玩家操作产生的离散变化由 Manager 广播，UI 刷新用）。</summary>
        public event Action Changed;

        /// <summary>
        /// 玩法收支的正负向提示（音效需求 #4，SfxManager 订阅）：只在「玩家应当感知到得失」的收支处广播——
        /// 服务结算/拒绝、对话奖励走这里；购买（反馈由商城的获得弹窗与其音效承担）、GM 后门（调试静音）、
        /// 装饰分回写（与家具摆放音重叠）**刻意不发**。
        /// </summary>
        public event Action<EEconomyFeedback> Feedback;

        public EconomyManager(CodexTable codex, FurnitureTable furnitureTable, FurnitureRoomTable roomTable,
            StoreTable storeTable)
        {
            this.furnitureTable = furnitureTable;
            this.roomTable = roomTable;
            this.storeTable = storeTable;
            var loaded = Resources.Load<EconomyConfig>(ConfigPath);
            if (loaded == null)
            {
                loaded = ScriptableObject.CreateInstance<EconomyConfig>();
                Debug.LogWarning("[EconomyManager] 流通数值配置缺失，使用内置默认值。请执行菜单 MasterHouse → 家具系统 → 创建配置表。");
            }
            config = loaded;
            ApplyDefaults();

            // 装饰分构成项的数量来源 = Def 资产统计（§16.7 毒点①的最终形态）；内容表运行时只读，统计一次即可
            if (codex != null)
            {
                Data.RoomCount = codex.rooms.Count;
                Data.DeviceCount = codex.CountOwnedDevices();
            }
        }

        /// <summary>House 装饰分 = 房间数量 × 权重 + 设备 × 权重 + 已摆放装饰品得分 + GM 加成。纯展示派生值，无去处。</summary>
        public int DecorationScore =>
            Data.RoomCount * config.decorScorePerRoom + Data.DeviceCount * config.decorScorePerDevice
            + Data.FurnitureDecorScore + Data.GmDecorationBonus;

        /// <summary>前台拒绝的声望惩罚（在「前台等待接待」谢客 / 等搭话超时，§5.2）。</summary>
        public int RefuseReputationPenalty => config.refuseReputationPenalty;

        /// <summary>服务中拒绝的声望惩罚（已接待后反悔 / 等交货超时，§5.2）。交付页按钮上要写明这个数。</summary>
        public int ServiceFailedReputationPenalty => config.serviceFailedReputationPenalty;

        /// <summary>按满意度档取奖励配置（UI 预览展示用）。</summary>
        public SatisfactionReward RewardFor(EServeSatisfaction satisfaction) => config.RewardFor(satisfaction);

        /// <summary>
        /// 货币来源：访客离场时留下的基础金钱（需求重做说明 §8）。
        /// **所有业务访客都给，包括被拒绝与超时流失的**——这是新模型下「不会陷入没钱死循环」的保证，
        /// 未满足需求只是拿不到 CompleteGuestService 的额外奖励，不扣钱。
        /// 返回实际入账值（0 表示策划把这项配成了 0，调用方不必再发反馈）。
        /// </summary>
        public int GuestLeaveTip()
        {
            var amount = Mathf.Max(0, config.guestLeaveTip);
            if (amount == 0) return 0;
            Data.Currency += amount;
            RaiseChanged();
            Feedback?.Invoke(EEconomyFeedback.CurrencyGain);
            return amount;
        }

        /// <summary>货币+声望来源：完成一次客人服务，按满意度四档结算（§6.2）。返回实际入账值（结算文案用）。</summary>
        public (int currency, int reputation) CompleteGuestService(EServeSatisfaction satisfaction)
        {
            var reward = config.RewardFor(satisfaction);
            Data.Currency += reward.currency;
            Data.Reputation += reward.reputation;
            RaiseChanged();
            Feedback?.Invoke(EEconomyFeedback.ReputationGain); // 货币声望同笔结算，正向提示只发一次
            return (reward.currency, reward.reputation);
        }

        /// <summary>
        /// 声望去处：在前台谢客（含等搭话超时，§5.2 前台档）。
        /// 服务中反悔走 RefuseServingGuest——那一档扣得更多，别把两者混用。
        /// </summary>
        public void RefuseGuestService()
        {
            Data.Reputation = Mathf.Max(0, Data.Reputation - config.refuseReputationPenalty);
            RaiseChanged();
            Feedback?.Invoke(EEconomyFeedback.ReputationLoss);
        }

        /// <summary>
        /// 声望去处：接待之后再拒绝（含等交货超时，§5.2 服务中档）。
        /// 下限口径与前台档一致（Mathf.Max(0, …)），只是数值更重。
        /// </summary>
        public void RefuseServingGuest()
        {
            Data.Reputation = Mathf.Max(0, Data.Reputation - config.serviceFailedReputationPenalty);
            RaiseChanged();
        }

        /// <summary>
        /// 货币增减（下限 0）：对话奖励事件等玩法入口（对话设计说明 §7）。
        /// 与 GmAddCurrency 分开是语义问题——GM 是调试后门，本方法是正经玩法收支，
        /// 将来要加日志/成就统计/数值埋点时只该挂在这一侧。
        /// </summary>
        public void AddCurrency(int amount)
        {
            if (amount == 0) return;
            Data.Currency = Mathf.Max(0, Data.Currency + amount);
            RaiseChanged();
            Feedback?.Invoke(amount > 0 ? EEconomyFeedback.CurrencyGain : EEconomyFeedback.CurrencyLoss);
        }

        /// <summary>声望增减（下限 0）：对话奖励事件等玩法入口。声望变化会实时影响 Item 解禁状态。</summary>
        public void AddReputation(int amount)
        {
            if (amount == 0) return;
            Data.Reputation = Mathf.Max(0, Data.Reputation + amount);
            RaiseChanged();
            Feedback?.Invoke(amount > 0 ? EEconomyFeedback.ReputationGain : EEconomyFeedback.ReputationLoss);
        }

        public bool IsFurnitureOwned(string furnitureId) => Data.OwnedFurniture.Contains(furnitureId);

        /// <summary>
        /// 家具售价（商店表，2026-08-13 拆表后的唯一读取口）。表缺失或家具不在表里 = 非卖品 → 0。
        /// 表现层要显示价格/解禁门槛一律走本方法与 <see cref="UnlockReputationOf"/>，不直接摸表（§11.4）。
        /// </summary>
        public int PriceOf(FurnitureEntry entry) =>
            entry != null && storeTable != null ? storeTable.PriceOf(entry.id) : 0;

        /// <summary>家具解禁所需声望（商店表）。表缺失或家具不在表里 = 非卖品 → 0（始终可见）。</summary>
        public int UnlockReputationOf(FurnitureEntry entry) =>
            entry != null && storeTable != null ? storeTable.UnlockReputationOf(entry.id) : 0;

        /// <summary>声望是否已达到 Item 的解禁阈值（未达到时商城/收纳栏呈「？」）。</summary>
        public bool IsFurnitureRevealed(FurnitureEntry entry) => entry != null && Data.Reputation >= UnlockReputationOf(entry);

        /// <summary>货币去处：购买装饰品。解禁（声望）与购买（货币）是两道独立的门。</summary>
        public FurniturePurchaseResult TryPurchaseFurniture(FurnitureEntry entry)
        {
            if (entry == null || Data.OwnedFurniture.Contains(entry.id)) return FurniturePurchaseResult.AlreadyOwned;
            var price = PriceOf(entry);
            if (Data.Reputation < UnlockReputationOf(entry)) return FurniturePurchaseResult.ReputationLocked;
            if (Data.Currency < price) return FurniturePurchaseResult.NotEnoughCurrency;
            Data.Currency -= price;
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

        /// <summary>导出存档快照（存档接缝占位，当前无调用方，待定 #9）。列表按 id 排序，序列化结果稳定（§11.2）。</summary>
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
            AddFreeFurniture();
            Data.FurnitureDecorScore = ComputeInitialFurnitureDecor();
        }

        /// <summary>把售价 &lt;=0 的基础家具补进所有权集合（含不在商店表里的非卖品）。</summary>
        private void AddFreeFurniture()
        {
            if (furnitureTable == null) return;
            foreach (var entry in furnitureTable.entries)
                if (entry != null && PriceOf(entry) <= 0) Data.OwnedFurniture.Add(entry.id);
        }

        /// <summary>家具模式尚未打开时，用房间表初始摆放估算装饰品得分基线。</summary>
        private int ComputeInitialFurnitureDecor()
        {
            if (furnitureTable == null || roomTable == null || roomTable.rooms.Count == 0 || roomTable.rooms[0] == null) return 0;
            var sum = 0;
            foreach (var placement in roomTable.rooms[0].initialPlacements)
            {
                var entry = placement == null ? null : furnitureTable.Find(placement.furnitureId);
                if (entry != null) sum += entry.decorationScore;
            }
            return sum;
        }

        private void RaiseChanged() => Changed?.Invoke();
    }
}
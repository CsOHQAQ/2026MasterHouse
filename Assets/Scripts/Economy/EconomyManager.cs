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
    /// 流通数值逻辑（§16.3，旧 HouseEconomy 静态服务平移）：货币/声望/装饰分与家具库存的唯一修改入口，
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
        /// 服务结算、离场小费、家具回收、对话奖励走这里；购买（反馈由商城的获得弹窗与其音效承担）、
        /// GM 后门（调试静音）、装饰分回写（与家具摆放音重叠）**刻意不发**。
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

        /// <summary>
        /// House 装饰分 = 房间数量 × 权重 + 设备 × 权重 + 已摆放装饰品得分 + GM 加成。
        /// 这是**全局展示值**（顶栏/GM 面板）。真正影响玩法的是**按房间**的装饰分——
        /// 它由 <see cref="FurniturePlacementQuery.DecorationScoreOf"/> 单独算，加成完成服务客人的离场小费（§6.1）。
        /// </summary>
        public int DecorationScore =>
            Data.RoomCount * config.decorScorePerRoom + Data.DeviceCount * config.decorScorePerDevice
            + Data.FurnitureDecorScore + Data.GmDecorationBonus;

        /// <summary>按满意度档取奖励配置（UI 预览展示用）。</summary>
        public SatisfactionReward RewardFor(EServeSatisfaction satisfaction) => config.RewardFor(satisfaction);

        /// <summary>
        /// 离场小费的**纯函数**口径（家具库存说明 §6.1）：
        /// <code>基础小费 + (完成服务 ? 房间装饰分 / decorScorePerTip : 0)</code>
        ///
        /// 基础小费所有业务访客都给（含被拒绝与超时流失的）——这是「不会陷入没钱死循环」的保证；
        /// **装饰分加成只给完成需求的客人**：被拒绝的客人不会因为房间漂亮就多给钱，
        /// 而且不这么限的话「装修好 + 全部拒绝」会变成纯收益最优解（拒绝已不扣声望）。
        ///
        /// UI 预览与实际入账共用本方法，两者永远不会漂开。
        /// </summary>
        public int LeaveTipPreview(int roomDecorScore, bool served)
        {
            var amount = Mathf.Max(0, config.guestLeaveTip);
            if (served) amount += Mathf.Max(0, roomDecorScore) / Mathf.Max(1, config.decorScorePerTip);
            return amount;
        }

        /// <summary>
        /// 货币来源：访客离场时留下的钱（需求重做说明 §8 + 家具库存说明 §6.1）。
        /// roomDecorScore 由调用方从 <see cref="FurniturePlacementQuery.DecorationScoreOf"/> 取——
        /// **不要让 EconomyManager 去查家具**，保持它对家具模块的单向依赖。
        /// 返回实际入账值（0 表示策划把基础小费配成了 0 且没有加成，调用方不必再发反馈）。
        /// </summary>
        public int GuestLeaveTip(int roomDecorScore, bool served)
        {
            var amount = LeaveTipPreview(roomDecorScore, served);
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

        // 拒绝的声望惩罚已于 2026-08-15 移除（家具库存说明 §6.4）：声望在业务路径上只增不减。
        // 这是有意为之，不是漏配——两段超时更早就不扣了，保留惩罚只会把玩家赶去
        // 「放着不管等超时」那条零代价且体验更差的路径。正向激励由服务奖励 + 解禁门槛 + 装饰分加成承担。

        /// <summary>
        /// 货币增减（下限 0）：对话奖励事件等玩法入口（对话设计说明 §7）。
        /// 与 GmAddCurrency 分开是语义问题——GM 是调试后门，本方法是正经玩法收支，
        /// 将来要加日志/成就统计/数值埋点时只该挂在这一侧。
        /// 返回**实际生效净值**（扣穿下限时被截断，可能小于 |amount|；日结累计要记这个值，不虚报）。
        /// 无实际变化时返回 0 且不广播不发反馈——余额 0 再扣钱不该响损失音。
        /// </summary>
        public int AddCurrency(int amount)
        {
            if (amount == 0) return 0;
            var before = Data.Currency;
            Data.Currency = Mathf.Max(0, Data.Currency + amount);
            var applied = Data.Currency - before;
            if (applied == 0) return 0;
            RaiseChanged();
            Feedback?.Invoke(applied > 0 ? EEconomyFeedback.CurrencyGain : EEconomyFeedback.CurrencyLoss);
            return applied;
        }

        /// <summary>声望增减（下限 0）：对话奖励事件等玩法入口。声望变化会实时影响 Item 解禁状态。
        /// 业务路径只增不减，但本方法**允许配负数**——它是策划可见的公开入口（也是 ReputationLoss 反馈的唯一产生方）。
        /// 返回实际生效净值（口径同 <see cref="AddCurrency"/>）。</summary>
        public int AddReputation(int amount)
        {
            if (amount == 0) return 0;
            var before = Data.Reputation;
            Data.Reputation = Mathf.Max(0, Data.Reputation + amount);
            var applied = Data.Reputation - before;
            if (applied == 0) return 0;
            RaiseChanged();
            Feedback?.Invoke(applied > 0 ? EEconomyFeedback.ReputationGain : EEconomyFeedback.ReputationLoss);
            return applied;
        }

        // ── 家具库存（家具库存说明 §5）──

        /// <summary>家具库存数量（未拥有 = 0）。</summary>
        public int OwnedCountOf(string furnitureId)
        {
            if (string.IsNullOrEmpty(furnitureId)) return 0;
            return Data.OwnedFurniture.TryGetValue(furnitureId, out var count) ? count : 0;
        }

        /// <summary>是否拥有至少一件（语义 = 数量 &gt; 0）。签名保持不变，既有调用方零改动。</summary>
        public bool IsFurnitureOwned(string furnitureId) => OwnedCountOf(furnitureId) > 0;

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

        /// <summary>家具回收额（售价 × sellbackPercent / 100，整数除法）。展示与实际入账共用。</summary>
        public int SellbackValueOf(FurnitureEntry entry) =>
            Mathf.Max(0, PriceOf(entry)) * Mathf.Clamp(config.sellbackPercent, 0, 100) / 100;

        /// <summary>
        /// 货币去处：购买家具（家具库存说明 §5.2）。**不限数量**——每次成功库存 +1。
        /// 解禁（声望）与购买（货币）是两道独立的门；非卖品（售价 ≤ 0）先被挡下，
        /// 否则漏配的家具会被 <see cref="PriceOf"/> 兑成 0 变成免费无限买，而装饰分直接加成小费。
        /// </summary>
        public FurniturePurchaseResult TryPurchaseFurniture(FurnitureEntry entry)
        {
            if (entry == null) return FurniturePurchaseResult.NotForSale;
            var price = PriceOf(entry);
            if (price <= 0) return FurniturePurchaseResult.NotForSale;
            if (Data.Reputation < UnlockReputationOf(entry)) return FurniturePurchaseResult.ReputationLocked;
            if (Data.Currency < price) return FurniturePurchaseResult.NotEnoughCurrency;
            Data.Currency -= price;
            Data.OwnedFurniture[entry.id] = OwnedCountOf(entry.id) + 1;
            RaiseChanged();
            return FurniturePurchaseResult.Success;
        }

        /// <summary>
        /// 货币来源：半价回收一件家具（家具库存说明 §5.5）。库存 −1、返还 <see cref="SellbackValueOf"/>。
        ///
        /// **调用方必须先确认「余量 &gt; 0」**——余量 = 拥有数 − 全部房间已摆放数，只有家具模式算得出，
        /// Manager 这里只守得住「拥有数 &gt; 0」这条底线。卖掉正摆着的家具是调用方的 bug。
        /// 返回实际回收额；0 = 没卖成（非卖品 / 库存为空）。
        /// </summary>
        public int SellFurniture(FurnitureEntry entry)
        {
            if (entry == null) return 0;
            var count = OwnedCountOf(entry.id);
            if (count <= 0 || PriceOf(entry) <= 0) return 0;
            var refund = SellbackValueOf(entry);
            if (count <= 1) Data.OwnedFurniture.Remove(entry.id);
            else Data.OwnedFurniture[entry.id] = count - 1;
            Data.Currency += refund;
            RaiseChanged();
            if (refund > 0) Feedback?.Invoke(EEconomyFeedback.CurrencyGain);
            return refund;
        }

        /// <summary>
        /// 无偿授予家具（2026-08-22 一轮测试改进 #11：对话赠送事件的入账口）。
        /// 不走商店三道门（非卖品/声望/货币）——赠送语义就是白给；
        /// 家具 id 不在表里时拒绝并报错，防止对话表拼错 id 造出永远摆不出的幽灵库存。
        /// 返回是否入账。
        /// </summary>
        public bool GrantFurniture(string furnitureId, int count = 1)
        {
            if (string.IsNullOrEmpty(furnitureId) || count <= 0) return false;
            if (furnitureTable != null && furnitureTable.Find(furnitureId) == null)
            {
                Debug.LogError($"[EconomyManager] 赠送家具「{furnitureId}」不在家具表里，已拒绝入账；" +
                               "请检查对话表里 GrantFurniture 的家具 id");
                return false;
            }
            Data.OwnedFurniture[furnitureId] = OwnedCountOf(furnitureId) + count;
            RaiseChanged();
            return true;
        }

        /// <summary>装饰分来源：家具摆放变化后由家具模式回写当前摆放的装饰品得分总和。</summary>
        public void SetFurnitureDecorationScore(int score)
        {
            // 相等守卫是幂等保护：去掉会让每次重算都广播 Changed，连带顶栏与商店无谓重刷。
            // ⚠ 因此「按房间的装饰分」不要挂 Changed 事件——见 FurnitureRoomController.RecomputeDecorationScore
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
            foreach (var pair in Data.OwnedFurniture)
                if (pair.Value > 0)
                    data.ownedFurniture.Add(new OwnedFurnitureSaveData { id = pair.Key, count = pair.Value });
            data.ownedFurniture.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            return data;
        }

        /// <summary>
        /// 从存档快照恢复；data 为 null 时重置为配置表默认值。
        /// 存档里的库存是**全量真相**（含场上摆着的那些），所以这里不再补发初始家具——
        /// 补发会让读档后数量翻倍。
        /// </summary>
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
                foreach (var owned in data.ownedFurniture)
                    if (owned != null && !string.IsNullOrEmpty(owned.id) && owned.count > 0)
                        Data.OwnedFurniture[owned.id] = owned.count;
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

        /// <summary>GM：增减装饰分加成项（装饰分本身是派生值，GM 只操作独立加成，下限 0）。
        /// ⚠ 已知不对称：本加成只进**全局展示值**、不进任何房间，所以 GM 调装饰分测不出小费变化——
        /// 要测小费加成必须真的往房间里摆家具（§6.2）。修正它需要给加成指定归属房间，收益不值当。</summary>
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
            GrantStartingFurniture();
            Data.FurnitureDecorScore = ComputeInitialFurnitureDecor();
        }

        /// <summary>
        /// 开局家具库存 = **全部房间的初始摆放数（自动统计）+ 配置的初始携带份数**（家具库存说明 §5.3）。
        ///
        /// 自动统计初始摆放是硬约束而不是便利：场上摆着的家具必须算进拥有数，
        /// 否则「余量 = 拥有数 − 已摆数」会算出负数。让代码统计而不是要求策划手配，
        /// 好处是这条不变式永远成立，且「初始携带」的语义干净——它就是「开局收纳栏里有几件可摆」。
        /// （2026-08-15 取代 AddFreeFurniture：「售价 ≤ 0 = 初始拥有」那条隐式约定已随之退役。）
        /// </summary>
        private void GrantStartingFurniture()
        {
            if (roomTable != null)
                foreach (var room in roomTable.rooms) // 房间表是 List，遍历顺序稳定（§11.2）
                {
                    if (room == null) continue;
                    foreach (var placement in room.initialPlacements)
                    {
                        if (placement == null || string.IsNullOrEmpty(placement.furnitureId)) continue;
                        Grant(placement.furnitureId, 1);
                    }
                }
            foreach (var starting in config.startingFurniture)
            {
                if (starting == null || string.IsNullOrEmpty(starting.furnitureId) || starting.count <= 0) continue;
                if (furnitureTable != null && furnitureTable.Find(starting.furnitureId) == null)
                {
                    Debug.LogError($"[EconomyManager] 初始携带家具「{starting.furnitureId}」不在家具表里，已跳过；" +
                                   "请在 HouseEconomyConfig 的「初始携带家具」里修正");
                    continue;
                }
                Grant(starting.furnitureId, starting.count);
            }

            void Grant(string id, int count) => Data.OwnedFurniture[id] = OwnedCountOf(id) + count;
        }

        /// <summary>
        /// 家具模式尚未打开时，用**全部房间**的初始摆放估算装饰品得分基线。
        /// 口径必须与 <c>FurnitureRoomController.SyncDecorationFromSession</c> 的回落分支一致——
        /// 只算 rooms[0] 是四宫格改造前的遗留，会让开局顶栏少算另外三间房。
        /// </summary>
        private int ComputeInitialFurnitureDecor()
        {
            if (furnitureTable == null || roomTable == null) return 0;
            var sum = 0;
            foreach (var room in roomTable.rooms)
            {
                if (room == null) continue;
                foreach (var placement in room.initialPlacements)
                {
                    var entry = placement == null ? null : furnitureTable.Find(placement.furnitureId);
                    if (entry != null) sum += entry.decorationScore;
                }
            }
            return sum;
        }

        private void RaiseChanged() => Changed?.Invoke();
    }
}

using System;
using System.Collections.Generic;

namespace MasterHouse
{
    /// <summary>家具购买结果。</summary>
    public enum FurniturePurchaseResult
    {
        Success,
        /// <summary>
        /// 【已废弃，不再返回】家具改为可重复购买后（家具库存说明 §5.2），「已拥有」不再是拒绝理由。
        /// 枚举值保留占位，避免旧存档/旧 switch 的序号漂移；各处 switch 的 default 文案不要再写「已拥有」。
        /// </summary>
        AlreadyOwned,
        /// <summary>声望未达到解禁阈值（商城/图鉴中呈「？」）。</summary>
        ReputationLocked,
        NotEnoughCurrency,
        /// <summary>
        /// 非卖品：售价 &lt;= 0。防漏配用——不在商店表里的家具会被 <c>PriceOf</c> 兑成 0，
        /// 不拦就是「免费无限买」，而装饰分直接加成小费，等于开放零成本刷钱。
        /// </summary>
        NotForSale,
    }

    /// <summary>已拥有家具的一条存档记录（id + 数量）。</summary>
    [Serializable]
    public sealed class OwnedFurnitureSaveData
    {
        public string id;
        public int count;
    }

    /// <summary>
    /// 流通数值的存档快照。**存档接缝占位（§16.5）**：旧局外存档已随 3.9 退役，本类当前无调用方，
    /// 保留数据快照能力等待统一存档接入（待定 #9）；届时字段结构按新设计调整，不再受旧档格式约束。
    /// ⚠ 维护税：改 <see cref="EconomyData"/> 的结构必须顺手改这里，而改错了编译器不报、测试不到。
    /// </summary>
    [Serializable]
    public sealed class EconomySaveData
    {
        public int currency;
        public int reputation;
        public int gmDecorationBonus;
        /// <summary>已拥有家具（id → 数量）。序列化前按 id 排序，保证结果稳定（§11.2）。</summary>
        public List<OwnedFurnitureSaveData> ownedFurniture = new List<OwnedFurnitureSaveData>();
    }

    /// <summary>
    /// 流通数值运行时数据（§16.3）：货币、玩家声望、装饰分构成项与家具库存。
    /// 只能被 EconomyManager 修改（§11.4）；装饰分是派生值，由 Manager 按配置权重计算。
    /// </summary>
    public class EconomyData
    {
        /// <summary>货币（HOUSE CREDIT）。来源=服务奖励+离场小费+对话事件；去处=买家具（可半价回收）。</summary>
        public int Currency;

        /// <summary>
        /// 玩家声望。来源=完成服务+对话事件；**业务路径上只增不减**——拒绝惩罚已于
        /// 2026-08-15 移除（家具库存说明 §6.4），两段超时更早就不扣了。
        /// 唯一作用是家具解禁门槛（商店表「解禁声望」列）。下限 0。
        /// </summary>
        public int Reputation;

        /// <summary>GM 装饰分加成项（装饰分本身是派生值，GM 只操作独立加成）。下限 0。</summary>
        public int GmDecorationBonus;

        /// <summary>当前已摆放装饰品的得分总和（家具模式回写）。</summary>
        public int FurnitureDecorScore;

        /// <summary>房间数量（装饰分构成项）。来源 = CodexTable 的 Def 资产统计（§16.7 毒点①已断）。</summary>
        public int RoomCount;

        /// <summary>已拥有设备数量（装饰分构成项）。来源同 RoomCount。</summary>
        public int DeviceCount;

        /// <summary>
        /// 家具库存：id → 拥有数（家具库存说明 §5.1）。同一款可以拥有多件、同一房间也可以摆多件。
        /// 只做键查询与计数；**如需 UI 遍历展示，须按 id 排序后再枚举**（§11.2）。
        /// </summary>
        public readonly Dictionary<string, int> OwnedFurniture = new Dictionary<string, int>();
    }
}

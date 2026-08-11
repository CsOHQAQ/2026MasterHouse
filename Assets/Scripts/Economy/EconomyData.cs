using System;
using System.Collections.Generic;

namespace MasterHouse
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

    /// <summary>
    /// 流通数值的存档快照。**存档接缝占位（§16.5）**：旧局外存档已随 3.9 退役，本类当前无调用方，
    /// 保留数据快照能力等待统一存档接入（待定 #9）；届时字段结构按新设计调整，不再受旧档格式约束。
    /// </summary>
    [Serializable]
    public sealed class EconomySaveData
    {
        public int currency;
        public int reputation;
        public int gmDecorationBonus;
        public List<string> ownedFurniture = new List<string>();
    }

    /// <summary>
    /// 流通数值运行时数据（§16.3）：货币、玩家声望、装饰分构成项与家具所有权。
    /// 只能被 EconomyManager 修改（§11.4）；装饰分是派生值，由 Manager 按配置权重计算。
    /// </summary>
    public class EconomyData
    {
        /// <summary>货币（HOUSE CREDIT）。来源=完成客人服务；去处=商城/家具购买。</summary>
        public int Currency;

        /// <summary>玩家声望。来源=完成服务；去处=拒绝服务、周结算未完成项；同时是家具解禁门槛。下限 0。</summary>
        public int Reputation;

        /// <summary>GM 装饰分加成项（装饰分本身是派生值，GM 只操作独立加成）。下限 0。</summary>
        public int GmDecorationBonus;

        /// <summary>当前已摆放装饰品的得分总和（家具模式回写）。</summary>
        public int FurnitureDecorScore;

        /// <summary>房间数量（装饰分构成项）。来源 = CodexTable 的 Def 资产统计（§16.7 毒点①已断）。</summary>
        public int RoomCount;

        /// <summary>已拥有设备数量（装饰分构成项）。来源同 RoomCount。</summary>
        public int DeviceCount;

        /// <summary>已拥有家具 id 集合。只做包含判断与累加；如需 UI 遍历展示，须按 id 排序后再枚举（§11.2）。</summary>
        public readonly HashSet<string> OwnedFurniture = new HashSet<string>();
    }
}
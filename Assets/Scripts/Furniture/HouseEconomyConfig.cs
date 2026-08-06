using UnityEngine;

namespace MasterPotion
{
    /// <summary>
    /// 流通数值配置表：货币 / 玩家声望 / House 装饰分 三值循环的全部数值参数。
    /// 依据《大House》文档：货币来源=客人服务，去处=购买设备与装饰品；
    /// 声望来源=完成服务，去处=拒绝服务与未完成服务；装饰分无去处，来源=房间数量+装饰品+设备。
    /// </summary>
    [CreateAssetMenu(menuName = "MasterPotion/流通数值配置", fileName = "HouseEconomyConfig")]
    public sealed class HouseEconomyConfig : ScriptableObject
    {
        [Header("初始值")]
        [Tooltip("初始货币（HOUSE CREDIT）")] public int startCurrency = 2480;
        [Tooltip("初始玩家声望")] public int startReputation = 40;

        [Header("货币来源")]
        [Tooltip("完成一次客人服务获得的货币")] public int serviceCurrencyReward = 320;

        [Header("声望来源与去处")]
        [Tooltip("完成一次客人服务获得的声望")] public int serviceReputationReward = 25;
        [Tooltip("拒绝服务客人扣除的声望")] public int refuseReputationPenalty = 15;
        [Tooltip("周结算时每项未完成服务扣除的声望")] public int failReputationPenalty = 30;

        [Header("装饰分权重（无去处，实时计算）")]
        [Tooltip("每间已解锁房间贡献的装饰分")] public int decorScorePerRoom = 50;
        [Tooltip("每台已拥有设备贡献的装饰分")] public int decorScorePerDevice = 30;
    }
}

using System;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>单档服务奖励（货币 + 声望）。</summary>
    [Serializable]
    public sealed class SatisfactionReward
    {
        public int currency = 320;
        public int reputation = 25;
    }

    /// <summary>
    /// 流通数值配置表（§16.3 EconomyConfig，原 HouseEconomyConfig 改名，资产文件名与 Resources 路径维持不变）：
    /// 货币 / 玩家声望 / House 装饰分 三值循环的全部数值参数。
    /// 依据《大House》文档：货币来源=客人服务，去处=购买设备与装饰品；
    /// 声望来源=完成服务，去处=拒绝服务（分前台/服务中两档，两段超时各随所处状态，§5.2）；
    /// 装饰分无去处，来源=房间数量+装饰品+设备。
    /// 服务奖励按满意度四档配置（访客交付说明 §6.2）；周制的 failReputationPenalty 已随周制退役删除。
    /// </summary>
    [CreateAssetMenu(menuName = "MasterHouse/流通数值配置", fileName = "HouseEconomyConfig")]
    public sealed class EconomyConfig : ScriptableObject
    {
        [Header("初始值")]
        [Tooltip("初始货币（HOUSE CREDIT）")] public int startCurrency = 2480;
        [Tooltip("初始玩家声望")] public int startReputation = 40;

        [Header("服务奖励（按满意度四档，§6.2）")]
        // 待确认（访客交付说明 §10）：各档数值先按旧单值（320/25）填四档，等策划调
        [Tooltip("不对味")] public SatisfactionReward rewardMismatch = new SatisfactionReward();
        [Tooltip("一般")] public SatisfactionReward rewardPlain = new SatisfactionReward();
        [Tooltip("满意")] public SatisfactionReward rewardSatisfied = new SatisfactionReward();
        [Tooltip("完美")] public SatisfactionReward rewardPerfect = new SatisfactionReward();

        [Header("评分阈值（§6.2）")]
        [Range(1, 100)]
        [Tooltip("阈值A：加分项命中比例（%）≥ 此值为「满意」，低于为「一般」；全命中为「完美」")]
        public int satisfactionThresholdPercent = 60;

        [Header("声望去处（拒绝分两档，交付页落地说明 §5.2）")]
        // 访谈定案：接待后反悔比在前台谢客更失礼，扣得更多。两段超时按**超时发生时所处状态**取档
        // （等搭话超时 → 前台档；等交货超时 → 服务中档），实现上就是 SettleRefusal 的同一个分支。
        [Tooltip("前台拒绝扣除的声望（在「前台等待接待」谢客，或等搭话超时）")]
        public int refuseReputationPenalty = 15;
        [Tooltip("服务中拒绝扣除的声望（已接待后反悔，或等交货超时）。应当 > 前台档")]
        public int serviceFailedReputationPenalty = 30;

        [Header("装饰分权重（无去处，实时计算）")]
        [Tooltip("每间已解锁房间贡献的装饰分")] public int decorScorePerRoom = 50;
        [Tooltip("每台已拥有设备贡献的装饰分")] public int decorScorePerDevice = 30;

        /// <summary>按满意度档取奖励配置。</summary>
        public SatisfactionReward RewardFor(EServeSatisfaction satisfaction)
        {
            switch (satisfaction)
            {
                case EServeSatisfaction.Mismatch: return rewardMismatch;
                case EServeSatisfaction.Plain: return rewardPlain;
                case EServeSatisfaction.Satisfied: return rewardSatisfied;
                default: return rewardPerfect;
            }
        }
    }
}

using System;
using System.Collections.Generic;
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
    /// 开局额外携带的一件家具（家具库存与交互重做说明 §5.3）。
    /// 语义是「开局收纳栏里有几件可摆」——场上初始摆放的家具由代码自动计入拥有数，不要在这里重复配。
    /// </summary>
    [Serializable]
    public sealed class StartingFurniture
    {
        [FurnitureId] public string furnitureId;
        [Min(0)] public int count = 1;
    }

    /// <summary>
    /// 流通数值配置表（§16.3 EconomyConfig，原 HouseEconomyConfig 改名，资产文件名与 Resources 路径维持不变）：
    /// 货币 / 玩家声望 / House 装饰分 三值循环的全部数值参数。
    ///
    /// 三值的来源与去处（2026-08-15 家具库存与交互重做后的口径）：
    ///   货币　　来源 = 离场小费 + 服务奖励 + 对话事件；去处 = 买家具（不限数量），可半价回收
    ///   声望　　来源 = 完成服务 + 对话事件；**业务路径上只增不减**（拒绝惩罚已移除）；作用 = 家具解禁门槛
    ///   装饰分　来源 = 房间数量 + 设备 + 已摆家具；去处 = **按房间加成完成服务客人的离场小费**
    ///
    /// 服务奖励按满意度四档配置（访客交付说明 §6.2）；周制的 failReputationPenalty 已随周制退役删除。
    /// </summary>
    [CreateAssetMenu(menuName = "MasterHouse/流通数值配置", fileName = "HouseEconomyConfig")]
    public sealed class EconomyConfig : ScriptableObject
    {
        [Header("初始值")]
        [Tooltip("初始货币（HOUSE CREDIT）")] public int startCurrency = 2480;
        [Tooltip("初始玩家声望")] public int startReputation = 40;

        [Header("初始携带家具（家具库存说明 §5.3）")]
        // 初始拥有数 = 全部房间 initialPlacements 里该 id 的出现次数（代码自动统计）+ 这里配的份数。
        // 让代码统计而不是要求策划手配，是为了保证「拥有数 ≥ 已摆数」这条硬约束——
        // 否则场上摆着的家具算不进拥有数，余量会是负数。
        [Tooltip("开局额外携带的家具（收纳栏里可直接摆的份数）。场上初始摆放的家具会自动计入拥有数，不必在这里重复配")]
        public List<StartingFurniture> startingFurniture = new List<StartingFurniture>();

        [Header("服务奖励（按满意度四档，§6.2）")]
        // 待确认（访客交付说明 §10）：各档数值先按旧单值（320/25）填四档，等策划调
        [Tooltip("不对味")] public SatisfactionReward rewardMismatch = new SatisfactionReward();
        [Tooltip("一般")] public SatisfactionReward rewardPlain = new SatisfactionReward();
        [Tooltip("满意")] public SatisfactionReward rewardSatisfied = new SatisfactionReward();
        [Tooltip("完美")] public SatisfactionReward rewardPerfect = new SatisfactionReward();

        [Header("离场基础金钱（需求重做说明 §8）")]
        // 待确认 #1：配置位置与数值。**默认实现**为全局单值，暂定与单次服务奖励同量级；
        // 策划实测后若需按种族差异化，再迁到 VisitorRaceDef。
        // 这笔钱是新模型下「不会陷入没钱死循环」的保证：所有业务访客离场都留下它，
        // **包括被拒绝与超时流失的**——未满足需求只是拿不到额外奖励，不扣钱。
        [Tooltip("访客离场时留下的基础货币（成功/拒绝/超时三条路径通用）")]
        public int guestLeaveTip = 320;

        [Header("装饰分（家具库存说明 §6.1）")]
        [Tooltip("每间已解锁房间贡献的装饰分")] public int decorScorePerRoom = 50;
        [Tooltip("每台已拥有设备贡献的装饰分")] public int decorScorePerDevice = 30;
        // 语义是「多少装饰分换 1 货币」，与旧设计稿的 tipPerDecorScore（每点加多少钱）方向相反，
        // 字段名一并改掉避免误读。取 2 的依据：摆 10 件家具（约 200 分）时单人加成 +100、
        // 约占当天服务奖励三成——玩家能明确感知到「装修在赚钱」（§3.4 感知不到就等于不存在），
        // 同时把铺满房间（单房容量约 3250 分）的极端值压到 +1625 且必须真的服务好客人才拿得到。
        [Tooltip("多少点房间装饰分换 1 货币小费加成（整数除法）。加成只给完成服务的客人")]
        [Min(1)] public int decorScorePerTip = 2;

        [Header("家具回收（家具库存说明 §5.5）")]
        // 整数百分比而不是 float 比例：§11.3 要求全整数运算。回收额 = 售价 * percent / 100。
        [Tooltip("家具回收价占售价的百分比（50 = 半价）")]
        [Range(0, 100)] public int sellbackPercent = 50;

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

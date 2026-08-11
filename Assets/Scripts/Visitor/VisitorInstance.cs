using System;
using System.Collections.Generic;
using System.Text;

namespace MasterHouse
{
    /// <summary>
    /// 访客生命周期状态（访客交付说明 §5）。到场前的「日程未到点」不建实例（由日程游标表达），
    /// 离场即从在场列表移除，Departed 仅作为移除瞬间的终态标记。
    /// </summary>
    public enum EVisitorState
    {
        /// <summary>前台等待接待：日程到点进场后站在前台，等玩家搭话/接待，超时按被拒绝口径离开。</summary>
        FrontDesk = 0,
        /// <summary>服务中：接待成功进入房间，等玩家提交物品；一次性、不可补交、不可重入。</summary>
        Serving = 1,
        /// <summary>闲逛：服务满意后在屋内游走冒泡，累计达种族上限自行离开；打烊时可 roll 跨天留宿。</summary>
        Wandering = 2,
        /// <summary>已离场（终态标记；实例同时从在场列表移除）。</summary>
        Departed = 3,
    }

    /// <summary>服务满意度四档（访客交付说明 §4.7）。</summary>
    public enum EServeSatisfaction
    {
        Mismatch = 0,  // 不对味：任一必要需求未命中
        Plain = 1,     // 一般：加分项命中比例低于阈值A
        Satisfied = 2, // 满意：加分项命中比例 ≥ 阈值A 且未全中
        Perfect = 3,   // 完美：加分项全命中（或需求里没有加分项）
    }

    /// <summary>满意度展示文案（下标 = (int)EServeSatisfaction）。</summary>
    public static class ServeSatisfactionText
    {
        public static readonly string[] Names = { "不对味", "一般", "满意", "完美" };

        public static string NameOf(EServeSatisfaction satisfaction) => Names[(int)satisfaction];
    }

    /// <summary>需求项（§4.6）：tag + 是否必要。</summary>
    public struct VisitorNeed
    {
        public TagDef Tag;
        public bool Required;
    }

    /// <summary>
    /// 运行时访客实例（访客交付说明 §4.6）。只能由 VisitorManager 修改（§11.4）。
    /// 所有计时以 VisitorData.BusinessTick 的整数比较进行（§11.3）。
    /// </summary>
    public sealed class VisitorInstance
    {
        /// <summary>稳定自增 id（遍历排序键，§11.2/11.3；随存档序列化）。</summary>
        public int InstanceId;

        public VisitorRaceDef Race;

        /// <summary>来源日程条目：第几天（重算派生种子用，§6.1）。</summary>
        public int ScheduleDay;

        /// <summary>来源日程条目：日程表 entries 的原始下标（重算派生种子用，§6.1）。</summary>
        public int ScheduleIndex;

        public EVisitorState State;

        /// <summary>进入当前状态时的业务 tick（算超时用，整数比较）。</summary>
        public long StateEnterTick;

        /// <summary>需求项列表（roll 顺序即派生随机流顺序，天然稳定）。</summary>
        public readonly List<VisitorNeed> Needs = new List<VisitorNeed>();

        /// <summary>满意度（Submit 结算之后有效）。</summary>
        public EServeSatisfaction Satisfaction;

        /// <summary>提交的物品（结算展示与日志用）。</summary>
        public ItemDef SubmittedItem;

        /// <summary>下次闲逛冒泡的业务 tick（0 = 未排程）。</summary>
        public long NextBubbleTick;

        /// <summary>实例随机流：派生自 rollSeed（§6.1），需求 roll 之后继续用于冒泡抖动与跨天留宿 roll。</summary>
        public DeterministicRng Rng;

        public string DisplayName => Race != null ? Race.displayName : "访客";

        /// <summary>
        /// 程序化需求句（§8「开始等待服务」附带）：按 (轴 sortOrder, 节点 sortOrder) 稳定排序（§4.1/§11.2），
        /// 形容词用描述词短语、名词用显示名，非必要项标注（加分）。
        /// </summary>
        public string BuildNeedSentence()
        {
            if (Needs.Count == 0) return "我随便看看就好。";
            var sorted = new List<VisitorNeed>(Needs);
            sorted.Sort((a, b) => TagDef.Compare(a.Tag, b.Tag));
            var text = new StringBuilder("我想要");
            var wroteAny = false;
            var hasNoun = false;
            foreach (var need in sorted) // 形容词短语在前（Compare 已按轴稳定排序，形容词轴/名词轴的先后由轴 sortOrder 配置）
            {
                if (wroteAny) text.Append("、");
                var tag = need.Tag;
                if (tag.EffectiveGrammarRole == ETagGrammarRole.Adjective)
                {
                    text.Append(tag.Phrase);
                }
                else
                {
                    text.Append(tag.displayName);
                    hasNoun = true;
                }
                if (!need.Required) text.Append("（加分）");
                wroteAny = true;
            }
            text.Append(hasNoun ? "。" : "的东西。");
            return text.ToString();
        }
    }
}

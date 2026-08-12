using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>对话组候选项（设计说明 §4.5 / §6）：组 + 权重 + 进入候选的条件。</summary>
    [Serializable]
    public sealed class DialogueGroupEntry
    {
        public DialogueGroupDef group;

        [Tooltip("加权抽取的权重，必须 > 0 才参与（=0 视为临时禁用）")]
        public int weight = 1;

        [SerializeReference, SubclassSelector]
        [Tooltip("进入候选的条件，多条之间 AND（§6「候选 = 池[种族][分类].筛(所有条件通过)」）。\n" +
                 "留空 = 无条件参与抽取。用来做「第 3 天之后才会说的话」这类内容门槛")]
        public List<IGameplayCondition> conditions = new List<IGameplayCondition>();

        /// <summary>是否为可用候选（组存在且权重为正）。条件判定另走 DialogueManager，因为需要上下文。</summary>
        public bool IsUsable => group != null && weight > 0;
    }

    /// <summary>
    /// 种族对话池（Model 层，运行时只读；设计说明 §4.5）：一个种族一个资产，
    /// 由 VisitorRaceDef.dialoguePool 引用。
    ///
    /// 用**八个具名字段**而不是「分类枚举 → 列表」的字典/数组：Inspector 一目了然、
    /// 缺哪个分类一眼看出，且不需要写重复键与缺键校验。
    ///
    /// 【务必独占本文件】ScriptableObject 必须与文件同名，否则 .asset 的脚本引用会损坏（见 RETRO）。
    /// </summary>
    [CreateAssetMenu(menuName = "MasterHouse/对话池", fileName = "DialoguePool")]
    public sealed class DialoguePoolDef : ScriptableObject
    {
        [Header("模态对话框（八个触发分类，§4.5）")]

        [Tooltip("初次见面：玩家点击前台等待接待的访客")]
        public List<DialogueGroupEntry> firstMeeting = new List<DialogueGroupEntry>();

        [Tooltip("开始等待服务：接待成功、进入服务中（含程序化需求句 §9）")]
        public List<DialogueGroupEntry> serviceStart = new List<DialogueGroupEntry>();

        [Tooltip("被拒绝：玩家拒绝 / 等搭话超时 / 等交货超时（三者同口径）")]
        public List<DialogueGroupEntry> rejected = new List<DialogueGroupEntry>();

        [Tooltip("完成服务·不对味：任一必要需求未命中")]
        public List<DialogueGroupEntry> doneMismatch = new List<DialogueGroupEntry>();

        [Tooltip("完成服务·一般：加分项命中比例低于阈值A")]
        public List<DialogueGroupEntry> donePlain = new List<DialogueGroupEntry>();

        [Tooltip("完成服务·满意：加分项命中比例达标但未全中")]
        public List<DialogueGroupEntry> doneSatisfied = new List<DialogueGroupEntry>();

        [Tooltip("完成服务·完美：加分项全命中（或需求里没有加分项）")]
        public List<DialogueGroupEntry> donePerfect = new List<DialogueGroupEntry>();

        [Header("场景气泡")]

        [Tooltip("满意后闲逛：闲逛期间由冒泡调度器定期请求")]
        public List<DialogueGroupEntry> wanderChat = new List<DialogueGroupEntry>();

        [Header("交付预览（单句，不结算）")]

        // 这四项存的是**单句而不是对话组**——从类型上就挂不了事件与分支，
        // 「预览绝不结算」是类型保证的，不是靠约定（§4.5）。预览也不参与 recent 去重（§6）。

        [Tooltip("交付预览·不对味")] public List<DialogueLine> previewMismatch = new List<DialogueLine>();
        [Tooltip("交付预览·一般")] public List<DialogueLine> previewPlain = new List<DialogueLine>();
        [Tooltip("交付预览·满意")] public List<DialogueLine> previewSatisfied = new List<DialogueLine>();
        [Tooltip("交付预览·完美")] public List<DialogueLine> previewPerfect = new List<DialogueLine>();

        /// <summary>
        /// 按触发分类取对话组候选列表（§4.5）。satisfaction 仅 ServiceDone 有意义，其余触发点忽略。
        /// 返回 null 表示分类不存在（不该发生，枚举已穷举）；返回空列表表示策划没配内容。
        /// </summary>
        public List<DialogueGroupEntry> GroupsFor(EVisitorDialogueTrigger trigger, EServeSatisfaction satisfaction)
        {
            switch (trigger)
            {
                case EVisitorDialogueTrigger.FirstMeeting: return firstMeeting;
                case EVisitorDialogueTrigger.ServiceStart: return serviceStart;
                case EVisitorDialogueTrigger.Rejected: return rejected;
                case EVisitorDialogueTrigger.WanderChat: return wanderChat;
                case EVisitorDialogueTrigger.ServiceDone: return DoneGroupsFor(satisfaction);
                default: return null;
            }
        }

        private List<DialogueGroupEntry> DoneGroupsFor(EServeSatisfaction satisfaction)
        {
            switch (satisfaction)
            {
                case EServeSatisfaction.Mismatch: return doneMismatch;
                case EServeSatisfaction.Plain: return donePlain;
                case EServeSatisfaction.Satisfied: return doneSatisfied;
                default: return donePerfect;
            }
        }

        /// <summary>按满意度档取交付预览单句列表（§4.5）。</summary>
        public List<DialogueLine> PreviewFor(EServeSatisfaction satisfaction)
        {
            switch (satisfaction)
            {
                case EServeSatisfaction.Mismatch: return previewMismatch;
                case EServeSatisfaction.Plain: return previewPlain;
                case EServeSatisfaction.Satisfied: return previewSatisfied;
                default: return previewPerfect;
            }
        }

        /// <summary>分类的中文名（日志与编辑器用）。ServiceDone 会带上满意度档。</summary>
        public static string CategoryName(EVisitorDialogueTrigger trigger, EServeSatisfaction satisfaction)
        {
            switch (trigger)
            {
                case EVisitorDialogueTrigger.FirstMeeting: return "初次见面";
                case EVisitorDialogueTrigger.ServiceStart: return "开始等待服务";
                case EVisitorDialogueTrigger.Rejected: return "被拒绝";
                case EVisitorDialogueTrigger.WanderChat: return "满意后闲逛";
                case EVisitorDialogueTrigger.ServiceDone:
                    return "完成服务·" + ServeSatisfactionText.NameOf(satisfaction);
                default: return trigger.ToString();
            }
        }
    }
}

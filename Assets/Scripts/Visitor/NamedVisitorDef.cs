using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 具名剧情客人覆写（访客交付说明 §4.4）：固定名字/立绘/需求/专属对话。
    /// **先建结构留空实现**——现阶段无内容，按 §16.6「只为已有内容建结构」可以只留字段不建资产；
    /// 运行时（VisitorManager）暂不消费本类，等剧情内容进场后接通。
    /// </summary>
    [CreateAssetMenu(menuName = "MasterHouse/具名访客覆写", fileName = "NamedVisitor")]
    public sealed class NamedVisitorDef : ScriptableObject
    {
        [Tooltip("稳定键（存档/日志用）")] public string id;
        [Tooltip("固定名字（覆写种族显示名）")] public string displayName;
        [Tooltip("固定立绘差分（覆写种族立绘）")] public List<ExpressionPortrait> portraits = new List<ExpressionPortrait>();
        // 固定需求 fixedNeeds（一组 NeedTagWeight）已随 tag 需求体系退役（需求重做说明 §9.1）。
        // 新模型下需求本来就配在日程条目上、一人一条、零随机，没有「覆写权重 roll」这回事——
        // 具名客人要带专属需求，直接在他那一行日程的「需求」列填对应 NeedDef 即可，本类不必掺和。
        [Tooltip("专属对话池：覆写种族对话池（对话设计说明 §4.5）")] public DialoguePoolDef dialoguePool;
    }
}

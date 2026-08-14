using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 具名剧情客人覆写（访客交付说明 §4.4）：固定名字/需求/专属对话。
    /// **先建结构留空实现**——现阶段无内容，按 §16.6「只为已有内容建结构」可以只留字段不建资产；
    /// 运行时（VisitorManager）暂不消费本类，等剧情内容进场后接通。
    /// </summary>
    [CreateAssetMenu(menuName = "MasterHouse/具名访客覆写", fileName = "NamedVisitor")]
    public sealed class NamedVisitorDef : ScriptableObject
    {
        [Tooltip("稳定键（存档/日志用）")] public string id;
        [Tooltip("固定名字（覆写种族显示名）")] public string displayName;
        // 固定立绘差分 portraits（一组 ExpressionPortrait）已随 2026-08-14 立绘 ID 化删除。
        // 立绘现在是一张全局索引表（Excel/立绘表.xlsx → PortraitTable），ID 本来就不分角色——
        // 具名客人要专属立绘，在立绘表里加几行 `老板娘_平静` 之类，再在对话表里引用即可，
        // 不需要在本类复制一份差分表。至于「覆写种族默认脸」，等具名内容真进场时再加一个
        // defaultPortraitId 字段就行，现在没有内容不预建（§15.3）。
        //
        // 固定需求 fixedNeeds（一组 NeedTagWeight）已随 tag 需求体系退役（需求重做说明 §9.1）。
        // 新模型下需求本来就配在日程条目上、一人一条、零随机，没有「覆写权重 roll」这回事——
        // 具名客人要带专属需求，直接在他那一行日程的「需求」列填对应 NeedDef 即可，本类不必掺和。
        // 专属对话池 dialoguePool 已随 2026-08-14 对话资源重构删除（对话池资产整体退役）。
        // 具名客人要说专属台词，将来在对话表第一页加一列「具名覆写」按同样的筛选口径实现即可——
        // 现阶段没有具名剧情内容，不预建接缝（§15.3）。
    }
}

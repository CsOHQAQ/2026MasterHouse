using UnityEngine;

namespace MasterHouse
{
    // 表情枚举 EVisitorExpression（访客重做期间的占位）已于 2026-08-12 退役，改用对话系统的
    // EDialogueEmotion；后者又于 2026-08-14 随立绘 ID 化一并退役（见 Dialogue/DialogueLine.cs 的说明）。
    // 立绘差分表 ExpressionPortrait 与 portraits 字段同批删除：立绘改由一张
    // Excel/立绘表.xlsx → PortraitTable 承载，种族这里只留一个「默认长什么样」的指针。

    // 需求权重项 NeedTagWeight 已随 tag 需求体系退役（需求重做说明 §9.1）：
    // 需求改为一条 NeedDef、由日程条目配死，种族不再参与需求生成。

    /// <summary>
    /// 访客种族模板（Model 层，运行时只读；访客交付说明 §4.3）。一个种族一个资产。
    /// 「这个种族是什么性格」的数值全在这里（急性子超时短、熟客爱赖着不走）。
    /// 没有覆写机制——每个数值只有一个家。
    /// </summary>
    [CreateAssetMenu(menuName = "MasterHouse/访客种族", fileName = "VisitorRace")]
    public sealed class VisitorRaceDef : ScriptableObject
    {
        [Header("身份")]
        [Tooltip("稳定键（存档/日志用）")] public string raceId;
        public string displayName;

        [Header("图鉴详情页内容（2026-08-19；策划在访客种族表里填，UI 只读）")]
        [Tooltip("名字下方的西文别名，如 CATCAT")] public string aliasName;
        [Tooltip("称号牌上的字，如「黑猫警官」")] public string title;
        [Tooltip("星级 0~3，画在称号牌左边")] [Range(0, 3)] public int stars = 3;
        [Tooltip("爱好一行，逗号分隔的自由文本")] public string hobbies;
        [TextArea] [Tooltip("介绍正文")] public string intro;
        [TextArea] [Tooltip("语录（QUOTE 纸上的话）")] public string quote;

        [Header("性格数值（tick，§11.3）")]
        [Tooltip("前台等搭话超时（tick）：等太久他自己走了。**不播对话、不扣声望**")]
        public int waitTalkTimeoutTicks = 1200;
        [Tooltip("服务中超时（tick）：**从他开口示意那一刻起算**，不是从进屋起算。\n" +
                 "超时不是「被拒绝」——他会说一句【需求反馈·失望】然后转停留，不扣声望，人也不当场走。\n" +
                 "【结束今天】跳过的夜间时长也按实际时长计入（2026-08-20）：夜里超时的静默转失望停留、不播台词")]
        public int waitDeliverTimeoutTicks = 2400;
        [Tooltip("停留时长上限（tick）：需求了结（交付成功或超时）后在屋里待这么久，到点转【待告别】等玩家道别。\n" +
                 "【结束今天】跳过的夜间时长也按实际时长计入（2026-08-20）——默认倍率下一夜（22:00→8:00）约 6000 tick，" +
                 "配得比这小意味着过夜的停留客次日一早就转待告别")]
        public int wanderMaxTicks = 3600;

        // 跨天留宿概率 stayOvernightPercent 已于 2026-08-14 删除。它在 2026-08-14 早些时候就随
        // 「闲逛访客的跨天留宿 roll」一起失去了消费方（服务中/待分房都无条件跨天，单给闲逛的掷骰子不一致），
        // 当时留字段等下一轮种族表清理——这就是那一轮。日结统计里的 StayOvernightCount 是实际留宿人数，
        // 与本字段无关，仍然活着。

        // 需求生成（needTagWeights / needCountMin / needCountMax）已随 tag 需求体系退役（§9.1）：
        // 谁带什么需求来，改由访客日程表逐条配死（需求重做说明 §4.2），种族不再插手。

        [Header("表现")]
        [Tooltip("默认立绘ID（Excel/立绘表.xlsx 的主键）。用在两处：\n" +
                 "① Hub 访客栏小卡——它跟对话无关，只需要「这位长什么样」；\n" +
                 "② 对话组首句没填立绘ID 时的起点（之后每句留空都沿用上一句）。\n" +
                 "留空 = 这个种族不显示立绘")]
        public string defaultPortraitId;

        [Tooltip("序列帧前缀（Resources 路径），实际资源为 前缀 + \"_await_sheet\"/\"_attack_sheet\" 的 PNG+JSON 组合。\n" +
                 "**这是场景里走动的小人，不是立绘**，两者互不相干")]
        public string sheetPath;

        // 对话池引用 dialoguePool 已于 2026-08-14 对话资源重构删除：
        // 对话内容改由一张 DialogueTable 整表承载，按 raceId 查表（Excel 第一页的「种族」列），
        // 不再需要每个种族挂一个 SO——也就顺带干掉了访客种族表 Excel 的「对话池」列
        // 与「改资产名即断引用」那个固有问题。

        // 立绘取图入口 GetPortraitPath 已迁至 PortraitTable.PathOf / TextureOf：
        // 立绘不再是种族的私产（具名 NPC、旁白特写将来都能引用同一张表），种族只持有一个 ID。
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    // 表情枚举 EVisitorExpression（访客重做期间的占位，仅有 Default）已于 2026-08-12 退役，
    // 由对话系统的 EDialogueEmotion 取代（Dialogue/DialogueLine.cs，访客交付说明 §9 的约定：
    // 表情枚举由对话系统定义，单句对话自带表情字段）。
    // 序列化兼容：两个枚举的 0 号项对应（Default → Calm），已配的差分表不受影响。

    // 需求权重项 NeedTagWeight 已随 tag 需求体系退役（需求重做说明 §9.1）：
    // 需求改为一条 NeedDef、由日程条目配死，种族不再参与需求生成。

    /// <summary>立绘差分表条目（§4.3）：表情枚举 → 贴图路径（Resources）。</summary>
    [Serializable]
    public sealed class ExpressionPortrait
    {
        public EDialogueEmotion expression;
        [Tooltip("立绘 Resources 路径，如 OutGameUI/Guests/fox")] public string portraitPath;
    }

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

        [Header("性格数值（tick / 整数百分比，§11.3）")]
        [Tooltip("前台等搭话超时（tick）：等太久他自己走了。**不播对话、不扣声望**")]
        public int waitTalkTimeoutTicks = 1200;
        [Tooltip("服务中超时（tick）：**从他开口示意那一刻起算**，不是从进屋起算。\n" +
                 "超时不是「被拒绝」——他会说一句【需求反馈·失望】然后转停留，不扣声望，人也不当场走")]
        public int waitDeliverTimeoutTicks = 2400;
        [Tooltip("停留时长上限（tick）：需求了结（交付成功或超时）后在屋里待这么久，到点自行离开")]
        public int wanderMaxTicks = 3600;
        [Range(0, 100)]
        [Tooltip("⚠ **已无消费方**：跨天留宿 roll 已于 2026-08-14 删除（服务中/待分房都无条件跨天了，\n" +
                 "单给闲逛的掷一次骰子不一致），现在统一按停留时长走。留字段等下一轮种族表清理")]
        public int stayOvernightPercent = 20;

        // 需求生成（needTagWeights / needCountMin / needCountMax）已随 tag 需求体系退役（§9.1）：
        // 谁带什么需求来，改由访客日程表逐条配死（需求重做说明 §4.2），种族不再插手。

        [Header("表现（Resources 路径）")]
        [Tooltip("立绘差分表：表情枚举 → 贴图路径。表情枚举由对话系统定义（EDialogueEmotion，§9）")]
        public List<ExpressionPortrait> portraits = new List<ExpressionPortrait>();
        [Tooltip("序列帧前缀，实际资源为 前缀 + \"_await_sheet\"/\"_attack_sheet\" 的 PNG+JSON 组合")]
        public string sheetPath;

        // 对话池引用 dialoguePool 已于 2026-08-14 对话资源重构删除：
        // 对话内容改由一张 DialogueTable 整表承载，按 raceId 查表（Excel 第一页的「种族」列），
        // 不再需要每个种族挂一个 SO——也就顺带干掉了访客种族表 Excel 的「对话池」列
        // 与「改资产名即断引用」那个固有问题。

        /// <summary>
        /// 取表情立绘路径；该差分缺失时回落到平静（默认表情）并打 Warning，不阻断播放（对话设计说明 §4.1）。
        /// 平静也没配时回落空串，由调用方决定怎么表现（通常是不显示立绘）。
        /// </summary>
        public string GetPortraitPath(EDialogueEmotion expression = EDialogueEmotion.Calm)
        {
            string fallback = null;
            foreach (var entry in portraits)
            {
                if (entry == null) continue;
                if (entry.expression == expression) return entry.portraitPath;
                if (entry.expression == EDialogueEmotion.Calm) fallback = entry.portraitPath;
            }
            if (expression != EDialogueEmotion.Calm)
                Debug.LogWarning($"[VisitorRaceDef] 种族「{displayName}」缺少表情差分「{DialogueEmotionText.NameOf(expression)}」，" +
                                 "已回落平静（§4.1：缺差分不阻断播放）", this);
            return fallback ?? string.Empty;
        }
    }
}

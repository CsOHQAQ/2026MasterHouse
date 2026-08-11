using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 访客表情占位枚举：表情枚举由对话系统定义（单句对话自带表情字段，访客交付说明 §9）。
    /// 对话系统交付前先只留默认项；交付时以其定义为准替换本枚举。
    /// </summary>
    public enum EVisitorExpression
    {
        Default = 0, // 默认
    }

    /// <summary>需求权重项（§4.3）：从种族权重表按权重抽取需求 tag。</summary>
    [Serializable]
    public sealed class NeedTagWeight
    {
        public TagDef tag;
        [Tooltip("权重（>0 才参与抽取）")] public int weight = 1;
        [Tooltip("是否必要：required 需求未命中即「不对味」（§6.2）")] public bool required;
    }

    /// <summary>立绘差分表条目（§4.3）：表情枚举 → 贴图路径（Resources）。</summary>
    [Serializable]
    public sealed class ExpressionPortrait
    {
        public EVisitorExpression expression;
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
        [Tooltip("前台等搭话超时（tick）：超时按「被拒绝」口径结算并离开")] public int waitTalkTimeoutTicks = 1200;
        [Tooltip("服务中等交货超时（tick）：超时按「被拒绝」口径结算并离开")] public int waitDeliverTimeoutTicks = 2400;
        [Tooltip("闲逛时长上限（tick）：累计闲逛达到上限后自行离开")] public int wanderMaxTicks = 3600;
        [Range(0, 100)]
        [Tooltip("跨天留宿概率（%）：打烊时仍在闲逛的访客按此概率保留到次日")] public int stayOvernightPercent = 20;

        [Header("需求生成（§4.3/§6.2）")]
        [Tooltip("需求权重表：(tag, 权重, 是否必要)")] public List<NeedTagWeight> needTagWeights = new List<NeedTagWeight>();
        [Tooltip("需求数量范围 [min, max]")] public int needCountMin = 1;
        public int needCountMax = 2;

        [Header("表现（Resources 路径）")]
        [Tooltip("立绘差分表：表情枚举 → 贴图路径；表情枚举由对话系统定义（§9），当前仅默认表情")]
        public List<ExpressionPortrait> portraits = new List<ExpressionPortrait>();
        [Tooltip("序列帧前缀，实际资源为 前缀 + \"_await_sheet\"/\"_attack_sheet\" 的 PNG+JSON 组合")]
        public string sheetPath;

        [Header("对话（占位）")]
        [Tooltip("对话池引用：类型由对话系统交付时给出，先留字段占位（§4.3）")]
        public ScriptableObject dialoguePool;

        /// <summary>取表情立绘路径；无对应差分时回落默认表情，再回落空串。</summary>
        public string GetPortraitPath(EVisitorExpression expression = EVisitorExpression.Default)
        {
            string fallback = null;
            foreach (var entry in portraits)
            {
                if (entry == null) continue;
                if (entry.expression == expression) return entry.portraitPath;
                if (entry.expression == EVisitorExpression.Default) fallback = entry.portraitPath;
            }
            return fallback ?? string.Empty;
        }
    }
}

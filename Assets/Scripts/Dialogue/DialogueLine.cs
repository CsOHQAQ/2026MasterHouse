using System;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 说话人（设计说明 §4.1）。三者的表现区分由 DialogueOverlay 负责：
    /// 访客句 = 立绘差分 + 名字条；玩家句 = 无立绘、另一种框；旁白句 = 居中无框。
    /// </summary>
    public enum EDialogueSpeaker
    {
        /// <summary>访客说话：显示立绘（按 emotion 取差分）与名字条。</summary>
        Visitor = 0,
        /// <summary>玩家说话：不显示立绘，用另一种对话框样式。</summary>
        Player = 1,
        /// <summary>旁白：居中无框，用于环境描写与提示。</summary>
        Narration = 2,
    }

    /// <summary>
    /// 对话表情（设计说明 §4.1）：索引 VisitorRaceDef.portraits 的立绘差分表，仅访客句生效。
    /// 【待确认，§12】具体取值先给五个，等美术定下差分数量后再调整——
    /// 改动这个枚举会影响已配的差分表，增删项时留意资产。
    /// 取代访客系统重做期间的占位枚举 EVisitorExpression（那时对话系统尚未落地）。
    /// </summary>
    public enum EDialogueEmotion
    {
        Calm = 0,      // 平静（默认表情，差分缺失时的回退目标）
        Happy = 1,     // 高兴
        Confused = 2,  // 困惑
        Sad = 3,       // 失望
        Surprised = 4, // 惊讶
    }

    /// <summary>表情显示名（下标 = (int)EDialogueEmotion，编辑器与日志用）。</summary>
    public static class DialogueEmotionText
    {
        public static readonly string[] Names = { "平静", "高兴", "困惑", "失望", "惊讶" };

        public static string NameOf(EDialogueEmotion emotion)
        {
            var index = (int)emotion;
            return index >= 0 && index < Names.Length ? Names[index] : emotion.ToString();
        }
    }

    /// <summary>
    /// 对话单句（设计说明 §4.1）。也用作交付预览的内容单位——
    /// **预览存的是单句而不是对话组，所以从类型上就挂不了事件与分支**，
    /// 「预览绝不结算」是类型保证的，不是靠约定（§4.5）。
    /// </summary>
    [Serializable]
    public sealed class DialogueLine
    {
        public EDialogueSpeaker speaker;

        [TextArea(2, 5)]
        [Tooltip("台词正文。支持占位符（§9），播放时由 DialogueTextFormatter 替换：\n" +
                 "{需求}  访客这次的需求短语，如「甜的、软的食物」\n" +
                 "{访客名} 访客显示名\n" +
                 "{物品名} 物品显示名：交付预览单句取交付框里的候选物品，其余场合取已提交物品")]
        public string text;

        [Tooltip("立绘差分，仅访客句生效。种族没配这个差分时回退到平静并打 Warning，不阻断播放（§4.1）")]
        public EDialogueEmotion emotion;
    }
}

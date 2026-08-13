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
    ///
    /// 它是**种族无关的键**——正因如此，一组台词可以配给多个种族（甚至「通用」）共用，
    /// 每个访客顶着自己那张脸（2026-08-14 重构第 15 题）。Excel 第二页的「表情」列写的是
    /// 下面 Keys 里的英文 key，访客种族表的「立绘差分」列写的是中文名，两者由 DialogueEmotionText 对齐。
    ///
    /// 【待确认，§12】具体取值先给五个，等美术定下差分数量后再调整——
    /// 改动这个枚举会影响已配的差分表，增删项时留意资产。
    /// </summary>
    public enum EDialogueEmotion
    {
        Calm = 0,      // 平静（默认表情，差分缺失时的回退目标）
        Happy = 1,     // 高兴
        Confused = 2,  // 困惑
        Sad = 3,       // 失望
        Surprised = 4, // 惊讶
    }

    /// <summary>表情的英文 key（Excel 列值）与中文名（访客种族表 / 日志）。下标 = (int)EDialogueEmotion。</summary>
    public static class DialogueEmotionText
    {
        public static readonly string[] Keys = { "calm", "happy", "confused", "sad", "surprised" };
        public static readonly string[] Names = { "平静", "高兴", "困惑", "失望", "惊讶" };

        public static string KeyOf(EDialogueEmotion emotion)
        {
            var index = (int)emotion;
            return index >= 0 && index < Keys.Length ? Keys[index] : emotion.ToString();
        }

        public static string NameOf(EDialogueEmotion emotion)
        {
            var index = (int)emotion;
            return index >= 0 && index < Names.Length ? Names[index] : emotion.ToString();
        }

        /// <summary>英文 key 或中文名 → 枚举。无法识别时返回 false（导入器据此报出 Excel 行号）。</summary>
        public static bool TryParse(string raw, out EDialogueEmotion emotion)
        {
            emotion = EDialogueEmotion.Calm;
            if (string.IsNullOrWhiteSpace(raw)) return true; // 留空 = 平静
            var trimmed = raw.Trim();
            for (var i = 0; i < Keys.Length; i++)
                if (string.Equals(Keys[i], trimmed, System.StringComparison.OrdinalIgnoreCase))
                {
                    emotion = (EDialogueEmotion)i;
                    return true;
                }
            for (var i = 0; i < Names.Length; i++)
                if (Names[i] == trimmed)
                {
                    emotion = (EDialogueEmotion)i;
                    return true;
                }
            return false;
        }
    }

    /// <summary>说话人的英文 key（Excel 列值）与中文名。下标 = (int)EDialogueSpeaker。</summary>
    public static class DialogueSpeakerText
    {
        public static readonly string[] Keys = { "visitor", "player", "narration" };
        public static readonly string[] Names = { "访客", "玩家", "旁白" };

        public static string KeyOf(EDialogueSpeaker speaker)
        {
            var index = (int)speaker;
            return index >= 0 && index < Keys.Length ? Keys[index] : speaker.ToString();
        }

        public static string NameOf(EDialogueSpeaker speaker)
        {
            var index = (int)speaker;
            return index >= 0 && index < Names.Length ? Names[index] : speaker.ToString();
        }

        /// <summary>英文 key 或中文名 → 枚举。留空按「访客」处理。</summary>
        public static bool TryParse(string raw, out EDialogueSpeaker speaker)
        {
            speaker = EDialogueSpeaker.Visitor;
            if (string.IsNullOrWhiteSpace(raw)) return true;
            var trimmed = raw.Trim();
            for (var i = 0; i < Keys.Length; i++)
                if (string.Equals(Keys[i], trimmed, System.StringComparison.OrdinalIgnoreCase))
                {
                    speaker = (EDialogueSpeaker)i;
                    return true;
                }
            for (var i = 0; i < Names.Length; i++)
                if (Names[i] == trimmed)
                {
                    speaker = (EDialogueSpeaker)i;
                    return true;
                }
            return false;
        }
    }

    /// <summary>对话单句（设计说明 §4.1）：Excel 第二页里一行「类型 = Line」的内容。</summary>
    [Serializable]
    public sealed class DialogueLine
    {
        public EDialogueSpeaker speaker;

        [TextArea(2, 5)]
        [Tooltip("台词正文。支持占位符，播放时由 DialogueTextFormatter 替换：\n" +
                 "{需求}  访客这次的需求描述（直接取 NeedDef.description，由策划在需求资产里写死）\n" +
                 "{访客名} 访客显示名")]
        public string text;

        [Tooltip("立绘差分，仅访客句生效。种族没配这个差分时回退到平静并打 Warning，不阻断播放（§4.1）")]
        public EDialogueEmotion emotion;
    }
}

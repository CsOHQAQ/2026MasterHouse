using System;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 说话人（设计说明 §4.1）。三者的表现区分由 DialogueOverlay 负责：
    /// 访客句与玩家句都是 立绘（按 portraitId 查表）+ 名字凸台，只有名字与名字配色不同；
    /// 旁白句无立绘、名字留空、正文居中。
    /// </summary>
    public enum EDialogueSpeaker
    {
        /// <summary>访客说话：显示立绘（按 portraitId 查立绘表），名字凸台写访客名。</summary>
        Visitor = 0,
        /// <summary>
        /// 玩家（旅馆老板）说话：同样显示立绘，名字凸台写玩家名（取自 DialogueTuningConfig.playerRace）。
        /// 2026-08-19 羊族定为玩家之前这里是「不显示立绘」——老板那时还不是个有脸的角色。
        /// </summary>
        Player = 1,
        /// <summary>旁白：居中无框，用于环境描写与提示。</summary>
        Narration = 2,
    }

    // 表情枚举 EDialogueEmotion（calm/happy/confused/sad/surprised）与 DialogueEmotionText
    // 已于 2026-08-14 退役，由 DialogueLine.portraitId + PortraitTable 取代。
    //
    // 退役理由：枚举的前提是「差分能归类」。美术要求更多、且不太好归类的差分之后这个前提没了——
    // 硬塞进五个格子只会让策划在「这张算 happy 还是 surprised」上做无意义的选择题，
    // 而加一个差分要改代码、改枚举、动已配的资产。现在退回最朴素的形式：
    // 一张 ID → 路径的索引表（Excel/立绘表.xlsx），加差分 = 加一行。
    //
    // 连带作废的还有「一组台词多种族共用」：枚举是**种族无关**的键，所以通用组能让每个访客顶自己那张脸；
    // 立绘ID 是具体的，通用组会串脸。故对话表第一页的「种族」列不再接受「通用」与 `/` 多选，
    // 改为一个对话组只属于一个种族（见 DialogueCsvImporter.ResolveRace 与 CrossValidate）。

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
                if (string.Equals(Keys[i], trimmed, StringComparison.OrdinalIgnoreCase))
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

        [Tooltip("立绘ID（Excel/立绘表.xlsx 的主键）。访客句与玩家句都生效，旁白句不显示立绘。\n" +
                 "**留空 = 沿用上一句的立绘**（GVN 惯例：只在需要换表情时才填）；\n" +
                 "组内首句留空则用该访客种族的「默认立绘ID」。承接逻辑见 DialogueManager.CurrentPortraitId。\n" +
                 "填了的 ID 必须存在于立绘表——导表期硬校验，不留到运行时")]
        public string portraitId;
    }
}

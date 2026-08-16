using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 「修理电路」的课程包：把若干张 <see cref="LevelDef"/> 串成**一局连打**的教程（2026-08-16）。
    ///
    /// **它自己就是一张关卡**（继承 <see cref="MinigameLevelDef"/>），这是整件事能做到「框架零改动」的关键：
    /// 宿主 <c>MinigameOverlay</c>、需求点名（<c>MinigameNeedDef.level</c>）、关卡池全都不认识"多关"这个概念，
    /// 递进来的仍是一张 <c>MinigameLevelDef</c>，逐关推进整个发生在 <see cref="CircuitMinigame"/> 内部。
    ///
    /// 清单是**手工列表**而不是按命名约定（"带 Intro 的"）自动收集：
    /// 运行时拿不到 AssetDatabase，自动收集只能在编辑器里烘焙；而且命名约定一断就静默漏关，
    /// 教程漏一关是玩家卡死、不是少个提示。顺序 = 数组顺序。
    ///
    /// 一局的口径（2026-08-16 访谈拍板）：
    /// 每关**必须全亮**才能进下一关 → 唯一出口是打穿最后一关 → 分数恒为 100。
    /// 因此配套的 MinigameDef 四档阈值应配成 100/100/100，让"只有完美一个结局"在资产里也是显式的。
    ///
    /// 【务必独占本文件】ScriptableObject 必须与文件同名，否则 .asset 的脚本引用会损坏（见 RETRO）。
    /// </summary>
    [CreateAssetMenu(fileName = "Pack_", menuName = "MasterHouse/修理电路课程包", order = 31)]
    public sealed class CircuitLessonPackDef : MinigameLevelDef
    {
        [InspectorName("开发者备注")]
        [TextArea(2, 6)]
        [Tooltip("仅供策划与开发者记录课程编排思路；不参与任何运行时逻辑")]
        public string DeveloperNotes;

        [Tooltip("按顺序连打的课程。空条目（没配关卡的行）会被跳过；全空则开不了局")]
        public List<CircuitLessonEntry> Lessons = new List<CircuitLessonEntry>();
    }

    /// <summary>
    /// 课程包里的一行：关卡 + 给玩家看的教学文案。
    ///
    /// 文案配在**这里**而不是 <see cref="LevelDef"/> 上（2026-08-16 拍板）：
    /// 同一关在不同课程包里能说不同的话，也不用往关卡资产里塞只有教程才用得上的字段。
    /// （<c>LevelDef.DeveloperNotes</c> 是给策划自己看的，明确不参与运行时，两者不要混。）
    /// </summary>
    [Serializable]
    public sealed class CircuitLessonEntry
    {
        [Tooltip("本课要打的关卡")]
        public LevelDef Level;

        [Tooltip("课程标题，显示在教学栏顶部。留空则回落关卡资产名")]
        public string Title;

        [TextArea(2, 8)]
        [Tooltip("教学说明，显示在标题下方。留空则该区域空着")]
        public string Brief;
    }
}

using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 各小游戏关卡资产的共同基类（小游戏说明 §3.2）。
    ///
    /// **几乎无字段，且刻意少加字段**：它存在的首要理由是给 `MinigameDef.levels` 关卡池一个类型约束，
    /// 让宿主能在不认识任何具体小游戏的前提下把一张关卡递进 `IMinigame.Launch`。
    /// 想往这里加"所有小游戏都该有的字段"之前先问一句：宿主用得到吗？
    /// 用不到就属于具体小游戏自己的关卡类型（如修理电路的 `LevelDef`）。
    ///
    /// `tutorialImage` 通过了上面那问（2026-08-22 一轮测试改进 #2）：教程图遮罩由**宿主**在
    /// Launch 之前盖，具体小游戏对它一无所知，字段只能落在宿主看得见的这一层。
    ///
    /// 独立成文件而不是塞进 IMimigame.cs（说明文档 §3.2 的代码块是示意）：
    /// 与 `NeedDef` 同例——抽象 SO 基类各占一个文件，避免将来长出子类资产时踩
    /// 「ScriptableObject 必须与文件同名」的坑。
    /// </summary>
    public abstract class MinigameLevelDef : ScriptableObject
    {
        [Tooltip("开局教程图（留空不弹）：宿主在开局前盖整屏遮罩显示，点击任意处关闭后才真正开局。\n" +
                 "正式包内同一张关卡只弹首次（内存标记，不持久化）；Editor 与开发包每次开局都会弹，方便调图；课程包配的是整包一张")]
        public Sprite tutorialImage;
    }
}

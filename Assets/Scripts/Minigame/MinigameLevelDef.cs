using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 各小游戏关卡资产的共同基类（小游戏说明 §3.2）。
    ///
    /// **无字段，且刻意不加字段**：它存在的唯一理由是给 `MinigameDef.levels` 关卡池一个类型约束，
    /// 让宿主能在不认识任何具体小游戏的前提下把一张关卡递进 `IMinigame.Launch`。
    /// 想往这里加"所有小游戏都该有的字段"之前先问一句：宿主用得到吗？
    /// 用不到就属于具体小游戏自己的关卡类型（如修理电路的 `LevelDef`）。
    ///
    /// 独立成文件而不是塞进 IMimigame.cs（说明文档 §3.2 的代码块是示意）：
    /// 与 `NeedDef` 同例——抽象 SO 基类各占一个文件，避免将来长出子类资产时踩
    /// 「ScriptableObject 必须与文件同名」的坑。
    /// </summary>
    public abstract class MinigameLevelDef : ScriptableObject
    {
    }
}

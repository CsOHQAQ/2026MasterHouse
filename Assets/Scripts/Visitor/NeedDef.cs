using UnityEngine;

namespace MasterHouse
{
    /// <summary>访客需求的两种类型（访客需求重做说明 §4.1）。</summary>
    public enum ENeedType
    {
        /// <summary>出现这个说明基类没被正确覆写。</summary>
        None = 0,
        /// <summary>条件类：所住房间里存在指定家具之一即通过。</summary>
        Condition = 1,
        /// <summary>小游戏类：玩一局小游戏，按分数定满意度（本包只占位，§7）。</summary>
        Minigame = 2,
    }

    /// <summary>
    /// 访客需求定义基类（Model 层，运行时只读；访客需求重做说明 §4.1）。一条需求一个 SO 资产。
    ///
    /// 多态用「抽象基类 + 子类」而不是「一个类塞两套字段 + 枚举开关」——与 NodeDef/ResourceNodeDef 同构，
    /// 项目已有先例：不该出现的字段在 Inspector 里就不该看得见。
    ///
    /// 需求**零随机**：谁在第几天几点带什么需求来完全由日程表配死（§4.2），
    /// 访客重做期的 tag 权重 roll（VisitorRaceDef.needTagWeights + VisitorManager.RollNeeds）已随本包退役。
    ///
    /// 【务必独占本文件】ScriptableObject 必须与文件同名，否则 .asset 的脚本引用会损坏（见 RETRO）。
    /// </summary>
    public abstract class NeedDef : ScriptableObject
    {
        [Tooltip("稳定键（日志/存档/编辑器索引用）")]
        public string needId;

        [TextArea]
        [Tooltip("需求描述：访客说出来的那句话，同时用于任务卡展示。对话里用 {需求} 占位符引用")]
        public string description;

        public abstract ENeedType NeedType { get; }

        /// <summary>日志用标识：优先 needId，未填时回落资产名。</summary>
        public string DisplayId => string.IsNullOrEmpty(needId) ? name : needId;
    }
}

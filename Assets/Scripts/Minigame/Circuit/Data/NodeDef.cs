using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>节点类型（小游戏说明 §4）。Processor / Storage 随物资链退役已删除。</summary>
    public enum ENodeType
    {
        None,       // 出现这个说明你忘了配置
        Resource,   // 电源：按各输出 Pin 的 MaxRate 供电
        Transit,    // 中转：十字 / 分流 / 合流，靠 PinDef.PinGroup 分组（§4.7）
        Condition,  // 电池：收到的电量与 RequiredAmount 一致即点亮
    }

    /// <summary>
    /// 节点定义基类（Model 层，运行时只读）。
    /// NodeType 用虚属性而非字段——Unity 无法序列化 readonly 字段，子类字段隐藏也不是覆写。
    /// </summary>
    public abstract class NodeDef : ScriptableObject
    {
        public abstract ENodeType NodeType { get; }

        public string DisplayName;

        [Tooltip("占格形状（相对坐标）。判定必须逐格查询，不得假设矩形")]
        public GridGroup Shape = new GridGroup();

        [Tooltip("Pin 布置：每个 Pin 在节点上的位置与朝向，策划手摆")]
        public List<PinLayout> Pins = new List<PinLayout>();
    }
}

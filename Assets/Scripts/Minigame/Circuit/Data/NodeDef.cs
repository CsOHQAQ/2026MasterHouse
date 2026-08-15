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

        [Header("视觉（节点本体）")]
        [Tooltip("节点底图。使用同一张九宫格 Sprite 即可覆盖不同的矩形尺寸；留空时沿用占位色格子渲染。")]
        public Sprite BackgroundSprite;

        [Tooltip("节点功能图标。运行时在底图可用区域内等比缩放，不承载 Pin 或交互状态。")]
        public Sprite FunctionIconSprite;

        [Tooltip("底图染色；未配置背景图时也会作为节点的基础色。")]
        public Color BackgroundColor = Color.white;

        [Tooltip("功能图标染色。通常保持白色，必要时可按节点单独覆写。")]
        public Color IconColor = Color.white;

        [Tooltip("占格形状（相对坐标）。判定必须逐格查询，不得假设矩形")]
        public GridGroup Shape = new GridGroup();

        [Tooltip("Pin 布置：每个 Pin 在节点上的位置与朝向，策划手摆")]
        public List<PinLayout> Pins = new List<PinLayout>();
    }
}

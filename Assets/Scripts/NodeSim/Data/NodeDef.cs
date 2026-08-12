using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>节点类型（§7 四类；原 Collector 改名 Storage 统一）。</summary>
    public enum ENodeType
    {
        None,       // 出现这个说明你忘了配置
        Resource,   // 资源型：无输入，按速率生产，自身暂存满则停产
        Processor,  // 加工型：消耗输入暂存推进配方，产出入输出暂存
        Storage,    // 仓库型：漏斗而非容器，收到即计入全局 PlayerCargo
        Transit,    // 中转型：无配方转运，配对 Pin 实现"立交"
        Condition,  // 条件型：检查上游供给速率是否达标，是家具「修好」的判据
    }

    /// <summary>
    /// 节点定义基类（Model 层，运行时只读）。
    /// NodeType 用虚属性而非字段——Unity 无法序列化 readonly 字段，
    /// 子类字段隐藏也不是覆写（§12 已知差距的修正）。
    /// </summary>
    public abstract class NodeDef : ScriptableObject
    {
        public abstract ENodeType NodeType { get; }

        public string DisplayName;

        [Tooltip("占格形状（相对坐标）。判定必须逐格查询，不得假设矩形（§4.1）")]
        public GridGroup Shape = new GridGroup();
        
        [Tooltip("Pin 布置：每个 Pin 在节点上的位置与朝向，策划手摆（§6.1）")]
        public List<PinLayout> Pins = new List<PinLayout>();
    }
}
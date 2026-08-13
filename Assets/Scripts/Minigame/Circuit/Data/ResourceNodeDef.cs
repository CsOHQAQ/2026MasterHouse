using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 电源（小游戏说明 §4.2 修订版）：可配多个输出 Pin，每个 Pin 按自己的 MaxRate 各供一条线。
    /// 电源提供的总电量 = 各输出 Pin 的 MaxRate 之和，没有独立的"总闸"字段。
    /// </summary>
    [CreateAssetMenu(fileName = "电源", menuName = "MasterHouse/节点/电源", order = 20)]
    public class ResourceNodeDef : NodeDef
    {
        public override ENodeType NodeType => ENodeType.Resource;

        /// <summary>
        /// 【已弃用，勿配】说明文档 §5.2 原定的"提供几份电"。
        /// 份额制（一条链接 = 一份电）在落地访谈中被推翻，改为「一个 Pin 一条线 + Pin 自带 MaxRate」，
        /// 本字段随之失去意义：电源总量已由各输出 Pin 的 MaxRate 表达。
        /// 保留字段只为让文档 §5.2 与代码可对照，不参与任何运算；HideInInspector 防策划误配。
        /// </summary>
        [HideInInspector] public int OutputCount;
    }
}

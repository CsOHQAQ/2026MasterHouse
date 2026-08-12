using System;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// [SerializeReference] 字段的「选择子类」标记（设计说明 §11.1，必做前置）。
    /// Unity 原生不给 [SerializeReference] 字段任何选择具体类型的 Inspector UI——
    /// 不挂本特性 + 对应抽屉（Dialogue/Editor/SubclassSelectorDrawer），
    /// 对话的事件与条件在 Inspector 里根本加不进去。
    ///
    /// 用法：
    ///   [SerializeReference, SubclassSelector]
    ///   public List&lt;IGameplayAction&gt; actions = new List&lt;IGameplayAction&gt;();
    /// 标在 List 字段上即可——Unity 对数组/列表字段会把 PropertyDrawer 逐元素调用。
    ///
    /// 本特性与抽屉本身与对话无耦合、全项目通用；按 §3「不预设抽象」暂放 Dialogue/，
    /// 出现第二个消费方（成就/任务/家具解禁）时整体平移进 Core/——同一命名空间下是零成本操作。
    /// </summary>
    public sealed class SubclassSelectorAttribute : PropertyAttribute
    {
    }

    /// <summary>
    /// 子类在选择菜单里的显示名，支持用「/」分组（如 "访客/接待"）。不挂则回落类名。
    /// 策划面对的是这个名字，不是 C# 类名——中文文案见 §16.6。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class SubclassLabelAttribute : Attribute
    {
        public string Label { get; }

        public SubclassLabelAttribute(string label)
        {
            Label = label;
        }
    }
}

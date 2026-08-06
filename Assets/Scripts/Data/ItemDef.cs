using System;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>物资定义（Model 层，运行时只读）。</summary>
    [CreateAssetMenu(fileName = "物资", menuName = "MasterHouse/物资定义", order = 10)]
    public class ItemDef : ScriptableObject
    {
        [Tooltip("展示给玩家的物资名")]
        public string DisplayName;

        /// <summary>占位表现用色（无美术素材阶段，Pin/连线/脉冲着色用）。</summary>
        [Tooltip("占位表现用色：无美术素材阶段用于 Pin / 连线 / 脉冲着色，节点编辑器的 Pin 标记同样取该色")]
        public Color DisplayColor = Color.white;
    }

    /// <summary>物资 + 数量，配方与稳态表通用的条目结构。</summary>
    [Serializable]
    public struct ItemStack
    {
        public ItemDef Item;
        public int Count;
    }
}
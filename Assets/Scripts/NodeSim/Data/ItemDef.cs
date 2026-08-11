using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>物资定义（Model 层，运行时只读）。局内局外共用：招待访客即 PlayerCargoData 的消费出口（访客交付说明 §4.2）。</summary>
    [CreateAssetMenu(fileName = "物资", menuName = "MasterHouse/物资定义", order = 10)]
    public class ItemDef : ScriptableObject
    {
        [Tooltip("展示给玩家的物资名")]
        public string DisplayName;

        /// <summary>占位表现用色（无美术素材阶段，Pin/连线/脉冲着色用）。</summary>
        [Tooltip("占位表现用色：无美术素材阶段用于 Pin / 连线 / 脉冲着色，节点编辑器的 Pin 标记同样取该色")]
        public Color DisplayColor = Color.white;

        [Tooltip("物品标签（访客需求匹配用，§4.1/§4.2）：只需标最具体的叶子，需求 tag 命中其任意祖先")]
        public List<TagDef> tags = new List<TagDef>();
    }

    /// <summary>物资 + 数量，配方与稳态表通用的条目结构。</summary>
    [Serializable]
    public struct ItemStack
    {
        public ItemDef Item;
        public int Count;
    }
}
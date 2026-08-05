using System;
using UnityEngine;

namespace MasterHouse
{
    public enum EPinDirection
    {
        /// <summary>未配置——仅中转节点的配对 Pin 允许，方向随连接运行时同步（§6.3）。</summary>
        None = 0,
        Input,
        Output,
    }

    /// <summary>
    /// Pin 定义（§6.1）：节点上的连接口。
    /// Pin 自身没有位置概念，位置/朝向由所在节点的 PinLayout 记录。
    /// </summary>
    [Serializable]
    public class PinDef
    {
        [Tooltip("传输的物资种类；中转节点的配对 Pin 可留空，运行时随连接同步（§6.3）")]
        public ItemDef ItemType;

        [Tooltip("单次请求的数量上限，「接口访问限流」语义（§6.2）")]
        public int MaxRate = 1;

        public EPinDirection Direction;

        [Tooltip("仅中转节点使用：指向同节点 Pins 列表中配对 Pin 的索引；-1 = 无配对")]
        public int PairedPinIndex = -1;
    }

    /// <summary>Pin 在节点上的布置（策划手摆，§6.1）。</summary>
    [Serializable]
    public class PinLayout
    {
        public PinDef Pin = new PinDef();

        [Tooltip("Pin 所在的节点本地格（相对节点原点）")]
        public Vector2Int LocalCell;

        [Tooltip("Pin 朝向节点外的哪一边——链接从该方向的相邻格接入")]
        public EDirection4 Facing;
    }
}
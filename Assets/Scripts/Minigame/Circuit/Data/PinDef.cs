using System;
using UnityEngine;

namespace MasterHouse
{
    public enum EPinDirection
    {
        /// <summary>未配置——方向随连接在运行时确定（§4.7 十字件靠它实现「哪边进都行」）。</summary>
        None = 0,
        Input,
        Output,
    }

    /// <summary>
    /// Pin 定义：节点上的接线口。
    /// Pin 自身没有位置概念，位置/朝向由所在节点的 PinLayout 记录。
    ///
    /// **一个 Pin 至多接一条线**（小游戏说明 §4.2 修订版），所以"这个口通多少电"是 Pin 的属性，
    /// 而不是靠"接几条线"表达——后者是被推翻的份额制模型。
    /// </summary>
    [Serializable]
    public class PinDef
    {
        [Tooltip("本 Pin 输出多少电。**仅电源的输出 Pin 有效**：" +
                 "中转/分流/合流一律不限流，它们的输出量由分组公式算出（§4.7）")]
        public int MaxRate = 1;

        [Tooltip("接线方向。电源固定 Output、电池固定 Input、分流合流由策划配死；" +
                 "十字件留 None，运行时随第一条接上的线确定")]
        public EPinDirection Direction;

        [Tooltip("中转件分组号，-1 = 不属于任何分组。\n" +
                 "同组内按方向自动分进出，输出量 = floor(组内输入之和 / 组内输出口总数)：\n" +
                 "  十字件 = 两个组各 1 进 1 出（floor(x/1) = x，直通）\n" +
                 "  分流器 = 一个组 1 进 N 出\n" +
                 "  合流器 = 一个组 N 进 1 出\n" +
                 "注意分母是**输出口总数**，不是实际接了线的口数——没接线的口那一份会浪费掉")]
        public int PinGroup = -1;
    }

    /// <summary>Pin 在节点上的布置（策划手摆）。</summary>
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

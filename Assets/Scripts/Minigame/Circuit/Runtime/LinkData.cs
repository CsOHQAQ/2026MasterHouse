using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 一条导线。只能由 LinkManager 与 CircuitSolver 修改。
    ///
    /// 旧的 ELinkState 状态机（Idle/InTransit/Blocked/Broken/TypeInvalid）已整体删除：
    /// 供电是纯函数一次求解、没有时间维度，所以没有"在途""阻塞"；
    /// 而"断线"这一态也不存在了——移动中转件时附着的线**直接删除并退还预算**
    /// （落地访谈拍板），玩家重画即可，不留半死不活的对象。
    /// </summary>
    public class LinkData
    {
        /// <summary>稳定自增 Id，一切链接遍历的排序键。</summary>
        public readonly long LinkId;

        /// <summary>源 Pin（输出侧）。</summary>
        public readonly PinData FromPin;

        /// <summary>目标 Pin（输入侧）。</summary>
        public readonly PinData ToPin;

        /// <summary>玩家手绘的折线途径格（画布坐标，from→to 有序）。导线预算按 Count 计。</summary>
        public List<Vector2Int> PathCells = new List<Vector2Int>();

        /// <summary>本条线携带的电量，由 CircuitSolver 每次求解重写。链接本身不设载量上限。</summary>
        public int Power;

        public LinkData(long linkId, PinData fromPin, PinData toPin)
        {
            LinkId = linkId;
            FromPin = fromPin;
            ToPin = toPin;
        }
    }
}

using System;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>格子类型。具体分类策划未细化，先只留默认类型占位。</summary>
    public enum EGridType
    {
        Default = 0,
    }

    /// <summary>
    /// Model 层最小单位（§4.1）：仅含相对坐标与格子类型。
    /// 不含任何运行时状态——占用与否归 LevelData 的占用索引（§10、§12）。
    /// </summary>
    [Serializable]
    public struct GridData
    {
        /// <summary>相对所属 GridGroup 原点的坐标。</summary>
        public Vector2Int DeltaPosition;

        public EGridType Type;
    }

    /// <summary>四方向（Pin 朝向、布线方向共用）。</summary>
    public enum EDirection4
    {
        Up = 0,
        Right = 1,
        Down = 2,
        Left = 3,
    }

    public static class Direction4
    {
        /// <summary>固定遍历顺序：上右下左。A* 等一切方向遍历统一用这个数组，保证确定性（§11）。</summary>
        public static readonly Vector2Int[] Offsets =
        {
            new Vector2Int(0, 1),
            new Vector2Int(1, 0),
            new Vector2Int(0, -1),
            new Vector2Int(-1, 0),
        };

        public static Vector2Int ToOffset(EDirection4 dir) => Offsets[(int)dir];
    }
}
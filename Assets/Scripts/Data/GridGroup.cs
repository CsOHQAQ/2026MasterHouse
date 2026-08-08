using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 一组格子，承担形状描述与基础查询（§4.1）。
    /// 节点占格形状、画布形状都用它表达。形状可能是非规则的，
    /// 一切占格判定必须逐格查询，不得假设矩形。
    /// </summary>
    [Serializable]
    public class GridGroup
    {
        public List<GridData> Grids = new List<GridData>();

        /// <summary>形状是否包含指定相对坐标。</summary>
        public bool ContainsDelta(Vector2Int delta)
        {
            foreach (var g in Grids)
                if (g.DeltaPosition == delta)
                    return true;
            return false;
        }

        /// <summary>以 origin 为原点，枚举形状覆盖的所有全局格坐标（按 Grids 列表顺序，稳定）。</summary>
        public IEnumerable<Vector2Int> CellsAt(Vector2Int origin)
        {
            foreach (var g in Grids)
                yield return origin + g.DeltaPosition;
        }
    }
}
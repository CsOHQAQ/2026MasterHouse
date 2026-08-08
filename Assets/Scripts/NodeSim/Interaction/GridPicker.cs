using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 数学拾取工具（需求记录·决策 6）：不挂 Collider——
    /// 屏幕坐标换算 + 转手查 LevelData 占用索引 + 按 Pin 布局计算命中。
    /// 仅供 Controller 层使用；拾取属 Controller 职责，不新增 Manager。
    /// </summary>
    public static class GridPicker
    {
        /// <summary>Pin 拾取半径（格尺寸的倍数）：光标离 Pin 标记中心小于该距离才命中。</summary>
        private const float PinPickRadiusFactor = 0.35f;

        public static Vector3 ScreenToWorld(Camera cam, Vector3 screenPos)
        {
            var world = cam.ScreenToWorldPoint(screenPos);
            world.z = 0f;
            return world;
        }

        public static Vector2Int WorldToCell(Vector3 world)
        {
            float s = ViewUtil.GridSize;
            return new Vector2Int(
                Mathf.FloorToInt(world.x / s),
                Mathf.FloorToInt(world.y / s));
        }

        public static Vector2Int ScreenToCell(Camera cam, Vector3 screenPos) =>
            WorldToCell(ScreenToWorld(cam, screenPos));

        /// <summary>拾取格上的节点（转手查占用索引）。</summary>
        public static NodeData PickNode(LevelData level, Vector2Int cell)
        {
            var occupant = level?.GetOccupant(cell);
            return occupant?.Node;
        }

        /// <summary>拾取格上的链接（转手查占用索引）。</summary>
        public static LinkData PickLink(LevelData level, Vector2Int cell)
        {
            var occupant = level?.GetOccupant(cell);
            return occupant?.Link;
        }

        /// <summary>
        /// 拾取 Pin：按 Pin 布局计算各 Pin 标记的世界坐标，取半径内最近者；
        /// 距离并列时按 NodeId、Pin 索引稳定序保留先者。
        /// </summary>
        public static PinData PickPin(LevelData level, Vector3 world)
        {
            if (level == null) return null;

            float radius = ViewUtil.GridSize * PinPickRadiusFactor;
            PinData best = null;
            float bestSqr = radius * radius;

            foreach (var node in level.Nodes) // 按 NodeId 稳定序
            {
                for (int i = 0; i < node.Pins.Count; i++)
                {
                    float sqr = ((Vector2)(world - PinMarkWorldPos(node, i))).sqrMagnitude;
                    if (sqr < bestSqr) // 严格小于：并列时保留先遍历到的
                    {
                        bestSqr = sqr;
                        best = node.Pins[i];
                    }
                }
            }
            return best;
        }

        /// <summary>Pin 标记的世界坐标：所在格中心向朝向外推半格（与 NodeGO.BuildPins 同一约定）。</summary>
        public static Vector3 PinMarkWorldPos(NodeData node, int pinIndex)
        {
            var layout = node.Def.Pins[pinIndex];
            var toward = Direction4.ToOffset(layout.Facing);
            var cell = node.Origin + layout.LocalCell;
            float s = ViewUtil.GridSize;
            return new Vector3(
                (cell.x + 0.5f + toward.x * 0.5f) * s,
                (cell.y + 0.5f + toward.y * 0.5f) * s, 0f);
        }
    }
}

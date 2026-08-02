using System.Collections.Generic;
using UnityEngine;

namespace MasterPotion
{
    /// <summary>
    /// 连线寻路：在画布空闲单元格上做 4 向 A*（带转弯惩罚，偏好直线），
    /// 返回沿格子中心的折线；画布内无可行路径时返回 null。
    /// </summary>
    public static class LinkRouter
    {
        private static readonly Vector2Int[] Dirs =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0),
            new Vector2Int(0, 1), new Vector2Int(0, -1),
        };

        private const float TurnPenalty = 0.1f;

        /// <summary>计算 from 端口到 to 端口的折线路径（含两端端口坐标）。</summary>
        public static List<Vector3> Route(Port from, Port to)
        {
            var board = BoardGrid.Instance;
            if (board == null || from == null || to == null) return null;

            // 端口位于卡片左/右边缘，起步格 = 端口外侧紧邻的格子
            int fromDir = OutwardDirIndex(from);
            int toDir = OutwardDirIndex(to);
            var start = BoardGrid.WorldToCell(
                (Vector2)from.transform.position + (Vector2)Dirs[fromDir] * 0.5f);
            var goal = BoardGrid.WorldToCell(
                (Vector2)to.transform.position + (Vector2)Dirs[toDir] * 0.5f);

            if (!Passable(board, start) || !Passable(board, goal)) return null;

            var cellPath = AStar(board, start, goal, fromDir);
            if (cellPath == null) return null;

            var points = new List<Vector3> { from.transform.position };
            foreach (var c in cellPath) points.Add(BoardGrid.CellCenter(c));
            points.Add(to.transform.position);
            SimplifyCollinear(points);
            return points;
        }

        private static bool Passable(BoardGrid board, Vector2Int cell) =>
            board.HasCell(cell) && !board.IsOccupied(cell);

        private static int OutwardDirIndex(Port port) =>
            port.transform.localPosition.x >= 0f ? 0 : 1; // 右边缘朝右出，左边缘朝左出

        private static List<Vector2Int> AStar(BoardGrid board, Vector2Int start, Vector2Int goal, int startDir)
        {
            // 状态 = (格子, 进入方向)，转弯加罚分以获得更平直的走线
            var open = new MinHeap();
            var gScore = new Dictionary<(Vector2Int, int), float>();
            var parent = new Dictionary<(Vector2Int, int), (Vector2Int, int)>();

            var s0 = (start, startDir);
            gScore[s0] = 0f;
            open.Push(Heuristic(start, goal), s0);

            while (open.Count > 0)
            {
                var cur = open.Pop();
                if (cur.cell == goal) return Reconstruct(parent, cur);

                float g = gScore[cur];
                for (int d = 0; d < 4; d++)
                {
                    var next = cur.cell + Dirs[d];
                    if (!Passable(board, next)) continue;

                    float ng = g + 1f + (d == cur.dir ? 0f : TurnPenalty);
                    var ns = (next, d);
                    if (gScore.TryGetValue(ns, out var old) && old <= ng) continue;

                    gScore[ns] = ng;
                    parent[ns] = cur;
                    open.Push(ng + Heuristic(next, goal), ns);
                }
            }
            return null;
        }

        private static float Heuristic(Vector2Int a, Vector2Int b) =>
            Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

        private static List<Vector2Int> Reconstruct(
            Dictionary<(Vector2Int, int), (Vector2Int, int)> parent, (Vector2Int cell, int dir) end)
        {
            var cells = new List<Vector2Int> { end.cell };
            var cur = end;
            while (parent.TryGetValue(cur, out var prev))
            {
                cur = prev;
                cells.Add(cur.Item1);
            }
            cells.Reverse();
            return cells;
        }

        private static void SimplifyCollinear(List<Vector3> pts)
        {
            for (int i = pts.Count - 2; i > 0; i--)
            {
                var a = pts[i - 1];
                var b = pts[i];
                var c = pts[i + 1];
                float cross = (b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x);
                if (Mathf.Abs(cross) < 1e-4f) pts.RemoveAt(i);
            }
        }

        /// <summary>按 f 值取最小的简易二叉堆。</summary>
        private class MinHeap
        {
            private readonly List<(float f, (Vector2Int cell, int dir) state)> items = new();

            public int Count => items.Count;

            public void Push(float f, (Vector2Int, int) state)
            {
                items.Add((f, state));
                int i = items.Count - 1;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (items[p].f <= items[i].f) break;
                    (items[p], items[i]) = (items[i], items[p]);
                    i = p;
                }
            }

            public (Vector2Int cell, int dir) Pop()
            {
                var top = items[0].state;
                int last = items.Count - 1;
                items[0] = items[last];
                items.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1, r = l + 1, min = i;
                    if (l < items.Count && items[l].f < items[min].f) min = l;
                    if (r < items.Count && items[r].f < items[min].f) min = r;
                    if (min == i) break;
                    (items[min], items[i]) = (items[i], items[min]);
                    i = min;
                }
                return top;
            }
        }
    }
}

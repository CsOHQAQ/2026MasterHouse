using UnityEditor;
using UnityEngine;

namespace MasterHouse.EditorTools
{
    /// <summary>节点编辑器 / 关卡编辑器共用的画布绘制原语。</summary>
    public static class CanvasDrawUtil
    {
        /// <summary>中转件分组配色：同一件上的不同分组要一眼分得开（十字件的上下组 vs 左右组）。</summary>
        static readonly Color[] GroupColors =
        {
            new Color(0.95f, 0.75f, 0.25f), // 0 黄
            new Color(0.35f, 0.80f, 0.95f), // 1 青
            new Color(0.95f, 0.45f, 0.75f), // 2 粉
            new Color(0.55f, 0.90f, 0.45f), // 3 绿
        };

        /// <summary>
        /// Pin 标记配色（物资链退役后的新口径）：
        /// 中转件按分组号取色——分组是它唯一的语义；其余节点按方向取色。
        /// </summary>
        public static Color PinColor(NodeDef owner, PinDef pin)
        {
            if (pin == null) return Color.gray;
            if (owner is TransitNodeDef)
            {
                if (pin.PinGroup < 0) return new Color(0.9f, 0.3f, 0.3f); // 未分组 = 红，校验也会报
                return GroupColors[pin.PinGroup % GroupColors.Length];
            }
            switch (pin.Direction)
            {
                case EPinDirection.Output: return new Color(0.40f, 0.85f, 0.45f); // 供电 = 绿
                case EPinDirection.Input: return new Color(0.45f, 0.65f, 0.95f);  // 收电 = 蓝
                default: return new Color(0.70f, 0.70f, 0.70f);
            }
        }

        /// <summary>画矩形描边（四条细边）。</summary>
        public static void DrawBorder(Rect r, float w, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, w), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - w, r.width, w), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, w, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - w, r.y, w, r.height), c);
        }

        /// <summary>格子朝向边的中点（GUI 坐标；格 Up = GUI 上边即 yMin）。</summary>
        public static Vector2 EdgeMid(Rect r, EDirection4 facing)
        {
            switch (facing)
            {
                case EDirection4.Up: return new Vector2(r.center.x, r.yMin);
                case EDirection4.Right: return new Vector2(r.xMax, r.center.y);
                case EDirection4.Down: return new Vector2(r.center.x, r.yMax);
                default: return new Vector2(r.xMin, r.center.y);
            }
        }

        /// <summary>朝向的 GUI 空间外法线。</summary>
        public static Vector2 EdgeOutward(EDirection4 facing)
        {
            switch (facing)
            {
                case EDirection4.Up: return new Vector2(0, -1);
                case EDirection4.Right: return new Vector2(1, 0);
                case EDirection4.Down: return new Vector2(0, 1);
                default: return new Vector2(-1, 0);
            }
        }

        /// <summary>
        /// 在格子朝向边上画 Pin 标记：输出=指向外的三角，输入=指向内的三角，
        /// 同步（中转配对 Pin）=菱形；选中时加白色描边。标记大小随格子尺寸缩放。
        /// </summary>
        public static void DrawPinMarker(Rect cellRect, EDirection4 facing, EPinDirection dir, Color color, bool selected)
        {
            Vector2 mid = EdgeMid(cellRect, facing);
            Vector2 outward = EdgeOutward(facing);
            Vector2 tangent = new Vector2(-outward.y, outward.x);
            float s = cellRect.width * 0.26f;

            Vector3[] pts;
            if (dir == EPinDirection.Output)
            {
                pts = new Vector3[]
                {
                    mid + outward * s,
                    mid - outward * s * 0.3f + tangent * s * 0.8f,
                    mid - outward * s * 0.3f - tangent * s * 0.8f,
                };
            }
            else if (dir == EPinDirection.Input)
            {
                pts = new Vector3[]
                {
                    mid - outward * s,
                    mid + outward * s * 0.3f + tangent * s * 0.8f,
                    mid + outward * s * 0.3f - tangent * s * 0.8f,
                };
            }
            else
            {
                pts = new Vector3[]
                {
                    mid + outward * s * 0.8f,
                    mid + tangent * s * 0.8f,
                    mid - outward * s * 0.8f,
                    mid - tangent * s * 0.8f,
                };
            }

            Handles.color = color;
            Handles.DrawAAConvexPolygon(pts);

            if (selected)
            {
                var loop = new Vector3[pts.Length + 1];
                pts.CopyTo(loop, 0);
                loop[pts.Length] = pts[0];
                Handles.color = Color.white;
                Handles.DrawAAPolyLine(3f, loop);
            }
        }
    }
}

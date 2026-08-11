using UnityEditor;
using UnityEngine;

namespace MasterHouse.EditorTools
{
    /// <summary>节点编辑器 / 关卡编辑器共用的画布绘制原语。</summary>
    public static class CanvasDrawUtil
    {
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

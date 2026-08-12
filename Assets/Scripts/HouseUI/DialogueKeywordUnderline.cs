using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 对白关键词下划线（顶点特效）：Legacy UI Text 不支持 u 标签，
    /// 本效果扫描文本网格，把「标红字符」（DialogueTextPlayer 的关键词色）连成段并在其下方追加下划线条。
    /// 随打字机逐字出现，下划线自然跟着延伸（即规格里的下划线出现动效）。
    /// 运行时由 DialogueTextPlayer 挂到 Text 所在物体，不改 Prefab 资产。
    /// </summary>
    [RequireComponent(typeof(Text))]
    public sealed class DialogueKeywordUnderline : BaseMeshEffect
    {
        private const float Thickness = 2.5f;
        private const float Offset = 4f;

        private static readonly List<UIVertex> Buffer = new List<UIVertex>();

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;
            Buffer.Clear();
            vh.GetUIVertexStream(Buffer); // 三角形流：每字符 6 顶点（两三角）

            var underlines = new List<(float xMin, float xMax, float y, Color32 color)>();
            float runStart = 0f, runEnd = 0f, runY = float.MaxValue;
            Color32 runColor = default;
            var inRun = false;

            for (var i = 0; i + 5 < Buffer.Count; i += 6)
            {
                var color = Buffer[i].color;
                var isKeyword = IsKeywordColor(color);
                float xMin = float.MaxValue, xMax = float.MinValue, yMin = float.MaxValue;
                for (var v = 0; v < 6; v++)
                {
                    var position = Buffer[i + v].position;
                    if (position.x < xMin) xMin = position.x;
                    if (position.x > xMax) xMax = position.x;
                    if (position.y < yMin) yMin = position.y;
                }
                // 同一行的连续标红字符并成一条线（换行时 y 突变则断开）
                if (isKeyword && inRun && Mathf.Abs(yMin - runY) < 2f && xMin >= runStart)
                {
                    runEnd = Mathf.Max(runEnd, xMax);
                    runY = Mathf.Min(runY, yMin);
                }
                else
                {
                    if (inRun) underlines.Add((runStart, runEnd, runY, runColor));
                    inRun = isKeyword;
                    if (isKeyword)
                    {
                        runStart = xMin;
                        runEnd = xMax;
                        runY = yMin;
                        runColor = color;
                    }
                }
            }
            if (inRun) underlines.Add((runStart, runEnd, runY, runColor));

            foreach (var (xMin, xMax, y, color) in underlines)
            {
                if (xMax - xMin < 1f) continue;
                AddQuad(vh, xMin, xMax, y - Offset, color);
            }
        }

        private static bool IsKeywordColor(Color32 color)
        {
            // DialogueTextPlayer.KeywordColor = #E22D76：红高绿低即认定为关键词字符
            return color.r > 190 && color.g < 110;
        }

        private static void AddQuad(VertexHelper vh, float xMin, float xMax, float top, Color32 color)
        {
            var start = vh.currentVertCount;
            var vertex = UIVertex.simpleVert;
            vertex.color = color;
            vertex.position = new Vector3(xMin, top, 0f); vh.AddVert(vertex);
            vertex.position = new Vector3(xMax, top, 0f); vh.AddVert(vertex);
            vertex.position = new Vector3(xMax, top - Thickness, 0f); vh.AddVert(vertex);
            vertex.position = new Vector3(xMin, top - Thickness, 0f); vh.AddVert(vertex);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}

using UnityEngine;
using UnityEngine.UI;

namespace MasterPotion
{
    /// <summary>给旧版 uGUI Text 补上网页 letter-spacing 能力。</summary>
    public sealed class OutGameLetterSpacing : BaseMeshEffect
    {
        public float spacing;

        public override void ModifyMesh(VertexHelper vertexHelper)
        {
            if (!IsActive() || spacing == 0 || vertexHelper.currentVertCount == 0) return;
            var label = graphic as Text;
            if (label == null || string.IsNullOrEmpty(label.text)) return;

            var glyphCount = Mathf.Min(label.text.Length, vertexHelper.currentVertCount / 4);
            var center = (glyphCount - 1) * .5f;
            var vertex = new UIVertex();
            for (var glyph = 0; glyph < glyphCount; glyph++)
            {
                var offset = (glyph - center) * spacing;
                for (var corner = 0; corner < 4; corner++)
                {
                    var vertexIndex = glyph * 4 + corner;
                    vertexHelper.PopulateUIVertex(ref vertex, vertexIndex);
                    vertex.position.x += offset;
                    vertexHelper.SetUIVertex(vertex, vertexIndex);
                }
            }
        }
    }
}

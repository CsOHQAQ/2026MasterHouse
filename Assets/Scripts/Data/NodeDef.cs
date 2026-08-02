using UnityEngine;

namespace MasterPotion
{
    /// <summary>节点卡片的静态定义基类。</summary>
    public abstract class NodeDef : ScriptableObject
    {
        public string displayName;
        [Tooltip("卡片占用的画布单元格数（宽 x 高），必须为正整数")]
        public Vector2Int gridSize = new Vector2Int(3, 3);
        public Color cardColor = new Color(0.22f, 0.25f, 0.3f);

        /// <summary>卡片的世界尺寸（1 单元格 = 1 世界单位）。</summary>
        public Vector2 WorldSize => new Vector2(Mathf.Max(1, gridSize.x), Mathf.Max(1, gridSize.y));

        private void OnValidate()
        {
            gridSize.x = Mathf.Max(1, gridSize.x);
            gridSize.y = Mathf.Max(1, gridSize.y);
        }
    }
}

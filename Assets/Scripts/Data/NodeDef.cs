using UnityEngine;

namespace MasterPotion
{
    /// <summary>节点卡片的静态定义基类。</summary>
    public abstract class NodeDef : ScriptableObject
    {
        public string displayName;
        [Tooltip("卡片尺寸（世界单位）")]
        public Vector2 size = new Vector2(2.4f, 3f);
        public Color cardColor = new Color(0.22f, 0.25f, 0.3f);
    }
}

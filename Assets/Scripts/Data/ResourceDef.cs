using UnityEngine;

namespace MasterPotion
{
    /// <summary>一种资源的静态定义。</summary>
    [CreateAssetMenu(menuName = "MasterPotion/Resource", fileName = "Resource")]
    public class ResourceDef : ScriptableObject
    {
        public string displayName;
        [Tooltip("端口、连线、运输动画使用的颜色")]
        public Color color = Color.white;
    }
}

using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 修理电路全局共用的表现资产。节点自身的底图、功能图标与底色仍归 NodeDef；
    /// 跨节点复用的状态图标统一放在这里，避免每个节点资产重复配置。
    /// </summary>
    [CreateAssetMenu(fileName = "CircuitVisualStyle_Default",
        menuName = "MasterHouse/小游戏/修理电路视觉样式", order = 100)]
    public sealed class CircuitVisualStyleConfig : ScriptableObject
    {
        [Header("节点 Pin")]
        [Tooltip("所有节点共用的 Pin 图标。留空时继续使用当前的纯色方块表现；运行时会保留分组/输入输出颜色作为 Tint。")]
        public Sprite pinSprite;

        [Min(0f)]
        [Tooltip("Pin 图标的边长，以单格边长为单位")]
        public float pinSizeInCells = 0.24f;

        [Tooltip("全局 Pin 颜色乘数；会与输入/输出/分组的语义颜色相乘。保持白色表示不额外染色。")]
        public Color pinColorMultiplier = Color.white;

        [Header("节点移动状态")]
        [Tooltip("节点按实际规则可移动时，叠加在右上角的图标")]
        public Sprite movableIcon;

        [Tooltip("节点按实际规则不可移动时，叠加在右上角的图标")]
        public Sprite immovableIcon;

        public Color movableIconColor = Color.white;
        public Color immovableIconColor = Color.white;

        [Min(0f)]
        [Tooltip("移动状态图标的边长，以单格边长为单位")]
        public float mobilityIconSizeInCells = 0.4f;

        [Min(0f)]
        [Tooltip("图标离节点右边和上边的内缩距离，以单格边长为单位")]
        public float mobilityIconPaddingInCells = 0.12f;
    }
}

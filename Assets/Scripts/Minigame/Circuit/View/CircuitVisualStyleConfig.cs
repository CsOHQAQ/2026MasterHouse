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
        public float pinSizeInCells = 0.75f;

        [Tooltip("全局 Pin 颜色乘数；会与输入/输出/分组的语义颜色相乘。保持白色表示不额外染色。")]
        public Color pinColorMultiplier = Color.white;

        [Header("连线")]
        [Tooltip("导线经过直线格时使用的 Sprite。图片默认朝上，运行时会按路径自动旋转。")]
        public Sprite wireStraightSprite;

        [Tooltip("导线转弯格使用的 Sprite。图片默认连接下方与右方，运行时会按路径自动旋转。")]
        public Sprite wireCornerSprite;

        [Tooltip("已完成连线的两端使用的 Sprite。图片默认接口朝右，运行时会朝向对应节点。")]
        public Sprite wireConnectedEndSprite;

        [Tooltip("正在描线但尚未接到目标的末端 Sprite。图片默认末端朝右，运行时会沿描线方向旋转。")]
        public Sprite wireOpenEndSprite;

        [Header("连线颜色")]
        [Tooltip("已接通且正在供电的连线颜色")]
        public Color wirePoweredColor = new Color(0.85f, 0.88f, 0.92f, 0.95f);

        [Tooltip("已接通但没有电量通过的连线颜色")]
        public Color wireUnpoweredColor = new Color(0.45f, 0.47f, 0.52f, 0.85f);

        [Tooltip("玩家正在描画、尚未提交的连线颜色")]
        public Color wirePreviewColor = new Color(0.95f, 0.90f, 0.45f, 0.75f);

        [Tooltip("描线超出导线预算时，超出部分的颜色")]
        public Color wireOverflowColor = new Color(0.92f, 0.35f, 0.32f, 0.55f);

        [Header("节点移动状态")]
        [Tooltip("节点按实际规则可移动时，叠加在右上角的图标")]
        public Sprite movableIcon;

        [Tooltip("节点按实际规则不可移动时，叠加在右上角的图标")]
        public Sprite immovableIcon;

        public Color movableIconColor = Color.white;
        public Color immovableIconColor = Color.white;

        [Min(0f)]
        [Tooltip("移动状态图标的边长，以单格边长为单位")]
        public float mobilityIconSizeInCells = 0.8f;

        [Min(0f)]
        [Tooltip("图标离节点右边和上边的内缩距离，以单格边长为单位")]
        public float mobilityIconPaddingInCells = 0.12f;
    }
}

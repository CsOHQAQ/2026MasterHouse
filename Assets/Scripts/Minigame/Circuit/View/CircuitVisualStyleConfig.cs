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

        [Header("节点交互易用性配置")]
        [Min(0f)]
        [Tooltip("Pin 可点击范围相对 Pin Size In Cells 的倍率。命中 Pin 时优先开始拉线；不改变视觉大小。")]
        public float pinInteractionScale = 1.75f;

        [Range(0f, 1f)]
        [Tooltip("节点可开始拖动的中心区域，相对节点整体外框的缩放比例。Pin 命中始终优先于节点拖动。")]
        public float nodeDragInteractionScale = 0.65f;

        [Header("连线")]
        [Tooltip("导线经过直线格时使用的 Sprite。图片默认朝上，运行时会按路径自动旋转。")]
        public Sprite wireStraightSprite;

        [Tooltip("导线转弯格使用的 Sprite。图片默认连接下方与右方，运行时会按路径自动旋转。")]
        public Sprite wireCornerSprite;

        [Tooltip("已完成连线的两端使用的 Sprite。图片默认接口朝右，运行时会朝向对应节点。")]
        public Sprite wireConnectedEndSprite;

        [Tooltip("两个节点的 Pin 共用同一接口格时，已完成直连使用的 Sprite。图片默认朝上（从起始 Pin 指向目标 Pin），运行时会按 Pin 朝向旋转。留空时回退为普通直线。")]
        public Sprite wirePinToPinSprite;

        [Tooltip("连线在端点格转弯后接入 Pin 时，叠加在转角上的接口 Sprite。图片默认接口朝右。")]
        public Sprite wireCornerPinSprite;

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

        [Header("棋盘格")]
        [Tooltip("棋盘单格底图。留空时使用默认矩形 Image。")]
        public Sprite cellSprite;

        [Tooltip("棋盘单格的颜色与透明度，会乘在 Cell Sprite 上。")]
        public Color cellColor = new Color(0.25f, 0.25f, 0.25f, 0.20f);

        [Min(0f)]
        [Tooltip("相邻棋盘格之间的间隙，单位为 UI 像素。")]
        public float cellGapPixels = 2f;

        [Header("关卡背景")]

        [Tooltip("小游戏最底层的全屏背景图。会覆盖 Prefab 根节点 levelBackground 的默认 Sprite。")]
        public Sprite levelBackgroundSprite;

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

        [Header("节点 Caption 数字角标")]
        [Tooltip("电源/电池右上角 Caption 数字的 0-9 Sprite。电源显示供电量、电池显示 received/required。" +
                 "留空时继续使用文本渲染。")]
        public Sprite[] captionDigits;

        [Min(0f)]
        [Tooltip("Caption 数字的边长，以单格边长为单位")]
        public float captionDigitSize = 0.4f;

        [Min(0f)]
        [Tooltip("Caption 数字之间的间距，以单格边长为单位")]
        public float captionDigitSpacing = 0.05f;

        [Tooltip("Caption 数字染色。保持白色表示不额外染色。")]
        public Color captionDigitColor = Color.white;

        [Tooltip("斜杠 '/' 分隔符 Sprite。电池 Caption 格式为 received/required；留空时跳过斜杠字符。")]
        public Sprite captionSlashSprite;

        [Tooltip("电源节点 Caption 左侧的小闪电/电力图标。留空时不显示。")]
        public Sprite captionPowerIcon;

        [Min(0f)]
        [Tooltip("电池图标的边长，以单格边长为单位")]
        public float captionPowerIconSize = 0.45f;
    }
}

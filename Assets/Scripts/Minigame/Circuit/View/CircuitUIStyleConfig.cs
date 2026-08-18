using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 「修理电路」Prefab 的界面主题。它只描述固定 UI 的皮肤与尺寸规则，
    /// 不承载节点的功能图标——功能图标属于 NodeDef，随具体节点资产走。
    /// </summary>
    [CreateAssetMenu(fileName = "CircuitUIStyle_Default",
        menuName = "MasterHouse/小游戏/修理电路 UI 样式", order = 101)]
    public sealed class CircuitUIStyleConfig : ScriptableObject
    {
        [Header("字体")]
        [Tooltip("修理电路全部 UGUI 文本使用的中文字体。应与项目通用 UI 字体保持一致。")]
        public Font uiFont;

        [Header("顶部状态条")]
        [Tooltip("顶部状态条的九宫格底图。留空时使用纯色背景。")]
        public Sprite topBarBackgroundSprite;

        public Color topBarBackgroundColor = new Color(1f, 1f, 1f, .05f);

        [Min(0f)]
        [Tooltip("顶部状态条高度（Canvas 参考像素）。")]
        public float topBarHeight = 72f;

        public Color topBarTextColor = new Color(.94f, .94f, .96f, 1f);
        public Color topBarProgressTextColor = new Color(.72f, .72f, .78f, 1f);

        [Header("件库面板")]
        [Tooltip("件库整体的九宫格底图。留空时沿用纯色面板。")]
        public Sprite palettePanelBackgroundSprite;

        public Color palettePanelColor = Color.white;

        [Min(0f)]
        [Tooltip("件库面板顶部到首个节点条目的距离（Canvas 参考像素）；用于避开「件库」标题。")]
        public float paletteContentTopPadding = 78f;

        [Header("件库节点卡")]
        [Tooltip("所有可放置节点共用的九宫格底图。会按节点 Shape 的外接长宽等比适配。")]
        public Sprite palettePieceBackgroundSprite;

        public Color palettePieceNormalColor = Color.white;
        public Color palettePieceSelectedColor = new Color(0.55f, 1f, 0.62f, 1f);
        public Color palettePieceDisabledColor = new Color(0.42f, 0.42f, 0.46f, 0.8f);

        [Min(0f)]
        [Tooltip("节点预览可使用的最大宽度（Canvas 参考像素）。")]
        public float palettePiecePreviewMaxWidth = 196f;

        [Min(0f)]
        [Tooltip("节点预览可使用的最大高度（Canvas 参考像素）。")]
        public float palettePiecePreviewMaxHeight = 132f;

        [Min(0f)]
        [Tooltip("功能图标离节点卡边缘的内缩距离（Canvas 参考像素）。")]
        public float palettePieceIconPadding = 14f;

        [Min(0f)]
        [Tooltip("节点卡下方为名称与数量预留的高度（Canvas 参考像素）。")]
        public float palettePieceTextHeight = 42f;

        [Min(0f)]
        [Tooltip("每个条目顶部到节点卡的距离（Canvas 参考像素）。")]
        public float palettePieceTopPadding = 8f;

        public Color paletteNameColor = new Color(0.94f, 0.94f, 0.96f, 1f);
        public Color paletteCountColor = new Color(0.72f, 0.72f, 0.78f, 1f);
        public Color paletteCountDisabledColor = new Color(0.95f, 0.36f, 0.33f, 1f);

        [Header("件库数量角标")]
        [Tooltip("按 0 到 9 的顺序填写数字 Sprite。件库显示的是当前剩余可摆数量。")]
        public Sprite[] paletteCountDigits = new Sprite[10];

        [Min(0f)]
        [Tooltip("数量角标中单个数字的边长（Canvas 参考像素）。")]
        public float paletteCountDigitSize = 34f;

        [Min(0f)]
        public float paletteCountDigitSpacing = 0f;

        public Color paletteCountDigitColor = Color.white;
        public Color paletteCountDigitDisabledColor = new Color(0.75f, 0.42f, 0.42f, 1f);
    }
}

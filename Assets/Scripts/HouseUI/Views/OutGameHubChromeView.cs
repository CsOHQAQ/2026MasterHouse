using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// Hub 主页面壳的 Prefab 引用（2026-08-20 按 2.0 设计图重做，素材 `Assets/PC ui 2.0/主界面`
    /// 与 `房间放置`，版式见 `Docs/待办工作流/主页面UI参考.png`）。
    ///
    /// 八块：左上时间牌、左侧图鉴/商店、右上装饰分/声望值、右下结束今日营业、左下房间卡与布置入口。
    /// **哪一块在哪一档可见不在这里，也不在代码里**——各块根上挂 <see cref="HubTierVisibility"/>，
    /// 勾 Inspector 即可，切档过渡由 <see cref="HubTierUiBinder"/> 统一执行（§16.2）。
    /// 图鉴/商店/结束/布置四颗按钮的默认态与悬停态素材同尺寸，所以走 Button 自带的 SpriteSwap，
    /// 不需要对话选项 2.0 那套二图叠放。
    /// </summary>
    public sealed class OutGameHubChromeView : MonoBehaviour
    {
        [Header("左上 · 时间牌（三档常显，不可点）")]
        [Tooltip("底板：按时段在 daySprite / nightSprite 之间换图")]
        public Image timeCard;
        [Tooltip("白天底板（早晨~下午）")] public Sprite daySprite;
        [Tooltip("夜晚底板（晚上、深夜，即 18:00 之后）")] public Sprite nightSprite;
        [Tooltip("时钟：HH:MM")] public Text clockLabel;
        [Tooltip("天数：DAY-N")] public Text dayLabel;

        [Header("左侧 · 图鉴 / 商店（第二、三档）")]
        [Tooltip("图鉴：进访客图鉴（CodexOverlay），不是家具图鉴")]
        public Button codexButton;
        public Button storeButton;

        [Header("右上 · 装饰分 / 声望值（第二、三档，只显示不可点）")]
        [Tooltip("全屋装饰分（EconomyManager.DecorationScore）")] public Text decorationLabel;
        public Text reputationLabel;

        [Header("右下 · 结束今日营业（第二、三档）")]
        public Button endDayButton;

        [Header("左下 · 房间卡与布置入口（第三档）")]
        [Tooltip("卡片与按钮的共同淡入淡出组：镜头落在底层大厅（接待室不是业务房间，没有装饰分也不能布置）时整组淡出。\n" +
                 "档位显隐由父节点的 HubTierVisibility 负责，两层 CanvasGroup 各管各的，不会互相抢补间")]
        public CanvasGroup roomBody;
        [Tooltip("当前房间显示名（CodexTable.rooms[i].displayName）")] public Text roomNameLabel;
        [Tooltip("**本房间**的装饰分（FurniturePlacementQuery.DecorationScoreOf），与右上角的全屋装饰分不是一个口径")]
        public Text roomDecorationLabel;
        [Tooltip("布置房间：进家具摆放模式（2.0 撤掉右侧 dock 后这里是唯一入口）")]
        public Button furnishButton;
    }
}

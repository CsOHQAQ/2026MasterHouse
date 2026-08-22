using DG.Tweening;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// Hub 主页面壳绑定（2026-08-20 按 2.0 设计图重做，§16.3 每页绑定独立成文件）：
    /// 时间牌、图鉴、商店、装饰分、声望值、结束今日营业、房间卡与布置入口。
    ///
    /// 本类只管**内容与点击**。「哪一块在哪一档可见」是 Prefab 上 HubTierVisibility 的 Inspector 勾选，
    /// 由 HubTierUiBinder 统一开合（§16.2 改显隐 = 改 Inspector，不碰代码）。
    /// 时钟走字与房间卡的大厅淡出由 Tick() 每帧轮询（连续量），经济数值走 Economy.Changed 事件（离散变化）。
    /// </summary>
    public sealed class HubChromeBinder
    {
        private HubPage page;
        private OutGameHubChromeView view;

        /// <summary>已呈现的「天 × 10 + 时段」：跨天或换时段时才重建 DAY 文案与日/夜底板。</summary>
        private int phaseShown = -1;

        /// <summary>房间卡是否正显示（null = 尚未应用过，首帧无条件落一次）。</summary>
        private bool? roomBodyShown;

        private static HouseClockData Clock => GameManager.Instance.HouseClockManager.Data;
        private static EconomyManager Economy => GameManager.Instance.EconomyManager;

        public void Bind(OutGameHubChromeView chrome, HubPage owner)
        {
            view = chrome;
            page = owner;

            if (view.clockLabel != null) view.clockLabel.text = Clock.HourText;
            ApplyPhase(true);

            HouseUIUtil.BindButton(view.codexButton, page.OpenCodex);
            HouseUIUtil.BindButton(view.storeButton, () => page.OpenPanel(EHousePanel.Market));
            HouseUIUtil.BindButton(view.endDayButton, page.TryEndDay);
            HouseUIUtil.BindButton(view.furnishButton, page.OpenFurnitureMode);

            Economy.Changed += RefreshEconomy;
            RefreshEconomy();
            RefreshRoom();
            UpdateRoomBody(false);
        }

        /// <summary>每帧：时钟走字；跨时段时换日/夜底板与 DAY 文案；镜头进出底层大厅时开合房间卡。</summary>
        public void Tick()
        {
            if (view == null) return;
            // 一轮测试改进 #6：时间牌只显示小时（分钟继续在业务层走，别处消费方不受影响）
            if (view.clockLabel != null) view.clockLabel.text = Clock.HourText;
            ApplyPhase(false);
            UpdateRoomBody(true);
        }

        /// <summary>经济数值刷新（Economy.Changed 与进场各调一次）：右上两块牌都是**全屋**口径。</summary>
        public void RefreshEconomy()
        {
            if (view == null) return;
            if (view.decorationLabel != null) view.decorationLabel.text = Economy.DecorationScore.ToString();
            if (view.reputationLabel != null) view.reputationLabel.text = Economy.Data.Reputation.ToString();
        }

        /// <summary>
        /// 左下房间卡刷新：当前房间显示名 + **本房间**装饰分。
        ///
        /// 装饰分不挂 Economy.Changed——按房间的装饰分刻意不广播事件
        /// （见 EconomyManager.SetFurnitureDecorationScore 的告诫），所以由换房与退出摆放模式两处显式调。
        /// </summary>
        public void RefreshRoom()
        {
            if (view == null) return;
            var rooms = GameManager.Instance.CodexTable.rooms;
            var index = page.RoomIndex;
            if (index < 0 || index >= rooms.Count) return;
            if (view.roomNameLabel != null) view.roomNameLabel.text = rooms[index].displayName;
            if (view.roomDecorationLabel != null)
                view.roomDecorationLabel.text = "装饰分 " + FurniturePlacementQuery.DecorationScoreOf(index);
        }

        /// <summary>页面退场时退订（应用退出时销毁顺序不定，判空守卫）。</summary>
        public void Dispose()
        {
            if (GameManager.Instance != null) Economy.Changed -= RefreshEconomy;
            if (view != null && view.roomBody != null) view.roomBody.DOKill();
            view = null;
        }

        /// <summary>DAY 文案与日/夜底板：只在跨天或换时段时重建。</summary>
        private void ApplyPhase(bool force)
        {
            var phase = Clock.CurrentPhase;
            var key = Clock.Day * 10 + (int)phase;
            if (!force && key == phaseShown) return;
            phaseShown = key;
            if (view.dayLabel != null) view.dayLabel.text = "DAY-" + Clock.Day;
            if (view.timeCard == null) return;
            // 18:00 换夜（用户定案）：晚上与深夜用夜晚底板，早晨~下午用白天底板
            var sprite = phase >= EHousePhase.Evening ? view.nightSprite : view.daySprite;
            if (sprite != null) view.timeCard.sprite = sprite;
        }

        /// <summary>
        /// 房间卡与布置按钮的开合：视口中心落在**业务房间**才显示。
        ///
        /// 底层大厅（接待室）是第五个区域，不能住客、没有装饰分、也不能布置家具，
        /// 而 HubSceneBinder 碰到它是「保持上一间不变」，直接挂着卡会指着一间玩家没在看的房。
        /// </summary>
        private void UpdateRoomBody(bool animated)
        {
            if (view == null || view.roomBody == null) return;
            var region = page.ViewportRegion;
            var show = region >= 0 && region < HubWorldGrid.RoomCount;
            if (roomBodyShown == show) return;
            roomBodyShown = show;
            view.roomBody.blocksRaycasts = show;
            view.roomBody.interactable = show;
            view.roomBody.DOKill();
            if (!animated)
            {
                view.roomBody.alpha = show ? 1f : 0f;
                return;
            }
            view.roomBody.DOFade(show ? 1f : 0f, .22f).SetUpdate(true);
        }
    }
}

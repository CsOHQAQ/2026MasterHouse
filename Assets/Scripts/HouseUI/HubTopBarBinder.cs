using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// Hub 顶栏绑定：DAY/时段/时钟、HOUSE CREDIT、欢迎语、品牌（回标题）、设置入口；
    /// 附带声望/装饰分数值条（运行时动态件）。时钟走字与时段文案由 Tick() 每帧轮询（§2.1）。
    /// </summary>
    public sealed class HubTopBarBinder
    {
        private HubPage page;
        private Text clockLabel;
        private Text phaseLabel;
        private Text phaseRangeLabel;
        private Text creditLabel;
        private Text economyChipLabel;
        private int phaseShown = -1;

        private static HouseClockData Clock => GameManager.Instance.HouseClockManager.Data;
        private static EconomyManager Economy => GameManager.Instance.EconomyManager;

        public void Bind(OutGameHubTopBarView hud, HubPage owner, Transform chromeRoot)
        {
            page = owner;
            var phase = (int)Clock.CurrentPhase;
            hud.weekDatePhase.text = $"<size=14>GAME TIME · 加速时间</size>\n<size=31>DAY {Clock.Day:00}</size>    {HousePhaseText.Names[phase]}";
            hud.phaseRange.text = HousePhaseText.Ranges[phase];
            hud.clock.text = Clock.TimeText;
            clockLabel = hud.clock;
            phaseLabel = hud.weekDatePhase;
            phaseRangeLabel = hud.phaseRange;
            phaseShown = Clock.Day * 10 + phase;
            creditLabel = hud.creditLabel;
            hud.welcomeLabel.text = "WELCOME HOME.\n本周将有 <color=#E22D76>" +
                GameManager.Instance.VisitorManager.CountRemaining() + "</color> 位访客来访";
            HouseUIUtil.BindButton(hud.timeButton, () => page.OpenPanel(EHousePanel.Calendar));
            HouseUIUtil.BindButton(hud.creditButton, () => page.OpenPanel(EHousePanel.Market));
            HouseUIUtil.BindButton(hud.brandButton, page.BackToTitle);
            HouseUIUtil.BindButton(hud.optionsButton, page.OpenSettings);

            // 声望与装饰分数值条（流通数值三件套中，货币在顶栏 HOUSE CREDIT 显示）
            var chip = HouseUIRuntime.Panel(chromeRoot, "EconomyChip", new Vector2(.5f, 1),
                new Vector2(-233, -160), new Vector2(400, 50), new Color(.025f, .025f, .045f, .77f));
            economyChipLabel = HouseUIRuntime.StretchLabel(chip.transform, "Value", string.Empty, 18,
                HouseUIUtil.White, TextAnchor.MiddleCenter, FontStyle.Bold);

            Economy.Changed += RefreshEconomy;
            RefreshEconomy();
        }

        /// <summary>每帧：时钟走字；时段/跨天时刷新 DAY 文案。</summary>
        public void Tick()
        {
            if (clockLabel != null) clockLabel.text = Clock.TimeText;
            if (phaseLabel == null) return;
            var phase = (int)Clock.CurrentPhase;
            var key = Clock.Day * 10 + phase; // 跨天时 DAY 文案也要刷新
            if (key == phaseShown) return;
            phaseShown = key;
            phaseLabel.text = $"<size=14>GAME TIME · 加速时间</size>\n<size=31>DAY {Clock.Day:00}</size>    {HousePhaseText.Names[phase]}";
            if (phaseRangeLabel != null) phaseRangeLabel.text = HousePhaseText.Ranges[phase];
        }

        public void RefreshEconomy()
        {
            if (creditLabel != null)
                creditLabel.text = $"<size=13>HOUSE CREDIT</size>\n◈ {Economy.Data.Currency:N0}     ＋";
            if (economyChipLabel != null)
                economyChipLabel.text =
                    $"<color=#74D8D1>声望 {Economy.Data.Reputation}</color>      <color=#E22D76>装饰分 {Economy.DecorationScore}</color>";
        }

        /// <summary>页面退场时退订（应用退出时销毁顺序不定，判空守卫）。</summary>
        public void Dispose()
        {
            if (GameManager.Instance != null) Economy.Changed -= RefreshEconomy;
        }
    }
}

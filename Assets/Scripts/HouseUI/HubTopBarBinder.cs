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
        private Text welcomeLabel;
        private Text economyChipLabel;
        private int phaseShown = -1;
        private int onStageShown = -1;

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
            // 金币数字节点：优先取用户在 Prefab 里新加的 Credit/CreditLabel（Label 为静态文案），找不到再退回视图字段
            var creditNode = hud.creditButton != null ? hud.creditButton.transform.Find("CreditLabel") : null;
            creditLabel = creditNode != null ? creditNode.GetComponent<Text>() : hud.creditLabel;
            welcomeLabel = hud.welcomeLabel;
            RefreshWelcome();
            HouseUIUtil.BindButton(hud.timeButton, () => page.OpenPanel(EHousePanel.Calendar));
            HouseUIUtil.BindButton(hud.creditButton, () => page.OpenPanel(EHousePanel.Market));
            HouseUIUtil.BindButton(hud.brandButton, page.BackToTitle);
            HouseUIUtil.BindButton(hud.optionsButton, page.OpenSettings);
            // 顶栏卡片统一 common 框（半透明；小件边框缩细）；商店卡样式以手调 Prefab 为准，不换肤
            HouseUIUtil.ApplyPanelSkin(hud.timeButton.targetGraphic as Image, .8f, 2f);
            HouseUIUtil.ApplyPanelSkin(hud.optionsButton.targetGraphic as Image, .8f, 2.5f);

            // 声望与装饰分数值条：已收编进 HubTopBar Prefab（2026-08-11，可在 Prefab 模式调整）；
            // 旧版 Prefab 尚未经生成器修复时回退运行时生成
            if (hud.economyChipLabel != null)
            {
                economyChipLabel = hud.economyChipLabel;
                HouseUIUtil.ApplyPanelSkin(hud.economyChipLabel.transform.parent.GetComponent<Image>(), .8f, 2.5f);
            }
            else
            {
                var chip = HouseUIRuntime.Panel(chromeRoot, "EconomyChip", new Vector2(.5f, 1),
                    new Vector2(-233, -160), new Vector2(400, 50), new Color(.025f, .025f, .045f, .77f));
                economyChipLabel = HouseUIRuntime.StretchLabel(chip.transform, "Value", string.Empty, 18,
                    HouseUIUtil.White, TextAnchor.MiddleCenter, FontStyle.Bold);
            }

            Economy.Changed += RefreshEconomy;
            RefreshEconomy();
        }

        /// <summary>每帧：时钟走字；时段/跨天时刷新 DAY 文案；在场访客数变化时刷新欢迎语。</summary>
        public void Tick()
        {
            if (clockLabel != null) clockLabel.text = Clock.TimeText;
            RefreshWelcome();
            if (phaseLabel == null) return;
            var phase = (int)Clock.CurrentPhase;
            var key = Clock.Day * 10 + phase; // 跨天时 DAY 文案也要刷新
            if (key == phaseShown) return;
            phaseShown = key;
            phaseLabel.text = $"<size=14>GAME TIME · 加速时间</size>\n<size=31>DAY {Clock.Day:00}</size>    {HousePhaseText.Names[phase]}";
            if (phaseRangeLabel != null) phaseRangeLabel.text = HousePhaseText.Ranges[phase];
        }

        /// <summary>欢迎语：「当前在场访客」语义（访客交付说明 §10，周制退役）；数值变化时才重建字符串。</summary>
        private void RefreshWelcome()
        {
            if (welcomeLabel == null) return;
            var onStage = GameManager.Instance.VisitorManager.CountOnStage;
            if (onStage == onStageShown) return;
            onStageShown = onStage;
            welcomeLabel.text = "WELCOME HOME.\n当前在场 <color=#E22D76>" + onStage + "</color> 位访客";
        }

        public void RefreshEconomy()
        {
            // 商店卡版式归手调 Prefab：Label=「商店」静态；CreditLabel=金币数字，运行时只刷新这一段（2026-08-11）
            if (creditLabel != null)
                creditLabel.text = $"金币 ◈ {Economy.Data.Currency:N0}  ＋";
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

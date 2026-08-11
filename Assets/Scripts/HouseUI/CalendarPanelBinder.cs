using System;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 日历面板绑定：现实日期大卡 + 烘焙的日历格（Prefab 槽位）+ 时段列表。
    /// 读 DateTime.Now 渲染现实日历是已知遗留（与游戏 Day 脱节，见局外系统说明 §12），维持原样。
    /// </summary>
    public static class CalendarPanelBinder
    {
        public static void Bind(OutGameCalendarPanelView view, HubPage page)
        {
            if (view == null) return;
            var now = DateTime.Now;
            var phase = (int)GameManager.Instance.HouseClockManager.Data.CurrentPhase;
            if (view.dateText != null)
                view.dateText.text = $"{now:yyyy / MMMM}\n<size=100>{now:dd}</size>\n{now:dddd} · {HousePhaseText.Names[phase]}\n<size=28>{now:HH:mm}</size>";

            var firstOfMonth = new DateTime(now.Year, now.Month, 1);
            var weekOffset = ((int)firstOfMonth.DayOfWeek + 6) % 7;
            var daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
            if (view.dayCells != null && view.dayCells.Length > 0 && view.dayCells[0] != null)
            {
                // Prefab 烘焙槽位：只设置数字、显隐与今日高亮
                for (var i = 0; i < view.dayCells.Length; i++)
                {
                    if (view.dayCells[i] == null) continue;
                    var day = i - weekOffset + 1;
                    var visible = day >= 1 && day <= daysInMonth;
                    view.dayCells[i].gameObject.SetActive(visible);
                    if (!visible) continue;
                    if (view.dayCellLabels != null && i < view.dayCellLabels.Length && view.dayCellLabels[i] != null)
                        view.dayCellLabels[i].text = day.ToString();
                    if (view.dayCellBackgrounds != null && i < view.dayCellBackgrounds.Length && view.dayCellBackgrounds[i] != null)
                        view.dayCellBackgrounds[i].color = day == now.Day ? HouseUIUtil.Wine : new Color(1, 1, 1, .035f);
                }
            }
            else
            {
                Debug.LogError("[HouseUI] 日历 Prefab 缺少烘焙日历格槽位（§16.2 不回退代码布局）");
            }

            for (var i = 0; i < 6; i++)
            {
                if (view.phaseLabels != null && i < view.phaseLabels.Length && view.phaseLabels[i] != null)
                    view.phaseLabels[i].text = $"{HousePhaseText.Names[i]}   <size=13>{HousePhaseText.Ranges[i]}</size>       {(i == 5 ? "休息" : "可服务")}";
                if (view.phaseBackgrounds != null && i < view.phaseBackgrounds.Length && view.phaseBackgrounds[i] != null)
                    view.phaseBackgrounds[i].color = phase == i ? HouseUIUtil.Wine : new Color(1, 1, 1, .035f);
            }
            if (view.syncButton != null)
                HouseUIUtil.BindButton(view.syncButton, () =>
                    page.Toast("已同步现实时间 · " + DateTime.Now.ToString("HH:mm")));
        }
    }
}

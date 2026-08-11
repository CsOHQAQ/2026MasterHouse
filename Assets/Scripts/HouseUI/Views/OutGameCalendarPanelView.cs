using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 「日程与时间」面板内容的 Prefab 引用。日期格为 Prefab 内烘焙的 6×7=42 个槽位，
    /// 运行时只设置数字、显隐与今日高亮；dayGridRoot 仅作旧版 Prefab 的运行时生成兜底。
    /// </summary>
    public sealed class OutGameCalendarPanelView : MonoBehaviour
    {
        public Text dateText;
        public RectTransform dayGridRoot;
        public Button[] dayCells = new Button[42];
        public Image[] dayCellBackgrounds = new Image[42];
        public Text[] dayCellLabels = new Text[42];
        public Text scheduleTitle;
        public Image[] phaseBackgrounds = new Image[6];
        public Text[] phaseLabels = new Text[6];
        public Button syncButton;
    }
}

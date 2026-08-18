using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 设置页内容绑定（2026-08-16 重做；标题设置页与 Hub 设置叠加层共用）：
    /// 左侧分页 sprite swap 高亮，右侧行按分页用模板实例化；
    /// 改动**即时生效**（音量/昼夜等当场可感知）但不落盘——回车「应用」才 Save，
    /// R「重置修改」/ ESC「返回」都恢复到上次应用的快照。
    /// </summary>
    public sealed class SettingsPageBinder
    {
        // 2026-08-18 按 2.0 设计图：音频并入基础、账户下线，分页收敛为 5 个（与 Prefab 的 tab 数量一致）
        private static readonly string[] TabNames = { "基础", "画面", "控制", "玩法", "制作组" };

        private OutGameSettingsPageView view;
        private HouseUIManager ui;
        private int tabIndex = -1;
        /// <summary>上次应用（或进页时）的设置快照，R/返回 用它回滚。</summary>
        private string savedSnapshot;
        private readonly List<GameObject> rows = new List<GameObject>();
        private float rowCursor;
        /// <summary>各分页的基准矩形（选中态按切图比例放大凸出，取消选中还原）。</summary>
        private Vector2[] tabBasePositions;
        private Vector2[] tabBaseSizes;

        /// <summary>热键闸门（叠加层语境用）：确认弹窗压顶时挂起 R/空格，避免连开多个弹窗。空 = 常开。</summary>
        public Func<bool> HotkeyGate;

        public void Bind(OutGameSettingsPageView pageView, HouseUIManager manager, Action onBack)
        {
            view = pageView;
            ui = manager;
            savedSnapshot = JsonUtility.ToJson(HouseSettings.Data);
            tabBasePositions = new Vector2[view.tabButtons.Length];
            tabBaseSizes = new Vector2[view.tabButtons.Length];
            for (var i = 0; i < view.tabButtons.Length; i++)
            {
                if (view.tabButtons[i] == null) continue;
                var rect = (RectTransform)view.tabButtons[i].transform;
                tabBasePositions[i] = rect.anchoredPosition;
                tabBaseSizes[i] = rect.sizeDelta;
                if (i < TabNames.Length && view.tabLabels[i] != null) view.tabLabels[i].text = TabNames[i];
                var index = i;
                HouseUIUtil.BindButton(view.tabButtons[i], () => ShowTab(index));
                BindTabHover(index);
            }
            // 顶部 Q/E 翻页按钮（Prefab 节点，view 里没留字段，按名字取）
            BindStepButton("PrevTab", -1);
            BindStepButton("NextTab", 1);
            if (view.backButton != null && onBack != null) HouseUIUtil.BindButton(view.backButton, () => onBack(), ESfx.None);
            if (view.resetButton != null) HouseUIUtil.BindButton(view.resetButton, RequestReset);
            if (view.applyButton != null) HouseUIUtil.BindButton(view.applyButton, RequestApply);
            ShowTab(0);
            HouseUIUtil.ApplyFallbackFont(view.transform);
        }

        /// <summary>Q/E 翻页 + R 重置 / 空格应用（页面 HandleInput 或叠加层 SettingsHotkeys 每帧转发；都先过确认弹窗）。</summary>
        public void HandleHotkeys()
        {
            if (HotkeyGate != null && !HotkeyGate()) return;
            if (Input.GetKeyDown(KeyCode.Q)) StepTab(-1);
            if (Input.GetKeyDown(KeyCode.E)) StepTab(1);
            if (Input.GetKeyDown(KeyCode.R)) RequestReset();
            if (Input.GetKeyDown(KeyCode.Space)) RequestApply();
        }

        /// <summary>相邻分页切换（Q/E 与顶部箭头按钮共用），首尾循环。</summary>
        private void StepTab(int direction)
        {
            var count = view != null && view.tabButtons != null ? view.tabButtons.Length : 0;
            if (count <= 0) return;
            ShowTab((tabIndex + direction + count) % count);
        }

        private void BindStepButton(string nodeName, int direction)
        {
            var node = view.transform.Find(nodeName);
            var button = node != null ? node.GetComponent<Button>() : null;
            if (button != null) HouseUIUtil.BindButton(button, () => StepTab(direction));
        }

        /// <summary>分页悬停换图（Button 的 transition 关着，选中态由本类统一管，悬停也一并在这里做）。</summary>
        private void BindTabHover(int index)
        {
            if (view.tabHover == null || view.tabBackgrounds[index] == null) return;
            var trigger = view.tabButtons[index].GetComponent<EventTrigger>();
            if (trigger == null) trigger = view.tabButtons[index].gameObject.AddComponent<EventTrigger>();
            trigger.triggers.Clear(); // 重复 Bind（再次进页）不叠加监听
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ =>
            {
                if (index != tabIndex) view.tabBackgrounds[index].sprite = view.tabHover;
            });
            trigger.triggers.Add(enter);
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ =>
            {
                if (index != tabIndex && view.tabNormal != null) view.tabBackgrounds[index].sprite = view.tabNormal;
            });
            trigger.triggers.Add(exit);
        }

        /// <summary>「应用」入口（空格/点击）：确认后落盘（2026-08-16 反馈：先弹确认）。</summary>
        public void RequestApply() =>
            ConfirmOverlay.Open(ui, "应用设置", "保存当前设置并立即生效。", "应用", ApplyAndSave);

        /// <summary>「重置修改」入口（R/点击）：确认后回滚到上次应用的快照。</summary>
        public void RequestReset() =>
            ConfirmOverlay.Open(ui, "重置修改", "放弃未应用的改动，恢复为上次应用的设置。", "重置",
                () => ResetToSaved("已重置为上次应用的设置"));

        private void ApplyAndSave()
        {
            HouseSettings.Apply();
            HouseSettings.Save();
            savedSnapshot = JsonUtility.ToJson(HouseSettings.Data);
            ui.ShowToast("设置已应用");
        }

        /// <summary>返回时丢弃未应用的改动（页面/叠加层关闭路径都要调）。</summary>
        public void DiscardUnsaved() => ResetToSaved(null);

        private void ResetToSaved(string toast)
        {
            if (string.IsNullOrEmpty(savedSnapshot)) return;
            JsonUtility.FromJsonOverwrite(savedSnapshot, HouseSettings.Data);
            HouseSettings.Apply();
            if (view != null && tabIndex >= 0) RebuildRows();
            if (!string.IsNullOrEmpty(toast)) ui.ShowToast(toast);
        }

        // ── 分页 ──

        private void ShowTab(int index)
        {
            tabIndex = index;
            for (var i = 0; i < view.tabBackgrounds.Length; i++)
            {
                if (view.tabBackgrounds[i] == null) continue;
                var selected = i == index;
                if (view.tabSelected != null && view.tabNormal != null)
                    view.tabBackgrounds[i].sprite = selected ? view.tabSelected : view.tabNormal;
                // 2.0 底板是暖米色纸面：选中用主题蓝，未选中压成灰蓝
                if (i < view.tabLabels.Length && view.tabLabels[i] != null)
                    view.tabLabels[i].color = selected ? new Color32(0x4A, 0x6F, 0xA5, 0xFF)
                        : new Color32(0x8F, 0x9A, 0xA8, 0xFF);
                // 2.0 三态切图同尺寸（420×141），不再做选中放大；位置尺寸一律以 Prefab 为准
                var rect = (RectTransform)view.tabBackgrounds[i].transform;
                rect.sizeDelta = tabBaseSizes[i];
                rect.anchoredPosition = tabBasePositions[i];
            }
            RebuildRows();
        }

        private void RebuildRows()
        {
            foreach (var row in rows)
                if (row != null) UnityEngine.Object.Destroy(row);
            rows.Clear();
            rowCursor = 0f;

            var data = HouseSettings.Data;
            switch (tabIndex)
            {
                case 0: // 基础：音量三条 + 通用开关（2.0 设计图把音频并进了这一页）
                    AddHeader("通用");
                    AddSlider("游戏主音量", () => data.masterVolume, value => data.masterVolume = value);
                    AddSlider("音效", () => data.sfxVolume, value => data.sfxVolume = value);
                    AddSlider("背景音乐", () => data.bgmVolume, value => data.bgmVolume = value);
                    AddHeader("通用");
                    AddOption("昼夜交替", new[] { "关", "开" }, data.dayNightEnabled ? 1 : 0,
                        picked => data.dayNightEnabled = picked == 1);
                    AddOption("语言", new[] { "中文" }, 0, _ => { });
                    break;
                case 1: // 画面
                    AddHeader("通用");
                    AddOption("窗口模式", new[] { "无边框", "全屏", "窗口" },
                        Mathf.Max(0, Array.IndexOf(new[] { "无边框", "全屏", "窗口" }, data.windowMode)),
                        picked => data.windowMode = new[] { "无边框", "全屏", "窗口" }[picked]);
                    break;
                case 4: // 制作组
                    AddHeader("制作组");
                    AddCredit("缑悦然", "研发-策划", "Project F1", "策划");
                    AddCredit("许铭杰", "研发-客户端", "Project F1", "客户端");
                    AddCredit("蔡一帆", "质量", "质量管理中心-ZGame", "客户端");
                    AddCredit("徐露露", "研发-策划-UX", "Zgame", "美术");
                    AddCredit("窦艺帆", "发行", "国内UA", "美术/市场");
                    AddCredit("何昕昕", "发行", "国内UA", "PM/市场");
                    break;
                default:
                    AddHeader("该分页开发中，敬请期待");
                    break;
            }
            // Rows 是滚动内容层（顶部对齐、宽度拉伸），高度必须跟着行数走，否则内容超出视口时滚不动
            if (view.rowsRoot != null)
                view.rowsRoot.sizeDelta = new Vector2(view.rowsRoot.sizeDelta.x, rowCursor);
        }

        // ── 行构建（模板实例化，硬约定：动态列表项不走代码布局）──

        private T SpawnRow<T>(T template, float height) where T : Component
        {
            var row = UnityEngine.Object.Instantiate(template, view.rowsRoot);
            row.gameObject.SetActive(true);
            var rect = (RectTransform)row.transform;
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -rowCursor);
            rowCursor += height;
            rows.Add(row.gameObject);
            return row;
        }

        private void AddHeader(string title)
        {
            if (view.headerTemplate == null) return;
            var row = SpawnRow(view.headerTemplate, 58f);
            if (row.title != null) row.title.text = $"<color=#4A6FA5>|</color> {title}";
        }

        /// <summary>制作组名单行：复用分节标题模板（藏掉分隔线），一行 = 成员 + 分工 + 通道/部门。</summary>
        private void AddCredit(string name, string channel, string department, string role)
        {
            if (view.headerTemplate == null) return;
            var row = SpawnRow(view.headerTemplate, 54f);
            if (row.rule != null) row.rule.gameObject.SetActive(false);
            if (row.title == null) return;
            // 放宽到整行：锚住原左边缘、只向右生长（pivot 居中直接改宽会向左伸出去压到页签列）
            var rect = row.title.rectTransform;
            var leftEdge = rect.anchoredPosition.x - rect.sizeDelta.x * rect.pivot.x;
            rect.pivot = new Vector2(0, rect.pivot.y);
            rect.sizeDelta = new Vector2(900, 36);
            rect.anchoredPosition = new Vector2(leftEdge, rect.anchoredPosition.y);
            row.title.text = $"{name}　<color=#4A6FA5>{role}</color>　" +
                             $"<size=17><color=#9A8C7E>{channel} · {department}</color></size>";
        }

        private void AddSlider(string label, Func<int> getter, Action<int> setter)
        {
            if (view.sliderTemplate == null) return;
            var row = SpawnRow(view.sliderTemplate, 78f);
            if (row.label != null) row.label.text = label;
            if (row.slider == null) return;
            // 圆点保正圆（2026-08-16 反馈）：取短边压成正方形——尊重 Prefab 手调的大小，只修比例
            var handle = row.slider.handleRect;
            if (handle != null)
            {
                var side = Mathf.Min(handle.sizeDelta.x, handle.sizeDelta.y);
                if (side > 0f) handle.sizeDelta = new Vector2(side, side);
            }
            row.slider.SetValueWithoutNotify(getter());
            if (row.value != null) row.value.text = getter().ToString();
            row.slider.onValueChanged.AddListener(raw =>
            {
                var value = Mathf.RoundToInt(raw);
                setter(value);
                if (row.value != null) row.value.text = value.ToString();
                HouseSettings.Apply(); // 即时生效（音量当场可感知），回车才落盘
            });
        }

        private void AddOption(string label, string[] options, int selected, Action<int> setter)
        {
            if (view.optionTemplate == null) return;
            var row = SpawnRow(view.optionTemplate, 78f);
            if (row.label != null) row.label.text = label;
            var current = Mathf.Clamp(selected, 0, options.Length - 1);
            if (row.value != null) row.value.text = options[current];
            void Step(int direction)
            {
                current = (current + direction + options.Length) % options.Length;
                if (row.value != null) row.value.text = options[current];
                setter(current);
                HouseSettings.Apply(); // 即时生效（昼夜开关当场可见），回车才落盘
            }
            if (row.left != null) HouseUIUtil.BindButton(row.left, () => Step(-1));
            if (row.right != null) HouseUIUtil.BindButton(row.right, () => Step(1));
        }
    }
}

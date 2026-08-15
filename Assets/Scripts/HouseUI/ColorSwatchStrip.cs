using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>一个色块的展示数据。</summary>
    public readonly struct ColorSwatchItem
    {
        /// <summary>内芯颜色（家具表「色值」列）。</summary>
        public readonly Color Color;
        /// <summary>压暗：商城里 = 已拥有该配色；收纳栏里 = 该配色余量为 0。</summary>
        public readonly bool Dimmed;

        public ColorSwatchItem(Color color, bool dimmed)
        {
            Color = color;
            Dimmed = dimmed;
        }
    }

    /// <summary>
    /// 色块条：一族的全部配色排成一行/一列，点击选中、悬停预览。
    ///
    /// **商城的选色行、商城获得弹窗的配色列、收纳栏槽位的色块条共用本类**（家具族体系说明 §4.3：
    /// 「与商城的交互形态保持一致，玩家学一次就会」）。三处共用一个 <c>ColorSwatch.prefab</c> 模板，
    /// 差异全部走 <see cref="Build"/> 的参数，不是三份抄来抄去的代码。
    ///
    /// 容器（root）来自各自的 Prefab，位置尺寸以 Prefab 为准（§16.2）；本类只负责往里塞色块。
    /// </summary>
    public sealed class ColorSwatchStrip
    {
        /// <summary>点选了某个配色（下标）。</summary>
        public event Action<int> Selected;
        /// <summary>悬停变化：下标，或 -1 表示移出（用于「悬停即预览」）。</summary>
        public event Action<int> Previewed;

        private readonly List<OutGameColorSwatchView> chips = new List<OutGameColorSwatchView>();
        private IReadOnlyList<ColorSwatchItem> items;
        private RectTransform root;
        private float spacing = 34f;
        private Vector2 chipSize = new Vector2(26, 26);
        private bool vertical;
        private bool interactive = true;
        private int selectedIndex;
        private int hoverIndex = -1;

        /// <summary>色块数量（供调用方判断「这一族有没有多配色」）。</summary>
        public int Count => chips.Count;
        /// <summary>色块条铺开后的总长度（横向 = 宽），供外层滚动容器设置内容尺寸。</summary>
        public float ContentLength => chips.Count == 0 ? 0f : (chips.Count - 1) * spacing + (vertical ? chipSize.y : chipSize.x);

        /// <param name="container">来自 Prefab 的容器；色块作为它的子物体生成</param>
        /// <param name="interactive">false = 只展示不可点（商城的获得弹窗就是这种）</param>
        public void Build(RectTransform container, Vector2 size, float gap, bool vertical = false, bool interactive = true)
        {
            root = container;
            chipSize = size;
            spacing = gap;
            this.vertical = vertical;
            this.interactive = interactive;
        }

        /// <summary>重建色块（配色数量变了才需要调用；只是选中态变化用 <see cref="Refresh"/>）。</summary>
        public void Rebuild(IReadOnlyList<ColorSwatchItem> items, int selected)
        {
            Clear();
            if (root == null || items == null) return;
            var template = Resources.Load<GameObject>(OutGamePrefabResourcePaths.ColorSwatch);
            if (template == null)
            {
                Debug.LogError("[HouseUI] 色块模板缺失（§16.2 不回退代码布局）：" + OutGamePrefabResourcePaths.ColorSwatch);
                return;
            }
            selectedIndex = selected;
            hoverIndex = -1;
            for (var i = 0; i < items.Count; i++)
            {
                var index = i;
                var go = UnityEngine.Object.Instantiate(template, root, false);
                go.name = "Swatch" + i;
                var chip = go.GetComponent<OutGameColorSwatchView>();
                if (chip == null) { UnityEngine.Object.Destroy(go); continue; }
                var rect = (RectTransform)go.transform;
                rect.sizeDelta = chipSize;
                // 横向从左往右铺；纵向从上往下铺（获得弹窗那一列的既有观感）
                rect.anchorMin = rect.anchorMax = vertical ? new Vector2(.5f, .5f) : new Vector2(0f, .5f);
                rect.pivot = new Vector2(.5f, .5f);
                rect.anchoredPosition = vertical
                    ? new Vector2(0f, (items.Count - 1) * spacing * .5f - i * spacing)
                    : new Vector2(chipSize.x * .5f + 2f + i * spacing, 0f);
                chips.Add(chip);

                if (chip.button != null)
                {
                    chip.button.transition = Selectable.Transition.None;
                    chip.button.interactable = interactive;
                    chip.button.onClick.RemoveAllListeners();
                    if (interactive) chip.button.onClick.AddListener(() => Select(index));
                }
                if (!interactive) continue;
                // 悬停即预览：本类只管自己的视觉，把「预览哪一件」抛给调用方
                var trigger = go.AddComponent<EventTrigger>();
                var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                enter.callback.AddListener(_ => { hoverIndex = index; RefreshVisuals(); Previewed?.Invoke(index); });
                trigger.triggers.Add(enter);
                var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                exit.callback.AddListener(_ => { hoverIndex = -1; RefreshVisuals(); Previewed?.Invoke(-1); });
                trigger.triggers.Add(exit);
            }
            this.items = items;
            RefreshVisuals();
        }

        /// <summary>只更新视觉（选中态/压暗态变了，色块数量没变）。</summary>
        public void Refresh(IReadOnlyList<ColorSwatchItem> updated, int selected)
        {
            if (updated != null) items = updated;
            selectedIndex = selected;
            RefreshVisuals();
        }

        private void Select(int index)
        {
            selectedIndex = index;
            hoverIndex = -1;
            RefreshVisuals();
            Selected?.Invoke(index);
        }

        /// <summary>外框三态：选中 &gt; 悬停 &gt; 默认；压暗的配色内芯调暗以示区分。</summary>
        private void RefreshVisuals()
        {
            if (items == null) return;
            for (var i = 0; i < chips.Count && i < items.Count; i++)
            {
                var chip = chips[i];
                if (chip == null) continue;
                if (chip.frame != null)
                {
                    // 素材文件名 color-deault 是既有拼写（美术资源名），不要顺手"修正"
                    var state = i == selectedIndex ? "selected" : i == hoverIndex ? "hover" : "deault";
                    var sprite = Resources.Load<Sprite>("OutGameUI/store/color-" + state);
                    if (sprite != null) chip.frame.sprite = sprite;
                }
                if (chip.fill != null)
                {
                    var color = items[i].Color;
                    chip.fill.color = items[i].Dimmed
                        ? new Color(color.r * .55f, color.g * .55f, color.b * .55f, 1f)
                        : color;
                }
            }
        }

        public void Clear()
        {
            foreach (var chip in chips)
                if (chip != null) UnityEngine.Object.Destroy(chip.gameObject);
            chips.Clear();
            items = null;
            hoverIndex = -1;
        }
    }
}

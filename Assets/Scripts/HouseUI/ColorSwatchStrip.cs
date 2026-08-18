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

        /// <summary>选中/悬停外框的染色（2.0 商店主题蓝；1.0 素材本身是粉色框）。</summary>
        private static readonly Color SelectedFrameTint = new Color(.35f, .55f, .82f);
        private static readonly Color HoverFrameTint = new Color(.58f, .72f, .9f);

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
            var host = EnsureScrollContent(items.Count); // 配色多到装不下时，容器变成可滚动条
            for (var i = 0; i < items.Count; i++)
            {
                var index = i;
                var go = UnityEngine.Object.Instantiate(template, host, false);
                go.name = "Swatch" + i;
                var chip = go.GetComponent<OutGameColorSwatchView>();
                if (chip == null) { UnityEngine.Object.Destroy(go); continue; }
                var rect = (RectTransform)go.transform;
                rect.sizeDelta = chipSize;
                // 横向从左往右铺；纵向从上往下铺（获得弹窗那一列的既有观感）。
                // 滚动态的内容层是顶部对齐的，锚点必须跟着挪到顶，否则色块会排到视口外（看起来就是「列表没了」）
                rect.anchorMin = rect.anchorMax = vertical
                    ? (scrollContent != null ? new Vector2(.5f, 1f) : new Vector2(.5f, .5f))
                    : new Vector2(0f, .5f);
                rect.pivot = new Vector2(.5f, .5f);
                // 滚动态从起点依次铺开；非滚动态保持原来的居中排布
                rect.anchoredPosition = vertical
                    ? new Vector2(0f, scrollContent != null
                        ? -(chipSize.y * .5f + 2f + i * spacing)
                        : (items.Count - 1) * spacing * .5f - i * spacing)
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
                    // 外框素材是 1.0 的粉色，2.0 商店是蓝色主题：选中/悬停时染成蓝框（默认态保持原色）
                    chip.frame.color = i == selectedIndex ? SelectedFrameTint
                        : i == hoverIndex ? HoverFrameTint : Color.white;
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

        /// <summary>滚动内容层（配色装不下时才建）；null = 当前不需要滚动，色块直接挂在容器上。</summary>
        private RectTransform scrollContent;

        /// <summary>
        /// 配色多到超出容器时，把容器就地改造成滚动条：容器当视口（裁剪 + ScrollRect），
        /// 色块塞进运行时新建的内容层。装得下就退化为原来的居中排布。
        /// **只动运行时实例，Prefab 的位置尺寸仍是唯一真相源**（§16.2）。
        /// </summary>
        private RectTransform EnsureScrollContent(int count)
        {
            if (root == null) return null;
            var viewport = vertical ? root.rect.height : root.rect.width;
            var needed = count <= 0 ? 0f : (count - 1) * spacing + (vertical ? chipSize.y : chipSize.x) + 4f;
            if (viewport <= 1f || needed <= viewport + 1f)
            {
                // 装得下：拆掉可能存在的滚动改造，回到直接挂在容器上的老行为
                if (scrollContent != null) UnityEngine.Object.Destroy(scrollContent.gameObject);
                scrollContent = null;
                var staleScroll = root.GetComponent<ScrollRect>();
                if (staleScroll != null) staleScroll.enabled = false;
                return root;
            }

            if (scrollContent == null)
            {
                var go = new GameObject("SwatchContent", typeof(RectTransform)) { layer = root.gameObject.layer };
                scrollContent = (RectTransform)go.transform;
                scrollContent.SetParent(root, false);
            }
            if (vertical)
            {
                scrollContent.anchorMin = new Vector2(0f, 1f);
                scrollContent.anchorMax = new Vector2(1f, 1f);
                scrollContent.pivot = new Vector2(.5f, 1f);
                scrollContent.sizeDelta = new Vector2(0f, needed);
            }
            else
            {
                scrollContent.anchorMin = new Vector2(0f, 0f);
                scrollContent.anchorMax = new Vector2(0f, 1f);
                scrollContent.pivot = new Vector2(0f, .5f);
                scrollContent.sizeDelta = new Vector2(needed, 0f);
            }
            scrollContent.anchoredPosition = Vector2.zero;

            if (root.GetComponent<RectMask2D>() == null) root.gameObject.AddComponent<RectMask2D>();
            // 视口需要一块可接收射线的图形，否则色块之间的空隙滚不动
            if (root.GetComponent<Graphic>() == null)
            {
                var pad = root.gameObject.AddComponent<Image>();
                pad.color = new Color(0f, 0f, 0f, 0f);
            }
            var scroll = root.GetComponent<ScrollRect>();
            if (scroll == null) scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.enabled = true;
            scroll.content = scrollContent;
            scroll.viewport = root;
            scroll.horizontal = !vertical;
            scroll.vertical = vertical;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 22f;
            scroll.inertia = false; // 色块条短，惯性滑动反而不好停
            return scrollContent;
        }
    }
}

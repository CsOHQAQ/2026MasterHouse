using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 相机档位 → UI 显隐绑定（§16.3 每页绑定独立成文件）：收集 Hub Prefab 里挂了 HubTierVisibility
    /// 的区块，切档时错峰「淡入+位移浮入」（隐藏反向浮出），并同步射线与交互开关。
    /// 可见性只有一条组合规则：**非观景模式 且 当前档位允许**——观景模式开着时切档不回显任何区块，
    /// 展开界面时恢复的也只是当前档该见的那些。
    /// </summary>
    public sealed class HubTierUiBinder
    {
        private sealed class Entry
        {
            public HubTierVisibility marker;
            public RectTransform rect;
            public CanvasGroup group;
            /// <summary>Prefab 手调原位（浮入终点）。Bind 时采样，必须在任何入场动效挪位之前。</summary>
            public Vector2 home;
        }

        private readonly List<Entry> entries = new List<Entry>();
        private HubPage page;
        private bool immersive;

        public void Bind(OutGameHubView view, HubPage owner)
        {
            page = owner;
            immersive = false;
            entries.Clear();
            foreach (var marker in view.GetComponentsInChildren<HubTierVisibility>(true))
            {
                if (!(marker.transform is RectTransform rect)) continue;
                entries.Add(new Entry
                {
                    marker = marker,
                    rect = rect,
                    group = HouseUIUtil.Group(marker.gameObject),
                    home = rect.anchoredPosition,
                });
            }
            // 进场按初始档位直达不播切档动画；可见区块的错峰入场动效仍归 AnimateHubIn
            Apply(false);
        }

        /// <summary>相机跨过档位分界（HubPage.NotifyCameraTierChanged 转发）。</summary>
        public void OnTierChanged() => Apply(true);

        /// <summary>观景模式开合（HubPage.SetImmersive 调）：开 = 档位区块全部浮出；关 = 恢复当前档该见的。</summary>
        public void SetImmersive(bool on)
        {
            immersive = on;
            Apply(true);
        }

        /// <summary>该区块此刻是否可见。没挂标记恒可见（SetImmersive/AnimateHubIn 以此判断归属与跳过）。</summary>
        public bool ShouldShow(Transform block)
        {
            var marker = block.GetComponent<HubTierVisibility>();
            return marker == null || (!immersive && marker.VisibleAt(page.ViewTier));
        }

        private void Apply(bool animated)
        {
            foreach (var entry in entries)
            {
                if (entry.rect == null) continue; // 区块可能已随页面销毁（补间回调边缘）
                var show = !immersive && entry.marker.VisibleAt(page.ViewTier);
                entry.group.DOKill();
                entry.rect.DOKill();
                entry.group.blocksRaycasts = show;
                entry.group.interactable = show;
                var hidden = entry.home + entry.marker.floatOffset;
                if (!animated)
                {
                    entry.group.alpha = show ? 1f : 0f;
                    entry.rect.anchoredPosition = show ? entry.home : hidden;
                    continue;
                }
                if (show)
                {
                    // 完全隐着时从偏移位起浮；半途被打断则就地续接，不跳位
                    if (entry.group.alpha <= .01f) entry.rect.anchoredPosition = hidden;
                    var delay = Random.Range(.03f, .22f); // 错峰浮入，与 AnimateHubIn 同手感
                    entry.group.DOFade(1f, entry.marker.fadeDuration).SetUpdate(true).SetDelay(delay);
                    entry.rect.DOAnchorPos(entry.home, entry.marker.moveDuration)
                        .SetEase(Ease.OutCubic).SetUpdate(true).SetDelay(delay);
                }
                else
                {
                    entry.group.DOFade(0f, entry.marker.fadeDuration).SetUpdate(true);
                    entry.rect.DOAnchorPos(hidden, entry.marker.moveDuration)
                        .SetEase(Ease.InCubic).SetUpdate(true);
                }
            }
        }
    }
}

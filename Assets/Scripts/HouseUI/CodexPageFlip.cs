using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 图鉴详情页的翻书动效（2026-08-19）：切换角色时书页沿书脊合上再摊开，
    /// 内容在完全合拢的那一帧替换——于是看不到「原地换图」，只看到翻了一页。
    ///
    /// 做法是把页面内容整体挂到一个以**书脊**为轴心的容器下，横向缩放 1 → 0 → 1；
    /// 合拢过程压一层暗色当书页的背光。整屏底图（书本本身）与底部键位条留在外面不参与，
    /// 否则连书带桌一起没了。
    ///
    /// 非布局件，运行时挂：容器是运行时建的，节点用 worldPositionStays 移进去，
    /// 位置尺寸一概不改，Prefab 不动（§16.2）。
    /// </summary>
    public sealed class CodexPageFlip : MonoBehaviour
    {
        /// <summary>合拢与摊开的时长；合拢略快，摊开略慢，手感上像纸被翻过去再落下。</summary>
        private const float FoldSeconds = .16f;
        private const float UnfoldSeconds = .2f;

        private RectTransform pageRoot;
        private Image shade;
        private Sequence sequence;

        /// <summary>把这些节点之外的内容收进翻页容器（底图与键位条不参与翻页）。</summary>
        public void Bind(RectTransform root, params Transform[] excluded)
        {
            if (pageRoot != null) return;
            var go = new GameObject("PageFlipRoot", typeof(RectTransform)) { layer = root.gameObject.layer };
            pageRoot = (RectTransform)go.transform;
            pageRoot.SetParent(root, false);
            pageRoot.anchorMin = Vector2.zero;
            pageRoot.anchorMax = Vector2.one;
            pageRoot.offsetMin = pageRoot.offsetMax = Vector2.zero;
            pageRoot.pivot = new Vector2(.5f, .5f); // 书脊在画面正中，缩放轴就取这里

            // 收集要翻的内容：先记下来再搬，边遍历边改父级会漏
            var moving = new System.Collections.Generic.List<Transform>();
            foreach (Transform child in root)
            {
                if (child == pageRoot) continue;
                var skip = false;
                foreach (var keep in excluded)
                    if (keep != null && (child == keep || keep.IsChildOf(child))) { skip = true; break; }
                if (!skip) moving.Add(child);
            }
            foreach (var child in moving) child.SetParent(pageRoot, true);

            // 合拢时压暗：纸背光的意思，摊开后归零
            var shadeGo = new GameObject("FlipShade", typeof(RectTransform)) { layer = root.gameObject.layer };
            var shadeRect = (RectTransform)shadeGo.transform;
            shadeRect.SetParent(pageRoot, false);
            shadeRect.anchorMin = Vector2.zero;
            shadeRect.anchorMax = Vector2.one;
            shadeRect.offsetMin = shadeRect.offsetMax = Vector2.zero;
            shade = shadeGo.AddComponent<Image>();
            shade.color = Color.clear;
            shade.raycastTarget = false;
        }

        /// <summary>翻一页：合拢 → 在完全合拢时执行 swap → 摊开。未绑定时直接执行 swap。</summary>
        public void Play(Action swap)
        {
            if (pageRoot == null) { swap?.Invoke(); return; }
            sequence?.Kill();
            pageRoot.localScale = Vector3.one;
            sequence = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            sequence.Append(pageRoot.DOScaleX(0f, FoldSeconds).SetEase(Ease.InQuad));
            sequence.Join(shade.DOColor(new Color(.18f, .16f, .14f, .45f), FoldSeconds).SetEase(Ease.InQuad));
            sequence.AppendCallback(() => swap?.Invoke());
            sequence.Append(pageRoot.DOScaleX(1f, UnfoldSeconds).SetEase(Ease.OutQuad));
            sequence.Join(shade.DOColor(Color.clear, UnfoldSeconds).SetEase(Ease.OutQuad));
        }

        private void OnDestroy() => sequence?.Kill();
    }
}

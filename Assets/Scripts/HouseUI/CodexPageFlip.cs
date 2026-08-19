using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 图鉴详情页的翻书动效（2026-08-19）：切换角色时**右页绕书脊翻过去**，
    /// 内容在页面立起来（宽度为 0）的那一帧替换，再落下——于是看到的是翻了一页，
    /// 不是原地换图。
    ///
    /// 关键是轴心在**书脊**而不是画面中心：右半页的 pivot 取在左边缘（正好压着书脊），
    /// 横向缩放 1 → 0 就是页面立起来的过程，0 → 1 是落到另一面。
    /// 两半页都翻，但**错峰**：立起时右页先、左页晚一拍；落下时左页先、右页晚一拍。
    /// 于是读到的是一页从右扫向左，而不是两边同时朝中间折（那是合上书）。
    ///
    /// 非布局件，运行时挂：两个半页容器是运行时建的，节点按 worldPositionStays 移进去，
    /// 位置尺寸一概不改，Prefab 不动（§16.2）。
    /// </summary>
    public sealed class CodexPageFlip : MonoBehaviour
    {
        /// <summary>立起来与落下的时长；立起略快、落下略慢，像纸被掀过去再铺平。</summary>
        private const float FoldSeconds = .24f;
        private const float UnfoldSeconds = .3f;
        /// <summary>翻起来时纸面略微鼓一点（纸被掀起会拱），比纯横向压扁柔和。</summary>
        private const float CurlBulge = 1.035f;

        /// <summary>左右两页的错峰：差这么一拍，才读得出「从右扫向左」。</summary>
        private const float PageStagger = .06f;

        private RectTransform leftPage;
        private RectTransform rightPage;
        /// <summary>两页各自的纸背：翻起来的时候盖在内容上，落下时撤掉露出新内容。</summary>
        private Texture2D paper;
        private RawImage leftBack;
        private RawImage rightBack;
        private Sequence sequence;

        /// <summary>
        /// 把内容按左右半页收进两个以书脊为轴的容器。
        /// </summary>
        /// <param name="paperBack">纸背贴图（书页纸面，烘在 Prefab 上）。</param>
        /// <param name="background">整屏底图：不参与翻页，且**必须留在最底下**。</param>
        /// <param name="topmost">不参与翻页、且要压在翻页层之上的（帆船、键位条）。</param>
        public void Bind(RectTransform root, Texture2D paperBack, Transform background, params Transform[] topmost)
        {
            if (rightPage != null) return;
            paper = paperBack;
            var excluded = new List<Transform>(topmost) { background };
            // 左半页：pivot 在右边缘；右半页：pivot 在左边缘。两者的轴心都落在书脊上
            leftPage = CreateHalf(root, "PageLeft", new Vector2(0f, 0f), new Vector2(.5f, 1f), new Vector2(1f, .5f));
            rightPage = CreateHalf(root, "PageRight", new Vector2(.5f, 0f), new Vector2(1f, 1f), new Vector2(0f, .5f));

            // 先记下来再搬：边遍历边改父级会漏
            var moving = new List<Transform>();
            foreach (Transform child in root)
            {
                if (child == leftPage || child == rightPage) continue;
                var skip = false;
                foreach (var keep in excluded)
                    if (keep != null && (child == keep || keep.IsChildOf(child))) { skip = true; break; }
                if (!skip) moving.Add(child);
            }
            var spine = root.rect.width * .5f;
            foreach (var child in moving)
            {
                // 按节点中心落在书脊哪一侧分页；跨中缝的（大立绘）算右页
                var rect = child as RectTransform;
                var center = rect != null ? RootCenterX(root, rect) : spine;
                child.SetParent(center >= spine ? rightPage : leftPage, true);
            }
            // 纸背要盖住本页全部内容，所以最后建（画在最上），默认不显示
            leftBack = CreatePaperBack(leftPage);
            rightBack = CreatePaperBack(rightPage);
            // 底图压回最底（它也在排除名单里，但绝不能提到上面——提上去整本书就盖住内容了）
            if (background != null && background.parent == root) background.SetAsFirstSibling();
            // 帆船与键位条压到最上：两个半页容器是后建的，不提上来会被翻页层盖住
            foreach (var keep in topmost)
                if (keep != null && keep.parent == root && keep != leftPage && keep != rightPage)
                    keep.SetAsLastSibling();
        }


        private static RectTransform CreateHalf(RectTransform root, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = root.gameObject.layer };
            var rect = (RectTransform)go.transform;
            rect.SetParent(root, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.pivot = pivot;
            return rect;
        }

        /// <summary>节点中心换算到 root 的局部 x（各节点锚点不一，直接比 anchoredPosition 不作数）。</summary>
        private static float RootCenterX(RectTransform root, RectTransform rect)
        {
            var world = rect.TransformPoint(rect.rect.center);
            return root.InverseTransformPoint(world).x + root.rect.width * .5f;
        }

        /// <summary>
        /// 纸背：整页铺一张纸面贴图（从书页底图裁出的干净纸，左右两页共用）。
        /// 翻的过程盖在内容上——看到的是一张空白的纸立起来，而不是内容被横向压扁；
        /// 落下时撤掉，新内容就「露」出来了。
        /// </summary>
        private RawImage CreatePaperBack(RectTransform page)
        {
            var go = new GameObject("PageBack", typeof(RectTransform)) { layer = page.gameObject.layer };
            var rect = (RectTransform)go.transform;
            rect.SetParent(page, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = go.AddComponent<RawImage>();
            image.texture = paper;
            image.raycastTarget = false;
            go.SetActive(false);
            return image;
        }

        /// <summary>翻一页：右页立起 → 在立直那一帧执行 swap → 落下。未绑定时直接执行 swap。</summary>
        public void Play(Action swap)
        {
            if (rightPage == null) { swap?.Invoke(); return; }
            sequence?.Kill();
            rightPage.localScale = Vector3.one;
            leftPage.localScale = Vector3.one;
            sequence = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            // 立起：翻的那一面先亮出纸背，于是看到的是空白纸在翻，而不是内容被压扁
            sequence.AppendCallback(() => SetBack(rightBack, true));
            sequence.Append(rightPage.DOScaleX(0f, FoldSeconds).SetEase(Ease.InOutSine));
            sequence.Join(rightPage.DOScaleY(CurlBulge, FoldSeconds).SetEase(Ease.OutSine));
            sequence.InsertCallback(PageStagger, () => SetBack(leftBack, true));
            sequence.Insert(PageStagger, leftPage.DOScaleX(0f, FoldSeconds).SetEase(Ease.InOutSine));
            sequence.Insert(PageStagger, leftPage.DOScaleY(CurlBulge, FoldSeconds).SetEase(Ease.OutSine));
            sequence.AppendInterval(PageStagger);
            sequence.AppendCallback(() => swap?.Invoke());
            // 落下：左页先、右页晚一拍（方向和立起时相反，才是「扫过去」）；
            // 纸背在这一页立直（宽度为 0，看不见）时撤掉，落下的过程就是新内容露出来
            var landAt = sequence.Duration();
            sequence.InsertCallback(landAt, () => SetBack(leftBack, false));
            sequence.Insert(landAt, leftPage.DOScaleX(1f, UnfoldSeconds).SetEase(Ease.InOutSine));
            sequence.Insert(landAt, leftPage.DOScaleY(1f, UnfoldSeconds).SetEase(Ease.InOutSine));
            sequence.InsertCallback(landAt + PageStagger, () => SetBack(rightBack, false));
            sequence.Insert(landAt + PageStagger, rightPage.DOScaleX(1f, UnfoldSeconds).SetEase(Ease.InOutSine));
            sequence.Insert(landAt + PageStagger, rightPage.DOScaleY(1f, UnfoldSeconds).SetEase(Ease.InOutSine));
        }

        private static void SetBack(RawImage back, bool on)
        {
            if (back == null) return;
            back.transform.SetAsLastSibling(); // 内容可能被别处重排过，纸背必须压最上
            back.gameObject.SetActive(on);
        }

        private void OnDestroy()
        {
            sequence?.Kill();
            SetBack(leftBack, false);
            SetBack(rightBack, false);
        }
    }
}

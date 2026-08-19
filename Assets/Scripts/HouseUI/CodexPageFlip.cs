using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 图鉴详情页的翻书动效（2026-08-19 重做）：一张纸从右页扫向书脊，**纸底下就是新内容**。
    ///
    /// 与前一版的区别（反馈「太大、太僵硬、翻的时候看不到第二页」）：
    /// ① 内容本身不再参与动画——换页在纸盖住的那一瞬完成，纸扫过去时新内容已经在下面，
    ///    于是「翻开的过程」就是新页一点点露出来，而不是旧内容被横向压扁；
    /// ② 一次只有**一张**纸在动（右页那张），不再左右两块一起折，画面中央那条苍白的大板子没了；
    /// ③ 纸的前缘带一道柔和投影、纸面本身有极轻微的鼓与偏转，读起来是纸被掀起的弧，不是硬压。
    ///
    /// 非布局件，运行时挂：容器与纸都是运行时建的，内容节点按 worldPositionStays 移进去，
    /// 位置尺寸一概不改，Prefab 不动（§16.2）。
    /// </summary>
    public sealed class CodexPageFlip : MonoBehaviour
    {
        /// <summary>一次翻页的总时长。</summary>
        private const float TurnSeconds = .42f;
        /// <summary>左页内容的淡入时长（右页靠纸扫开来露出，左页只需要柔和地换掉）。</summary>
        private const float LeftFadeSeconds = .26f;
        /// <summary>纸被掀起时的鼓起与偏转：幅度都很小，多了就假。</summary>
        private const float CurlBulge = 1.02f;
        private const float CurlTilt = 1.6f;
        /// <summary>纸前缘投影的宽度（占半页宽的比例）。</summary>
        private const float EdgeShadowWidth = .16f;
        /// <summary>
        /// 书页在底图里的上下边（锚点口径，左下原点）：沿左页中线量底图的蓝色外框得到
        /// —— 上沿 y 0.1625、下沿 0.8889（从上算）。纸只在这条带里翻，
        /// 铺满整屏高度会从书的上下边探出去（2026-08-19 反馈）。
        /// </summary>
        /// 再各让进 0.8%：纸掀起时会纵向鼓 2%，不留这点余量鼓的时候又会探出书页。
        private const float PageBottom = .119f;
        private const float PageTop = .8295f;

        private RectTransform leftPage;
        private RectTransform rightPage;
        private CanvasGroup leftGroup;
        /// <summary>翻动的那张纸；pivot 在书脊上，横向缩放 1 → 0 就是被掀过去。</summary>
        private RectTransform sheet;
        private Sequence sequence;

        /// <summary>一条横向渐变（外侧深、内侧透明）：给纸的前缘当投影，比硬边好看得多。</summary>
        private static Texture2D edgeShadow;

        /// <summary>
        /// 把内容按左右半页收进两个容器，并在右页上方备好那张翻动的纸。
        /// </summary>
        /// <param name="paperBack">纸面贴图（书页纸，烘在 Prefab 上）。</param>
        /// <param name="background">整屏底图：不参与翻页，且必须留在最底。</param>
        /// <param name="topmost">不参与翻页、且要压在翻页层之上的（帆船、键位条）。</param>
        public void Bind(RectTransform root, Texture2D paperBack, Transform background, params Transform[] topmost)
        {
            if (sheet != null) return;
            var excluded = new List<Transform>(topmost) { background };
            leftPage = CreateHalf(root, "PageLeft", new Vector2(0f, 0f), new Vector2(.5f, 1f), new Vector2(.5f, .5f));
            rightPage = CreateHalf(root, "PageRight", new Vector2(.5f, 0f), new Vector2(1f, 1f), new Vector2(.5f, .5f));
            leftGroup = leftPage.gameObject.AddComponent<CanvasGroup>();

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

            // 翻动的纸：盖在右半页上，pivot 落在书脊（左边缘）
            sheet = CreateHalf(root, "TurningSheet",
                new Vector2(.5f, PageBottom), new Vector2(1f, PageTop), new Vector2(0f, .5f));
            var paper = sheet.gameObject.AddComponent<RawImage>();
            paper.texture = paperBack;
            paper.raycastTarget = false;
            // 前缘投影：压在纸的外侧边上，随纸一起扫过去，做出「纸是拱起来的」那点厚度感
            var edge = new GameObject("SheetEdge", typeof(RectTransform)) { layer = root.gameObject.layer };
            var edgeRect = (RectTransform)edge.transform;
            edgeRect.SetParent(sheet, false);
            edgeRect.anchorMin = new Vector2(1f - EdgeShadowWidth, 0f);
            edgeRect.anchorMax = Vector2.one;
            edgeRect.offsetMin = edgeRect.offsetMax = Vector2.zero;
            var edgeImage = edge.AddComponent<RawImage>();
            edgeImage.texture = EdgeShadowTexture();
            edgeImage.raycastTarget = false;
            sheet.gameObject.SetActive(false);

            // 底图压回最底（提上去整本书就盖住内容了）；帆船与键位条压到最上
            if (background != null && background.parent == root) background.SetAsFirstSibling();
            foreach (var keep in topmost)
                if (keep != null && keep.parent == root) keep.SetAsLastSibling();
        }

        private static Texture2D EdgeShadowTexture()
        {
            if (edgeShadow != null) return edgeShadow;
            const int width = 64;
            edgeShadow = new Texture2D(width, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (var x = 0; x < width; x++)
            {
                var t = x / (float)(width - 1);
                edgeShadow.SetPixel(x, 0, new Color(.35f, .3f, .26f, t * t * .28f));
            }
            edgeShadow.Apply();
            return edgeShadow;
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
        /// 翻一页：纸先整张盖住右页 → **立刻换内容**（此刻被纸挡着，看不见）→
        /// 纸向书脊扫过去，新内容一点点露出来。左页同时柔和地淡入新内容。
        /// </summary>
        public void Play(Action swap)
        {
            if (sheet == null) { swap?.Invoke(); return; }
            sequence?.Kill();
            sheet.gameObject.SetActive(true);
            sheet.localScale = Vector3.one;
            sheet.localEulerAngles = Vector3.zero;

            swap?.Invoke(); // 纸此刻盖着右页，换内容看不见；纸扫开的过程就是新页露出来

            sequence = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            sequence.Append(sheet.DOScaleX(0f, TurnSeconds).SetEase(Ease.InOutSine));
            // 纸被掀起来会先拱一点再落平，配一点点偏转，弧度就出来了
            sequence.Join(sheet.DOScaleY(CurlBulge, TurnSeconds * .45f).SetEase(Ease.OutSine));
            sequence.Insert(TurnSeconds * .45f, sheet.DOScaleY(1f, TurnSeconds * .55f).SetEase(Ease.InSine));
            sequence.Join(sheet.DOLocalRotate(new Vector3(0, 0, CurlTilt), TurnSeconds * .5f).SetEase(Ease.OutSine));
            sequence.Insert(TurnSeconds * .5f, sheet.DOLocalRotate(Vector3.zero, TurnSeconds * .5f).SetEase(Ease.InSine));
            if (leftGroup != null)
            {
                leftGroup.alpha = 0f;
                sequence.Join(leftGroup.DOFade(1f, LeftFadeSeconds).SetEase(Ease.OutSine));
            }
            sequence.OnComplete(() => sheet.gameObject.SetActive(false));
        }

        private void OnDestroy()
        {
            sequence?.Kill();
            if (sheet != null) sheet.gameObject.SetActive(false);
        }
    }
}

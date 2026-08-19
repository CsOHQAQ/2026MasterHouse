using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 图鉴详情页的翻书动效（2026-08-19）：分帧里**只有那张飞着的纸**（按用户要求
    /// 从视频帧里单独抠出，书和背景全部透明），翻页时它直接飞在真实 UI 之上——
    /// 内容全程可见、被纸实打实地遮住，不需要盖层/条带/裁切那一套。
    /// 往后翻正放（纸从右往左），往前翻倒放（纸从左往右）。
    /// 换页藏在纸飞到中途的一瞬：内容快速压暗换掉再回来，眼睛在纸上注意不到。
    /// 连点每次都新开一层、各放完整段，几张纸同时在空中就是快速哗哗翻。
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

        /// <summary>换页时刻（进度 0~1）：纸飞到中途、最吸睛的一瞬。</summary>
        private const float SwapAt = .5f;
        /// <summary>换页前后内容快速压暗再回来的半窗（进度）。</summary>
        private const float SwapDip = .12f;


        /// <summary>裁切窗：翻页时它的左右边跟着纸的前缘收，内容被切掉的地方就露出空白书页。</summary>
        private RectTransform clipWindow;
        /// <summary>装内容的那层：钉在裁切窗的角上、尺寸恒等于整页，裁切窗怎么收它都不动。</summary>
        private RectTransform pageHolder;
        private RectTransform book;
        private float bookWidth;
        private RectTransform leftPage;
        private RectTransform rightPage;
        private CanvasGroup leftGroup;
        private CanvasGroup rightGroup;
        /// <summary>整幅底图：静止不动，只用来拿 cover 裁切的 uvRect。</summary>
        private RawImage background;
        /// <summary>翻动的那张纸；pivot 在书脊上，横向缩放 1 → 0 就是被掀过去。</summary>
        private RectTransform sheet;
        private Sequence sequence;
        /// <summary>连点时最新一次翻页的令牌：内容的显隐只听最新那次的。</summary>
        private int turnToken;

        /// <summary>一层并发的翻页动画：整幅只有那张纸（其余透明），直接叠在 UI 上。</summary>
        private sealed class TurnLayer
        {
            public RectTransform rect;
            public RawImage image;
        }
        private readonly List<TurnLayer> turnLayers = new List<TurnLayer>();
        private RectTransform turnLayerRoot;

        /// <summary>一条横向渐变（外侧深、内侧透明）：给纸的前缘当投影，比硬边好看得多。</summary>
        private static Texture2D edgeShadow;

        /// <summary>
        /// 把内容按左右半页收进两个容器，并在右页上方备好那张翻动的纸。
        /// </summary>
        /// <param name="paperBack">纸面贴图（书页纸，烘在 Prefab 上）。</param>
        /// <param name="backdrop">整屏底图：不参与翻页、留在最底，翻页时由它来播分帧。</param>
        /// <param name="topmost">不参与翻页、且要压在翻页层之上的（帆船、键位条）。</param>
        public void Bind(RectTransform root, Texture2D paperBack, RawImage backdrop, params Transform[] topmost)
        {
            if (sheet != null) return;
            background = backdrop;
            var backdropTransform = backdrop != null ? backdrop.transform : null;
            var excluded = new List<Transform>(topmost) { backdropTransform };
            book = root;
            bookWidth = root.rect.width;
            clipWindow = CreateHalf(root, "PageClip", Vector2.zero, Vector2.one, new Vector2(.5f, .5f));
            // 内容层钉在裁切窗的左下角、尺寸写死成整页：裁切窗收边时它岿然不动，
            // 只是被裁掉一截（若跟着锚点拉伸，内容会跟着挤变形）
            pageHolder = CreateHalf(clipWindow, "PageHolder", Vector2.zero, Vector2.zero, Vector2.zero);
            pageHolder.sizeDelta = root.rect.size;
            pageHolder.anchoredPosition = Vector2.zero;
            // 两半页的 pivot 都放在书脊上：横向缩放就是「以书脊为轴翻过去」
            leftPage = CreateHalf(pageHolder, "PageLeft", new Vector2(0f, 0f), new Vector2(.5f, 1f), new Vector2(1f, .5f));
            rightPage = CreateHalf(pageHolder, "PageRight", new Vector2(.5f, 0f), new Vector2(1f, 1f), new Vector2(0f, .5f));
            leftGroup = leftPage.gameObject.AddComponent<CanvasGroup>();
            rightGroup = rightPage.gameObject.AddComponent<CanvasGroup>();

            var moving = new List<Transform>();
            foreach (Transform child in root)
            {
                if (child == clipWindow) continue;
                var skip = false;
                foreach (var keep in excluded)
                    if (keep != null && (child == keep || keep.IsChildOf(child))) { skip = true; break; }
                if (!skip) moving.Add(child);
            }
            if (moving.Count == 0)
                Debug.LogWarning("[Codex] 翻页容器没收到任何内容节点：翻页时内容不会跟着纸走，检查详情页层级");
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

            // 翻页动画的图层容器：压在内容**之上**（2026-08-19 反馈：纸扫过来要挡住 UI）。
            // 条带窗只露前缘右侧，所以内容是被纸一点点盖掉的，不会一开场就全没
            turnLayerRoot = CreateHalf(root, "TurnLayers", Vector2.zero, Vector2.one, new Vector2(.5f, .5f));

            // 底图压回最底（提上去整本书就盖住内容了）
            if (backdropTransform != null && backdropTransform.parent == root) backdropTransform.SetAsFirstSibling();
            // 翻页层盖在内容上，帆船与键位条再压到翻页层之上（船永远最上）
            turnLayerRoot.SetAsLastSibling();
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
                edgeShadow.SetPixel(x, 0, new Color(.35f, .3f, .26f, Mathf.Pow(t, 1.6f) * .36f));
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
        /// 翻一页：每次调用都**新开一层**、把整段分帧从头放到尾，绝不打断上一次——
        /// 连点时几层同时在放，几张纸一起在空中，就是快速哗哗翻的样子。
        /// 分帧里只有那张纸，直接飞在内容之上；换页藏在纸飞到中途的一瞬
        ///（内容快速压暗 → 换 → 回来）。分帧缺失时退回代码模拟的纸片。
        /// </summary>
        /// <param name="reversed">true = 往前翻一页（倒放，纸从左往右飞）。</param>
        public void Play(Action swap, bool reversed = false)
        {
            if (turnLayerRoot == null || CodexPageTurnFrames.Count == 0) { PlayFallback(swap); return; }
            SetContentAlpha(1f);
            SyncSize();
            var layer = TakeLayer();
            var token = ++turnToken;
            var swapped = false;
            Sequence seq = null;
            seq = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            sequence = seq;
            seq.Append(DOTween.To(() => 0f, t =>
            {
                var frame = CodexPageTurnFrames.Sample(t, reversed);
                if (frame != null) layer.image.texture = frame;
                if (!swapped && t >= SwapAt) { swapped = true; swap?.Invoke(); }
                // 换页那一瞬内容压暗再回来；只有最新一层有权动内容
                if (token == turnToken)
                    SetContentAlpha(Mathf.Clamp01(Mathf.Abs(t - SwapAt) / SwapDip));
            }, 1f, TurnSeconds).SetEase(Ease.Linear));
            seq.OnComplete(() =>
            {
                layer.rect.gameObject.SetActive(false);
                if (token == turnToken) { sequence = null; SetContentAlpha(1f); }
            });
            seq.OnKill(() =>
            {
                layer.rect.gameObject.SetActive(false);
                if (!swapped) { swapped = true; swap?.Invoke(); } // 中断也要把这页落上
                if (token == turnToken) SetContentAlpha(1f);
            });
        }

        private void SetContentAlpha(float alpha)
        {
            if (leftGroup != null) leftGroup.alpha = alpha;
            if (rightGroup != null) rightGroup.alpha = alpha;
        }

        /// <summary>取一层空闲的翻页层（都在忙就新建），并压到容器最上——新纸盖在旧纸上面。</summary>
        private TurnLayer TakeLayer()
        {
            var layer = turnLayers.Find(x => !x.rect.gameObject.activeSelf);
            if (layer == null)
            {
                layer = new TurnLayer();
                layer.rect = CreateHalf(turnLayerRoot, "Turn", Vector2.zero, Vector2.one, new Vector2(.5f, .5f));
                layer.image = layer.rect.gameObject.AddComponent<RawImage>();
                layer.image.raycastTarget = false;
                turnLayers.Add(layer);
            }
            // 分帧与底图同一套构图、同为 16:9：照抄底图的 cover 裁切就严丝合缝
            if (background != null) layer.image.uvRect = background.uvRect;
            layer.image.texture = null;
            layer.rect.SetAsLastSibling();
            layer.rect.gameObject.SetActive(true);
            return layer;
        }

        /// <summary>整页尺寸对齐一次：Bind 那会儿画布未必已经排过版，宽度可能还是 0。</summary>
        private void SyncSize()
        {
            if (book == null || pageHolder == null) return;
            var size = book.rect.size;
            if (size.x <= 0f) return;
            bookWidth = size.x;
            pageHolder.sizeDelta = size;
        }

        /// <summary>分帧缺失时的兜底：还是那张代码模拟的纸扫过去。</summary>
        private void PlayFallback(Action swap)
        {
            if (sheet == null) { swap?.Invoke(); return; }
            sequence?.Kill();
            sheet.gameObject.SetActive(true);
            sheet.localScale = Vector3.one;
            sheet.localEulerAngles = Vector3.zero;
            swap?.Invoke();
            sequence = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            sequence.Append(sheet.DOScaleX(0f, TurnSeconds).SetEase(Ease.InOutSine));
            sequence.Join(sheet.DOScaleY(CurlBulge, TurnSeconds * .45f).SetEase(Ease.OutSine));
            sequence.Insert(TurnSeconds * .45f, sheet.DOScaleY(1f, TurnSeconds * .55f).SetEase(Ease.InSine));
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

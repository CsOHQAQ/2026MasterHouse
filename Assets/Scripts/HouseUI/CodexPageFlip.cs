using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 图鉴详情页的翻书动效（2026-08-19）：翻页时在底图上盖一本**抠出来的书**播分帧
    /// （BiRefNet 去背景、带透明通道）——纸的卷曲、投影、落页全是手绘的；
    /// 外圈云纹始终是详情页自己的底图，不会因视频背景不同而整屏跳变。往后翻正放、往前翻倒放。
    ///
    /// 内容**跟着纸走**（2026-08-19 反馈：不能只是原地不动地被切掉）。从分帧里量出每帧
    /// 「纸的前缘」在画面的横向位置，一条曲线同时驱动两件事：
    /// · 被翻走的那半页**以书脊为轴横向收拢**（纸转过去，印在上面的内容跟着转，直到立成一条线）；
    /// · 另半页被落下来的纸**从书脊往外盖住**（裁切边跟着纸的前缘走，盖到哪儿内容就没到哪儿）。
    /// 两页都清干净了才换内容，再按同一道边把新页抹回来。
    /// 素材缺失时退回代码模拟的纸片（见 PlayFallback），不至于完全没有动效。
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

        /// <summary>纸落定之后，新内容顺着同一道边抹回来的时长。</summary>
        private const float RevealSeconds = .2f;
        /// <summary>裁切边的横向羽化：硬边太像"擦除"，糊两个像素就贴着纸的前缘了。</summary>
        private const int ClipSoftness = 12;
        /// <summary>
        /// 被翻走那页的内容淡到全无的时机（以纸的前缘曲线取值计）。
        /// 比书脊(0.432)早不少——纸一卷起来内容就该跟着走，留到最后会看见"内容浮在纸上面"。
        /// </summary>
        private const float CarriedGone = .62f;
        /// <summary>另半页被落下来的纸盖住、内容淡到全无的时机。</summary>
        private const float RestingGone = .30f;

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
        /// <summary>翻页时盖在底图上的那本「抠出来的书」（分帧带透明通道，背景已去掉）。</summary>
        private RawImage turnBook;
        /// <summary>翻动的那张纸；pivot 在书脊上，横向缩放 1 → 0 就是被掀过去。</summary>
        private RectTransform sheet;
        private Sequence sequence;

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
            clipWindow.gameObject.AddComponent<RectMask2D>().softness = new Vector2Int(ClipSoftness, 0);
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

            // 播分帧的那本书：贴着底图的正上方、内容之下——分帧里的书页是空白的，
            // 盖到内容上就回到「一翻页内容立刻全没」的老问题
            var bookGo = new GameObject("TurnBook", typeof(RectTransform)) { layer = root.gameObject.layer };
            var bookRect = (RectTransform)bookGo.transform;
            bookRect.SetParent(root, false);
            bookRect.anchorMin = Vector2.zero;
            bookRect.anchorMax = Vector2.one;
            bookRect.offsetMin = bookRect.offsetMax = Vector2.zero;
            turnBook = bookGo.AddComponent<RawImage>();
            turnBook.raycastTarget = false;
            bookGo.SetActive(false);

            // 底图压回最底（提上去整本书就盖住内容了），抠出来的书紧跟其后；帆船与键位条压到最上
            if (backdropTransform != null && backdropTransform.parent == root) backdropTransform.SetAsFirstSibling();
            bookRect.SetSiblingIndex(backdropTransform != null ? backdropTransform.GetSiblingIndex() + 1 : 0);
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
        /// 翻一页。素材就位时**整幅底图播美术那圈翻书分帧**——纸的卷曲、投影、落页都是手绘的；
        /// 往后翻正放、往前翻倒放。
        ///
        /// 当前页不消失：裁切窗跟着纸的前缘收，纸扫到哪儿内容就被切到哪儿，
        /// 切掉的那块露出底下的空白书页。纸落定后换内容，再按同一道边把新页抹回来。
        /// 分帧缺失时退回代码模拟的纸片（不至于没有动效）。
        /// </summary>
        /// <param name="reversed">true = 往前翻一页（倒放，纸从左往右扫）。</param>
        public void Play(Action swap, bool reversed = false)
        {
            if (turnBook == null || CodexPageTurnFrames.Count == 0) { PlayFallback(swap); return; }
            sequence?.Kill();
            SyncSize();
            // 抠出来的书与底图同一套构图、同为 16:9：照抄底图的 cover 裁切就严丝合缝
            if (background != null) turnBook.uvRect = background.uvRect;
            turnBook.gameObject.SetActive(true);
            var swapped = false;
            ApplyTurn(1f, reversed);
            sequence = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            sequence.Append(DOTween.To(() => 0f, t =>
            {
                var frame = CodexPageTurnFrames.Sample(t, reversed);
                if (frame != null) turnBook.texture = frame;
                ApplyTurn(CodexPageTurnFrames.FrontAt(t, reversed), reversed);
            }, 1f, TurnSeconds).SetEase(Ease.Linear));
            sequence.AppendCallback(() =>
            {
                // 此刻内容已被纸清干净，换页看不见；书也落定了，撤掉分帧层
                swapped = true;
                swap?.Invoke();
                turnBook.gameObject.SetActive(false);
            });
            sequence.Append(DOTween.To(() => CodexPageTurnFrames.FrontAt(1f, reversed),
                f => ApplyTurn(f, reversed), 1f, RevealSeconds).SetEase(Ease.OutSine));
            // 正常播完也会走 OnKill（autoKill），收尾与被打断时一致
            sequence.OnKill(() =>
            {
                if (turnBook != null) turnBook.gameObject.SetActive(false);
                if (!swapped) swap?.Invoke();
                ApplyTurn(1f, reversed);
            });
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

        /// <summary>
        /// 按纸的位置摆内容。<paramref name="visible"/> = 纸还没扫到的那一段占整页宽的比例。
        /// 被翻走的那半页以书脊为轴收拢（内容跟着纸转过去），另半页按裁切边被盖掉。
        /// 正放翻走的是右页、留左边一段；倒放翻走的是左页、留右边一段。
        /// </summary>
        private void ApplyTurn(float visible, bool reversed)
        {
            // 被纸带走的那半页：书脊为轴横向压扁，压到 0 就是纸立成了一条线
            var carried = reversed ? leftPage : rightPage;
            var resting = reversed ? rightPage : leftPage;
            var carriedGroup = reversed ? leftGroup : rightGroup;
            var restingGroup = reversed ? rightGroup : leftGroup;
            var squash = Mathf.InverseLerp(CodexPageTurnFrames.SpineAt, 1f, visible);
            if (carried != null) carried.localScale = new Vector3(squash, 1f, 1f);
            if (resting != null) resting.localScale = Vector3.one;
            // 透明度和裁切各管一半、又互为兜底：纸卷走的那页跟着淡掉，
            // 被盖住的那页在纸压过来时淡掉，都比裁切边更早清干净（"消失得不够快"）
            if (carriedGroup != null) carriedGroup.alpha = Mathf.InverseLerp(CarriedGone, 1f, visible);
            if (restingGroup != null) restingGroup.alpha = Mathf.InverseLerp(0f, RestingGone, visible);
            SetClip(visible, reversed);
        }

        private void SetClip(float visible, bool reversed)
        {
            if (clipWindow == null || pageHolder == null) return;
            var cut = Mathf.Clamp01(1f - visible) * bookWidth;
            clipWindow.offsetMin = new Vector2(reversed ? cut : 0f, 0f);
            clipWindow.offsetMax = new Vector2(reversed ? 0f : -cut, 0f);
            pageHolder.anchoredPosition = new Vector2(-clipWindow.offsetMin.x, 0f);
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

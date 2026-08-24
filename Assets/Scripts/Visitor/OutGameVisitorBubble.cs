using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using F = MasterHouse.HouseUIRuntime;

namespace MasterHouse
{
    /// <summary>
    /// 访客头顶气泡（§9，2026-08-22 一轮测试改进 #5 重做）：一个组件、两套视觉，语义彻底分开——
    /// ①「···」气泡（美术贴图）：**可交互提示**，「此刻点他有对话」时常驻显示，取代原黄色感叹号；
    /// ②闲聊文字气泡：把从闲聊池抽到的那句台词真正显示出来（调度与选句都在业务侧，
    ///   见 DialogueManager.RequestBubble），淡入、上飘、停留、淡出。
    /// 原「墙钟自发无字冒泡」随本次改版退役：无字气泡在新语义下没有可承担的信息。
    ///
    /// 两套视觉在显示期间都吃点击（#7 点击热区扩大到气泡，仅气泡显示时）：
    /// uGUI 的点击会沿层级向上冒到演员根上的 Button，这里只开 raycastTarget、不自己转发；
    /// 隐藏时由 CanvasGroup.blocksRaycasts 一并关掉，热区不残留。
    /// 挂在演员节点下自动跟随移动。
    /// </summary>
    internal sealed class OutGameVisitorBubble : MonoBehaviour
    {
        /// <summary>
        /// 「···」气泡皮肤。**美术源在 Assets/PC ui 2.0/通用组件（toast）/聊天气泡.png**，
        /// 这里加载的是它在 Resources 下的副本（口径同 common/Toast）——换图时两处一起换。
        /// </summary>
        private const string SkinPath = "OutGameUI/common/ChatBubble";
        private const float BubbleSize = 60f;   // 显示边长（素材 200×200 方图，四周含发光留白）
        private const float FloatDistance = 5f;

        /// <summary>文字气泡的底板与墨色：美术的可拉伸底板未到位，先程序化纸底（素材到位改 Create）。</summary>
        private static readonly Color LinePaper = new Color(.97f, .95f, .92f, .95f);
        private static readonly Color LineInk = new Color(.27f, .27f, .29f, 1f);
        private static readonly Vector2 LineSize = new Vector2(250f, 58f);

        private RectTransform talkRect;   // ···（可交互提示）
        private CanvasGroup talkGroup;
        private RectTransform lineRect;   // 闲聊文字
        private CanvasGroup lineGroup;
        private Text lineLabel;

        private Vector2 basePosition;
        /// <summary>立绘头顶留白造成的下压量（演员按深度每帧喂）：气泡挂点跟着头走，不浮在半空。</summary>
        private float headDrop;
        private bool talkReady;
        private bool lineShowing;
        private float lineTimer; // 文字气泡剩余展示时长

        /// <summary>实际挂点：作者给的基准位置减去立绘头顶留白。</summary>
        private Vector2 AnchorPosition => basePosition - new Vector2(0f, headDrop);

        public static OutGameVisitorBubble Create(Transform parent, Vector2 anchoredPosition)
        {
            var root = F.Rect(parent, "Bubble", new Vector2(.5f, 1), new Vector2(.5f, 1),
                anchoredPosition, Vector2.zero);
            var bubble = root.gameObject.AddComponent<OutGameVisitorBubble>();
            bubble.basePosition = anchoredPosition;

            // ── ···气泡：底边挂点，anchoredPosition.y 就是气泡尾巴与演员头顶的真实间距 ──
            var talk = F.Panel(root, "Talk", new Vector2(.5f, 0), new Vector2(.5f, 0),
                Vector2.zero, new Vector2(BubbleSize, BubbleSize), Color.white);
            talk.raycastTarget = true; // 显示期间可点（#7）；隐藏时由 CanvasGroup 关热区
            talk.rectTransform.pivot = new Vector2(.5f, 0f);
            var skin = Resources.Load<Sprite>(SkinPath);
            if (skin == null) Debug.LogError($"[访客气泡] 缺少气泡皮肤 Resources/{SkinPath}，头顶将只剩一块白底");
            talk.sprite = skin;
            talk.type = Image.Type.Simple;   // 素材是整张图，不切九宫
            talk.preserveAspect = true;
            bubble.talkRect = talk.rectTransform;
            bubble.talkGroup = F.Group(talk.gameObject, 0f);
            bubble.talkGroup.blocksRaycasts = false;
            bubble.talkGroup.interactable = false;

            // ── 闲聊文字气泡：纸底 + 墨字，同一挂点（两套不会同时显示，见 SetTalkReady）──
            var line = F.Panel(root, "Line", new Vector2(.5f, 0), new Vector2(.5f, 0),
                Vector2.zero, LineSize, LinePaper);
            line.raycastTarget = true;
            line.rectTransform.pivot = new Vector2(.5f, 0f);
            F.Outline(line.gameObject, new Color(.35f, .3f, .25f, .35f), new Vector2(1, -1));
            bubble.lineRect = line.rectTransform;
            bubble.lineLabel = F.Label(line.transform, "Text", "", 16, LineInk,
                TextAnchor.MiddleCenter, FontStyle.Normal);
            bubble.lineLabel.raycastTarget = false;
            bubble.lineGroup = F.Group(line.gameObject, 0f);
            bubble.lineGroup.blocksRaycasts = false;
            bubble.lineGroup.interactable = false;

            return bubble;
        }

        /// <summary>演员按当前显示高度算出的头顶留白像素数（每帧喂；变了才重摆位置）。</summary>
        public void SetHeadDrop(float value)
        {
            if (Mathf.Abs(value - headDrop) < .5f) return;
            headDrop = value;
            if (transform is RectTransform rect) rect.anchoredPosition = AnchorPosition;
        }

        /// <summary>
        /// 「此刻点他有对话」的常驻提示（演员每帧按业务判据喂）：
        /// 亮起时把正在展示的闲聊文字收掉——可交互与闲聊不叠加，一个头顶只说一件事。
        /// </summary>
        public void SetTalkReady(bool ready)
        {
            if (talkReady == ready) return;
            talkReady = ready;
            if (talkGroup == null) return;
            talkGroup.DOKill();
            talkGroup.DOFade(ready ? 1f : 0f, .2f).SetTarget(this).SetUpdate(true);
            talkGroup.blocksRaycasts = ready;
            if (ready && lineShowing) HideLine();
        }

        /// <summary>
        /// 冒一句闲聊（业务调度触发，台词已选好格式好）：淡入、上飘、按配置时长停留后淡出。
        /// 可交互提示亮着时忽略——那时头顶的语义是「点我」，不该被闲聊盖掉。
        /// </summary>
        public void ShowLine(string text, float holdSeconds)
        {
            if (lineRect == null || talkReady || string.IsNullOrEmpty(text)) return;
            lineShowing = true;
            lineTimer = Mathf.Max(1f, holdSeconds);
            lineLabel.text = text;
            lineGroup.DOKill();
            lineRect.DOKill();
            lineRect.anchoredPosition = Vector2.zero;
            lineGroup.DOFade(1f, .22f).SetTarget(this).SetUpdate(true);
            lineGroup.blocksRaycasts = true;
            lineRect.DOAnchorPos(new Vector2(0, FloatDistance), lineTimer + .3f)
                .SetEase(Ease.OutSine).SetTarget(this).SetUpdate(true);
        }

        private void HideLine()
        {
            lineShowing = false;
            lineGroup.DOKill();
            lineGroup.DOFade(0f, .3f).SetTarget(this).SetUpdate(true);
            lineGroup.blocksRaycasts = false;
        }

        private void Update()
        {
            if (!lineShowing) return;
            lineTimer -= Time.unscaledDeltaTime;
            if (lineTimer <= 0f) HideLine();
        }

        private void OnDestroy()
        {
            DOTween.Kill(this);
        }
    }
}

using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using F = MasterHouse.HouseUIRuntime;

namespace MasterHouse
{
    /// <summary>
    /// 访客头顶气泡（§9）：一张美术贴图，**不显示任何文字**（2026-08-20 改版，原来的符号/整句都取消）。
    /// 触发有两路，表现完全一样——淡入、上飘一小段、淡出：
    /// ①自发冒泡：隔一段随机时间冒一次，冒不冒由外部谓词把关（返回 false = 这一轮跳过）；
    /// ②业务冒泡：闲逛台词调度器触发（ShowFor），按配置时长停留。
    /// 挂在演员节点下自动跟随移动。
    /// </summary>
    internal sealed class OutGameVisitorBubble : MonoBehaviour
    {
        /// <summary>
        /// 气泡皮肤。**美术源在 Assets/PC ui 2.0/通用组件（toast）/聊天气泡.png**，
        /// 这里加载的是它在 Resources 下的副本（口径同 common/Toast）——换图时两处一起换。
        /// </summary>
        private const string SkinPath = "OutGameUI/common/ChatBubble";
        private const float BubbleSize = 60f;   // 显示边长（素材 200×200 方图，四周含发光留白）
        private const float FloatDistance = 5f;

        /// <summary>「这一轮该不该冒泡」的外部谓词（演员按自身状态答；null = 从不自发冒泡）。</summary>
        private Func<bool> canBubble;
        private CanvasGroup group;
        private RectTransform rect;
        private Vector2 basePosition;
        /// <summary>立绘头顶留白造成的下压量（演员按深度每帧喂）：气泡挂点跟着头走，不浮在半空。</summary>
        private float headDrop;

        /// <summary>实际挂点：作者给的基准位置减去立绘头顶留白。</summary>
        private Vector2 AnchorPosition => basePosition - new Vector2(0f, headDrop);

        /// <summary>演员按当前显示高度算出的头顶留白像素数（每帧喂；变了才重摆位置）。</summary>
        public void SetHeadDrop(float value)
        {
            if (Mathf.Abs(value - headDrop) < .5f) return;
            headDrop = value;
            if (rect != null && !DOTween.IsTweening(rect)) rect.anchoredPosition = AnchorPosition;
        }
        private bool showing;
        private float timer;      // 隐藏时=距下次浮现；显示时=剩余展示时长

        public static OutGameVisitorBubble Create(Transform parent, Vector2 anchoredPosition, Func<bool> canBubble)
        {
            var panel = F.Panel(parent, "Bubble", new Vector2(.5f, 1), new Vector2(.5f, 1),
                anchoredPosition, new Vector2(BubbleSize, BubbleSize), Color.white);
            panel.raycastTarget = false;
            // 用底边做挂点：anchoredPosition.y 就是气泡尾巴与演员头顶的真实间距，
            // 不再因气泡高度而额外抬高半个气泡。
            panel.rectTransform.pivot = new Vector2(.5f, 0f);
            var skin = Resources.Load<Sprite>(SkinPath);
            if (skin == null) Debug.LogError($"[访客气泡] 缺少气泡皮肤 Resources/{SkinPath}，头顶将只剩一块白底");
            panel.sprite = skin;
            panel.type = Image.Type.Simple;   // 素材是整张图，不切九宫
            panel.preserveAspect = true;
            var bubble = panel.gameObject.AddComponent<OutGameVisitorBubble>();
            bubble.rect = panel.rectTransform;
            bubble.basePosition = anchoredPosition;
            bubble.group = F.Group(panel.gameObject, 0f);
            bubble.group.blocksRaycasts = false;
            bubble.group.interactable = false;
            bubble.canBubble = canBubble;
            bubble.timer = UnityEngine.Random.Range(2f, 7f);
            return bubble;
        }

        /// <summary>
        /// 按指定时长冒一次泡（闲逛台词调度器触发）：打断当前的自发气泡。
        /// 台词内容已不再显示，业务层给的只剩「什么时候冒、冒多久」。
        /// </summary>
        public void ShowFor(float holdSeconds)
        {
            if (rect == null) return;
            showing = true;
            timer = Mathf.Max(1f, holdSeconds);
            Pop(timer);
        }

        private void Update()
        {
            timer -= Time.unscaledDeltaTime;
            if (timer > 0f) return;
            if (!showing)
            {
                if (canBubble == null || !canBubble())
                {
                    timer = 2f; // 当前状态不冒泡，稍后再试
                    return;
                }
                showing = true;
                var hold = UnityEngine.Random.Range(1.6f, 2.4f);
                timer = hold;
                Pop(hold);
            }
            else
            {
                showing = false;
                timer = UnityEngine.Random.Range(4f, 10f);
                group.DOKill();
                group.DOFade(0f, .3f).SetTarget(this).SetUpdate(true);
            }
        }

        /// <summary>淡入 + 上飘动效（两路触发共用）。</summary>
        private void Pop(float hold)
        {
            group.DOKill();
            rect.DOKill();
            rect.anchoredPosition = AnchorPosition;
            group.DOFade(1f, .22f).SetTarget(this).SetUpdate(true);
            rect.DOAnchorPos(AnchorPosition + new Vector2(0, FloatDistance), hold + .3f)
                .SetEase(Ease.OutSine).SetTarget(this).SetUpdate(true);
        }

        private void OnDestroy()
        {
            DOTween.Kill(this);
        }
    }
}

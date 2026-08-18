using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using F = MasterHouse.HouseUIRuntime;

namespace MasterHouse
{
    /// <summary>
    /// 访客头顶气泡（§9：单套气泡两种内容，不新建第二套）：
    /// ①情绪符号：随机间隔浮现一个符号（♪ ？ ！ … ♥ 等），上飘一小段后消失，循环播放；
    /// ②闲逛台词句子：由业务层冒泡调度器触发（ShowSentence），气泡加宽显示整句并按配置时长停留。
    /// 挂在演员节点下自动跟随移动；符号内容由外部提供（随演员状态变化），返回空串表示本轮跳过。
    /// </summary>
    internal sealed class OutGameVisitorBubble : MonoBehaviour
    {
        private const float EmoteWidth = 46f;
        private const float SentenceMaxWidth = 320f;
        private const float FloatDistance = 5f;

        private Func<string> emoteProvider;
        private CanvasGroup group;
        private Text label;
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

        public static OutGameVisitorBubble Create(Transform parent, Vector2 anchoredPosition, Func<string> emoteProvider)
        {
            var panel = F.Panel(parent, "Bubble", new Vector2(.5f, 1), new Vector2(.5f, 1),
                anchoredPosition, new Vector2(EmoteWidth, 42), new Color(.97f, .94f, .88f, .95f));
            panel.raycastTarget = false;
            // 用底边做挂点：anchoredPosition.y 就是气泡与演员头顶的真实间距，
            // 不再因气泡高度而额外抬高半个气泡。
            panel.rectTransform.pivot = new Vector2(.5f, 0f);
            F.Outline(panel.gameObject, new Color(.25f, .12f, .18f, .35f), new Vector2(1, -1));
            var bubble = panel.gameObject.AddComponent<OutGameVisitorBubble>();
            bubble.rect = panel.rectTransform;
            bubble.basePosition = anchoredPosition;
            bubble.label = F.Label(panel.transform, "Emote", "", 22, F.Hex("3F292E"), TextAnchor.MiddleCenter, FontStyle.Bold);
            bubble.label.raycastTarget = false;
            bubble.group = F.Group(panel.gameObject, 0f);
            bubble.group.blocksRaycasts = false;
            bubble.group.interactable = false;
            bubble.emoteProvider = emoteProvider;
            bubble.timer = UnityEngine.Random.Range(2f, 7f);
            return bubble;
        }

        /// <summary>显示一整句台词（闲逛冒泡，业务层触发）；打断当前符号气泡，宽度按文字长度自适应。</summary>
        public void ShowSentence(string text, float holdSeconds)
        {
            if (label == null || string.IsNullOrEmpty(text)) return;
            label.text = text;
            label.fontSize = 16;
            var width = Mathf.Clamp(34f + text.Length * 17f, EmoteWidth, SentenceMaxWidth);
            rect.sizeDelta = new Vector2(width, 42f);
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
                var emote = emoteProvider?.Invoke();
                if (string.IsNullOrEmpty(emote))
                {
                    timer = 2f; // 当前状态不冒泡，稍后再试
                    return;
                }
                label.text = emote;
                label.fontSize = 22;
                rect.sizeDelta = new Vector2(EmoteWidth, 42f);
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

        /// <summary>淡入 + 上飘动效（符号与句子共用）。</summary>
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

using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using F = MasterPotion.OutGameUIFactory;

namespace MasterPotion
{
    /// <summary>
    /// 访客头顶的小情绪气泡：随机间隔浮现一个符号（♪ ？ ！ … ♥ 等），上飘一小段后消失，循环播放。
    /// 挂在演员节点下自动跟随移动；内容由外部提供（随演员状态变化），返回空串表示本轮跳过。
    /// </summary>
    internal sealed class OutGameVisitorBubble : MonoBehaviour
    {
        private Func<string> emoteProvider;
        private CanvasGroup group;
        private Text label;
        private RectTransform rect;
        private Vector2 basePosition;
        private bool showing;
        private float timer;      // 隐藏时=距下次浮现；显示时=剩余展示时长

        public static OutGameVisitorBubble Create(Transform parent, Vector2 anchoredPosition, Func<string> emoteProvider)
        {
            var panel = F.Panel(parent, "Bubble", new Vector2(.5f, 1), new Vector2(.5f, 1),
                anchoredPosition, new Vector2(46, 42), new Color(.97f, .94f, .88f, .95f));
            panel.raycastTarget = false;
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
                showing = true;
                var hold = UnityEngine.Random.Range(1.6f, 2.4f);
                timer = hold;
                group.DOKill();
                rect.DOKill();
                rect.anchoredPosition = basePosition;
                group.DOFade(1f, .22f).SetTarget(this).SetUpdate(true);
                rect.DOAnchorPos(basePosition + new Vector2(0, 14f), hold + .3f)
                    .SetEase(Ease.OutSine).SetTarget(this).SetUpdate(true);
            }
            else
            {
                showing = false;
                timer = UnityEngine.Random.Range(4f, 10f);
                group.DOKill();
                group.DOFade(0f, .3f).SetTarget(this).SetUpdate(true);
            }
        }

        private void OnDestroy()
        {
            DOTween.Kill(this);
        }
    }
}

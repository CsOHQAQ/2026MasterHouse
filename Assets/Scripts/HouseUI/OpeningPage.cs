using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 开门过场页：1.35 秒双门推开动画后进入 Hub。纯动画页（无 Prefab，§16.2 认可的动态过场表现），
    /// 期间时钟不走（HubPage 进场才开闸门）。
    /// </summary>
    public sealed class OpeningPage : HousePage
    {
        protected override string PrefabPath => null;

        protected override void OnEnter()
        {
            HouseUIRuntime.StretchTexture(Root, "HomeReveal", "OutGameUI/house-hub-v2");
            HouseUIRuntime.StretchPanel(Root, "RevealVignette", new Color(.02f, .02f, .035f, .28f));
            var welcome = HouseUIRuntime.Panel(Root, "Welcome", new Vector2(.5f, .5f),
                Vector2.zero, new Vector2(370, 126), new Color(.83f, .77f, .67f, .94f));
            HouseUIRuntime.StretchLabel(welcome.transform, "Text", "<size=15>THE DOOR IS OPEN</size>\n欢迎回家",
                34, HouseUIUtil.Hex("3F292E"), TextAnchor.MiddleCenter, FontStyle.Bold);
            var welcomeGroup = HouseUIUtil.Group(welcome.gameObject, 0);

            var left = HouseUIRuntime.Rect(Root, "DoorLeft", new Vector2(0, .5f), new Vector2(0, .5f),
                new Vector2(480, 0), new Vector2(960, 1080));
            var leftArt = HouseUIRuntime.StretchTexture(left, "Cover", "OutGameUI/og-meros");
            leftArt.uvRect = new Rect(0, 0, .5f, 1);
            var right = HouseUIRuntime.Rect(Root, "DoorRight", new Vector2(1, .5f), new Vector2(1, .5f),
                new Vector2(-480, 0), new Vector2(960, 1080));
            var rightArt = HouseUIRuntime.StretchTexture(right, "Cover", "OutGameUI/og-meros");
            rightArt.uvRect = new Rect(.5f, 0, .5f, 1);
            var light = HouseUIRuntime.Panel(Root, "DoorLight", new Vector2(.5f, .5f), Vector2.zero,
                new Vector2(8, 1080), new Color(1f, .77f, .48f, .95f));
            var shadow = light.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(1f, .55f, .25f, .9f);
            shadow.effectDistance = new Vector2(18, 0);

            DOTween.Sequence().SetTarget(Root).SetUpdate(true)
                .AppendInterval(.12f)
                .Append(left.DOAnchorPosX(-500, 1.35f).SetEase(Ease.InOutCubic))
                .Join(right.DOAnchorPosX(500, 1.35f).SetEase(Ease.InOutCubic))
                .Join(light.rectTransform.DOSizeDelta(new Vector2(500, 1080), 1.15f).SetEase(Ease.InQuad))
                .Join(light.DOFade(0, 1.15f).SetEase(Ease.InQuad))
                .Insert(.55f, welcomeGroup.DOFade(1, .45f))
                .AppendCallback(() => UI.ShowPage(new HubPage("新的一周开始了 · 欢迎回家")));
        }
    }
}

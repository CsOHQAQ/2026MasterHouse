using DG.Tweening;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 纸张风格整页的公共绑定基类（存档占位/画廊/设置/退出）：
    /// 统一处理返回按钮（回标题）、字体回退与入场动效；ESC = 返回主菜单。
    /// </summary>
    public abstract class PaperPage<TView> : HousePage where TView : OutGamePaperView
    {
        protected TView View { get; private set; }

        protected sealed override void OnEnter()
        {
            View = Root != null ? Root.GetComponent<TView>() : null;
            if (View == null)
            {
                Debug.LogError("[HouseUI] 纸张页 Prefab 缺少视图组件：" + typeof(TView).Name);
                return;
            }
            View.backButton.onClick.RemoveAllListeners();
            View.backButton.onClick.AddListener(() => UI.ShowPage(new TitlePage()));
            HouseUIUtil.ApplyFallbackFont(Root);

            var target = View.frame.anchoredPosition;
            var group = HouseUIUtil.Group(View.frame.gameObject, 0);
            View.frame.anchoredPosition = target + new Vector2(0, -30);
            group.DOFade(1, .28f).SetEase(Ease.OutQuad).SetUpdate(true);
            View.frame.DOAnchorPos(target, .42f).SetEase(Ease.OutCubic).SetUpdate(true);

            OnBind();
        }

        /// <summary>视图组件已就绪后的页面内容绑定。</summary>
        protected abstract void OnBind();

        public override bool OnEscape()
        {
            UI.ShowPage(new TitlePage());
            return true;
        }
    }
}

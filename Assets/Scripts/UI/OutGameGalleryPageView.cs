using UnityEngine;
using UnityEngine.UI;

namespace MasterPotion
{
    /// <summary>完整画廊页面，日志与成就两套布局都保存在 Prefab 内。</summary>
    public sealed class OutGameGalleryPageView : OutGamePaperView
    {
        public Button logTab;
        public Button achievementTab;
        public RectTransform logRoot;
        public RectTransform achievementRoot;
    }
}

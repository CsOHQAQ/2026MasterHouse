using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>整页系统面板 Prefab 的公共外壳（遮罩 + 右侧面板 + 头部返回/标题/角标 + 内容挂点）。</summary>
    public sealed class OutGamePanelPageView : MonoBehaviour
    {
        public Image scrim;
        public Button scrimButton;
        public Image panel;
        public Button backButton;
        public Text headerTitle;
        public Text headerMark;
        public RectTransform contentRoot;
    }
}

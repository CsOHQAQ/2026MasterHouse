using UnityEngine;
using UnityEngine.UI;

namespace MasterPotion
{
    /// <summary>「叙事资源档案」面板内容的 Prefab 引用。条目格子与底部操作区随页签/选中项变化，运行时生成。</summary>
    public sealed class OutGameArchivePanelView : MonoBehaviour
    {
        public Button[] tabButtons = new Button[2];
        public Image[] tabBackgrounds = new Image[2];
        public Text[] tabLabels = new Text[2];
        public RectTransform gridRoot;
        public RawImage detailPreview;
        public Text detailText;
        public RectTransform actionRoot;
    }
}

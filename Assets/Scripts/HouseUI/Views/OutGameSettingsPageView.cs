using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 设置页 Prefab 引用（2026-08-16 按新美术重做：左侧分页 + 右侧内容行 + 底部键位栏）。
    /// 行内容随分页变化，走「模板 + 运行时实例化」（硬约定）；模板节点在 Prefab 里保持未激活。
    /// 标题设置页与 Hub 设置叠加层共用本 Prefab（§16.8），逻辑在 SettingsPageBinder。
    /// </summary>
    public sealed class OutGameSettingsPageView : MonoBehaviour
    {
        public RawImage background;
        public Button[] tabButtons = new Button[7];
        public Image[] tabBackgrounds = new Image[7];
        public Text[] tabLabels = new Text[7];
        [Tooltip("内容行容器（行由模板实例化后从上往下排）")]
        public RectTransform rowsRoot;
        public OutGameSettingsHeaderRow headerTemplate;
        public OutGameSettingsSliderRow sliderTemplate;
        public OutGameSettingsOptionRow optionTemplate;
        [Tooltip("分页按钮三态皮肤（生成器塞引用，运行时 sprite swap）")]
        public Sprite tabNormal;
        public Sprite tabSelected;
        public Sprite tabHover;
        [Tooltip("底部键位栏的三个可点热区（2026-08-16 反馈：可鼠标点击）")]
        public Button backButton;
        public Button resetButton;
        public Button applyButton;
    }
}

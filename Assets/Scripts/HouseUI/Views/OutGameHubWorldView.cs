using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// Hub 场景世界层的 Prefab 引用（2026-08-16 场景固化）：主楼剖面底图 + 四间房的画面矩形 + 接待室区域标记。
    /// **Prefab 锚点是区域布局的唯一真相**：运行时 HubSceneBinder 反读各矩形的锚点/uvRect
    /// 同步给 HubWorldGrid（相机聚焦、访客站位、家具热点全跟着走），对不齐直接在 Prefab 里挪。
    /// 洗色层/热点/访客舞台/环境光是动态表现件，仍由运行时生成（§16.2）。
    /// </summary>
    public sealed class OutGameHubWorldView : MonoBehaviour
    {
        [Tooltip("主楼剖面底图（铺满世界）")]
        public RawImage houseBackdrop;
        [Tooltip("四间房的画面矩形：锚点 = 房间在主楼图中的区域；uvRect = 房间背景图的内容裁切")]
        public RawImage[] roomArts = new RawImage[4];
        [Tooltip("接待室区域标记（只取锚点，不渲染）：访客排队/等分房的站位范围")]
        public RectTransform receptionArea;
        [Tooltip("接待室的贴地可走带（只取锚点，不渲染）：访客脚底只能落在这条带里。\n" +
                 "缺省时按接待室区域底部推算，但那条推算带贴不准地面——把这个矩形拖到地板上就准了")]
        public RectTransform receptionWalkArea;
    }
}

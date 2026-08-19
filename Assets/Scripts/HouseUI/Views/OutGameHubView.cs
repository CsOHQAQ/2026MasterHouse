using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// House 主界面的可编辑布局槽。
    ///
    /// 2026-08-20 起壳换成 2.0 设计图（<see cref="chrome"/>），**只有 chrome 是必填**；
    /// 下面六个 1.0 区块槽位与页脚一并转为可选，留空即整块不呈现（对应 Prefab 与绑定文件都还在，便于回滚）。
    /// </summary>
    public sealed class OutGameHubView : MonoBehaviour
    {
        public RectTransform sceneRoot;
        public RectTransform chromeRoot;
        public RectTransform modalRoot;

        [Tooltip("2.0 主页面壳（必填）：时间牌 / 图鉴 / 商店 / 装饰分 / 声望值 / 结束今日营业 / 房间卡")]
        public OutGameHubChromeView chrome;

        [Header("1.0 旧壳（可选，2026-08-20 起默认不装配）")]
        public Text footer;
        public OutGameHubTopBarView topBar;
        public OutGameHubTaskCardView taskCard;
        public OutGameHubGuestRailView guestRail;
        public OutGameHubRightDockView rightDock;
        public OutGameHubRoomNavigationView roomNavigation;
        public OutGameHubSceneOverlayView sceneOverlay;
    }
}

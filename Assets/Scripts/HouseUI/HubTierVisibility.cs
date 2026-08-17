using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// Hub 相机视角档位：由 HubSceneBinder 按 camZoom 判定（分界带回滞）。
    /// 档位是 View 层纯表现派生态（§11 豁免区）：只驱动 UI 显隐，不入存档、不回写业务——
    /// 业务判定（DetectCurrentRoom 等）继续直接比 camZoom，与本枚举无关。
    /// </summary>
    public enum EHubViewTier
    {
        /// <summary>第一档：全局（外景）视角，主楼剖面已淡出。</summary>
        Exterior,
        /// <summary>第二档：房间总览视角，整栋主楼剖面尽收眼底。</summary>
        Overview,
        /// <summary>第三档：房间详细视角，聚焦单个房间。</summary>
        RoomFocus,
    }

    /// <summary>
    /// 相机档位显隐标记：挂在 Hub Prefab 的 UI 区块根上，Inspector 勾选该区块在哪些档位可见，
    /// 切档过渡（淡入+位移浮入、隐藏反向浮出）由 HubTierUiBinder 统一执行。
    /// 没挂本组件的区块不受档位控制（恒显示）。改显隐/手感 = 改 Inspector，不碰代码（§16.2）。
    /// </summary>
    public sealed class HubTierVisibility : MonoBehaviour
    {
        [Header("各档位可见性")]
        [Tooltip("第一档：全局（外景）视角下可见")]
        public bool visibleInExterior = true;
        [Tooltip("第二档：房间总览视角下可见")]
        public bool visibleInOverview = true;
        [Tooltip("第三档：房间详细（聚焦单房）视角下可见")]
        public bool visibleInRoomFocus = true;

        [Header("过渡动画：淡入+位移浮入（隐藏时反向浮出）")]
        [Tooltip("隐藏位相对原位的偏移（像素）：顶部区块给正 y 往上飘走，底部区块给负 y 往下沉")]
        public Vector2 floatOffset = new Vector2(0f, 22f);
        [Tooltip("淡入淡出时长（秒）")]
        public float fadeDuration = .3f;
        [Tooltip("位移浮动时长（秒）")]
        public float moveDuration = .42f;

        public bool VisibleAt(EHubViewTier tier) => tier switch
        {
            EHubViewTier.Exterior => visibleInExterior,
            EHubViewTier.RoomFocus => visibleInRoomFocus,
            _ => visibleInOverview,
        };
    }
}

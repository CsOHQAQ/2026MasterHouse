using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// Hub 场景里一件已摆放家具的可点区域（运行时动态生成的表现件，§16.2 允许）。
    ///
    /// 它**不挂 Button**（家具库存与交互重做说明 §4.1）：uGUI 的 Button 没有拖拽阈值，
    /// 拖完在同一个热点上松手照样触发 onClick，于是「想拖动画面」会变成「打开家具详情」；
    /// 而且热点吃射线会让 HubSceneBinder 的相机平移完全起不来（`IsPointerOverBlockingUI` 一刀切）。
    ///
    /// 现在它只负责两件事：吃射线以便悬停弹提示卡，以及**携带「我是哪个房间的哪件家具」这两个数据**。
    /// 点击与拖拽的裁决统一收到相机层（松手时按位移阈值判），命中哪一件就靠本组件回答。
    /// </summary>
    internal sealed class HubFurnitureHotspot : MonoBehaviour
    {
        /// <summary>所在房间下标。总览态下点到的可能是别的房间的家具，所以必须随身带。</summary>
        public int RoomIndex;

        /// <summary>家具表 id。**传 id 不传下标**——热点来自 FurnitureSceneComposer.Collect（先地面后桌面），
        /// 详情面板列表来自 FurniturePlacementQuery.FurnitureIdsIn（原始顺序），两者排序不同。</summary>
        public string FurnitureId;
    }
}

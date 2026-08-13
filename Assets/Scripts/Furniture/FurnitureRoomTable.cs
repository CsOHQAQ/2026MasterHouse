using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>房间内一块可摆放网格。坐标与尺寸都用场景图像素表示（原点左上、Y 向下）。</summary>
    [Serializable]
    public sealed class FurnitureGridConfig
    {
        [Tooltip("网格 id，同一房间内唯一")] public string id;
        [Tooltip("表面类型：只接受同类型家具")] public FurnitureSurfaceType surface;
        public int cols = 1;
        public int rows = 1;
        [Tooltip("单元格宽（场景像素）")] public float cellWidth = 60f;
        [Tooltip("单元格高（场景像素）")] public float cellHeight = 60f;
        [Tooltip("网格左上角 X（场景像素）")] public float x;
        [Tooltip("网格左上角 Y（场景像素）")] public float y;
    }

    /// <summary>被背景画面占用、禁止摆放的格子（沙发、人物、落地灯等画在背景里的物件）。</summary>
    [Serializable]
    public sealed class FurnitureBlockedCellConfig
    {
        public string gridId;
        public int col;
        public int row;
    }

    /// <summary>初始摆放。桌面家具用 hostFurnitureId 指向宿主家具，其余用 gridId。</summary>
    [Serializable]
    public sealed class FurniturePlacementConfig
    {
        public string furnitureId;
        [Tooltip("基础网格 id（地面/壁挂家具）")] public string gridId;
        [Tooltip("宿主家具 id（桌面家具）")] public string hostFurnitureId;
        public int col;
        public int row;
        [Tooltip("左右镜像摆放")] public bool flipped;
    }

    /// <summary>房间配置表中的一行。</summary>
    [Serializable]
    public sealed class FurnitureRoomEntry
    {
        public string id;
        public string displayName;
        [Tooltip("场景图逻辑尺寸（像素）")] public float sceneWidth = 1672f;
        [Tooltip("场景图逻辑尺寸（像素）")] public float sceneHeight = 941f;
        [Tooltip("干净背景（家具洞位已修补）")] public Sprite background;
        [Tooltip("远景渐变模糊层（常驻，做景深）")] public Sprite depthBlurOverlay;
        [Tooltip("整幅模糊层（拖拽时淡入，做失焦）")] public Sprite focusBlurOverlay;
        [Tooltip("初始 HOUSE CREDIT")] public int startCredit = 2480;
        [Tooltip("访客活动区（归一化坐标，左下原点）：Hub 场景里访客游走/拖拽落点被钳在此矩形内，按房间美术的红框标定")]
        public Rect visitorWalkArea = Rect.MinMaxRect(.04f, .03f, .96f, .35f);
        [Tooltip("访客入口区（归一化坐标，左下原点）：访客进场出现/离场走向的门口范围，按房间美术的门位标定")]
        public Rect visitorEntryArea = Rect.MinMaxRect(.08f, .15f, .18f, .33f);
        public List<FurnitureGridConfig> grids = new List<FurnitureGridConfig>();
        public List<FurnitureBlockedCellConfig> blockedCells = new List<FurnitureBlockedCellConfig>();
        public List<FurniturePlacementConfig> initialPlacements = new List<FurniturePlacementConfig>();
    }

    /// <summary>房间配置表（一张表，一行一个房间）。</summary>
    [CreateAssetMenu(menuName = "MasterHouse/家具房间配置表", fileName = "FurnitureRoomTable")]
    public sealed class FurnitureRoomTable : ScriptableObject
    {
        public List<FurnitureRoomEntry> rooms = new List<FurnitureRoomEntry>();

        public FurnitureRoomEntry Find(string id)
        {
            for (var i = 0; i < rooms.Count; i++)
                if (rooms[i] != null && rooms[i].id == id) return rooms[i];
            return null;
        }
    }
}

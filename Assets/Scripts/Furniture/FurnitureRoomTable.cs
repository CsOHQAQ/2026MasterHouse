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
        [Tooltip("远端宽度比（2.5D 假透视，2026-08-14）：最远一行的横向收缩比例，1 = 关闭；" +
                 "仅横向向中心收拢、行高不变，吸附/落位/烘焙走同一映射")]
        public float farWidthScale = 1f;
    }

    /// <summary>被背景画面占用、禁止摆放的格子（沙发、人物、落地灯等画在背景里的物件）。</summary>
    [Serializable]
    public sealed class FurnitureBlockedCellConfig
    {
        public string gridId;
        public int col;
        public int row;
    }

    /// <summary>
    /// 一次家具摆放（初始摆放配置与运行时会话布局共用本结构）。
    ///
    /// 位置有两种表达，靠 <see cref="hostGridId"/> 是否为空区分：
    ///   地面/壁挂家具 → <see cref="gridId"/> + col/row（房间网格内的格子）
    ///   桌面家具　　　→ <see cref="hostGridId"/>/<see cref="hostCol"/>/<see cref="hostRow"/> 指向**宿主的落位**，
    ///                    col/row 则是宿主桌面网格内的格子
    ///
    /// **宿主用坐标而不是家具 id**（2026-08-15，家具库存说明 §5.4）：家具改为可重复购买、
    /// 同一房间能摆多件同款之后，「宿主 = round_table_01」不再唯一——两张一样的圆桌会让桌上的东西
    /// 全部解析到第一张，挤不下的还会被 FootprintFree 静默丢弃（家具凭空消失）。
    /// 而一个网格的一个格子上只能站一件基础家具，所以 (网格id, 列, 行) 是唯一键。
    ///
    /// 策划侧不受影响：CSV 的「宿主家具id」列照旧填家具 id，由导表器翻译成坐标（同房间存在多个同 id
    /// 候选时报错要求换配色）。见 FurnitureCsvImporter.ImportRoomCsv。
    /// </summary>
    [Serializable]
    public sealed class FurniturePlacementConfig
    {
        public string furnitureId;
        [Tooltip("基础网格 id（地面/壁挂家具）")] public string gridId;
        [Tooltip("宿主所在的基础网格 id（桌面家具；非空即表示这是一件桌面家具）")] public string hostGridId;
        [Tooltip("宿主在基础网格内的列")] public int hostCol;
        [Tooltip("宿主在基础网格内的行")] public int hostRow;
        public int col;
        public int row;
        [Tooltip("左右镜像摆放")] public bool flipped;

        /// <summary>是不是一件摆在别的家具桌面上的家具。</summary>
        public bool IsOnHost => !string.IsNullOrEmpty(hostGridId);

        /// <summary>本条摆放是否正好落在 (gridId, col, row) 这个格子上（宿主匹配用）。</summary>
        public bool OccupiesBaseCell(string baseGridId, int baseCol, int baseRow) =>
            !IsOnHost && gridId == baseGridId && col == baseCol && row == baseRow;
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
        [Header("夜间几何（2026-08-18）：昼夜两张房间图是分别绘制的，墙脚线高度不一样。" +
                "网格按白天图标定，夜里若不校正就会穿到墙上/浮在地板外。" +
                "两条线都是**从图顶算起的归一化 y**，可在房间表里量了填，量法见 Docs")]
        [Tooltip("白天图的墙脚线（后墙与地板的交界）")] [Range(0f, 1f)] public float dayFloorLine = .8f;
        [Tooltip("夜间图的墙脚线；与白天相同 = 不做纵向校正")] [Range(0f, 1f)] public float nightFloorLine = .8f;
        [Tooltip("夜间图相对白天图的横向缩放（以房间中线为轴）：1 = 不变")] public float nightWidthScale = 1f;
        [Tooltip("夜间图相对白天图的横向偏移（场景像素）")] public float nightShiftX;

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

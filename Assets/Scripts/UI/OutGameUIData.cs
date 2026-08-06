using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterPotion
{
    [Serializable]
    internal sealed class OutGameSaveData
    {
        /// <summary>存档版本。2 = 含流通数值、家具所有权与摆放布局；3 = 含游戏时钟与访客到访状态；
        /// 旧档读入时新字段回落默认值。</summary>
        public int version;
        public int slot;
        public string room = "living";
        public bool[] served = new bool[4];
        public bool[] refused = new bool[4];
        /// <summary>v3：访客是否已到访过（到访后常驻屋内，跨天也在，直到服务完成/被拒绝）。</summary>
        public bool[] guestArrived = new bool[4];
        /// <summary>v3：游戏时钟（加速时间，与现实时间无关）。</summary>
        public int gameDay = 1;
        public float gameMinute = 8 * 60f;
        public int bgm = 64;
        public int sfx = 78;
        public string windowMode = "无边框";
        public string savedAt = "";
        public HouseEconomySaveData economy = new HouseEconomySaveData();
        /// <summary>是否保存过家具布局（区分「摆空了」与「从未编辑过（用房间默认摆放）」）。</summary>
        public bool hasFurnitureLayout;
        public List<FurniturePlacementConfig> furniturePlacements = new List<FurniturePlacementConfig>();
    }

    internal sealed class OutGameRoom
    {
        public string id;
        public string name;
        public string code;
        public string note;
        public string art;

        public OutGameRoom(string id, string name, string code, string note, string art)
        {
            this.id = id;
            this.name = name;
            this.code = code;
            this.note = note;
            this.art = art;
        }
    }

    internal sealed class OutGameGuest
    {
        public string id;
        public string name;
        public string type;
        public string status;
        public string hint;
        public int affinity;
        public string need;
        public string solution;
        public string gift;
        public string portrait;
        public bool special;
        /// <summary>拜访时间（游戏时钟小时，到点从大门进场；此后常驻屋内直到服务完成/被拒绝）。</summary>
        public int visitHour;
        /// <summary>可服务时间窗口（游戏时钟小时）。窗口外访客留在屋内但不开放服务；特殊客人不受限。</summary>
        public int serviceStart;
        public int serviceEnd;

        public OutGameGuest(string id, string name, string type, string status, string hint, int affinity,
            string need, string solution, string gift, string portrait, bool special = false,
            int visitHour = 8, int serviceStart = 9, int serviceEnd = 22)
        {
            this.id = id;
            this.name = name;
            this.type = type;
            this.status = status;
            this.hint = hint;
            this.affinity = affinity;
            this.need = need;
            this.solution = solution;
            this.gift = gift;
            this.portrait = portrait;
            this.special = special;
            this.visitHour = visitHour;
            this.serviceStart = serviceStart;
            this.serviceEnd = serviceEnd;
        }

        public bool InServiceWindow(float hourF) => special || (hourF >= serviceStart && hourF < serviceEnd);

        public string ServiceWindowText => special ? "全天（特殊客人）" : $"{serviceStart:00}:00–{serviceEnd:00}:00";
    }

    internal sealed class OutGameArchiveItem
    {
        public string id;
        public string name;
        public string type;
        public string owner;
        public string note;
        public string image;

        public OutGameArchiveItem(string id, string name, string type, string owner, string note, string image)
        {
            this.id = id;
            this.name = name;
            this.type = type;
            this.owner = owner;
            this.note = note;
            this.image = image;
        }
    }

    internal static class OutGameUIData
    {
        public static readonly OutGameRoom[] Rooms =
        {
            new("living", "起居室", "HOME", "客人会在这里等待服务", "OutGameUI/house-hub-v2"),
            new("bedroom", "卧室", "REST", "恢复状态并推进至下一天", "OutGameUI/dream-house"),
            new("kitchen", "厨房", "MAKE", "使用设备制作访客需要的物品", "OutGameUI/house-hub-v2"),
            new("study", "书房", "READ", "解锁配方与客人线索", "OutGameUI/study-room-clean"),
        };

        public static readonly OutGameGuest[] Guests =
        {
            new("lorn", "洛恩", "特殊客人", "初次来访", "总能从旧物中认出不属于这个时代的细节。", 12,
                "一杯温热的赤茶，以及关于这栋房子的答案", "鲸声电话亭", "一枚停在 08:20 的怀表", "OutGameUI/Guests/fox", true,
                8, 8, 24),
            new("crow", "赫墨", "一般客人", "等待回应", "总把最糟糕的句子，改写成可以继续走下去的话。", 46,
                "一扇能唱回来的窗户", "琴弦窗户", "一根沾着星尘的黑羽毛", "OutGameUI/Guests/crow", false,
                9, 10, 18),
            new("rabbit", "米娅", "一般客人", "悄悄观察", "她不太会开口请求，却会把想说的话写在风铃下面。", 31,
                "一串能替她说话的回声风铃", "兔耳回声风铃", "一张画着胡萝卜的小纸条", "OutGameUI/Guests/rabbit", false,
                10, 12, 20),
            new("hedgehog", "霍奇", "一般客人", "坐在门边", "他的刺总比话先竖起来，但会替屋里坏掉的东西包扎。", 58,
                "一盏不会逼人开口的暖灯", "蒲公英吊灯", "一卷重新缠好的绷带", "OutGameUI/Guests/hedgehog", false,
                13, 14, 22),
        };

        /// <summary>访客场景 NPC 序列帧图集路径前缀（占位动物素材，来自 CatVsDog）。索引与 Guests 对齐；
        /// 实际资源为 前缀 + "_await_sheet" / "_attack_sheet" 的 PNG+JSON 组合。</summary>
        public static readonly string[] VisitorSheets =
        {
            "OutGameUI/Visitors/orange_cat",
            "OutGameUI/Visitors/rottweiler",
            "OutGameUI/Visitors/xueqiu",
            "OutGameUI/Visitors/wangcai",
        };

        /// <summary>串门邻居名册（表现层氛围 NPC，与业务访客无关）。格式：图集路径前缀|显示名。
        /// 会随机轮换进场，在门口排队等玩家决定「请进屋/请回吧」。</summary>
        public static readonly string[] AmbientVisitors =
        {
            "OutGameUI/Visitors/laoda|老大",
            "OutGameUI/Visitors/laomao|老猫",
            "OutGameUI/Visitors/longhair_cat|长毛",
            "OutGameUI/Visitors/panghu|胖虎",
            "OutGameUI/Visitors/sangbiao|桑彪",
            "OutGameUI/Visitors/tufu|土福",
        };

        public static readonly OutGameArchiveItem[] Furniture =
        {
            new("whale", "鲸声电话亭", "回应家具", "洛恩", "没有号码，也没有接线员。拿起话筒，只会听见很远的鲸鸣。", "OutGameUI/Furniture/whale-call"),
            new("strings", "琴弦窗户", "回应家具", "赫墨", "白天收下屋里的话，夜晚用另一种情绪唱回来。", "OutGameUI/Furniture/string-window"),
            new("chimes", "兔耳回声风铃", "纪念家具", "米娅", "每一张垂纸都能留下一句没来得及说出口的话。", "OutGameUI/Furniture/wind-chimes"),
            new("lamp", "蒲公英吊灯", "照明家具", "霍奇", "灯亮起时会有种子般的微光飘开，让沉默也有安全的位置。", "OutGameUI/Furniture/dandelion-lamp"),
            new("planter", "月牙植物台", "纪念家具", "所有访客", "客人留下的植物与小物会逐周长进这弯月亮里。", "OutGameUI/Furniture/moon-planter"),
        };

        public static readonly OutGameArchiveItem[] World =
        {
            new("house", "雨夜之家", "场景概念", "HOME NODE", "一栋刚有人住进来的老房子：什么都有，却还不像一个家。", "OutGameUI/dream-house"),
            new("map", "模糊宇宙路线", "世界观", "WORLD MAP", "旅馆、星光酒廊与孤独加油站组成的远行路线。", "OutGameUI/World/universe-map"),
            new("fox-sheet", "狐狸访客设定", "角色设定", "CHARACTER 01", "精致、克制、习惯计算，也相信所有关系都能够交换。", "OutGameUI/World/fox-sheet"),
            new("owl", "猫头鹰访客", "角色候选", "FUTURE GUEST", "抱着一本不愿让人翻开的旧书，似乎知道房子的历史。", "OutGameUI/World/owl"),
        };

        public static readonly string[][] Devices =
        {
            new[] { "黑胶唱机|LV.2|舒缓情绪|1", "旧式壁炉|LV.1|提高停留时长|1" },
            new[] { "梦境捕捉器|LV.1|获得记忆碎片|0", "床头留声机|LV.2|恢复主角状态|1" },
            new[] { "手冲咖啡台|LV.3|制作热饮|1", "微波烤箱|LV.1|制作简餐|1", "玻璃药罐|LV.2|合成特殊配方|0" },
            new[] { "旧书检索机|LV.2|发现线索|1", "观星镜|LV.1|预测特殊访客|0" },
        };

        /// <summary>当前时段按加速的游戏时钟计算（OutGameClock），不再读现实时间。</summary>
        public static int CurrentPhase
        {
            get
            {
                var hour = OutGameClock.HourF;
                if (hour >= 7 && hour < 9) return 0;
                if (hour >= 9 && hour < 12) return 1;
                if (hour >= 12 && hour < 14) return 2;
                if (hour >= 14 && hour < 18) return 3;
                if (hour >= 18 && hour < 22) return 4;
                return 5;
            }
        }

        public static readonly string[] PhaseNames = { "早晨", "上午", "中午", "下午", "晚上", "深夜" };
        public static readonly string[] PhaseRanges = { "07:00–09:00", "09:00–12:00", "12:00–14:00", "14:00–18:00", "18:00–22:00", "22:00–07:00" };
    }
}

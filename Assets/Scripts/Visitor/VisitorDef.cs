using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 业务访客配置（Model 层，运行时只读，§16.6）。
    /// </summary>
    [Serializable]
    public sealed class VisitorDef
    {
        [Header("身份")]
        [Tooltip("稳定键：演员命名用；待定 #9 统一存档后接手存档键")]
        public string id;
        public string displayName;
        [Tooltip("面板抬头文案：特殊客人 / 一般客人")]
        public string type;
        [Tooltip("特殊客人：不受服务窗口限制，对话抬头显示「硬植入事件」")]
        public bool special;

        [Header("时间（游戏时钟小时，整数）")]
        [Tooltip("到点从大门进场；此后常驻屋内直到服务完成/被拒绝")]
        public int visitHour;
        [Tooltip("可服务窗口 [start, end)，窗口外访客留在屋内但不开放服务")]
        public int serviceStart;
        public int serviceEnd;

        [Header("委托内容")]
        [Tooltip("访客卡状态文案，如「初次来访」")]
        public string status;
        [Tooltip("人物描述句（访客卡/通讯录引用）")]
        public string hint;
        [Tooltip("信赖 %（原型静态值，暂无增减玩法）")]
        public int affinity;
        public string need;
        [Tooltip("适配家具名")]
        public string solution;
        [Tooltip("完成服务后可能留下的东西")]
        public string gift;
        [Tooltip("事务台词（3.7 对话接缝的取内容来源）")]
        [TextArea] public string transactionLine;

        [Header("表现资源（Resources 路径，沿用约定式加载）")]
        [Tooltip("立绘，如 OutGameUI/Guests/fox")]
        public string portraitPath;
        [Tooltip("序列帧前缀，实际资源为 前缀 + \"_await_sheet\"/\"_attack_sheet\" 的 PNG+JSON 组合")]
        public string sheetPath;

        /// <summary>是否在可服务窗口内（整数分钟比较，§16.4）。</summary>
        public bool InServiceWindow(int minuteOfDay) =>
            special || (minuteOfDay >= serviceStart * 60 && minuteOfDay < serviceEnd * 60);

        public string ServiceWindowText => special ? "全天（特殊客人）" : $"{serviceStart:00}:00–{serviceEnd:00}:00";
    }

    /// <summary>串门邻居（纯表现氛围 NPC，随机轮换进场、门口排队，无业务字段）。</summary>
    [Serializable]
    public sealed class AmbientVisitorDef
    {
        [Tooltip("稳定键（图集文件名），演员命名为 neighbor_ + id")]
        public string id;
        public string displayName;
        [Tooltip("序列帧前缀，如 OutGameUI/Visitors/laoda")]
        public string sheetPath;
    }

    /// <summary>
    /// 访客配置表（§16.3 Visitor 模块的 Model）：业务访客 + 氛围邻居。
    /// </summary>
    [CreateAssetMenu(menuName = "MasterHouse/访客配置表", fileName = "VisitorTable")]
    public sealed class VisitorTable : ScriptableObject
    {
        [Tooltip("业务访客。列表顺序即业务下标——旧存档的 served/refused/guestArrived 按下标对齐，" +
                 "重排/插入会串旧档（待定 #9 统一存档时改用 id 键）")]
        public List<VisitorDef> visitors = new List<VisitorDef>();

        [Tooltip("串门邻居名册，顺序 = 轮换名册顺序")]
        public List<AmbientVisitorDef> ambientVisitors = new List<AmbientVisitorDef>();
    }
}
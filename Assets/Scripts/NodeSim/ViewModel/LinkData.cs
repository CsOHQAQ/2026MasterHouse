using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>链接状态机（§6.5）。</summary>
    public enum ELinkState
    {
        /// <summary>槽空：到达节拍时向源 Pin 请求取货。</summary>
        Idle,

        /// <summary>槽有货，在途计时未到：脉冲沿线移动。</summary>
        InTransit,

        /// <summary>在途到期但目标无空位：持货，每 tick 重试投递；不取新货。</summary>
        Blocked,

        /// <summary>走线非法（棋盘变化/被移动）：全部停止，等玩家修线。</summary>
        Broken,

        /// <summary>两端 Pin 物资类型不兼容：清空槽（有意蒸发 §6.2），停止工作。</summary>
        TypeInvalid,
    }

    /// <summary>
    /// 链接运行时数据（§10）。只能由 Manager（LinkManager）修改。
    /// 链接强类型：一条链接只运一种物资（§6.2）。
    /// </summary>
    public class LinkData
    {
        /// <summary>稳定自增 Id（§3.3）：创建时分配、随存档保存，一切链接遍历的排序键。</summary>
        public readonly long LinkId;

        /// <summary>源 Pin（输出侧）。</summary>
        public readonly PinData FromPin;

        /// <summary>目标 Pin（输入侧）。</summary>
        public readonly PinData ToPin;

        /// <summary>绑定物资，连接时根据 Pin 信息自动推导（§6.2）。</summary>
        public ItemDef ItemType;

        /// <summary>仲裁优先级（§3.3）：数值大者先服务。设置入口待定 #16，先只留字段。</summary>
        public int Priority;

        /// <summary>玩家著作的折线途径格（全局坐标，§5）。随存档序列化。</summary>
        public List<Vector2Int> PathCells = new List<Vector2Int>();

        // ── 持货槽（§6.4）：(物资类型, 数量)，投递不完的余量原地持有 ──
        public ItemDef SlotItem;
        public int SlotCount;

        /// <summary>节拍周期：每 N tick 发起一次取货（数值来源待定 #4）。</summary>
        public int BeatTicks;

        /// <summary>节拍计时。随存档序列化（§11.5）。</summary>
        public int BeatCounter;

        /// <summary>在途总时长（tick，数值来源待定 #4）。</summary>
        public int TransitTicks;

        /// <summary>在途已进行计时。随存档序列化（§11.5）。</summary>
        public int TransitCounter;

        public ELinkState State = ELinkState.Idle;

        public bool SlotEmpty => SlotCount <= 0;

        public LinkData(long linkId, PinData fromPin, PinData toPin)
        {
            LinkId = linkId;
            FromPin = fromPin;
            ToPin = toPin;
        }
    }
}
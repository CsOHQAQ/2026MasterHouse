using System.Collections.Generic;

namespace MasterHouse
{
    /// <summary>单个业务访客的运行时状态。只能被 VisitorManager 修改（§11.4）。</summary>
    public sealed class VisitorState
    {
        /// <summary>已到访（到点进场后常驻屋内，跨天保留，直到本周处理完毕或周结算清空）。</summary>
        public bool Arrived;

        /// <summary>本周已处理完毕（完成服务或被拒绝后不再出现）。</summary>
        public bool Served;

        /// <summary>被拒绝（Served 的子集；当前无 UI 展示，随存档保留）。</summary>
        public bool Refused;
    }

    /// <summary>
    /// 访客运行时数据（§16.3）：下标与 VisitorTable.visitors 对齐（旧存档按下标序列化，待定 #9 统一存档时改 id 键）。
    /// </summary>
    public class VisitorData
    {
        public readonly List<VisitorState> States = new List<VisitorState>();

        public VisitorData(int visitorCount)
        {
            for (var i = 0; i < visitorCount; i++) States.Add(new VisitorState());
        }
    }
}
namespace MasterHouse
{
    /// <summary>
    /// 访客业务逻辑（§16.3，自旧 OutGameUI 抽出重写）：进场判定/服务窗口/服务与拒绝结算/周结算，
    /// 全部挂全局 tick、整数分钟比较（§16.4）。到访是时间的纯函数，由本 Manager 在 tick 内判定——
    /// 表现层只读状态生成演员，不再回写业务（旧版舞台回调写 guestArrived 的反例已废除）。
    /// 事件广播暂缺：唯一消费方（冻结旧 UI）调用后自行刷新，3.5 页面重写需要时再加（不预设抽象）。
    /// </summary>
    public class VisitorManager
    {
        private readonly VisitorTable table;
        private readonly HouseClockManager clock;
        private readonly EconomyManager economy;

        public VisitorData Data { get; }

        public VisitorManager(VisitorTable table, HouseClockManager clock, EconomyManager economy)
        {
            this.table = table;
            this.clock = clock;
            this.economy = economy;
            Data = new VisitorData(table != null ? table.visitors.Count : 0);
        }

        /// <summary>每全局 tick 调用一次（GameManager，时钟推进之后）：到点进场判定，整数分钟比较。</summary>
        public void Tick()
        {
            if (!clock.IsRunning) return; // 标题/过场时间冻结，访客业务同样不推进
            var minute = clock.Data.MinuteOfDay;
            for (var i = 0; i < Data.States.Count; i++)
            {
                var state = Data.States[i];
                if (state.Arrived || state.Served) continue;
                if (minute >= table.visitors[i].visitHour * 60)
                    state.Arrived = true;
            }
        }

        /// <summary>是否可服务：未处理完毕且在服务窗口内（整数分钟；特殊客人全天）。</summary>
        public bool CanServe(int index)
        {
            if (!ValidIndex(index) || Data.States[index].Served) return false;
            return table.visitors[index].InServiceWindow(clock.Data.MinuteOfDay);
        }

        /// <summary>完成服务结算：置状态并产出货币/声望。已处理过返回 false。</summary>
        public bool Serve(int index)
        {
            if (!ValidIndex(index) || Data.States[index].Served) return false;
            Data.States[index].Served = true;
            economy.CompleteGuestService();
            return true;
        }

        /// <summary>拒绝接待结算：置状态（Served=处理完毕 + Refused 标记）并扣声望。已处理过返回 false。</summary>
        public bool Refuse(int index)
        {
            if (!ValidIndex(index) || Data.States[index].Served) return false;
            var state = Data.States[index];
            state.Served = true;
            state.Refused = true;
            economy.RefuseGuestService();
            return true;
        }

        /// <summary>周结算：未完成项扣声望 → 清空本周状态 → 时钟跳次日早晨。返回未完成数（结算文案用）。</summary>
        public int EndWeek()
        {
            var missed = 0;
            foreach (var state in Data.States)
                if (!state.Served) missed++;
            economy.FailGuestServices(missed);
            ResetWeek();
            clock.NextDay();
            return missed;
        }

        public int CountServed()
        {
            var count = 0;
            foreach (var state in Data.States)
                if (state.Served) count++;
            return count;
        }

        public int CountRemaining() => Data.States.Count - CountServed();

        /// <summary>清空全部访客状态（新游戏/GM 重置）。</summary>
        public void ResetNew() => ResetWeek();

        private void ResetWeek()
        {
            foreach (var state in Data.States)
            {
                state.Arrived = false;
                state.Served = false;
                state.Refused = false;
            }
        }

        // ── 过渡：旧存档 v3 按下标序列化的三个 bool 数组（待定 #9 统一存档定案后取代）──

        public bool[] CaptureServed()
        {
            var result = new bool[Data.States.Count];
            for (var i = 0; i < result.Length; i++) result[i] = Data.States[i].Served;
            return result;
        }

        public bool[] CaptureRefused()
        {
            var result = new bool[Data.States.Count];
            for (var i = 0; i < result.Length; i++) result[i] = Data.States[i].Refused;
            return result;
        }

        public bool[] CaptureArrived()
        {
            var result = new bool[Data.States.Count];
            for (var i = 0; i < result.Length; i++) result[i] = Data.States[i].Arrived;
            return result;
        }

        /// <summary>从旧存档三数组恢复；数组为 null 或长度不足的位置回落 false（旧版本存档降级）。</summary>
        public void RestoreFromArrays(bool[] served, bool[] refused, bool[] arrived)
        {
            for (var i = 0; i < Data.States.Count; i++)
            {
                var state = Data.States[i];
                state.Served = served != null && i < served.Length && served[i];
                state.Refused = refused != null && i < refused.Length && refused[i];
                state.Arrived = arrived != null && i < arrived.Length && arrived[i];
            }
        }

        private bool ValidIndex(int index) => index >= 0 && index < Data.States.Count;
    }
}
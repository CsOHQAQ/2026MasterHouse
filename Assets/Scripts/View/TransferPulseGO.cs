using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 线上脉冲（§10 View 类）：由链接槽位数据直接驱动，无需额外逻辑（§6.4）。
    /// View 只读；连续感全部由插值实现，逻辑层只给 tick 计数（§3.1）。
    /// </summary>
    public class TransferPulseGO : MonoBehaviour
    {
        public LinkData Data { get; private set; }

        public void Bind(LinkData data)
        {
            Data = data;
        }

        private void Update()
        {
            if (Data == null) return;

            // TODO（待定 #10：数据变化感知机制未定，v1 暂定每帧轮询）：
            // - State == InTransit：按 TransitCounter / TransitTicks 沿 PathCells 折线插值前进
            //   （帧间可用视觉时间平滑，仅表现，不回写数据）
            // - State == Blocked：停驻在 PathCells 末端（目标门口），玩家一眼看出堵点
            // - 槽空 / Broken / TypeInvalid：隐藏
        }
    }
}
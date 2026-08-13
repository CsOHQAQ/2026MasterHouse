using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 中转件（小游戏说明 §4.7 修订版）：十字 / 分流 / 合流三种件型**共用本类**，
    /// 差异全在资产的 Pins 配置里——靠 PinDef.PinGroup 分组 + PinDef.Direction 定进出。
    ///
    /// 求解规则只有一条（CircuitSolver）：
    ///     每个输出口的电量 = floor( 组内输入之和 / 组内输出口总数 )
    /// 十字件（组内 1 进 1 出）退化为直通，合流（N 进 1 出）得到求和，分流（1 进 N 出）得到平分。
    /// 所以本类是空壳：加件型 = 加资产，不加代码（§15.3 不预设抽象）。
    ///
    /// 本轮只做 1:N 与 N:1；组内既多进又多出（M:N）公式同样成立，但未做验收，
    /// 编辑器校验会对这种配置给警告。
    /// </summary>
    [CreateAssetMenu(fileName = "中转件", menuName = "MasterHouse/节点/中转件", order = 23)]
    public class TransitNodeDef : NodeDef
    {
        public override ENodeType NodeType => ENodeType.Transit;
    }
}

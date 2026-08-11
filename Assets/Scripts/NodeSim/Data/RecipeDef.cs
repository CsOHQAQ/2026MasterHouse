using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 配方定义（Model 层）。加工节点消耗 Inputs、经 WorkTicks 后产出 Outputs（§7）。
    /// 注意：同一物资在 Inputs/Outputs 中不要重复出现多条（暂存计算按物资聚合，v1 不做合并）。
    /// </summary>
    [CreateAssetMenu(fileName = "配方", menuName = "MasterHouse/配方定义", order = 11)]
    public class RecipeDef : ScriptableObject
    {
        [Tooltip("配方输入：每条一种物资及数量；同一物资不要重复出现多条（暂存按物资聚合，v1 不合并）")]
        public List<ItemStack> Inputs = new List<ItemStack>();

        [Tooltip("配方产出：每条一种物资及数量；同一物资不要重复出现多条。物资可同时出现在输入与产出（催化剂类配方）")]
        public List<ItemStack> Outputs = new List<ItemStack>();

        /// <summary>一批配方的加工时长。速率一律以 tick 为单位（§3.1）。</summary>
        [Tooltip("一批配方的加工时长（tick）。速率一律以 tick 为单位（§3.1）")]
        public int WorkTicks = 10;
    }
}
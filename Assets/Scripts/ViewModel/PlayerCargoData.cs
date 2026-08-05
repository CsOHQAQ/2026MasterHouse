using System.Collections.Generic;

namespace MasterHouse
{
    /// <summary>
    /// 玩家全局仓库（§8.2）：所有产出经仓库节点一律进这里。
    /// 运行时数据，归 ViewModel 数据层（不是 Def）。
    /// 展示与跨关消费待定 #15；如需按关分账，加来源维度。
    /// </summary>
    public class PlayerCargoData
    {
        /// <summary>累计量。仅做键查询与累加；如需 UI 遍历展示，须按稳定键排序后再枚举（§11.2）。</summary>
        private readonly Dictionary<ItemDef, long> items = new Dictionary<ItemDef, long>();

        public void Add(ItemDef item, long count)
        {
            if (item == null || count <= 0) return;
            items.TryGetValue(item, out var current);
            items[item] = current + count;
        }

        public long Get(ItemDef item)
        {
            if (item == null) return 0;
            items.TryGetValue(item, out var count);
            return count;
        }
    }
}
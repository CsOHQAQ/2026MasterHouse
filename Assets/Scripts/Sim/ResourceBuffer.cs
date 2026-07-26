using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MasterPotion
{
    /// <summary>资源计数缓存（资源种类 -> 数量）。上限判断由使用方负责。</summary>
    public class ResourceBuffer
    {
        private readonly Dictionary<ResourceDef, int> counts = new();

        public int Get(ResourceDef r) => counts.TryGetValue(r, out var c) ? c : 0;

        public void Add(ResourceDef r, int amount = 1) => counts[r] = Get(r) + amount;

        public bool TryRemove(ResourceDef r, int amount = 1)
        {
            if (Get(r) < amount) return false;
            counts[r] -= amount;
            return true;
        }

        public string ToDisplayString()
        {
            var sb = new StringBuilder();
            foreach (var kv in counts.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key.displayName))
            {
                if (sb.Length > 0) sb.Append("  ");
                sb.Append(kv.Key.displayName).Append('x').Append(kv.Value);
            }
            return sb.Length > 0 ? sb.ToString() : "-";
        }
    }
}

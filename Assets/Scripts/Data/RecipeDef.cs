using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MasterPotion
{
    /// <summary>加工配方：若干输入 -> 加工时长 -> 若干输出。</summary>
    [CreateAssetMenu(menuName = "MasterPotion/Recipe", fileName = "Recipe")]
    public class RecipeDef : ScriptableObject
    {
        public string displayName;
        public List<ResourceAmount> inputs = new();
        public List<ResourceAmount> outputs = new();
        [Tooltip("单次加工耗时（秒）")]
        public float craftTime = 2f;

        public IEnumerable<ResourceDef> InputTypes => inputs.Select(i => i.resource);
        public IEnumerable<ResourceDef> OutputTypes => outputs.Select(o => o.resource);
    }
}

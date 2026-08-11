using UnityEngine;

namespace MasterHouse
{
    /// <summary>标签语法角色（访客交付说明 §4.1）：仅轴根节点填写，后代沿父链继承，用于程序化需求句的造句顺序。</summary>
    public enum ETagGrammarRole
    {
        Noun = 0,      // 名词（品类等）
        Adjective = 1, // 形容词（口味/质地等）
    }

    /// <summary>
    /// 需求/物品标签（Model 层，运行时只读；访客交付说明 §4.1）。
    /// 单亲父链构成森林：每棵树的根即一条「轴」（品类/口味/质地…），不单独建轴枚举。
    /// 匹配规则：需求 tag 命中 ⇔ 物品的某个 tag 等于它、或以它为祖先；物品只需标最具体的叶子。
    /// 局内局外共用（物品挂 tag、访客需求引 tag），按 §15.3 准入标准放 Core。
    /// 跨轴语义必须正交（编辑器校验见 Core/Editor/TagDefValidator）。
    /// </summary>
    [CreateAssetMenu(menuName = "MasterHouse/标签定义", fileName = "Tag")]
    public sealed class TagDef : ScriptableObject
    {
        /// <summary>父链遍历的防环上限（正常配置远达不到；成环时靠它保证不死循环）。</summary>
        public const int MaxDepth = 64;

        [Tooltip("稳定键（存档/日志用）")]
        public string id;

        [Tooltip("显示名，如「甜」")]
        public string displayName;

        [Tooltip("描述词：造句用短语，如「甜的」")]
        public string phrase;

        [Tooltip("父标签；null 表示自己是某条轴的根")]
        public TagDef parent;

        [Tooltip("语法角色：仅根节点填写，后代沿父链继承")]
        public ETagGrammarRole grammarRole;

        [Tooltip("同层排序，保证造句与展示顺序稳定（§11.2）")]
        public int sortOrder;

        /// <summary>所在轴（沿父链上溯到根；带防环上限，成环时返回上限处节点并由编辑器校验报错）。</summary>
        public TagDef Root
        {
            get
            {
                var node = this;
                for (var depth = 0; depth < MaxDepth && node.parent != null; depth++)
                    node = node.parent;
                return node;
            }
        }

        /// <summary>生效语法角色：后代继承轴根的配置（§4.1）。</summary>
        public ETagGrammarRole EffectiveGrammarRole => Root.grammarRole;

        /// <summary>造句用短语；未配置时回落显示名。</summary>
        public string Phrase => string.IsNullOrEmpty(phrase) ? displayName : phrase;

        /// <summary>
        /// 匹配判定（§4.1）：本 tag（需求）是否命中 itemTag（物品标签）——
        /// 相等、或本 tag 是 itemTag 的祖先。
        /// </summary>
        public bool Covers(TagDef itemTag)
        {
            var node = itemTag;
            for (var depth = 0; depth < MaxDepth && node != null; depth++)
            {
                if (node == this) return true;
                node = node.parent;
            }
            return false;
        }

        /// <summary>父链是否成环（编辑器校验用，§4.1「必做」）。</summary>
        public bool HasParentCycle()
        {
            var slow = parent;
            var fast = parent != null ? parent.parent : null;
            while (fast != null)
            {
                if (slow == fast || fast == this) return true;
                slow = slow.parent;
                fast = fast.parent != null ? fast.parent.parent : null;
            }
            return false;
        }

        /// <summary>
        /// 稳定遍历排序（§4.1/§11.2）：按 (轴 sortOrder, 轴 id, 节点 sortOrder, 节点 id) 比较，
        /// 禁止依赖资产枚举顺序。
        /// </summary>
        public static int Compare(TagDef a, TagDef b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return -1;
            if (b == null) return 1;
            var rootA = a.Root;
            var rootB = b.Root;
            if (rootA != rootB)
            {
                var byAxisOrder = rootA.sortOrder.CompareTo(rootB.sortOrder);
                if (byAxisOrder != 0) return byAxisOrder;
                var byAxisId = string.CompareOrdinal(rootA.id, rootB.id);
                if (byAxisId != 0) return byAxisId;
            }
            var byOrder = a.sortOrder.CompareTo(b.sortOrder);
            if (byOrder != 0) return byOrder;
            return string.CompareOrdinal(a.id, b.id);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (parent == this)
                Debug.LogError($"[TagDef] {name}：parent 指向自己（父链成环）", this);
            else if (HasParentCycle())
                Debug.LogError($"[TagDef] {name}：父链成环，请检查 parent 配置（§4.1）", this);
        }
#endif
    }
}

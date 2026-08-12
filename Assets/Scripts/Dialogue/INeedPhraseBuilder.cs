using System.Collections.Generic;
using System.Text;

namespace MasterHouse
{
    /// <summary>
    /// 需求短语组装接缝（设计说明 §9）：把访客的一组需求 tag 说成一句人话。
    /// 将来若要做专门的需求描述生成器（词库/语序/语气变体），只换实现、不动对话系统——
    /// 与项目对 IDialogueService 的接缝做法一致。
    /// </summary>
    public interface INeedPhraseBuilder
    {
        /// <summary>组装需求短语，如「甜的、软的食物」。需求为空时返回空串，由调用方决定兜底文案。</summary>
        string Build(IReadOnlyList<VisitorNeed> needs);
    }

    /// <summary>
    /// 默认实现（设计说明 §9 的规则）：
    ///   按 (轴 sortOrder, tag sortOrder) 稳定排序 → 形容词用「、」串接 → 取树最深的名词作中心词。
    ///   示例：[食物, 甜, 软] → 「甜的、软的食物」
    ///
    /// **不输出「（加分）」标注**——那是评分规则，写进台词等于给玩家漏答案，而且不像人说话。
    /// 访客重做期间 VisitorInstance.BuildNeedSentence 的老规则（平铺全部需求项 + 标注加分）
    /// 已按本节作废，该方法现转调本接口。
    /// </summary>
    public sealed class DefaultNeedPhraseBuilder : INeedPhraseBuilder
    {
        public string Build(IReadOnlyList<VisitorNeed> needs)
        {
            if (needs == null || needs.Count == 0) return string.Empty;

            var adjectives = new List<TagDef>();
            TagDef centerNoun = null;
            var centerDepth = -1;

            foreach (var need in needs)
            {
                var tag = need.Tag;
                if (tag == null) continue;
                if (tag.EffectiveGrammarRole == ETagGrammarRole.Adjective)
                {
                    adjectives.Add(tag);
                    continue;
                }
                // 中心词取「树最深的名词」：品类轴上越深越具体（食物 → 甜点 → 蛋糕），
                // 说最具体的那个才是人话。同深度时按稳定序取靠前的（§11.2）
                var depth = DepthOf(tag);
                if (depth > centerDepth || (depth == centerDepth && TagDef.Compare(tag, centerNoun) < 0))
                {
                    centerNoun = tag;
                    centerDepth = depth;
                }
            }

            adjectives.Sort(TagDef.Compare);

            var text = new StringBuilder();
            for (var i = 0; i < adjectives.Count; i++)
            {
                if (i > 0) text.Append('、');
                text.Append(adjectives[i].Phrase); // Phrase = 描述词（「甜的」），未配时回落显示名
            }
            text.Append(centerNoun != null ? centerNoun.displayName : "东西");
            return text.ToString();
        }

        /// <summary>节点在所属轴上的深度（根 = 0）。带防环上限，成环由 TagDef 的编辑器校验负责报错。</summary>
        private static int DepthOf(TagDef tag)
        {
            var depth = 0;
            var node = tag;
            while (depth < TagDef.MaxDepth && node.parent != null)
            {
                node = node.parent;
                depth++;
            }
            return depth;
        }
    }
}

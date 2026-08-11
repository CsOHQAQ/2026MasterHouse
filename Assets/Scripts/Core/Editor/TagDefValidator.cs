using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// Tag 森林编辑器校验（访客交付说明 §4.1「必做」）：
    /// ①父链成环 → 报错；②跨轴同名/同 id tag → 警告（跨轴语义必须正交，「口味/甜」与「品类/甜点」类重叠是配置事故高发点）。
    /// TagDef 资产导入时自动跑一遍，也可从菜单手动触发。
    /// </summary>
    public sealed class TagDefValidator : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var path in imported)
            {
                if (!path.EndsWith(".asset")) continue;
                if (AssetDatabase.LoadAssetAtPath<TagDef>(path) == null) continue;
                ValidateAll();
                return;
            }
        }

        [MenuItem("MasterHouse/访客系统/校验标签森林")]
        public static void ValidateAll()
        {
            var guids = AssetDatabase.FindAssets("t:TagDef");
            var all = new List<TagDef>();
            foreach (var guid in guids)
            {
                var tag = AssetDatabase.LoadAssetAtPath<TagDef>(AssetDatabase.GUIDToAssetPath(guid));
                if (tag != null) all.Add(tag);
            }
            all.Sort(TagDef.Compare);

            var problems = 0;
            foreach (var tag in all)
            {
                if (tag.parent == tag || tag.HasParentCycle())
                {
                    Debug.LogError($"[TagDef] 父链成环：{tag.name}（id={tag.id}）——请修正 parent 引用（§4.1）", tag);
                    problems++;
                }
                if (string.IsNullOrEmpty(tag.id))
                {
                    Debug.LogError($"[TagDef] 缺少稳定键 id：{tag.name}（存档/日志依赖 id，§4.1）", tag);
                    problems++;
                }
            }

            // 跨轴同名/同 id 检查（同轴同名同样可疑，一并警告）
            WarnDuplicates(all, tag => tag.displayName, "显示名");
            WarnDuplicates(all, tag => tag.id, "id");

            if (problems == 0)
                Debug.Log($"[TagDef] 标签森林校验通过：共 {all.Count} 个 tag。");
        }

        private static void WarnDuplicates(List<TagDef> all, System.Func<TagDef, string> key, string keyName)
        {
            var seen = new Dictionary<string, TagDef>();
            foreach (var tag in all)
            {
                var value = key(tag);
                if (string.IsNullOrEmpty(value)) continue;
                if (seen.TryGetValue(value, out var first))
                {
                    var axisA = first.Root != null ? first.Root.displayName : "?";
                    var axisB = tag.Root != null ? tag.Root.displayName : "?";
                    Debug.LogWarning($"[TagDef] {keyName}重复：「{value}」同时出现在轴「{axisA}」（{first.name}）与轴「{axisB}」（{tag.name}）。" +
                                     "跨轴语义必须正交，请确认不是同义重复（§4.1）。", tag);
                }
                else
                {
                    seen.Add(value, tag);
                }
            }
        }
    }
}

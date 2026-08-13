using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MasterHouse.EditorTools
{
    /// <summary>一条需求校验结果。</summary>
    public struct NeedIssue
    {
        /// <summary>true = 阻断性错误（这条需求跑起来必然出问题）；false = 提示性警告（能跑，但多半配错了）。</summary>
        public bool IsError;

        public string Message;

        /// <summary>点击日志能定位到的资产。</summary>
        public Object Context;
    }

    /// <summary>
    /// 需求资产校验器（需求重做说明 §4.5）。分级参照 DialogueAssetValidator。
    ///
    /// 错误（跑不起来，必须改）：
    ///   · description 为空——任务卡与 {需求} 占位符都会渲染成空白
    ///   · 条件类的 furnitureIds 为空——OR 语义下永远不满足，这位访客只能超时或被拒绝
    ///   · furnitureIds 里的 id 不在家具表中——家具改名或删行导致的失联
    /// 警告（能跑，但很可能是事故）：
    ///   · needId 留空（DisplayId 回落资产名）或与别的需求重复
    ///   · 同一 furnitureIds 里出现重复 id
    ///
    /// **家具表本身缺失时跳过 id 存在性校验**并单独给一条警告——否则全部 id 都会被误报成错误，
    /// 把真问题淹在噪声里。
    /// </summary>
    public static class NeedAssetValidator
    {
        /// <summary>家具表资产路径（与 FurnitureIdDrawer 同一处）。</summary>
        private const string FurnitureTablePath = "Assets/Resources/OutGameUI/FurnitureTable.asset";

        [MenuItem("MasterHouse/访客系统/校验全部需求资产")]
        public static void ValidateAllFromMenu()
        {
            var issues = ValidateAll();
            var errors = 0;
            foreach (var issue in issues)
            {
                if (issue.IsError)
                {
                    errors++;
                    Debug.LogError("[需求校验] " + issue.Message, issue.Context);
                }
                else
                {
                    Debug.LogWarning("[需求校验] " + issue.Message, issue.Context);
                }
            }
            if (issues.Count == 0) Debug.Log("[需求校验] 全部需求资产通过校验。");
            else Debug.Log($"[需求校验] 完成：{errors} 个错误、{issues.Count - errors} 个警告。");
        }

        /// <summary>扫描工程里全部 NeedDef（含两个子类）。</summary>
        public static List<NeedIssue> ValidateAll()
        {
            var issues = new List<NeedIssue>();
            var needs = LoadAllSorted();

            var table = AssetDatabase.LoadAssetAtPath<FurnitureTable>(FurnitureTablePath);
            if (table == null && needs.Count > 0)
                Warn(issues, null, $"家具表缺失（{FurnitureTablePath}），已跳过全部「家具 id 是否存在」校验；" +
                                   "请执行菜单 MasterHouse → 家具系统 → 从 CSV 导入家具三表");

            // needId 查重：先建「id → 首个占用者」，第二次出现才报（报在后来者身上，指名前一个是谁）
            var owners = new Dictionary<string, NeedDef>();

            foreach (var need in needs)
            {
                var id = need.DisplayId;

                if (string.IsNullOrWhiteSpace(need.description))
                    Error(issues, need, $"需求「{id}」的描述（description）是空的——" +
                                        "任务卡与台词里的 {需求} 占位符都会渲染成空白（§4.1）");

                if (string.IsNullOrWhiteSpace(need.needId))
                    Warn(issues, need, $"需求资产「{need.name}」没有填 needId，日志与索引会回落到资产名");
                else if (owners.TryGetValue(need.needId, out var owner))
                    Warn(issues, need, $"需求「{need.name}」的 needId「{need.needId}」与「{owner.name}」重复——" +
                                       "稳定键重复会让日志分不清是哪一条");
                else
                    owners[need.needId] = need;

                if (need is ConditionNeedDef condition) ValidateCondition(condition, id, table, issues);
            }

            return issues;
        }

        private static void ValidateCondition(ConditionNeedDef need, string id, FurnitureTable table,
            List<NeedIssue> issues)
        {
            if (need.furnitureIds == null || need.furnitureIds.Count == 0)
            {
                Error(issues, need, $"条件类需求「{id}」的家具列表是空的——" +
                                    "OR 语义下永远不可能满足，这位访客只能超时或被拒绝（§4.1）");
                return;
            }

            var seen = new HashSet<string>();
            for (var i = 0; i < need.furnitureIds.Count; i++)
            {
                var furnitureId = need.furnitureIds[i];
                if (string.IsNullOrWhiteSpace(furnitureId))
                {
                    Error(issues, need, $"条件类需求「{id}」的家具列表第 {i} 行没有选家具");
                    continue;
                }
                if (!seen.Add(furnitureId))
                {
                    Warn(issues, need, $"条件类需求「{id}」的家具列表里「{furnitureId}」重复了——" +
                                       "OR 语义下重复项不改变判定，只是噪声");
                    continue;
                }
                // 家具表缺失时上面已经统一报过一次，这里静默跳过，不逐行刷屏
                if (table != null && table.Find(furnitureId) == null)
                    Error(issues, need, $"条件类需求「{id}」引用的家具 id「{furnitureId}」不在家具表中" +
                                        "（可能已改名或删行），请在需求编辑器里重选");
            }
        }

        /// <summary>按资产路径排序后返回，保证同一份工程每次得到同样顺序的报告（便于 diff）。</summary>
        public static List<NeedDef> LoadAllSorted()
        {
            var paths = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:NeedDef"))
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            paths.Sort(System.StringComparer.Ordinal);

            var result = new List<NeedDef>();
            foreach (var path in paths)
            {
                var need = AssetDatabase.LoadAssetAtPath<NeedDef>(path);
                if (need != null) result.Add(need);
            }
            return result;
        }

        private static void Error(List<NeedIssue> issues, Object context, string message) =>
            issues.Add(new NeedIssue { IsError = true, Message = message, Context = context });

        private static void Warn(List<NeedIssue> issues, Object context, string message) =>
            issues.Add(new NeedIssue { IsError = false, Message = message, Context = context });
    }
}

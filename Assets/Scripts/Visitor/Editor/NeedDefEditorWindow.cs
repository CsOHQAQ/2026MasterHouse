using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// 需求编辑器（需求重做说明 §4.5）：左树（按需求类型分两组，可搜索）右编辑区，
    /// 形态与 DialogueEditorWindow 同构。
    ///
    /// 需求是**分散的独立 SO 资产**（§4.1 选它是为了 diff 友好、可拖拽引用、Unity 原生「查找引用」可用），
    /// 聚合浏览由本窗口提供，不靠文件结构。
    ///
    /// 右侧用 Unity 默认 Inspector 绘制资产本体——家具 id 的下拉是 PropertyDrawer
    /// （Furniture/Editor/FurnitureIdDrawer.cs），所以这里字段 UI 一行都不用写，
    /// 而且在 Project 视图里直接选中资产编辑同样是下拉。
    ///
    /// **不做**反向引用展示（「这条需求被第 3、7 天引用」）：日程表在 Excel 里存的是资产名字符串，
    /// 反查只能扫 CSV 做字符串匹配，改名后还会误报孤儿——性价比不足，孤儿需求靠人工（§4.5）。
    ///
    /// 打开方式：菜单 MasterHouse/访客系统/需求编辑器、配置中心，或双击 NeedDef 资产。
    /// </summary>
    public sealed class NeedDefEditorWindow : EditorWindow
    {
        /// <summary>需求资产落点（§4.1）。</summary>
        public const string NeedDir = "Assets/GameData/Needs";

        private readonly List<NeedDef> conditionNeeds = new List<NeedDef>();
        private readonly List<NeedDef> minigameNeeds = new List<NeedDef>();
        private readonly HashSet<Object> errorAssets = new HashSet<Object>();

        private NeedDef target;
        private Editor inlineEditor;
        private List<NeedIssue> issues = new List<NeedIssue>();

        private Vector2 scrollLeft, scrollRight;
        private string search = string.Empty;
        private string newNeedName = "新需求";
        private bool showCondition = true;
        private bool showMinigame = true;

        [MenuItem("MasterHouse/访客系统/需求编辑器")]
        public static void Open()
        {
            var window = GetWindow<NeedDefEditorWindow>("需求编辑器");
            window.minSize = new Vector2(860, 520);
        }

        [OnOpenAsset]
        private static bool OnOpenNeedAsset(int instanceId, int line)
        {
            var asset = EditorUtility.InstanceIDToObject(instanceId) as NeedDef;
            if (asset == null) return false;
            Open();
            GetWindow<NeedDefEditorWindow>().SetTarget(asset);
            return true;
        }

        private void OnEnable()
        {
            Refresh();
            Undo.undoRedoPerformed += Repaint;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= Repaint;
            DestroyInlineEditor();
        }

        private void SetTarget(NeedDef asset)
        {
            if (target == asset) return;
            target = asset;
            DestroyInlineEditor();
            Repaint();
        }

        private void DestroyInlineEditor()
        {
            if (inlineEditor != null) DestroyImmediate(inlineEditor);
            inlineEditor = null;
        }

        private void Refresh()
        {
            conditionNeeds.Clear();
            minigameNeeds.Clear();
            // 按资产路径排序（校验器内做），左树顺序不受 AssetDatabase 返回顺序影响
            foreach (var need in NeedAssetValidator.LoadAllSorted())
            {
                if (need.NeedType == ENeedType.Minigame) minigameNeeds.Add(need);
                else conditionNeeds.Add(need);
            }

            issues = NeedAssetValidator.ValidateAll();
            errorAssets.Clear();
            foreach (var issue in issues)
                if (issue.IsError && issue.Context != null)
                    errorAssets.Add(issue.Context);
        }

        private void OnGUI()
        {
            DrawToolbar();
            EditorGUILayout.BeginHorizontal();
            DrawTree();
            DrawEditor();
            EditorGUILayout.EndHorizontal();
        }

        // ══════════ 顶栏 ══════════

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("刷新 / 重新校验", EditorStyles.toolbarButton, GUILayout.Width(120))) Refresh();
            GUILayout.FlexibleSpace();

            var errors = 0;
            foreach (var issue in issues) if (issue.IsError) errors++;
            var warnings = issues.Count - errors;
            var summary = errors > 0
                ? $"✖ {errors} 个错误 · ⚠ {warnings} 个警告"
                : warnings > 0 ? $"⚠ {warnings} 个警告" : "✔ 校验通过";
            var style = new GUIStyle(EditorStyles.toolbarButton)
            {
                normal =
                {
                    textColor = errors > 0 ? new Color(1f, .45f, .4f)
                        : warnings > 0 ? new Color(1f, .8f, .3f) : new Color(.5f, .9f, .6f),
                },
            };
            if (GUILayout.Button(summary, style, GUILayout.Width(180)))
                NeedAssetValidator.ValidateAllFromMenu(); // 把明细打到 Console，可点击定位
            EditorGUILayout.EndHorizontal();
        }

        // ══════════ 左树 ══════════

        private void DrawTree()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(320));
            scrollLeft = EditorGUILayout.BeginScrollView(scrollLeft);

            // 需求条数会随天数线性增长，搜索框是必需品而不是装饰
            search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
            EditorGUILayout.Space(4);

            if (conditionNeeds.Count == 0 && minigameNeeds.Count == 0)
                EditorGUILayout.HelpBox("工程里还没有需求资产。用下面的「新建」按钮建一条，" +
                                        $"落点 {NeedDir}。\n\n" +
                                        "日程表的「需求」列按**资产名**引用（同对话池写 Pool_fox 的做法）。",
                    MessageType.Info);

            showCondition = Group(showCondition, "条件类", conditionNeeds);
            showMinigame = Group(showMinigame, "小游戏类", minigameNeeds);

            EditorGUILayout.Space(8);
            DrawCreate();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private bool Group(bool expanded, string label, List<NeedDef> needs)
        {
            var shown = new List<NeedDef>();
            foreach (var need in needs)
                if (Matches(need)) shown.Add(need);

            var suffix = shown.Count == needs.Count ? $"（{needs.Count}）" : $"（{shown.Count}/{needs.Count}）";
            var next = EditorGUILayout.Foldout(expanded, label + suffix, true);
            if (!next) return false;

            EditorGUI.indentLevel++;
            if (shown.Count == 0) EditorGUILayout.LabelField("（无匹配）", EditorStyles.miniLabel);
            foreach (var need in shown) NeedRow(need);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);
            return true;
        }

        /// <summary>搜索命中：资产名 / needId / 描述任一包含关键字（忽略大小写）。</summary>
        private bool Matches(NeedDef need)
        {
            if (string.IsNullOrWhiteSpace(search)) return true;
            var key = search.Trim();
            return Contains(need.name, key) || Contains(need.needId, key) || Contains(need.description, key);
        }

        private static bool Contains(string text, string key) =>
            !string.IsNullOrEmpty(text) && text.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) >= 0;

        private void NeedRow(NeedDef need)
        {
            EditorGUILayout.BeginHorizontal();
            // 带错误的行直接标红，不用等玩到才发现（与对话编辑器同款）
            var broken = errorAssets.Contains(need);
            var color = GUI.color;
            if (broken) GUI.color = new Color(1f, .55f, .5f);
            var label = (broken ? "✖ " : "") + need.DisplayId;
            var style = target == need ? EditorStyles.whiteLabel : EditorStyles.label;
            if (GUILayout.Button(label, style)) SetTarget(need);
            GUI.color = color;
            if (GUILayout.Button("→", EditorStyles.miniButton, GUILayout.Width(24)))
                EditorGUIUtility.PingObject(need);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawCreate()
        {
            EditorGUILayout.LabelField("新建需求", EditorStyles.boldLabel);
            newNeedName = EditorGUILayout.TextField(newNeedName);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(newNeedName)))
            {
                if (GUILayout.Button("＋ 条件类")) Create<ConditionNeedDef>(newNeedName.Trim());
                if (GUILayout.Button("＋ 小游戏类")) Create<MinigameNeedDef>(newNeedName.Trim());
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField($"落点：{NeedDir}／Need_<名称>.asset", EditorStyles.miniLabel);
        }

        private void Create<T>(string id) where T : NeedDef
        {
            EnsureFolder(NeedDir);
            var path = AssetDatabase.GenerateUniqueAssetPath($"{NeedDir}/Need_{id}.asset");
            var need = CreateInstance<T>();
            need.needId = id; // 稳定键默认跟名称走，策划可以再改
            AssetDatabase.CreateAsset(need, path);
            AssetDatabase.SaveAssets();
            Refresh();
            SetTarget(need);
        }

        // ══════════ 右编辑区 ══════════

        private void DrawEditor()
        {
            EditorGUILayout.BeginVertical();
            scrollRight = EditorGUILayout.BeginScrollView(scrollRight);

            if (target == null)
            {
                EditorGUILayout.HelpBox("在左侧选一条需求开始编辑。\n\n" +
                                        "· 条件类：所住房间里存在家具列表中的**任意一件**即通过（OR 语义），" +
                                        "家具 id 走下拉，不要手打字符串。\n" +
                                        "· 小游戏类：选一个小游戏；「指定关卡」可留空——留空则从该小游戏的关卡池" +
                                        "按访客确定性抽取，填了就固定打那一关（修理电路的手工题面一般逐条点名）。\n\n" +
                                        "描述（description）就是访客说出来的那句话，台词里用 {需求} 占位符引用它。",
                    MessageType.Info);
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{target.name}　（{TypeName(target.NeedType)}）", EditorStyles.boldLabel);
            if (GUILayout.Button("在工程中定位", GUILayout.Width(100))) EditorGUIUtility.PingObject(target);
            EditorGUILayout.EndHorizontal();

            DrawTargetIssues();

            if (inlineEditor == null || inlineEditor.target != target)
            {
                DestroyInlineEditor();
                inlineEditor = Editor.CreateEditor(target);
            }
            // 用默认 Inspector 绘制：家具 id 的下拉抽屉直接复用（§4.5），不重写字段 UI
            if (inlineEditor != null) inlineEditor.OnInspectorGUI();

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        /// <summary>只显示与当前编辑对象相关的校验结果，省得在一长串里找自己那条。</summary>
        private void DrawTargetIssues()
        {
            var any = false;
            foreach (var issue in issues)
            {
                if (issue.Context != (Object)target) continue;
                any = true;
                EditorGUILayout.HelpBox(issue.Message, issue.IsError ? MessageType.Error : MessageType.Warning);
            }
            if (any) EditorGUILayout.Space(4);
        }

        // ══════════ 小工具 ══════════

        private static string TypeName(ENeedType type) => type switch
        {
            ENeedType.Condition => "条件类",
            ENeedType.Minigame => "小游戏类",
            _ => "未知类型",
        };

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = path.Substring(0, path.LastIndexOf('/'));
            var leaf = path.Substring(path.LastIndexOf('/') + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}

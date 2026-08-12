using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// 对话编辑器（设计说明 §11.2）：左树（种族对话池 → 触发分类 → 对话组）右编辑区，
    /// 与项目已有的物资/配方/节点/关卡四个编辑器窗口同构。
    ///
    /// 对话组是**分散的独立 SO 资产**（§4.4 选它是为了 diff 友好、多人并行写台词不冲突），
    /// 聚合浏览由本窗口提供，不靠文件结构。
    ///
    /// 右侧用 Unity 默认 Inspector 绘制对话组本体——这样事件与条件的
    /// SubclassSelector 抽屉（§11.1）直接复用，不需要在这里重写一套字段 UI。
    ///
    /// 打开方式：菜单 MasterHouse/对话系统/对话编辑器，或双击 DialogueGroupDef / DialoguePoolDef 资产。
    /// </summary>
    public sealed class DialogueEditorWindow : EditorWindow
    {
        private const string SharedGroupDir = "Assets/GameData/Dialogue/通用";

        private readonly List<DialoguePoolDef> pools = new List<DialoguePoolDef>();
        private readonly List<DialogueGroupDef> allGroups = new List<DialogueGroupDef>();
        private readonly Dictionary<Object, bool> foldouts = new Dictionary<Object, bool>();

        private Object target;              // 当前编辑对象：DialogueGroupDef 或 DialoguePoolDef
        private Editor inlineEditor;
        private List<DialogueIssue> issues = new List<DialogueIssue>();

        private Vector2 scrollLeft, scrollRight;
        private string newGroupName = "新对话组";
        private bool showOrphans;

        [MenuItem("MasterHouse/对话系统/对话编辑器")]
        public static void Open()
        {
            var window = GetWindow<DialogueEditorWindow>("对话编辑器");
            window.minSize = new Vector2(860, 520);
        }

        [OnOpenAsset]
        private static bool OnOpenDialogueAsset(int instanceId, int line)
        {
            var asset = EditorUtility.InstanceIDToObject(instanceId);
            if (!(asset is DialogueGroupDef) && !(asset is DialoguePoolDef)) return false;
            Open();
            GetWindow<DialogueEditorWindow>().SetTarget(asset);
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

        private void SetTarget(Object asset)
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
            pools.Clear();
            allGroups.Clear();

            // 按资产路径排序：保证左树顺序稳定，不受 AssetDatabase 返回顺序影响
            var poolPaths = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:DialoguePoolDef"))
                poolPaths.Add(AssetDatabase.GUIDToAssetPath(guid));
            poolPaths.Sort(System.StringComparer.Ordinal);
            foreach (var path in poolPaths)
            {
                var pool = AssetDatabase.LoadAssetAtPath<DialoguePoolDef>(path);
                if (pool != null) pools.Add(pool);
            }

            var groupPaths = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:DialogueGroupDef"))
                groupPaths.Add(AssetDatabase.GUIDToAssetPath(guid));
            groupPaths.Sort(System.StringComparer.Ordinal);
            foreach (var path in groupPaths)
            {
                var group = AssetDatabase.LoadAssetAtPath<DialogueGroupDef>(path);
                if (group != null) allGroups.Add(group);
            }

            issues = DialogueAssetValidator.ValidateAll();
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
            if (GUILayout.Button("补齐示例资产", EditorStyles.toolbarButton, GUILayout.Width(100)))
            {
                DialogueConfigSetupUtility.CreateIfMissing();
                Refresh();
            }
            GUILayout.FlexibleSpace();

            var errors = 0;
            foreach (var issue in issues) if (issue.IsError) errors++;
            var warnings = issues.Count - errors;
            var summary = errors > 0
                ? $"✖ {errors} 个错误 · ⚠ {warnings} 个警告"
                : warnings > 0 ? $"⚠ {warnings} 个警告" : "✔ 校验通过";
            var style = new GUIStyle(EditorStyles.toolbarButton)
            {
                normal = { textColor = errors > 0 ? new Color(1f, .45f, .4f) : warnings > 0 ? new Color(1f, .8f, .3f) : new Color(.5f, .9f, .6f) },
            };
            if (GUILayout.Button(summary, style, GUILayout.Width(180)))
                DialogueAssetValidator.ValidateAllFromMenu(); // 把明细打到 Console，可点击定位
            EditorGUILayout.EndHorizontal();
        }

        // ══════════ 左树 ══════════

        private void DrawTree()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(320));
            scrollLeft = EditorGUILayout.BeginScrollView(scrollLeft);

            if (pools.Count == 0)
                EditorGUILayout.HelpBox("工程里还没有对话池。点顶栏「补齐示例资产」生成一套，" +
                                        "或用菜单 Assets → Create → MasterHouse → 对话池 手动建。", MessageType.Info);

            foreach (var pool in pools)
            {
                if (pool == null) continue;
                EditorGUILayout.BeginHorizontal();
                var expanded = Foldout(pool, pool.name);
                if (GUILayout.Button("编辑池", EditorStyles.miniButton, GUILayout.Width(52))) SetTarget(pool);
                EditorGUILayout.EndHorizontal();
                if (!expanded) continue;

                EditorGUI.indentLevel++;
                Category(pool, "初次见面", pool.firstMeeting);
                Category(pool, "开始等待服务", pool.serviceStart);
                Category(pool, "被拒绝", pool.rejected);
                Category(pool, "完成服务·不对味", pool.doneMismatch);
                Category(pool, "完成服务·一般", pool.donePlain);
                Category(pool, "完成服务·满意", pool.doneSatisfied);
                Category(pool, "完成服务·完美", pool.donePerfect);
                Category(pool, "满意后闲逛", pool.wanderChat);
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(4);
            }

            EditorGUILayout.Space(6);
            showOrphans = EditorGUILayout.Foldout(showOrphans, $"全部对话组（{allGroups.Count}）", true);
            if (showOrphans)
            {
                EditorGUI.indentLevel++;
                foreach (var group in allGroups)
                    GroupRow(group);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(8);
            DrawCreateGroup();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void Category(DialoguePoolDef pool, string label, List<DialogueGroupEntry> entries)
        {
            var count = entries != null ? entries.Count : 0;
            // 分类为空是错误（§4.5：该触发点没话可说），在树上直接标红，不用等玩到才发现
            var title = count == 0 ? $"✖ {label}（空）" : $"{label}（{count}）";
            var color = GUI.color;
            if (count == 0) GUI.color = new Color(1f, .55f, .5f);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            GUI.color = color;
            if (entries == null) return;
            EditorGUI.indentLevel++;
            foreach (var entry in entries)
                GroupRow(entry != null ? entry.group : null, entry);
            EditorGUI.indentLevel--;
        }

        private void GroupRow(DialogueGroupDef group, DialogueGroupEntry entry = null)
        {
            EditorGUILayout.BeginHorizontal();
            if (group == null)
            {
                EditorGUILayout.LabelField("（空引用）");
                EditorGUILayout.EndHorizontal();
                return;
            }
            var label = entry != null && entry.weight != 1 ? $"{group.DisplayId}  ×{entry.weight}" : group.DisplayId;
            var selected = target == group;
            var style = selected ? EditorStyles.whiteLabel : EditorStyles.label;
            if (GUILayout.Button(label, style)) SetTarget(group);
            if (GUILayout.Button("→", EditorStyles.miniButton, GUILayout.Width(24)))
                EditorGUIUtility.PingObject(group);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawCreateGroup()
        {
            EditorGUILayout.LabelField("新建对话组", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            newGroupName = EditorGUILayout.TextField(newGroupName);
            if (GUILayout.Button("创建", GUILayout.Width(56)) && !string.IsNullOrWhiteSpace(newGroupName))
                CreateGroup(newGroupName.Trim());
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField($"落点：{SharedGroupDir}", EditorStyles.miniLabel);
        }

        private void CreateGroup(string id)
        {
            EnsureFolder(SharedGroupDir);
            var path = AssetDatabase.GenerateUniqueAssetPath($"{SharedGroupDir}/Group_{id}.asset");
            var group = CreateInstance<DialogueGroupDef>();
            group.id = id;
            group.steps = new List<DialogueStep>();
            AssetDatabase.CreateAsset(group, path);
            AssetDatabase.SaveAssets();
            Refresh();
            SetTarget(group);
        }

        // ══════════ 右编辑区 ══════════

        private void DrawEditor()
        {
            EditorGUILayout.BeginVertical();
            scrollRight = EditorGUILayout.BeginScrollView(scrollRight);

            if (target == null)
            {
                EditorGUILayout.HelpBox("在左侧选一个对话组或对话池开始编辑。\n\n" +
                                        "事件与条件是 [SerializeReference] 多态字段——点字段右侧的下拉选具体类型，" +
                                        "参数就地填（§4.2）。", MessageType.Info);
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(target.name, EditorStyles.boldLabel);
            if (GUILayout.Button("在工程中定位", GUILayout.Width(100))) EditorGUIUtility.PingObject(target);
            EditorGUILayout.EndHorizontal();

            DrawTargetIssues();

            if (inlineEditor == null || inlineEditor.target != target)
            {
                DestroyInlineEditor();
                inlineEditor = Editor.CreateEditor(target);
            }
            // 用默认 Inspector 绘制：事件/条件的 SubclassSelector 抽屉直接复用（§11.1），不重写字段 UI
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
                if (issue.Context != target) continue;
                any = true;
                EditorGUILayout.HelpBox(issue.Message, issue.IsError ? MessageType.Error : MessageType.Warning);
            }
            if (any) EditorGUILayout.Space(4);
        }

        // ══════════ 小工具 ══════════

        private bool Foldout(Object key, string label)
        {
            foldouts.TryGetValue(key, out var expanded);
            var next = EditorGUILayout.Foldout(expanded, label, true);
            foldouts[key] = next;
            return next;
        }

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

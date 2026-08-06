using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// 配方定义编辑器（待定 #11 的一部分）：创建 / 编辑 RecipeDef 资产。
    /// - 字段用 SerializedObject 自动遍历绘制，RecipeDef 以后加字段零维护；
    /// - 校验面板对「同一列表内物资重复」等问题警告不阻止（输入+产出同物资视为合法，催化剂类配方）；
    /// - 列出引用此配方的加工节点，Pin 失配的可一键同步（复用节点编辑器的同步逻辑）。
    /// 打开方式：菜单 MasterHouse/配方编辑器，或直接双击 RecipeDef 资产。
    /// </summary>
    public class RecipeDefEditorWindow : EditorWindow
    {
        const string kRecipeFolder = "Assets/GameData/Recipes";

        RecipeDef _target;
        SerializedObject _serialized;
        readonly List<RecipeDef> _all = new List<RecipeDef>();
        readonly List<ProcessorNodeDef> _referencing = new List<ProcessorNodeDef>();

        Vector2 _scrollLeft, _scrollRight;
        string _newName = "新配方";

        GUIStyle _emptyHintStyle;
        GUIStyle _syncOkStyle;
        GUIStyle _syncBadStyle;

        // ==================== 入口 ====================

        [MenuItem("MasterHouse/配方编辑器")]
        public static void Open()
        {
            var w = GetWindow<RecipeDefEditorWindow>("配方编辑器");
            w.minSize = new Vector2(620, 400);
        }

        /// <summary>双击 RecipeDef 资产直接在本窗口打开。</summary>
        [OnOpenAsset]
        static bool OnOpenRecipeDef(int instanceId, int line)
        {
            if (EditorUtility.InstanceIDToObject(instanceId) is RecipeDef def)
            {
                Open();
                GetWindow<RecipeDefEditorWindow>().SetTarget(def);
                return true;
            }
            return false;
        }

        void OnEnable()
        {
            RefreshList();
            RefreshReferences();
            Undo.undoRedoPerformed += Repaint;
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= Repaint;
        }

        void SetTarget(RecipeDef def)
        {
            _target = def;
            RefreshReferences();
            Repaint();
        }

        void RefreshList()
        {
            _all.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:RecipeDef"))
            {
                var def = AssetDatabase.LoadAssetAtPath<RecipeDef>(AssetDatabase.GUIDToAssetPath(guid));
                if (def != null) _all.Add(def);
            }
            _all.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        }

        /// <summary>收集引用当前配方的全部加工节点。</summary>
        void RefreshReferences()
        {
            _referencing.Clear();
            if (_target == null) return;
            foreach (var guid in AssetDatabase.FindAssets("t:ProcessorNodeDef"))
            {
                var node = AssetDatabase.LoadAssetAtPath<ProcessorNodeDef>(AssetDatabase.GUIDToAssetPath(guid));
                if (node != null && node.Recipe == _target)
                    _referencing.Add(node);
            }
            _referencing.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        }

        // ==================== 总布局 ====================

        void OnGUI()
        {
            EnsureStyles();

            DrawToolbar();
            EditorGUILayout.BeginHorizontal();
            DrawLeftPanel();
            DrawRightPanel();
            EditorGUILayout.EndHorizontal();
        }

        void EnsureStyles()
        {
            if (_emptyHintStyle != null) return;
            _emptyHintStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 13 };
            _syncOkStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.4f, 0.8f, 0.4f) },
            };
            _syncBadStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(1f, 0.75f, 0.25f) },
            };
        }

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label(_target != null ? $"正在编辑：{_target.name}" : "未选择配方");
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(_target == null))
            {
                if (GUILayout.Button("定位资产", EditorStyles.toolbarButton))
                    EditorGUIUtility.PingObject(_target);
                if (GUILayout.Button("保存", EditorStyles.toolbarButton))
                {
                    AssetDatabase.SaveAssets();
                    ShowNotification(new GUIContent("已保存"));
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        // ==================== 左栏：资产管理 ====================

        void DrawLeftPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(230));

            GUILayout.Label("新建配方", EditorStyles.boldLabel);
            _newName = EditorGUILayout.TextField(_newName);
            if (GUILayout.Button("创建"))
            {
                CreateNew();
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("已有配方", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("刷新", GUILayout.Width(44)))
            {
                RefreshList();
                RefreshReferences();
            }
            EditorGUILayout.EndHorizontal();

            _scrollLeft = EditorGUILayout.BeginScrollView(_scrollLeft);
            foreach (var def in _all)
            {
                if (def == null) continue;
                bool sel = def == _target;
                if (GUILayout.Toggle(sel, $"{def.name}（{def.Inputs.Count}→{def.Outputs.Count}）", "Button") && !sel)
                {
                    SetTarget(def);
                    GUIUtility.ExitGUI(); // 目标切换会改变右栏控件数量，结束本帧 GUI 防布局不匹配
                }
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        void CreateNew()
        {
            string name = string.IsNullOrWhiteSpace(_newName) ? "新配方" : _newName.Trim();

            if (!AssetDatabase.IsValidFolder(kRecipeFolder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/GameData"))
                    AssetDatabase.CreateFolder("Assets", "GameData");
                AssetDatabase.CreateFolder("Assets/GameData", "Recipes");
            }

            var def = CreateInstance<RecipeDef>();
            string path = AssetDatabase.GenerateUniqueAssetPath($"{kRecipeFolder}/{name}.asset");
            AssetDatabase.CreateAsset(def, path);
            AssetDatabase.SaveAssets();

            RefreshList();
            SetTarget(def);
        }

        // ==================== 右栏：字段 / 校验 / 引用节点 ====================

        void DrawRightPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (_target == null)
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("← 在左侧选择或新建一个配方", _emptyHintStyle);
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndVertical();
                return;
            }

            _scrollRight = EditorGUILayout.BeginScrollView(_scrollRight);
            DrawFields();
            DrawValidation();
            DrawReferences();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        void DrawFields()
        {
            GUILayout.Label("字段", EditorStyles.boldLabel);

            // SerializedObject 自动遍历绘制，与物资编辑器同一套路：
            // RecipeDef 新增字段零维护，Undo/脏标记由 ApplyModifiedProperties 统一处理。
            if (_serialized == null || _serialized.targetObject != _target)
                _serialized = new SerializedObject(_target);

            _serialized.Update();
            var prop = _serialized.GetIterator();
            bool enterChildren = true;
            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (prop.propertyPath == "m_Script") continue; // 跳过脚本引用行
                EditorGUILayout.PropertyField(prop, true);
            }
            _serialized.ApplyModifiedProperties();
        }

        // ==================== 校验（警告不阻止） ====================

        void DrawValidation()
        {
            GUILayout.Space(6);
            GUILayout.Label("校验", EditorStyles.boldLabel);
            var issues = Validate(_target);
            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox("配置无问题。", MessageType.Info);
                return;
            }
            foreach (var s in issues)
                EditorGUILayout.HelpBox(s, MessageType.Warning);
        }

        static List<string> Validate(RecipeDef def)
        {
            var issues = new List<string>();
            CheckStackList(def.Inputs, "输入", issues);
            CheckStackList(def.Outputs, "产出", issues);
            if (def.Outputs.Count == 0)
                issues.Add("没有配置任何产出。");
            if (def.WorkTicks < 1)
                issues.Add("加工时长应 ≥ 1 tick（速率一律以 tick 为单位，§3.1）。");
            // 输入+产出出现同一物资（催化剂类配方）为合法，不警告
            return issues;
        }

        static void CheckStackList(List<ItemStack> stacks, string label, List<string> issues)
        {
            var counted = new Dictionary<ItemDef, int>();
            for (int i = 0; i < stacks.Count; i++)
            {
                var s = stacks[i];
                if (s.Item == null)
                {
                    issues.Add($"{label}第 {i + 1} 条未配置物资。");
                    continue;
                }
                if (s.Count < 1)
                    issues.Add($"{label}「{s.Item.name}」数量应 ≥ 1。");
                counted[s.Item] = counted.TryGetValue(s.Item, out int n) ? n + 1 : 1;
            }
            foreach (var kv in counted)
                if (kv.Value > 1)
                    issues.Add($"{label}中物资「{kv.Key.name}」重复出现 {kv.Value} 条——暂存按物资聚合，v1 不做合并（RecipeDef 注释）。");
        }

        // ==================== 引用此配方的加工节点 ====================

        void DrawReferences()
        {
            GUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"引用此配方的加工节点（{_referencing.Count}）", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("刷新", GUILayout.Width(44)))
                RefreshReferences();
            EditorGUILayout.EndHorizontal();

            if (_referencing.Count == 0)
            {
                EditorGUILayout.LabelField("暂无节点引用。", EditorStyles.miniLabel);
                return;
            }

            int outOfSync = 0;
            foreach (var node in _referencing)
            {
                if (node == null) continue;
                bool inSync = NodeDefEditUtil.ProcessorPinsInSync(node);
                if (!inSync) outOfSync++;

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(inSync ? "同步" : "失配", inSync ? _syncOkStyle : _syncBadStyle, GUILayout.Width(30));
                if (GUILayout.Button(node.name))
                {
                    NodeDefEditorWindow.Open(node); // 跳转到节点编辑器
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
            }

            if (outOfSync > 0)
            {
                EditorGUILayout.HelpBox($"有 {outOfSync} 个节点的 Pin 与配方失配（配方改动后 Pin 需重新一一对应）。", MessageType.Warning);
                if (GUILayout.Button("同步全部失配节点的 Pin"))
                {
                    int fixedCount = 0;
                    foreach (var node in _referencing)
                    {
                        if (node == null || NodeDefEditUtil.ProcessorPinsInSync(node)) continue;
                        NodeDefEditUtil.SyncProcessorPins(node); // 同物资同方向的已摆 Pin 保留摆位与速率
                        fixedCount++;
                    }
                    ShowNotification(new GUIContent($"已同步 {fixedCount} 个节点的 Pin"));
                    GUIUtility.ExitGUI();
                }
            }
        }
    }
}

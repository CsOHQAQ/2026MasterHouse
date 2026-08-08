using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// 物资定义编辑器（待定 #11 的一部分）：创建 / 编辑 ItemDef 资产的字段。
    /// 打开方式：菜单 MasterHouse/物资编辑器，或直接双击 ItemDef 资产。
    /// </summary>
    public class ItemDefEditorWindow : EditorWindow
    {
        const string kItemFolder = "Assets/GameData/Items";

        ItemDef _target;
        SerializedObject _serialized;
        readonly List<ItemDef> _all = new List<ItemDef>();

        Vector2 _scrollLeft, _scrollRight;
        string _newName = "新物资";

        GUIStyle _emptyHintStyle;

        // ==================== 入口 ====================

        [MenuItem("MasterHouse/物资编辑器")]
        public static void Open()
        {
            var w = GetWindow<ItemDefEditorWindow>("物资编辑器");
            w.minSize = new Vector2(560, 360);
        }

        /// <summary>双击 ItemDef 资产直接在本窗口打开。</summary>
        [OnOpenAsset]
        static bool OnOpenItemDef(int instanceId, int line)
        {
            if (EditorUtility.InstanceIDToObject(instanceId) is ItemDef def)
            {
                Open();
                GetWindow<ItemDefEditorWindow>().SetTarget(def);
                return true;
            }
            return false;
        }

        void OnEnable()
        {
            RefreshList();
            Undo.undoRedoPerformed += Repaint;
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= Repaint;
        }

        void SetTarget(ItemDef def)
        {
            _target = def;
            Repaint();
        }

        void RefreshList()
        {
            _all.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:ItemDef"))
            {
                var def = AssetDatabase.LoadAssetAtPath<ItemDef>(AssetDatabase.GUIDToAssetPath(guid));
                if (def != null) _all.Add(def);
            }
            _all.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        }

        // ==================== 总布局 ====================

        void OnGUI()
        {
            if (_emptyHintStyle == null)
                _emptyHintStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 13 };

            DrawToolbar();
            EditorGUILayout.BeginHorizontal();
            DrawLeftPanel();
            DrawRightPanel();
            EditorGUILayout.EndHorizontal();
        }

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label(_target != null ? $"正在编辑：{_target.name}" : "未选择物资");
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

            GUILayout.Label("新建物资", EditorStyles.boldLabel);
            _newName = EditorGUILayout.TextField(_newName);
            if (GUILayout.Button("创建"))
            {
                CreateNew();
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("已有物资", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("刷新", GUILayout.Width(44)))
                RefreshList();
            EditorGUILayout.EndHorizontal();

            _scrollLeft = EditorGUILayout.BeginScrollView(_scrollLeft);
            foreach (var def in _all)
            {
                if (def == null) continue;
                EditorGUILayout.BeginHorizontal();

                // 颜色块预览，与节点编辑器中的 Pin 配色一致
                var swatch = GUILayoutUtility.GetRect(14, 14, GUILayout.Width(14), GUILayout.Height(18));
                swatch.y += 2;
                swatch.height = 14;
                EditorGUI.DrawRect(swatch, def.DisplayColor);

                bool sel = def == _target;
                if (GUILayout.Toggle(sel, def.name, "Button") && !sel)
                    SetTarget(def);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        void CreateNew()
        {
            string name = string.IsNullOrWhiteSpace(_newName) ? "新物资" : _newName.Trim();

            if (!AssetDatabase.IsValidFolder(kItemFolder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/GameData"))
                    AssetDatabase.CreateFolder("Assets", "GameData");
                AssetDatabase.CreateFolder("Assets/GameData", "Items");
            }

            var def = CreateInstance<ItemDef>();
            def.DisplayName = name;
            string path = AssetDatabase.GenerateUniqueAssetPath($"{kItemFolder}/{name}.asset");
            AssetDatabase.CreateAsset(def, path);
            AssetDatabase.SaveAssets();

            RefreshList();
            SetTarget(def);
        }

        // ==================== 右栏：字段编辑 ====================

        void DrawRightPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (_target == null)
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("← 在左侧选择或新建一个物资", _emptyHintStyle);
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndVertical();
                return;
            }

            GUILayout.Label("字段", EditorStyles.boldLabel);

            // SerializedObject 自动遍历绘制：ItemDef 以后新增可序列化字段，
            // 编辑器零改动自动显示；[Tooltip]/[Header]/[Range] 等特性直接生效；
            // Undo 与脏标记由 ApplyModifiedProperties 统一处理。
            // 域重载后 _serialized 会丢失，在此惰性重建。
            if (_serialized == null || _serialized.targetObject != _target)
                _serialized = new SerializedObject(_target);

            _serialized.Update();
            _scrollRight = EditorGUILayout.BeginScrollView(_scrollRight);
            var prop = _serialized.GetIterator();
            bool enterChildren = true;
            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (prop.propertyPath == "m_Script") continue; // 跳过脚本引用行
                EditorGUILayout.PropertyField(prop, true);
            }
            EditorGUILayout.EndScrollView();
            _serialized.ApplyModifiedProperties();

            EditorGUILayout.EndVertical();
        }
    }
}

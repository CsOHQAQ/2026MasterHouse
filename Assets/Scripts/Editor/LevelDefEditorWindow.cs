using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// 关卡定义编辑器（待定 #11 的一部分）：创建 / 编辑 LevelDef。
    /// - 画布形状：逐格绘制 + 矩形框选填充/擦除 + 一键生成 W×H 矩形，保存时归一化到最左下 (0,0)；
    /// - 预置节点：列表配置 + 画布点击摆放，越界/重叠标红警告不阻止；
    /// - 其余字段（WorldOrigin、可建列表、Goals 占位等）SerializedObject 自动绘制，加字段零维护。
    /// 打开方式：菜单 MasterHouse/关卡编辑器，或直接双击 LevelDef 资产。
    /// </summary>
    public class LevelDefEditorWindow : EditorWindow
    {
        const string kLevelFolder = "Assets/GameData/Levels";

        /// <summary>自动绘制时跳过的字段：画布与预置节点走画布/定制列表编辑。</summary>
        static readonly HashSet<string> kCustomDrawnProps = new HashSet<string> { "m_Script", "Canvas", "PresetNodes" };

        LevelDef _target;
        SerializedObject _serialized;
        readonly LevelCanvas _canvas = new LevelCanvas();
        readonly List<LevelDef> _all = new List<LevelDef>();

        Vector2 _scrollLeft, _scrollRight, _scrollCanvas;
        string _newName = "新关卡";
        int _genW = 20;
        int _genH = 15;

        GUIStyle _emptyHintStyle;

        // ==================== 入口 ====================

        [MenuItem("MasterHouse/关卡编辑器")]
        public static void Open()
        {
            var w = GetWindow<LevelDefEditorWindow>("关卡编辑器");
            w.minSize = new Vector2(1000, 560);
        }

        /// <summary>双击 LevelDef 资产直接在本窗口打开。</summary>
        [OnOpenAsset]
        static bool OnOpenLevelDef(int instanceId, int line)
        {
            if (EditorUtility.InstanceIDToObject(instanceId) is LevelDef def)
            {
                Open();
                GetWindow<LevelDefEditorWindow>().SetTarget(def);
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

        void SetTarget(LevelDef def)
        {
            _target = def;
            _canvas.SelectedPreset = -1;
            if (def != null) _canvas.FitTo(def);
            Repaint();
        }

        void RefreshList()
        {
            _all.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:LevelDef"))
            {
                var def = AssetDatabase.LoadAssetAtPath<LevelDef>(AssetDatabase.GUIDToAssetPath(guid));
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
            DrawCanvasPanel();
            DrawRightPanel();
            EditorGUILayout.EndHorizontal();
            DrawHintBar();
        }

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label(_target != null ? $"正在编辑：{_target.name}" : "未选择关卡");
            GUILayout.FlexibleSpace();

            _canvas.Mode = (LevelCanvas.EMode)GUILayout.Toolbar(
                (int)_canvas.Mode, new[] { "逐格绘制", "矩形框选", "摆放节点" },
                EditorStyles.toolbarButton, GUILayout.Width(220));

            GUILayout.Space(10);
            GUILayout.Label("缩放", EditorStyles.miniLabel);
            _canvas.CellSize = (int)GUILayout.HorizontalSlider(_canvas.CellSize, 8, 48, GUILayout.Width(80));

            GUILayout.Space(10);
            using (new EditorGUI.DisabledScope(_target == null))
            {
                if (GUILayout.Button("定位资产", EditorStyles.toolbarButton))
                    EditorGUIUtility.PingObject(_target);
                if (GUILayout.Button("保存（归一化）", EditorStyles.toolbarButton))
                    Save();
            }
            EditorGUILayout.EndHorizontal();
        }

        void Save()
        {
            LevelDefEditUtil.Normalize(_target);
            _canvas.FitTo(_target);
            AssetDatabase.SaveAssets();
            ShowNotification(new GUIContent("已保存，画布已归一化到最左下 (0,0)"));
        }

        void DrawHintBar()
        {
            string hint;
            switch (_canvas.Mode)
            {
                case LevelCanvas.EMode.Paint:
                    hint = "逐格绘制：左键拖动绘制；从已有格开始拖动或按右键擦除。Ctrl+滚轮缩放。保存时自动以最左下格为 (0,0)。";
                    break;
                case LevelCanvas.EMode.Rect:
                    hint = "矩形框选：左键拖出矩形松开后整片填充；右键拖出矩形整片擦除。";
                    break;
                default:
                    hint = "摆放节点：右侧列表点 #序号 选中后左键摆放（越界/重叠标红但不阻止）；未选中时左键点节点可选中，右键取消选中。";
                    break;
            }
            EditorGUILayout.HelpBox(hint, MessageType.None);
        }

        // ==================== 左栏：资产管理 ====================

        void DrawLeftPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(230));

            GUILayout.Label("新建关卡", EditorStyles.boldLabel);
            _newName = EditorGUILayout.TextField(_newName);
            if (GUILayout.Button("创建"))
            {
                CreateNew();
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("已有关卡", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("刷新", GUILayout.Width(44)))
                RefreshList();
            EditorGUILayout.EndHorizontal();

            _scrollLeft = EditorGUILayout.BeginScrollView(_scrollLeft);
            foreach (var def in _all)
            {
                if (def == null) continue;
                bool sel = def == _target;
                if (GUILayout.Toggle(sel, $"{def.name}（{def.Canvas.Grids.Count} 格）", "Button") && !sel)
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
            string name = string.IsNullOrWhiteSpace(_newName) ? "新关卡" : _newName.Trim();

            if (!AssetDatabase.IsValidFolder(kLevelFolder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/GameData"))
                    AssetDatabase.CreateFolder("Assets", "GameData");
                AssetDatabase.CreateFolder("Assets/GameData", "Levels");
            }

            var def = CreateInstance<LevelDef>();
            string path = AssetDatabase.GenerateUniqueAssetPath($"{kLevelFolder}/{name}.asset");
            AssetDatabase.CreateAsset(def, path);
            AssetDatabase.SaveAssets();

            RefreshList();
            SetTarget(def);
        }

        // ==================== 中栏：画布 ====================

        void DrawCanvasPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (_target == null)
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("← 在左侧选择或新建一个关卡", _emptyHintStyle);
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("视野宽", GUILayout.Width(40));
            _canvas.ViewCols = EditorGUILayout.IntSlider(_canvas.ViewCols, 4, 128, GUILayout.Width(160));
            GUILayout.Label("视野高", GUILayout.Width(40));
            _canvas.ViewRows = EditorGUILayout.IntSlider(_canvas.ViewRows, 4, 128, GUILayout.Width(160));
            GUILayout.Space(16);
            GUILayout.Label($"画布 {_target.Canvas.Grids.Count} 格", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            // 快速起底：一键生成矩形画布
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("生成矩形", GUILayout.Width(52));
            _genW = EditorGUILayout.IntField(_genW, GUILayout.Width(36));
            GUILayout.Label("×", GUILayout.Width(12));
            _genH = EditorGUILayout.IntField(_genH, GUILayout.Width(36));
            if (GUILayout.Button("从 (0,0) 填充", GUILayout.Width(90)))
            {
                _genW = Mathf.Clamp(_genW, 1, 128);
                _genH = Mathf.Clamp(_genH, 1, 128);
                LevelDefEditUtil.FillRect(_target, Vector2Int.zero, new Vector2Int(_genW - 1, _genH - 1), erase: false);
                _canvas.FitTo(_target);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            _scrollCanvas = EditorGUILayout.BeginScrollView(_scrollCanvas);
            var rect = GUILayoutUtility.GetRect(_canvas.ContentWidth, _canvas.ContentHeight,
                GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
            _canvas.OnGUI(rect, _target, this);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // ==================== 右栏：字段 / 预置节点 / 校验 ====================

        void DrawRightPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(340));
            _scrollRight = EditorGUILayout.BeginScrollView(_scrollRight);
            if (_target != null)
            {
                DrawAutoFields();
                DrawPresetSection();
                DrawValidation();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        void DrawAutoFields()
        {
            GUILayout.Label("字段", EditorStyles.boldLabel);

            // 除画布与预置节点外全部自动绘制：LevelDef 加字段零维护；
            // Goals / UnlockRequirement 为待定 #1 占位结构，随策划定案自动长出内容。
            if (_serialized == null || _serialized.targetObject != _target)
                _serialized = new SerializedObject(_target);

            _serialized.Update();
            var prop = _serialized.GetIterator();
            bool enterChildren = true;
            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (kCustomDrawnProps.Contains(prop.propertyPath)) continue;
                EditorGUILayout.PropertyField(prop, true);
            }
            _serialized.ApplyModifiedProperties();
        }

        // ==================== 预置节点 ====================

        void DrawPresetSection()
        {
            GUILayout.Space(6);
            GUILayout.Label($"预置节点（{_target.PresetNodes.Count} 个）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("资源点、中转节点靠预置放入关卡（§7/§8.1）。选中一条后在画布左键摆放；越界/重叠标红但不阻止，保存前请清掉校验警告。", MessageType.None);

            for (int i = 0; i < _target.PresetNodes.Count; i++)
                DrawPresetRow(i);

            if (GUILayout.Button("+ 添加预置节点"))
            {
                LevelDefEditUtil.AddPreset(_target);
                _canvas.SelectedPreset = _target.PresetNodes.Count - 1;
                _canvas.Mode = LevelCanvas.EMode.Node;
                GUIUtility.ExitGUI();
            }
        }

        void DrawPresetRow(int i)
        {
            var entry = _target.PresetNodes[i];
            bool selected = _canvas.SelectedPreset == i;
            bool doRemove = false;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // 行 1：选中按钮 / 类型标签 / 节点资产 / 删除
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Toggle(selected, $"#{i}", "Button", GUILayout.Width(34)) != selected)
            {
                if (selected)
                {
                    _canvas.SelectedPreset = -1;
                }
                else
                {
                    _canvas.SelectedPreset = i;
                    _canvas.Mode = LevelCanvas.EMode.Node;
                }
            }

            GUILayout.Label(entry.Node != null ? $"[{NodeDefEditUtil.TypeName(entry.Node)}]" : "[空]", GUILayout.Width(38));

            EditorGUI.BeginChangeCheck();
            var node = (NodeDef)EditorGUILayout.ObjectField(entry.Node, typeof(NodeDef), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_target, "修改预置节点");
                entry.Node = node;
                EditorUtility.SetDirty(_target);
            }

            if (GUILayout.Button("删", GUILayout.Width(26)))
                doRemove = true;
            EditorGUILayout.EndHorizontal();

            // 行 2：放置格 / 可移动 / 可删除
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            GUILayout.Label("格", GUILayout.Width(16));
            var cell = EditorGUILayout.Vector2IntField(GUIContent.none, entry.Cell, GUILayout.Width(96));
            bool canMove = EditorGUILayout.ToggleLeft("可移动", entry.CanMove, GUILayout.Width(58));
            bool canDelete = EditorGUILayout.ToggleLeft("可删除", entry.CanDelete, GUILayout.Width(58));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_target, "修改预置节点");
                entry.Cell = cell;
                entry.CanMove = canMove;
                entry.CanDelete = canDelete;
                EditorUtility.SetDirty(_target);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            if (doRemove)
            {
                LevelDefEditUtil.RemovePreset(_target, i);
                _canvas.SelectedPreset = -1;
                GUIUtility.ExitGUI();
            }
        }

        // ==================== 校验 ====================

        void DrawValidation()
        {
            GUILayout.Space(6);
            GUILayout.Label("校验", EditorStyles.boldLabel);
            var issues = LevelDefEditUtil.Validate(_target);
            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox("配置无问题。", MessageType.Info);
                return;
            }
            foreach (var s in issues)
                EditorGUILayout.HelpBox(s, MessageType.Warning);
        }
    }
}

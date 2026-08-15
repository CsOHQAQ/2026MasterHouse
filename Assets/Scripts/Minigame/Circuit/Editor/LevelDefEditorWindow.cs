using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// 关卡资产命名的项目级编辑器设置。只服务于创建 LevelDef 资产，不进入运行时数据。
    /// 保存到 ProjectSettings，方便团队共享同一套 A 段命名选项。
    /// </summary>
    [FilePath("ProjectSettings/MasterHouseLevelNamingSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class LevelNamingSettings : ScriptableSingleton<LevelNamingSettings>
    {
        [SerializeField] List<string> prefixes = new List<string> { "Default" };

        public List<string> Prefixes => prefixes;

        public void SaveSettings() => Save(true);
    }

    /// <summary>Project Settings 中维护关卡名 A 段的可选字符串。</summary>
    public static class LevelNamingSettingsProvider
    {
        public const string SettingsPath = "Project/MasterHouse/关卡命名";

        public static void Open() => SettingsService.OpenProjectSettings(SettingsPath);

        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            return new SettingsProvider(SettingsPath, SettingsScope.Project)
            {
                label = "关卡命名",
                guiHandler = _ =>
                {
                    var settings = LevelNamingSettings.instance;
                    var serialized = new SerializedObject(settings);
                    serialized.Update();

                    EditorGUILayout.HelpBox(
                        "在这里维护关卡资产名 A_B_C 中的 A 段选项。A 不可为空、不可包含下划线或文件名非法字符。",
                        MessageType.Info);
                    EditorGUILayout.PropertyField(serialized.FindProperty("prefixes"),
                        new GUIContent("A 段选项"), true);

                    if (serialized.ApplyModifiedProperties())
                        settings.SaveSettings();

                    DrawValidation(settings.Prefixes);
                },
                keywords = new HashSet<string> { "MasterHouse", "关卡", "命名", "Level", "A_B_C" },
            };
        }

        static void DrawValidation(List<string> prefixes)
        {
            if (prefixes == null || prefixes.Count == 0)
            {
                EditorGUILayout.HelpBox("至少需要配置一个 A 段选项。", MessageType.Warning);
                return;
            }

            var seen = new HashSet<string>();
            for (int i = 0; i < prefixes.Count; i++)
            {
                string value = prefixes[i]?.Trim();
                if (!LevelDefEditorWindow.IsValidNameSegment(value, out string reason))
                    EditorGUILayout.HelpBox($"第 {i + 1} 项无效：{reason}", MessageType.Warning);
                else if (!seen.Add(value))
                    EditorGUILayout.HelpBox($"A 段选项“{value}”重复。", MessageType.Warning);
            }
        }
    }

    /// <summary>
    /// 「修理电路」关卡编辑器（待定 #11 的一部分）：创建 / 编辑 LevelDef。
    /// - 画布形状：逐格绘制 + 矩形框选填充/擦除 + 一键生成 W×H 矩形，保存时归一化到最左下 (0,0)；
    /// - 预置节点：列表配置 + 画布点击摆放，越界/重叠标红警告不阻止；
    /// - 其余字段（开发者备注、可建中转件与数量上限、导线预算 MaxLinkCells）SerializedObject 自动绘制，加字段零维护。
    /// 打开方式：菜单 MasterHouse/关卡编辑器，或直接双击 LevelDef 资产。
    /// </summary>
    public class LevelDefEditorWindow : EditorWindow
    {
        const string kLevelFolder = "Assets/GameData/Levels";
        static readonly string[] kDifficultyNames = { "1（简单）", "2", "3", "4（最高）" };

        /// <summary>自动绘制时跳过的字段：画布/预置节点走定制编辑，其余自动绘制（加字段零维护）。</summary>
        static readonly HashSet<string> kCustomDrawnProps =
            new HashSet<string> { "m_Script", "Canvas", "PresetNodes" };

        LevelDef _target;
        SerializedObject _serialized;
        readonly LevelCanvas _canvas = new LevelCanvas();
        readonly List<LevelDef> _all = new List<LevelDef>();

        Vector2 _scrollLeft, _scrollRight, _scrollCanvas;
        int _newPrefixIndex;
        int _newDifficultyIndex;
        string _newIdentifier = "NewLevel";
        int _genW = 20;
        int _genH = 15;

        GUIStyle _emptyHintStyle;

        // ==================== 入口 ====================

        [MenuItem("MasterHouse/关卡编辑器")]
        public static void Open()
        {
            var w = GetWindow<LevelDefEditorWindow>("关卡编辑器");
            w.minSize = new Vector2(1050, 560);
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
            if (GUILayout.Button("适应内容", EditorStyles.toolbarButton))
            {
                if (_target != null) _canvas.FitTo(_target);
                _scrollCanvas = Vector2.zero;
            }

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
                    hint = "逐格绘制：左键拖动绘制；从已有格开始拖动或按右键擦除。滚轮缩放，中键拖动画布。保存时自动以最左下格为 (0,0)。";
                    break;
                case LevelCanvas.EMode.Rect:
                    hint = "矩形框选：左键拖出矩形松开后整片填充；右键拖出矩形整片擦除。滚轮缩放，中键拖动画布。";
                    break;
                default:
                    hint = "摆放节点：右侧列表点 #序号 选中后左键摆放（越界/重叠标红但不阻止）；未选中时左键点节点可选中，右键取消选中。滚轮缩放，中键拖动画布。";
                    break;
            }
            EditorGUILayout.HelpBox(hint, MessageType.None);
        }

        // ==================== 左栏：资产管理 ====================

        void DrawLeftPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(280));

            GUILayout.Label("新建关卡", EditorStyles.boldLabel);
            DrawNamingFields();

            bool canCreate = TryBuildLevelName(out string levelName, out string nameError);
            GUILayout.Label("文件名预览", EditorStyles.miniLabel);
            EditorGUILayout.SelectableLabel(canCreate ? $"{levelName}.asset" : "—", EditorStyles.textField,
                GUILayout.Height(EditorGUIUtility.singleLineHeight + 2));
            if (!canCreate)
                EditorGUILayout.HelpBox(nameError, MessageType.Warning);

            using (new EditorGUI.DisabledScope(!canCreate))
            {
                if (GUILayout.Button("创建"))
                {
                    CreateNew(levelName);
                    GUIUtility.ExitGUI();
                }
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

        void DrawNamingFields()
        {
            var prefixes = LevelNamingSettings.instance.Prefixes;
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("A", GUILayout.Width(16));
            if (prefixes != null && prefixes.Count > 0)
            {
                _newPrefixIndex = Mathf.Clamp(_newPrefixIndex, 0, prefixes.Count - 1);
                var options = new string[prefixes.Count];
                for (int i = 0; i < prefixes.Count; i++)
                    options[i] = string.IsNullOrWhiteSpace(prefixes[i]) ? $"（空项 #{i + 1}）" : prefixes[i];
                _newPrefixIndex = EditorGUILayout.Popup(_newPrefixIndex, options);
            }
            else
            {
                EditorGUILayout.LabelField("（未配置）", EditorStyles.miniLabel);
            }
            if (GUILayout.Button("配置", GUILayout.Width(42)))
                LevelNamingSettingsProvider.Open();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("B", GUILayout.Width(16));
            _newDifficultyIndex = EditorGUILayout.Popup(_newDifficultyIndex, kDifficultyNames);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("C", GUILayout.Width(16));
            _newIdentifier = EditorGUILayout.TextField(_newIdentifier);
            EditorGUILayout.EndHorizontal();
        }

        bool TryBuildLevelName(out string levelName, out string error)
        {
            levelName = null;
            error = null;

            var prefixes = LevelNamingSettings.instance.Prefixes;
            if (prefixes == null || prefixes.Count == 0)
            {
                error = "请先点击“配置”，至少添加一个 A 段选项。";
                return false;
            }

            _newPrefixIndex = Mathf.Clamp(_newPrefixIndex, 0, prefixes.Count - 1);
            string prefix = prefixes[_newPrefixIndex]?.Trim();
            string identifier = _newIdentifier?.Trim();

            if (!IsValidNameSegment(prefix, out string prefixError))
            {
                error = $"A 段无效：{prefixError}";
                return false;
            }
            if (!IsValidNameSegment(identifier, out string identifierError))
            {
                error = $"C 段无效：{identifierError}";
                return false;
            }

            int difficulty = Mathf.Clamp(_newDifficultyIndex + 1, 1, 4);
            levelName = $"{prefix}_{difficulty}_{identifier}";
            string assetPath = $"{kLevelFolder}/{levelName}.asset";
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath)))
            {
                error = $"已存在同名关卡“{levelName}”，请修改 C 段标识。";
                return false;
            }
            return true;
        }

        public static bool IsValidNameSegment(string value, out string reason)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                reason = "不可为空。";
                return false;
            }
            if (value.Contains("_"))
            {
                reason = "不可包含下划线，否则会破坏 A_B_C 结构。";
                return false;
            }
            if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                reason = "包含文件名非法字符。";
                return false;
            }
            reason = null;
            return true;
        }

        void CreateNew(string name)
        {
            if (!TryBuildLevelName(out string validatedName, out string error) || validatedName != name)
            {
                ShowNotification(new GUIContent(error ?? "关卡名已变化，请重新确认。"));
                return;
            }

            if (!AssetDatabase.IsValidFolder(kLevelFolder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/GameData"))
                    AssetDatabase.CreateFolder("Assets", "GameData");
                AssetDatabase.CreateFolder("Assets/GameData", "Levels");
            }

            var def = CreateInstance<LevelDef>();
            string path = $"{kLevelFolder}/{name}.asset";
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
            _canvas.OnGUI(rect, _target, this, ref _scrollCanvas);
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

        // 「关联家具」下拉已迁往 Furniture/Editor/FurnitureIdDrawer.cs（需求重做说明 §4.5）：
        // 做成 PropertyDrawer 后，窗口内编辑与 Project 视图直接选中编辑两条路径都生效。
        // LevelDef.FurnitureId 字段本身随第 2 步的字段瘦身删除（小游戏说明 §5.2）。

        void DrawAutoFields()
        {
            GUILayout.Label("字段", EditorStyles.boldLabel);

            // 除画布与预置节点外全部自动绘制：LevelDef 加字段零维护。
            // 当前会长出两个难度旋钮（小游戏说明 §4.3）：
            // BuildableNodes（本关可摆的中转件与各自数量上限）与 MaxLinkCells（导线总格数上限，0 = 不限）。
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
            EditorGUILayout.HelpBox(
                "电源与电池是本关的题面，靠预置放入（小游戏说明 §4.6），一律不可移动不可删除；" +
                "中转件不在这里配，走上面的「可建中转件」列表由玩家自己摆。\n" +
                "选中一条后在画布左键摆放；越界/重叠标红但不阻止，保存前请清掉校验警告。", MessageType.None);

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

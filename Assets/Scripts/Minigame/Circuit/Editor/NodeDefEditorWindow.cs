using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// 「修理电路」节点定义编辑器（待定 #11 的一部分）：
    /// - 创建 / 编辑三类 NodeDef 资产（电源 / 中转件 / 电池），配置各类型字段；
    /// - 网格画布绘制占格形状，保存时自动归一化到最左下 (0,0)；
    /// - 按类型规则配置 Pin：电源固定输出、电池固定输入、
    ///   中转件按 PinGroup 分组且方向由策划配（十字件留「同步」，分流/合流配死进出，见小游戏说明 §4.7）。
    /// 打开方式：菜单 MasterHouse/节点编辑器，或直接双击 NodeDef 资产。
    /// </summary>
    public class NodeDefEditorWindow : EditorWindow
    {
        const string kNodeFolder = "Assets/GameData/Nodes";
        const string kIdentifierUserDataPrefix = "MasterHouse.NodeIdentifier=";
        const string kTypeCodeUserDataPrefix = "MasterHouse.NodeTypeCode=";

        // 四个数组按下标一一对应。加工型/仓库型已随物资链退役删除，
        // 其余三种的 kTypeCodes **保持原值不变**——已有资产的文件名前缀依赖它（如 Input_2x3_*）。
        static readonly string[] kTypeNames =
        {
            "Input（电源）", "Connector（中转件）", "Condition（电池）",
        };
        static readonly Type[] kTypes =
        {
            typeof(ResourceNodeDef), typeof(TransitNodeDef), typeof(ConditionNodeDef),
        };
        static readonly string[] kTypeCodes = { "Input", "Con", "Cond" };
        static readonly string[] kNodeDefTypeNames = { "电源", "中转件", "电池" };
        static readonly string[] kFacingNames = { "上", "右", "下", "左" };      // 与 EDirection4 枚举顺序一致
        static readonly string[] kPinDirectionNames = { "同步", "输入", "输出" }; // 与 EPinDirection 枚举顺序一致

        NodeDef _target;
        readonly NodeShapeCanvas _canvas = new NodeShapeCanvas();
        readonly List<NodeDef> _all = new List<NodeDef>();

        Vector2 _scrollLeft, _scrollRight, _scrollCanvas;
        string _newIdentifier = "NewNode";
        int _newTypeIndex;

        GUIStyle _emptyHintStyle;

        // ==================== 入口 ====================

        [MenuItem("MasterHouse/节点编辑器")]
        public static void Open()
        {
            var w = GetWindow<NodeDefEditorWindow>("节点编辑器");
            w.minSize = new Vector2(1010, 520);
        }

        /// <summary>打开窗口并定位到指定节点（供关卡编辑器等其他工具跳转）。</summary>
        public static void Open(NodeDef def)
        {
            Open();
            GetWindow<NodeDefEditorWindow>().SetTarget(def);
        }

        /// <summary>双击 NodeDef 资产直接在本窗口打开。</summary>
        [OnOpenAsset]
        static bool OnOpenNodeDef(int instanceId, int line)
        {
            if (EditorUtility.InstanceIDToObject(instanceId) is NodeDef def)
            {
                Open(def);
                return true;
            }
            return false;
        }

        void OnEnable()
        {
            wantsMouseMove = true;
            RefreshList();
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        void OnUndoRedo()
        {
            ScheduleManagedNameSync(_target);
            Repaint();
        }

        void SetTarget(NodeDef def)
        {
            _target = def;
            _canvas.SelectedPin = -1;
            if (def != null) _canvas.FitTo(def);
            Repaint();
        }

        void RefreshList()
        {
            _all.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:NodeDef"))
            {
                var def = AssetDatabase.LoadAssetAtPath<NodeDef>(AssetDatabase.GUIDToAssetPath(guid));
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
            GUILayout.Label(_target != null ? $"正在编辑：{_target.name}（{TypeName(_target)}）" : "未选择节点");
            GUILayout.FlexibleSpace();

            _canvas.Mode = (NodeShapeCanvas.EMode)GUILayout.Toolbar(
                (int)_canvas.Mode, new[] { "绘制形状", "摆放 Pin" }, EditorStyles.toolbarButton, GUILayout.Width(160));

            GUILayout.Space(10);
            GUILayout.Label("缩放", EditorStyles.miniLabel);
            _canvas.CellSize = (int)GUILayout.HorizontalSlider(_canvas.CellSize, 16, 48, GUILayout.Width(80));
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
            NodeDefEditUtil.Normalize(_target);
            _canvas.FitTo(_target);
            bool renamed = SyncManagedAssetName(_target, true);
            AssetDatabase.SaveAssets();
            if (renamed)
                ShowNotification(new GUIContent("已保存并同步文件名，形状已归一化到最左下 (0,0)"));
        }

        void DrawHintBar()
        {
            string hint = _canvas.Mode == NodeShapeCanvas.EMode.Shape
                ? "形状模式：左键拖动绘制；从已有格开始拖动或按右键擦除。滚轮缩放，中键拖动画布。保存时自动以最左下格为 (0,0)。"
                : "Pin 模式：点击右侧「在画布摆放」，再在形状格上点击；也可直接拖动画布中的 Pin。Esc 取消摆放，右键切换朝向，滚轮缩放，中键拖动画布。";
            EditorGUILayout.HelpBox(hint, MessageType.None);
        }

        // ==================== 左栏：资产管理 ====================

        void DrawLeftPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(280));

            GUILayout.Label("新建节点", EditorStyles.boldLabel);
            _newTypeIndex = EditorGUILayout.Popup("A 类型", _newTypeIndex, kTypeNames);
            EditorGUILayout.LabelField("B 尺寸", "0x0（绘制形状后自动更新）");
            _newIdentifier = EditorGUILayout.TextField("C 识别符", _newIdentifier);
            string preview = BuildAssetName(kTypeCodes[_newTypeIndex], 0, 0, (_newIdentifier ?? "").Trim());
            GUILayout.Label("文件名预览", EditorStyles.miniLabel);
            EditorGUILayout.SelectableLabel(preview + ".asset", EditorStyles.textField,
                GUILayout.Height(EditorGUIUtility.singleLineHeight + 2));
            if (GUILayout.Button("创建"))
            {
                CreateNew();
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("已有节点", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("刷新", GUILayout.Width(44)))
                RefreshList();
            EditorGUILayout.EndHorizontal();

            _scrollLeft = EditorGUILayout.BeginScrollView(_scrollLeft);
            foreach (var def in _all)
            {
                if (def == null) continue;
                bool sel = def == _target;
                if (GUILayout.Toggle(sel, $"[{TypeName(def)}] {def.name}", "Button") && !sel)
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
            string identifier = (_newIdentifier ?? "").Trim();
            if (!ValidateIdentifier(identifier, out string error))
            {
                EditorUtility.DisplayDialog("无法创建节点", error, "确定");
                return;
            }

            if (!AssetDatabase.IsValidFolder(kNodeFolder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/GameData"))
                    AssetDatabase.CreateFolder("Assets", "GameData");
                AssetDatabase.CreateFolder("Assets/GameData", "Nodes");
            }

            string assetName = BuildAssetName(kTypeCodes[_newTypeIndex], 0, 0, identifier);
            string path = $"{kNodeFolder}/{assetName}.asset";
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
            {
                EditorUtility.DisplayDialog("无法创建节点", $"已存在同名节点：\n{path}", "确定");
                return;
            }

            // A 类型只决定 A_B_C 命名中的前缀；实际 SO 类型在右侧「类型字段」中转换。
            var def = CreateInstance<ResourceNodeDef>();
            def.DisplayName = identifier;
            AssetDatabase.CreateAsset(def, path);
            SetManagedNaming(path, kTypeCodes[_newTypeIndex], identifier);
            AssetDatabase.SaveAssets();

            RefreshList();
            SetTarget(AssetDatabase.LoadAssetAtPath<NodeDef>(path));
        }

        static bool ValidateIdentifier(string identifier, out string error)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                error = "C 识别符不能为空。";
                return false;
            }
            if (identifier.Contains("_"))
            {
                error = "C 识别符不能包含下划线；下划线由命名格式统一添加。";
                return false;
            }
            if (identifier.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || identifier.Contains("/") || identifier.Contains("\\"))
            {
                error = "C 识别符包含文件名不允许使用的字符。";
                return false;
            }

            error = null;
            return true;
        }

        static string BuildAssetName(
            string typeCode,
            int width,
            int height,
            string identifier,
            int? requiredAmount = null)
        {
            string size = $"{width}x{height}";
            return requiredAmount.HasValue
                ? $"{typeCode}_{size}_{requiredAmount.Value}_{identifier}"
                : $"{typeCode}_{size}_{identifier}";
        }

        static string GetActualTypeCode(NodeDef def)
        {
            int index = def == null ? -1 : Array.IndexOf(kTypes, def.GetType());
            return index >= 0 ? kTypeCodes[index] : null;
        }

        static bool TryGetConditionDemand(ConditionNodeDef def, out int requiredAmount, out string error)
        {
            requiredAmount = 0;
            error = null;

            if (def == null || def.Conditions.Count == 0)
            {
                error = "电池尚未配置点亮条件，无法确定文件名中的需求电量。";
                return false;
            }

            bool found = false;
            for (int i = 0; i < def.Conditions.Count; i++)
            {
                var entry = def.Conditions[i];
                if (entry == null || entry.RequiredAmount <= 0)
                {
                    error = $"电池的条件 #{i} 没有有效的需求电量。";
                    return false;
                }

                if (!found)
                {
                    requiredAmount = entry.RequiredAmount;
                    found = true;
                }
                else if (entry.RequiredAmount != requiredAmount)
                {
                    error = "电池存在多个不同的需求电量，无法用一个数字命名；请统一需求值或只保留一条条件。";
                    return false;
                }
            }

            return true;
        }

        static bool TryGetResourceTotalOutput(ResourceNodeDef def, out int totalOutput, out string error)
        {
            totalOutput = 0;
            error = null;

            if (def == null || def.Pins.Count == 0)
            {
                error = "电源尚未配置输出 Pin，无法确定文件名中的总输出电量。";
                return false;
            }

            for (int i = 0; i < def.Pins.Count; i++)
            {
                var pin = def.Pins[i]?.Pin;
                if (pin == null || pin.MaxRate <= 0)
                {
                    error = $"电源的 Pin #{i} 没有有效的输出电量。";
                    return false;
                }
                totalOutput += pin.MaxRate;
            }

            return true;
        }

        static bool TryGetNamingAmount(NodeDef def, out int? amount, out string error)
        {
            amount = null;
            error = null;

            if (def is ResourceNodeDef resource)
            {
                if (!TryGetResourceTotalOutput(resource, out int totalOutput, out error)) return false;
                amount = totalOutput;
            }
            else if (def is ConditionNodeDef condition)
            {
                if (!TryGetConditionDemand(condition, out int requiredAmount, out error)) return false;
                amount = requiredAmount;
            }

            return true;
        }

        static string GetManagedTypeCode(NodeDef def)
        {
            string path = AssetDatabase.GetAssetPath(def);
            var importer = AssetImporter.GetAtPath(path);
            if (importer != null && !string.IsNullOrEmpty(importer.userData))
            {
                foreach (string line in importer.userData.Split('\n'))
                {
                    if (!line.StartsWith(kTypeCodeUserDataPrefix, StringComparison.Ordinal)) continue;
                    string code = line.Substring(kTypeCodeUserDataPrefix.Length).TrimEnd('\r');
                    if (Array.IndexOf(kTypeCodes, code) >= 0) return code;
                }
            }

            // 旧资产没有命名元数据时，从已有文件名读取 A；转换 SO 类型也不会改它。
            string fileName = Path.GetFileNameWithoutExtension(path);
            int separator = fileName.IndexOf('_');
            string legacyCode = separator >= 0 ? fileName.Substring(0, separator) : null;
            return Array.IndexOf(kTypeCodes, legacyCode) >= 0 ? legacyCode : null;
        }

        static void GetShapeSize(NodeDef def, out int width, out int height)
        {
            if (def.Shape.Grids.Count == 0)
            {
                width = 0;
                height = 0;
                return;
            }

            var first = def.Shape.Grids[0].DeltaPosition;
            int minX = first.x, maxX = first.x, minY = first.y, maxY = first.y;
            for (int i = 1; i < def.Shape.Grids.Count; i++)
            {
                var p = def.Shape.Grids[i].DeltaPosition;
                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }
            width = maxX - minX + 1;
            height = maxY - minY + 1;
        }

        static string GetManagedIdentifier(NodeDef def)
        {
            string path = AssetDatabase.GetAssetPath(def);
            var importer = AssetImporter.GetAtPath(path);
            if (importer == null || string.IsNullOrEmpty(importer.userData)) return null;

            foreach (string line in importer.userData.Split('\n'))
                if (line.StartsWith(kIdentifierUserDataPrefix, StringComparison.Ordinal))
                    return line.Substring(kIdentifierUserDataPrefix.Length).TrimEnd('\r');
            return null;
        }

        static void SetManagedNaming(string path, string typeCode, string identifier)
        {
            var importer = AssetImporter.GetAtPath(path);
            if (importer == null) return;

            var lines = new List<string>();
            if (!string.IsNullOrEmpty(importer.userData))
            {
                foreach (string line in importer.userData.Split('\n'))
                    if (!string.IsNullOrWhiteSpace(line)
                        && !line.StartsWith(kIdentifierUserDataPrefix, StringComparison.Ordinal)
                        && !line.StartsWith(kTypeCodeUserDataPrefix, StringComparison.Ordinal))
                        lines.Add(line.TrimEnd('\r'));
            }
            lines.Add(kTypeCodeUserDataPrefix + typeCode);
            lines.Add(kIdentifierUserDataPrefix + identifier);
            importer.userData = string.Join("\n", lines);
            AssetDatabase.WriteImportSettingsIfDirty(path);
        }

        void ScheduleManagedNameSync(NodeDef def)
        {
            if (def == null || string.IsNullOrEmpty(GetManagedIdentifier(def))) return;
            EditorApplication.delayCall += () =>
            {
                if (def == null) return;
                SyncManagedAssetName(def, true);
                Repaint();
            };
        }

        bool SyncManagedAssetName(NodeDef def, bool notifyOnError)
        {
            if (def == null) return false;
            string identifier = GetManagedIdentifier(def);
            if (string.IsNullOrEmpty(identifier)) return true; // 旧节点没有命名标记，不自动改名

            string typeCode = GetManagedTypeCode(def);
            if (string.IsNullOrEmpty(typeCode)) return true;
            GetShapeSize(def, out int width, out int height);
            if (!TryGetNamingAmount(def, out int? amount, out string amountError))
            {
                if (notifyOnError) ShowNotification(new GUIContent(amountError));
                return false;
            }

            string desiredName = BuildAssetName(typeCode, width, height, identifier, amount);
            string path = AssetDatabase.GetAssetPath(def);
            if (Path.GetFileNameWithoutExtension(path) == desiredName) return true;

            string targetPath = $"{Path.GetDirectoryName(path)?.Replace('\\', '/')}/{desiredName}.asset";
            if (AssetDatabase.LoadMainAssetAtPath(targetPath) != null)
            {
                if (notifyOnError)
                    ShowNotification(new GUIContent($"无法同步文件名：{desiredName}.asset 已存在"));
                return false;
            }

            string renameError = AssetDatabase.RenameAsset(path, desiredName);
            if (!string.IsNullOrEmpty(renameError))
            {
                if (notifyOnError) ShowNotification(new GUIContent("文件名同步失败：" + renameError));
                return false;
            }

            RefreshList();
            return true;
        }

        static string TypeName(NodeDef def) => NodeDefEditUtil.TypeName(def);

        // ==================== 中栏：网格画布 ====================

        void DrawCanvasPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (_target == null)
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("← 在左侧选择或新建一个节点", _emptyHintStyle);
                GUILayout.FlexibleSpace();
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("视野宽", GUILayout.Width(40));
                _canvas.ViewCols = EditorGUILayout.IntSlider(_canvas.ViewCols, 4, 64, GUILayout.Width(150));
                GUILayout.Label("视野高", GUILayout.Width(40));
                _canvas.ViewRows = EditorGUILayout.IntSlider(_canvas.ViewRows, 4, 64, GUILayout.Width(150));
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                _scrollCanvas = EditorGUILayout.BeginScrollView(_scrollCanvas);
                var rect = GUILayoutUtility.GetRect(_canvas.ContentWidth, _canvas.ContentHeight,
                    GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
                _canvas.OnGUI(rect, _target, this, ref _scrollCanvas, () => ScheduleManagedNameSync(_target));
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.EndVertical();
        }

        // ==================== 右栏：字段 / Pin / 校验 ====================

        void DrawRightPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(340));
            _scrollRight = EditorGUILayout.BeginScrollView(_scrollRight);
            if (_target != null)
            {
                DrawBaseFields();
                DrawTypeFields();
                DrawPinSection();
                DrawValidation();
                DrawRenameSection();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        void DrawBaseFields()
        {
            GUILayout.Label("基本信息", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            string dn = EditorGUILayout.TextField("显示名", _target.DisplayName);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_target, "修改显示名");
                _target.DisplayName = dn;
                EditorUtility.SetDirty(_target);
            }
        }

        void DrawTypeFields()
        {
            GUILayout.Space(6);
            GUILayout.Label($"类型字段（{TypeName(_target)}型）", EditorStyles.boldLabel);

            int currentTypeIndex = Array.IndexOf(kTypes, _target.GetType());
            using (new EditorGUI.DisabledScope(currentTypeIndex < 0))
            {
                EditorGUI.BeginChangeCheck();
                int targetTypeIndex = EditorGUILayout.Popup("SO 类型", currentTypeIndex, kNodeDefTypeNames);
                if (EditorGUI.EndChangeCheck() && targetTypeIndex != currentTypeIndex)
                    ConvertTargetType(kTypes[targetTypeIndex]);
            }
            EditorGUILayout.HelpBox(
                "SO 类型决定节点运行规则与本区字段；左侧 A 类型只决定资产文件名的前缀。" +
                "转换会保留资产 GUID、引用、显示名、形状和 Pin；目标类型专属字段会重置为默认值。",
                MessageType.None);

            switch (_target)
            {
                case ResourceNodeDef r: DrawResourceFields(r); break;
                case TransitNodeDef t: DrawTransitFields(t); break;
                case ConditionNodeDef c: DrawConditionFields(c); break;
            }
        }

        void ConvertTargetType(Type targetType)
        {
            if (_target == null || _target.GetType() == targetType) return;

            string sourcePath = AssetDatabase.GetAssetPath(_target);
            if (string.IsNullOrEmpty(sourcePath))
            {
                ShowNotification(new GUIContent("只能转换已保存的 NodeDef 资产。"));
                return;
            }

            string targetName = kNodeDefTypeNames[Array.IndexOf(kTypes, targetType)];
            if (!EditorUtility.DisplayDialog(
                    "转换节点 SO 类型",
                    $"将「{_target.name}」转换为{targetName}。\n\n" +
                    "会保留 GUID、所有资产引用、显示名、形状与 Pin；目标类型专属字段将恢复默认值。" +
                    "此资产文件级转换不能通过 Undo 撤回。",
                    "转换", "取消"))
                return;

            NodeDef source = _target;
            string temporaryPath = sourcePath + ".type-conversion.asset";
            string sourceAbsolutePath = ToAbsoluteAssetPath(sourcePath);
            string temporaryAbsolutePath = ToAbsoluteAssetPath(temporaryPath);

            try
            {
                var replacement = (NodeDef)CreateInstance(targetType);
                CopyBaseNodeFields(source, replacement);
                ApplyTargetPinRules(replacement);
                AssetDatabase.CreateAsset(replacement, temporaryPath);
                AssetDatabase.SaveAssets();

                FileUtil.ReplaceFile(temporaryAbsolutePath, sourceAbsolutePath);
                FileUtil.DeleteFileOrDirectory(temporaryAbsolutePath);
                FileUtil.DeleteFileOrDirectory(temporaryAbsolutePath + ".meta");
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                var converted = AssetDatabase.LoadAssetAtPath<NodeDef>(sourcePath);
                if (converted == null || converted.GetType() != targetType)
                    throw new InvalidOperationException("转换后的资产类型校验失败。");

                RefreshList();
                SetTarget(converted);
                ShowNotification(new GUIContent($"已转换为{targetName}；请检查右侧校验提示。"));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                SetTarget(AssetDatabase.LoadAssetAtPath<NodeDef>(sourcePath));
                ShowNotification(new GUIContent("SO 类型转换失败，详情见 Console。"));
            }
        }

        static string ToAbsoluteAssetPath(string assetPath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(projectRoot, assetPath).Replace('\\', '/');
        }

        static void CopyBaseNodeFields(NodeDef source, NodeDef destination)
        {
            destination.DisplayName = source.DisplayName;
            destination.BackgroundSprite = source.BackgroundSprite;
            destination.FunctionIconSprite = source.FunctionIconSprite;
            destination.BackgroundColor = source.BackgroundColor;
            destination.IconColor = source.IconColor;
            destination.Shape = new GridGroup
            {
                Grids = source.Shape != null ? new List<GridData>(source.Shape.Grids) : new List<GridData>(),
            };
            destination.Pins = new List<PinLayout>();
            foreach (var layout in source.Pins)
            {
                if (layout == null) continue;
                var pin = layout.Pin;
                destination.Pins.Add(new PinLayout
                {
                    LocalCell = layout.LocalCell,
                    Facing = layout.Facing,
                    Pin = new PinDef
                    {
                        MaxRate = pin != null ? pin.MaxRate : 1,
                        Direction = pin != null ? pin.Direction : EPinDirection.None,
                        PinGroup = pin != null ? pin.PinGroup : -1,
                    },
                });
            }
        }

        static void ApplyTargetPinRules(NodeDef def)
        {
            if (def is TransitNodeDef)
            {
                // 转成中转件：方向与分组交给策划重配（转换前多半是电源/电池的单向口），
                // 这里只把两两相邻的 Pin 归成一组作为起点——十字件正好是这个形状。
                for (int i = 0; i < def.Pins.Count; i++)
                {
                    var pin = def.Pins[i].Pin;
                    pin.Direction = EPinDirection.None;
                    pin.PinGroup = i / 2;
                }
                return;
            }

            var forcedDirection = NodeDefEditUtil.ForcedDirection(def);
            if (forcedDirection == null) return;
            foreach (var layout in def.Pins)
                layout.Pin.Direction = forcedDirection.Value;
        }

        void DrawResourceFields(ResourceNodeDef r)
        {
            int total = 0;
            foreach (var layout in r.Pins)
                if (layout?.Pin != null)
                    total += Mathf.Max(0, layout.Pin.MaxRate);
            EditorGUILayout.HelpBox(
                $"电源没有自己的字段：供出多少电写在各输出 Pin 的「输出电量」上（下方 Pin 列表里改）。\n" +
                $"当前共 {r.Pins.Count} 个输出口，合计供电 {total}。",
                MessageType.None);
        }

        void DrawTransitFields(TransitNodeDef t)
        {
            EditorGUILayout.HelpBox(
                "中转件没有自己的字段：件型完全由 Pin 的分组与方向决定。\n" +
                "  十字件 = 两个组，每组两个口都留「同步」方向（运行时定向，哪边进都行）\n" +
                "  分流器 = 一个组，1 个输入口 + N 个输出口\n" +
                "  合流器 = 一个组，N 个输入口 + 1 个输出口\n" +
                "求解公式：每个输出口 = floor(组内输入之和 / 组内输出口总数)。" +
                "分母按输出口总数算，没接线的口那一份会浪费掉。",
                MessageType.None);
        }

        void DrawConditionFields(ConditionNodeDef c)
        {
            EditorGUILayout.HelpBox(
                "电池收到的电量 = 各输入 Pin 上导线携带电量之和；多条条件之间为「全部满足」才点亮。\n" +
                "「允许超额」不勾时必须刚好等于——这是把玩法从「尽量多连」变成「精确分配」的核心旋钮。",
                MessageType.None);

            GUILayout.Label("点亮条件（留空 = 恒亮）", EditorStyles.miniBoldLabel);
            for (int i = 0; i < c.Conditions.Count; i++)
            {
                var entry = c.Conditions[i];
                if (entry == null) continue; // 空条目由校验区提示

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUI.BeginChangeCheck();
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("需求电量", GUILayout.Width(58));
                int amount = EditorGUILayout.IntField(entry.RequiredAmount, GUILayout.Width(50));
                GUILayout.Space(10);
                bool allowExcess = EditorGUILayout.ToggleLeft("允许超额", entry.AllowExcess, GUILayout.Width(80));
                GUILayout.FlexibleSpace();
                bool doRemove = GUILayout.Button("×", GUILayout.Width(22));
                EditorGUILayout.EndHorizontal();
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(c, "修改点亮条件");
                    entry.RequiredAmount = Mathf.Max(1, amount);
                    entry.AllowExcess = allowExcess;
                    EditorUtility.SetDirty(c);
                }

                EditorGUILayout.LabelField(
                    entry.AllowExcess
                        ? $"收到 ≥ {entry.RequiredAmount} 即点亮"
                        : $"必须刚好收到 {entry.RequiredAmount}，多了也不亮",
                    EditorStyles.miniLabel);

                EditorGUILayout.EndVertical();

                if (doRemove)
                {
                    Undo.RecordObject(c, "删除点亮条件");
                    c.Conditions.RemoveAt(i);
                    EditorUtility.SetDirty(c);
                    GUIUtility.ExitGUI();
                }
            }

            if (GUILayout.Button("+ 添加点亮条件"))
            {
                Undo.RecordObject(c, "添加点亮条件");
                c.Conditions.Add(new ConditionEntry());
                EditorUtility.SetDirty(c);
                GUIUtility.ExitGUI();
            }
        }

        // ==================== Pin 列表 ====================

        void DrawPinSection()
        {
            GUILayout.Space(6);
            GUILayout.Label($"Pin 列表（{_target.Pins.Count} 个）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(PinRuleHint(_target), MessageType.None);

            for (int i = 0; i < _target.Pins.Count; i++)
                DrawPinRow(i);

            if (GUILayout.Button("+ 添加 Pin"))
            {
                NodeDefEditUtil.AddPin(_target);
                SelectPin(_target.Pins.Count - 1);
                GUIUtility.ExitGUI();
            }

            // 中转件按「整组」添加更省事：件型就是组的形状，逐个加 Pin 再手填组号容易配错
            if (_target is TransitNodeDef transit)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("+ 十字组（1 进 1 出）"))
                {
                    NodeDefEditUtil.AddTransitGroup(transit, 1, 1);
                    SelectPin(_target.Pins.Count - 2);
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button("+ 分流组（1 进 3 出）"))
                {
                    NodeDefEditUtil.AddTransitGroup(transit, 1, 3);
                    SelectPin(_target.Pins.Count - 4);
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button("+ 合流组（3 进 1 出）"))
                {
                    NodeDefEditUtil.AddTransitGroup(transit, 3, 1);
                    SelectPin(_target.Pins.Count - 4);
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        static string PinRuleHint(NodeDef def)
        {
            switch (def)
            {
                case ResourceNodeDef _:
                    return "电源：每个 Pin 就是一个输出口，「输出电量」= 这个口供出多少电；方向固定「输出」。";
                case ConditionNodeDef _:
                    return "电池：每个 Pin 是一个输入口，收到的电量按各口求和；方向固定「输入」。" +
                           "多配几个口，玩家不用合流器也能凑数。";
                case TransitNodeDef _:
                    return "中转件：Pin 按「分组」组织，同组内按方向分进出，" +
                           "每个输出口 = floor(组内输入之和 / 组内输出口总数)。\n" +
                           "十字件的组留「同步」方向（恰好两个口，运行时定向）；分流/合流请配死进出。" +
                           "分组号与 Pin 下标无关，删 Pin 不影响其他 Pin 的分组。";
                default:
                    return "";
            }
        }

        void SelectPin(int index)
        {
            _canvas.SelectedPin = index;
            _canvas.Mode = NodeShapeCanvas.EMode.Pin;
        }

        void DrawPinRow(int i)
        {
            var layout = _target.Pins[i];
            var pin = layout.Pin;
            bool selected = _canvas.SelectedPin == i;
            bool removable = NodeDefEditUtil.AllowFreePinEdit(_target) || _target is TransitNodeDef;
            bool doRemove = false;

            var oldBackground = GUI.backgroundColor;
            if (selected) GUI.backgroundColor = new Color(0.65f, 0.82f, 1f, 1f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = oldBackground;

            bool isTransit = _target is TransitNodeDef;

            // 行 1：选中按钮 / 配色块 / 方向 /（中转）分组 / 删除
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Toggle(selected, $"#{i}", "Button", GUILayout.Width(34)) != selected)
            {
                if (selected) _canvas.SelectedPin = -1;
                else SelectPin(i);
            }

            var swatch = GUILayoutUtility.GetRect(14, 14, GUILayout.Width(14), GUILayout.Height(14));
            EditorGUI.DrawRect(swatch, CanvasDrawUtil.PinColor(_target, pin));

            if (isTransit)
            {
                // 中转件的方向由策划配：十字件留「同步」，分流/合流配死进出
                EditorGUI.BeginChangeCheck();
                GUILayout.Label("方向", GUILayout.Width(28));
                var dir = (EPinDirection)EditorGUILayout.Popup((int)pin.Direction, kPinDirectionNames, GUILayout.Width(52));
                GUILayout.Label("组", GUILayout.Width(18));
                int group = EditorGUILayout.IntField(pin.PinGroup, GUILayout.Width(34));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_target, "修改 Pin 分组与方向");
                    pin.Direction = dir;
                    pin.PinGroup = group;
                    EditorUtility.SetDirty(_target);
                }
            }
            else
            {
                // 电源恒输出、电池恒输入，方向只读
                GUILayout.Label(NodeDefEditUtil.DirName(pin.Direction), GUILayout.Width(30));
            }

            GUILayout.FlexibleSpace();
            if (removable && GUILayout.Button("删", GUILayout.Width(26)))
                doRemove = true;
            EditorGUILayout.EndHorizontal();

            // 行 2：输出电量（仅电源有效）/ 所在格 / 朝向
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            bool ratePayload = _target is ResourceNodeDef;
            GUILayout.Label(new GUIContent(ratePayload ? "供电" : "供电*",
                    ratePayload ? "这个输出口供出多少电" : "只有电源的输出口用得到；中转与电池不参与限流"),
                GUILayout.Width(34));
            int rate;
            using (new EditorGUI.DisabledScope(!ratePayload))
                rate = EditorGUILayout.IntField(pin.MaxRate, GUILayout.Width(36));
            GUILayout.Label(new GUIContent("坐标", "用于精确输入；常规摆放请使用下方按钮或直接拖动画布中的 Pin。"), GUILayout.Width(28));
            var cell = EditorGUILayout.Vector2IntField(GUIContent.none, layout.LocalCell, GUILayout.Width(84));
            GUILayout.Label("朝向", GUILayout.Width(28));
            int facing = EditorGUILayout.Popup((int)layout.Facing, kFacingNames, GUILayout.Width(38));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_target, "修改 Pin 配置");
                pin.MaxRate = Mathf.Max(1, rate);
                layout.LocalCell = cell;
                layout.Facing = (EDirection4)facing;
                EditorUtility.SetDirty(_target);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button(selected ? "✓ 正在画布摆放（也可拖动画布中的 Pin）" : "在画布摆放"))
                SelectPin(i);

            EditorGUILayout.EndVertical();

            if (doRemove)
            {
                NodeDefEditUtil.RemovePin(_target, i);
                _canvas.SelectedPin = -1;
                GUIUtility.ExitGUI();
            }
        }

        // ==================== 校验 ====================

        void DrawValidation()
        {
            GUILayout.Space(6);
            GUILayout.Label("校验", EditorStyles.boldLabel);
            var issues = NodeDefEditUtil.Validate(_target);
            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox("配置无问题。", MessageType.Info);
                return;
            }

            foreach (var s in issues)
                EditorGUILayout.HelpBox(s, MessageType.Warning);

            if (NodeDefEditUtil.ForcedDirection(_target) != null
                && GUILayout.Button("按类型规则修正 Pin 方向"))
            {
                NodeDefEditUtil.FixPinDirections(_target);
                GUIUtility.ExitGUI();
            }
        }

        void DrawRenameSection()
        {
            GUILayout.Space(10);
            GUILayout.Label("资产命名", EditorStyles.boldLabel);

            string identifier = GetManagedIdentifier(_target);
            if (string.IsNullOrEmpty(identifier))
            {
                EditorGUILayout.HelpBox(
                    "该节点没有由编辑器记录的 C 识别符，无法安全拆分并更新文件名。请用左侧「新建节点」创建受命名规则管理的节点。",
                    MessageType.Warning);
            }
            else
            {
                GetShapeSize(_target, out int width, out int height);
                string typeCode = GetActualTypeCode(_target);
                bool hasNamingAmount = TryGetNamingAmount(_target, out int? amount, out string namingError);

                if (hasNamingAmount)
                {
                    string preview = BuildAssetName(typeCode, width, height, identifier, amount);
                    EditorGUILayout.LabelField("更新后文件名", preview + ".asset");
                }
                else
                {
                    EditorGUILayout.HelpBox(namingError, MessageType.Warning);
                }
            }

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(identifier)))
            {
                if (GUILayout.Button("更新节点名称", GUILayout.Height(28)))
                {
                    string typeCode = GetActualTypeCode(_target);
                    if (string.IsNullOrEmpty(typeCode))
                    {
                        ShowNotification(new GUIContent("当前 SO 类型不属于电源、中转件或电池。"));
                        return;
                    }

                    if (!TryGetNamingAmount(_target, out _, out string error))
                    {
                        ShowNotification(new GUIContent(error));
                        return;
                    }

                    string path = AssetDatabase.GetAssetPath(_target);
                    SetManagedNaming(path, typeCode, identifier);
                    if (SyncManagedAssetName(_target, true))
                    {
                        AssetDatabase.SaveAssets();
                        ShowNotification(new GUIContent("节点名称已更新。"));
                    }
                }
            }
        }
    }
}

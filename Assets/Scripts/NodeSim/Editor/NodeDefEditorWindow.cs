using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// 节点定义编辑器（待定 #11 的一部分）：
    /// - 创建 / 编辑四类 NodeDef 资产，配置各类型字段；
    /// - 网格画布绘制占格形状，保存时自动归一化到最左下 (0,0)；
    /// - 按类型规则配置 Pin（资源/仓库自由增删、中转成对配置、加工由配方决定）。
    /// 打开方式：菜单 MasterHouse/节点编辑器，或直接双击 NodeDef 资产。
    /// </summary>
    public class NodeDefEditorWindow : EditorWindow
    {
        const string kNodeFolder = "Assets/GameData/Nodes";

        static readonly string[] kTypeNames = { "资源型", "加工型", "仓库型", "中转型", "条件型" };
        static readonly Type[] kTypes =
        {
            typeof(ResourceNodeDef), typeof(ProcessorNodeDef), typeof(StorageNodeDef),
            typeof(TransitNodeDef), typeof(ConditionNodeDef),
        };
        static readonly string[] kFacingNames = { "上", "右", "下", "左" }; // 与 EDirection4 枚举顺序一致

        NodeDef _target;
        readonly NodeShapeCanvas _canvas = new NodeShapeCanvas();
        readonly List<NodeDef> _all = new List<NodeDef>();

        Vector2 _scrollLeft, _scrollRight, _scrollCanvas;
        string _newName = "新节点";
        int _newTypeIndex;

        GUIStyle _emptyHintStyle;

        // ==================== 入口 ====================

        [MenuItem("MasterHouse/节点编辑器")]
        public static void Open()
        {
            var w = GetWindow<NodeDefEditorWindow>("节点编辑器");
            w.minSize = new Vector2(960, 520);
        }

        /// <summary>打开窗口并定位到指定节点（供配方编辑器等其他工具跳转）。</summary>
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
            Undo.undoRedoPerformed += Repaint;
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= Repaint;
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
            AssetDatabase.SaveAssets();
            ShowNotification(new GUIContent("已保存，形状已归一化到最左下 (0,0)"));
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
            EditorGUILayout.BeginVertical(GUILayout.Width(230));

            GUILayout.Label("新建节点", EditorStyles.boldLabel);
            _newName = EditorGUILayout.TextField(_newName);
            _newTypeIndex = EditorGUILayout.Popup(_newTypeIndex, kTypeNames);
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
            string name = string.IsNullOrWhiteSpace(_newName) ? "新节点" : _newName.Trim();

            if (!AssetDatabase.IsValidFolder(kNodeFolder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/GameData"))
                    AssetDatabase.CreateFolder("Assets", "GameData");
                AssetDatabase.CreateFolder("Assets/GameData", "Nodes");
            }

            var def = (NodeDef)CreateInstance(kTypes[_newTypeIndex]);
            def.DisplayName = name;
            string path = AssetDatabase.GenerateUniqueAssetPath($"{kNodeFolder}/{name}.asset");
            AssetDatabase.CreateAsset(def, path);
            AssetDatabase.SaveAssets();

            RefreshList();
            SetTarget(def);
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
                _canvas.OnGUI(rect, _target, this, ref _scrollCanvas);
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
            switch (_target)
            {
                case ResourceNodeDef r: DrawResourceFields(r); break;
                case ProcessorNodeDef p: DrawProcessorFields(p); break;
                case StorageNodeDef s: DrawStorageFields(s); break;
                case TransitNodeDef t: DrawTransitFields(t); break;
                case ConditionNodeDef c: DrawConditionFields(c); break;
            }
        }

        void DrawResourceFields(ResourceNodeDef r)
        {
            EditorGUI.BeginChangeCheck();
            var item = (ItemDef)EditorGUILayout.ObjectField("产出物资", r.OutputItem, typeof(ItemDef), false);
            int ticks = EditorGUILayout.IntField("生产间隔（tick）", r.TicksPerProduction);
            int amount = EditorGUILayout.IntField("每次产量", r.AmountPerProduction);
            int cap = EditorGUILayout.IntField("暂存上限（满则停产）", r.StorageCap);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(r, "修改资源节点字段");
                r.OutputItem = item;
                r.TicksPerProduction = Mathf.Max(1, ticks);
                r.AmountPerProduction = Mathf.Max(1, amount);
                r.StorageCap = Mathf.Max(1, cap);
                EditorUtility.SetDirty(r);
            }
        }

        void DrawProcessorFields(ProcessorNodeDef p)
        {
            EditorGUI.BeginChangeCheck();
            var recipe = (RecipeDef)EditorGUILayout.ObjectField("配方（待定 #3：单条）", p.Recipe, typeof(RecipeDef), false);
            int inCap = EditorGUILayout.IntField("输入暂存上限/物资", p.InputStorageCapPerItem);
            int outCap = EditorGUILayout.IntField("输出暂存上限/物资", p.OutputStorageCapPerItem);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(p, "修改加工节点字段");
                bool recipeChanged = p.Recipe != recipe;
                p.Recipe = recipe;
                p.InputStorageCapPerItem = Mathf.Max(1, inCap);
                p.OutputStorageCapPerItem = Mathf.Max(1, outCap);
                EditorUtility.SetDirty(p);
                // 配方即 Pin 的唯一来源：换配方立刻同步（同物资同方向的摆位保留）
                if (recipeChanged)
                {
                    NodeDefEditUtil.SyncProcessorPins(p);
                    _canvas.SelectedPin = -1;
                    GUIUtility.ExitGUI(); // Pin 数量变化，结束本帧 GUI 防布局不匹配
                }
            }

            // 展示配方内容，方便对照 Pin
            if (p.Recipe != null)
            {
                EditorGUILayout.LabelField("配方内容：", EditorStyles.miniBoldLabel);
                using (new EditorGUI.IndentLevelScope())
                {
                    foreach (var s in p.Recipe.Inputs)
                        EditorGUILayout.LabelField($"输入  {(s.Item != null ? s.Item.name : "（空）")} × {s.Count}", EditorStyles.miniLabel);
                    foreach (var s in p.Recipe.Outputs)
                        EditorGUILayout.LabelField($"产出  {(s.Item != null ? s.Item.name : "（空）")} × {s.Count}", EditorStyles.miniLabel);
                }
            }
        }

        void DrawStorageFields(StorageNodeDef s)
        {
            GUILayout.Label("接收白名单（空 = 任意物资都收）", EditorStyles.miniBoldLabel);
            for (int i = 0; i < s.Whitelist.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                var it = (ItemDef)EditorGUILayout.ObjectField(s.Whitelist[i], typeof(ItemDef), false);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(s, "修改白名单");
                    s.Whitelist[i] = it;
                    EditorUtility.SetDirty(s);
                }
                if (GUILayout.Button("×", GUILayout.Width(22)))
                {
                    Undo.RecordObject(s, "删除白名单物资");
                    s.Whitelist.RemoveAt(i);
                    EditorUtility.SetDirty(s);
                    EditorGUILayout.EndHorizontal();
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("+ 添加白名单物资"))
            {
                Undo.RecordObject(s, "添加白名单物资");
                s.Whitelist.Add(null);
                EditorUtility.SetDirty(s);
                GUIUtility.ExitGUI();
            }
        }

        void DrawTransitFields(TransitNodeDef t)
        {
            EditorGUI.BeginChangeCheck();
            int cap = EditorGUILayout.IntField("暂存容量/物资（待定 #6）", t.StorageCapPerItem);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(t, "修改中转节点字段");
                t.StorageCapPerItem = Mathf.Max(1, cap);
                EditorUtility.SetDirty(t);
            }
        }

        void DrawConditionFields(ConditionNodeDef c)
        {
            EditorGUILayout.HelpBox(
                "条件节点判定「家具是否修好」：每条需求统计最近 W tick 内收到的量，" +
                "全部达标才算本节点满足。收到的物资即刻蒸发，不占暂存、也不会背压上游。",
                MessageType.None);

            GUILayout.Label("需求列表（留空 = 恒达标）", EditorStyles.miniBoldLabel);
            for (int i = 0; i < c.Conditions.Count; i++)
            {
                var entry = c.Conditions[i];
                if (entry == null) continue; // 空条目由校验区提示

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.BeginHorizontal();
                var swatch = GUILayoutUtility.GetRect(14, 14, GUILayout.Width(14), GUILayout.Height(14));
                EditorGUI.DrawRect(swatch, entry.Item != null ? entry.Item.DisplayColor : Color.gray);
                EditorGUI.BeginChangeCheck();
                var item = (ItemDef)EditorGUILayout.ObjectField(entry.Item, typeof(ItemDef), false);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(c, "修改需求物资");
                    entry.Item = item;
                    EditorUtility.SetDirty(c);
                }
                bool doRemove = GUILayout.Button("×", GUILayout.Width(22));
                EditorGUILayout.EndHorizontal();

                EditorGUI.BeginChangeCheck();
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("需求量", GUILayout.Width(46));
                int amount = EditorGUILayout.IntField(entry.RequiredAmount, GUILayout.Width(50));
                GUILayout.Label("窗口(tick)", GUILayout.Width(66));
                int window = EditorGUILayout.IntField(entry.WindowTicks, GUILayout.Width(50));
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(c, "修改需求数值");
                    entry.RequiredAmount = Mathf.Max(1, amount);
                    entry.WindowTicks = Mathf.Max(1, window);
                    EditorUtility.SetDirty(c);
                }

                // 速率换算：策划配的是「窗口内几个」，这里换算成每秒直觉值（tick 频率见 GameConfig）
                int tps = GameConfig.Instance != null ? Mathf.Max(1, GameConfig.Instance.TicksPerSecond) : 10;
                float perSecond = entry.RequiredAmount * (float)tps / Mathf.Max(1, entry.WindowTicks);
                EditorGUILayout.LabelField(
                    $"≈ 每 {entry.WindowTicks} tick 需 {entry.RequiredAmount} 个（约 {perSecond:0.##} 个/秒）",
                    EditorStyles.miniLabel);

                EditorGUILayout.EndVertical();

                if (doRemove)
                {
                    Undo.RecordObject(c, "删除需求");
                    c.Conditions.RemoveAt(i);
                    EditorUtility.SetDirty(c);
                    GUIUtility.ExitGUI();
                }
            }

            if (GUILayout.Button("+ 添加需求"))
            {
                Undo.RecordObject(c, "添加需求");
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

            if (NodeDefEditUtil.AllowFreePinEdit(_target))
            {
                if (GUILayout.Button("+ 添加 Pin"))
                {
                    NodeDefEditUtil.AddPin(_target);
                    SelectPin(_target.Pins.Count - 1);
                    GUIUtility.ExitGUI();
                }
            }
            else if (_target is TransitNodeDef t)
            {
                if (GUILayout.Button("+ 添加一对配对 Pin"))
                {
                    NodeDefEditUtil.AddTransitPair(t);
                    SelectPin(_target.Pins.Count - 2);
                    GUIUtility.ExitGUI();
                }
            }
            else if (_target is ProcessorNodeDef proc)
            {
                using (new EditorGUI.DisabledScope(proc.Recipe == null))
                {
                    if (GUILayout.Button("按配方同步 Pin"))
                    {
                        NodeDefEditUtil.SyncProcessorPins(proc);
                        _canvas.SelectedPin = -1;
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }

        static string PinRuleHint(NodeDef def)
        {
            switch (def)
            {
                case ResourceNodeDef _:
                    return "资源节点：可自由增删 Pin 与物资种类；方向固定为「输出」。";
                case StorageNodeDef _:
                    return "仓库节点：可自由增删 Pin 与物资种类；方向固定为「输入」。";
                case ProcessorNodeDef _:
                    return "加工节点：Pin 的数量与物资由配方的输入/产出一一对应决定，不能手动增删；改配方后自动同步，也可手动点「按配方同步 Pin」。";
                case ConditionNodeDef _:
                    return "条件节点：可自由增删 Pin，方向固定为「输入」。同一种物资允许配多个 Pin 并联供货" +
                           "（单条链接的速率有上限），到货合并计入同一条需求。";
                case TransitNodeDef _:
                    return "中转节点：Pin 必须成对配置、互为配对 Pin（§6.3 立交）；删除任一个会连同配对一起删除。物资可留空，方向固定「同步」，运行时随连接确定。";
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

            // 行 1：选中按钮 / 物资色块 / 物资 / 方向 / 删除
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Toggle(selected, $"#{i}", "Button", GUILayout.Width(34)) != selected)
            {
                if (selected) _canvas.SelectedPin = -1;
                else SelectPin(i);
            }

            var swatch = GUILayoutUtility.GetRect(14, 14, GUILayout.Width(14), GUILayout.Height(14));
            EditorGUI.DrawRect(swatch, pin.ItemType != null ? pin.ItemType.DisplayColor : Color.gray);

            if (_target is ProcessorNodeDef)
            {
                // 加工型物资来自配方，只读
                GUILayout.Label(pin.ItemType != null ? pin.ItemType.name : "（配方未指定）");
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                var it = (ItemDef)EditorGUILayout.ObjectField(pin.ItemType, typeof(ItemDef), false);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_target, "修改 Pin 物资");
                    pin.ItemType = it;
                    EditorUtility.SetDirty(_target);
                }
            }

            GUILayout.Label(NodeDefEditUtil.DirName(pin.Direction), GUILayout.Width(30));

            if (removable && GUILayout.Button("删", GUILayout.Width(26)))
                doRemove = true;
            EditorGUILayout.EndHorizontal();

            // 行 2：速率 / 所在格 / 朝向 /（中转）配对信息
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            GUILayout.Label("速率", GUILayout.Width(28));
            int rate = EditorGUILayout.IntField(pin.MaxRate, GUILayout.Width(36));
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
            if (_target is TransitNodeDef)
                GUILayout.Label($"配对 #{pin.PairedPinIndex}", EditorStyles.miniLabel, GUILayout.Width(50));
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
    }
}

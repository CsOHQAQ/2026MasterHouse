using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// MasterHouse 配置中心：全工程策划配置资产的统一入口。
    /// 左侧分组列出全部配置 SO——单资产配置可直接展开内嵌编辑，多资产内容（种族/标签/物资）列表化并支持一键新建；
    /// 资产缺失时给出红字提示与补齐按钮（调用各模块既有的生成器，只补缺失不覆盖手调）。
    /// 本窗口只是「入口」，不承载任何业务逻辑与默认值——权威数据仍在各资产本身（§16.6）。
    /// </summary>
    public sealed class ConfigHubWindow : EditorWindow
    {
        private const string GameConfigPath = "Assets/Resources/GameConfig/游戏设置.asset";
        private const string TuningPath = "Assets/Resources/OutGameUI/VisitorTuningConfig.asset";
        private const string SchedulePath = "Assets/Resources/OutGameUI/VisitorScheduleTable.asset";
        private const string EconomyPath = "Assets/Resources/OutGameUI/HouseEconomyConfig.asset";
        private const string CodexPath = "Assets/Resources/OutGameUI/CodexTable.asset";
        private const string FurnitureFamilyPath = "Assets/Resources/OutGameUI/FurnitureFamilyTable.asset";
        private const string FurniturePath = "Assets/Resources/OutGameUI/FurnitureTable.asset";
        private const string StorePath = "Assets/Resources/OutGameUI/StoreTable.asset";
        private const string FurnitureRoomPath = "Assets/Resources/OutGameUI/FurnitureRoomTable.asset";
        private const string RaceDir = "Assets/Resources/OutGameUI/VisitorRaces";
        private const string NeedDir = NeedDefEditorWindow.NeedDir;
        private const string CircuitNodeDir = "Assets/GameData/Nodes";
        private const string CircuitLevelDir = "Assets/GameData/Levels";
        private const string DialogueTuningPath = "Assets/Resources/OutGameUI/DialogueTuningConfig.asset";
        private const string SfxPath = "Assets/Resources/OutGameUI/SfxTable.asset";
        private const string DialogueTablePath = DialogueCsvImporter.TableAssetPath;
        private const string PortraitTablePath = PortraitCsvImporter.TableAssetPath;

        private Vector2 scroll;
        private readonly Dictionary<string, bool> foldouts = new Dictionary<string, bool>();
        private readonly Dictionary<Object, Editor> inlineEditors = new Dictionary<Object, Editor>();

        [MenuItem("MasterHouse/配置中心 %#m")]
        public static void Open()
        {
            var window = GetWindow<ConfigHubWindow>("配置中心");
            window.minSize = new Vector2(420, 500);
        }

        private void OnDisable()
        {
            foreach (var editor in inlineEditors.Values)
                if (editor != null) DestroyImmediate(editor);
            inlineEditors.Clear();
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("改数值 = 改 Inspector；加内容 = 加资产行。本窗口只是入口，不存任何数据。", EditorStyles.miniLabel);

            Section("全局（局内局外共用）", () =>
            {
                InlineAsset<GameConfig>(GameConfigPath, "游戏设置",
                    "tick 频率 / 局外时间倍率 / 链接默认参数", null);
            });

            Section("访客", () =>
            {
                InlineAsset<VisitorTuningConfig>(TuningPath, "调参配置",
                    "营业时段（开门/打烊）/ 闲逛冒泡节奏 / 氛围邻居名册", FixVisitorAssets);
                InlineAsset<VisitorScheduleTable>(SchedulePath, "日程表",
                    "谁在第几天几点出现。注意：已有条目别重排别插行（下标是需求随机的种子键），加内容追加表尾", FixVisitorAssets);
                AssetList<VisitorRaceDef>(RaceDir, "种族模板",
                    "性格数值（tick）/ 默认立绘ID / 序列帧（立绘差分已搬去立绘表，对话内容按 raceId 查对话整表）",
                    () => CreateAsset<VisitorRaceDef>(RaceDir, "Race_新种族"));
                // 新建不走通用按钮：NeedDef 是抽象基类，CreateInstance 造不出来，两个子类统一在编辑器窗口里建
                AssetList<NeedDef>(NeedDir, "需求",
                    "条件类（房间里要有指定家具之一）/ 小游戏类；由日程表的「需求」列按资产名引用", null);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("打开需求编辑器（左树右编辑）", GUILayout.Height(22)))
                    NeedDefEditorWindow.Open();
                if (GUILayout.Button("校验需求资产", GUILayout.Height(22), GUILayout.Width(110)))
                    NeedAssetValidator.ValidateAllFromMenu();
                EditorGUILayout.EndHorizontal();
            });

            Section("对话", () =>
            {
                InlineAsset<DialogueTuningConfig>(DialogueTuningPath, "调参配置",
                    "打字机速度 / recent 去重环长度 N（气泡时长与需求示意延迟在访客调参里，不重复配）",
                    FixDialogueAssets);
                InlineAsset<DialogueTable>(DialogueTablePath, "对话整表",
                    "**只读产物**：唯一数据源是 Excel/对话表.xlsx，在这里手改会被下次导表覆盖", null);
                InlineAsset<PortraitTable>(PortraitTablePath, "立绘索引表",
                    "**只读产物**：唯一数据源是 Excel/立绘表.xlsx。立绘ID → Resources 路径，" +
                    "对话表第二页与种族的「默认立绘ID」都引用它", null);
                EditorGUILayout.HelpBox(
                    "改台词 = 改 Excel/对话表.xlsx（两页：对话组 / 对话内容）→ 双击 Tools/导表/export_config.bat\n" +
                    "→ 切回 Unity 自动导表。对话编辑器已于 2026-08-14 退役，配置只有 Excel 一个家。\n" +
                    "加立绘差分 = 在 Excel/立绘表.xlsx 加一行（立绘ID + 资源路径），不必改代码——" +
                    "表情枚举已于 2026-08-14 退役，差分数量与命名完全交给美术。",
                    MessageType.Info);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("从 CSV 导入对话", GUILayout.Height(22)))
                    DialogueCsvImporter.ImportFromCsvMenu();
                if (GUILayout.Button("从 CSV 导入立绘", GUILayout.Height(22)))
                    PortraitCsvImporter.ImportFromCsvMenu();
                if (GUILayout.Button("校验对话表", GUILayout.Height(22), GUILayout.Width(110)))
                    DialogueAssetValidator.ValidateFromMenu();
                EditorGUILayout.EndHorizontal();
            });

            // 「标签森林」与「局内物资」两个分区已随 TagDef / ItemDef 删除：
            // 访客需求早就不用 tag 了（需求重做说明 §9.1），最后的消费方是局内物资链，
            // 已随小游戏框架落地第 2 步整体退役（§9.2）。

            Section("音效", () =>
            {
                InlineAsset<SfxTable>(SfxPath, "音效表",
                    "ESfx → 剪辑/音量倍率/节流间隔；音频源文件在 Resources/SoundEffect（全局音量在设置文件 sfxVolume）",
                    SfxConfigSetupUtility.CreateIfMissing);
            });

            Section("经济", () =>
            {
                InlineAsset<EconomyConfig>(EconomyPath, "流通数值配置",
                    "初始值 / 满意度四档奖励 + 阈值A / 拒绝扣声望 / 装饰分权重", null);
            });

            Section("小游戏·修理电路", () =>
            {
                AssetList<NodeDef>(CircuitNodeDir, "节点",
                    "电源（各输出口的电量）/ 电池（需求电量 + 是否允许超额）/ 中转件（Pin 分组）",
                    null);
                AssetList<LevelDef>(CircuitLevelDir, "关卡",
                    "画布形状 + 预置电源电池 + 可摆中转件上限 + 导线格数预算",
                    () => CreateAsset<LevelDef>(CircuitLevelDir, "新关卡"));
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("打开节点编辑器", GUILayout.Height(22)))
                    NodeDefEditorWindow.Open();
                if (GUILayout.Button("打开关卡编辑器", GUILayout.Height(22)))
                    LevelDefEditorWindow.Open();
                EditorGUILayout.EndHorizontal();
            });

            Section("图鉴与家具", () =>
            {
                InlineAsset<CodexTable>(CodexPath, "图鉴内容表", "房间/设备/档案/成就/日记", null);
                InlineAsset<FurnitureFamilyTable>(FurnitureFamilyPath, "家具族表",
                    "族级共有属性：分类/表面/占格/装饰分/音效（导表时展开进家具表每一行）", null);
                InlineAsset<FurnitureTable>(FurniturePath, "家具表", "变体列：显示名/显示尺寸/精灵图/色值/族id", FixFurnitureAssets);
                InlineAsset<StoreTable>(StorePath, "商店表", "价格/解禁声望（不列入 = 非卖品）", null);
                InlineAsset<FurnitureRoomTable>(FurnitureRoomPath, "家具房间表", "三层背景/网格/初始摆放", FixFurnitureAssets);
                // 家具四表走 Excel 导表流程：编辑 Excel/*.xlsx → Tools/导表/export_config.bat 出 Assets/Configs/*.csv → 自动导入
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Excel 导表（编辑 xlsx → export_config.bat → 自动导入）", GUILayout.MinWidth(120));
                if (GUILayout.Button("从 CSV 导入", GUILayout.Width(90))) FurnitureCsvImporter.ImportAll();
                if (GUILayout.Button("导出到 CSV", GUILayout.Width(90))) FurnitureCsvImporter.ExportAll();
                if (GUILayout.Button("Excel目录", GUILayout.Width(70)))
                    EditorUtility.RevealInFinder(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Excel"));
                EditorGUILayout.EndHorizontal();
            });

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("提示：设置项（音量/窗口模式）不在资产里，存 persistentDataPath/house-settings.json（§16.5）。",
                EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();
        }

        // ── 绘制原语 ──

        private void Section(string title, System.Action body)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("HelpBox");
            body();
            EditorGUILayout.EndVertical();
        }

        /// <summary>单资产配置行：标题 + 选中按钮 + 可展开的内嵌 Inspector；缺失时红字 + 补齐按钮。</summary>
        private void InlineAsset<T>(string path, string label, string hint, System.Action fixAction) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            EditorGUILayout.BeginHorizontal();
            if (asset == null)
            {
                EditorGUILayout.LabelField($"✗ {label}（缺失）", ErrorStyle, GUILayout.MinWidth(120));
                if (fixAction != null && GUILayout.Button("补齐", GUILayout.Width(50))) fixAction();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField("　" + path, EditorStyles.miniLabel);
                return;
            }
            var open = GetFoldout(path);
            var next = EditorGUILayout.Foldout(open, label + "　—　" + hint, true);
            if (next != open) foldouts[path] = next;
            if (GUILayout.Button("选中", GUILayout.Width(50))) Ping(asset);
            EditorGUILayout.EndHorizontal();
            if (!next) return;
            EditorGUI.indentLevel++;
            GetInlineEditor(asset).OnInspectorGUI(); // 直接编辑资产本体，Undo/存盘走 Unity 默认序列化
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);
        }

        /// <summary>多资产内容列表：逐个列出 + 选中，底部一键新建。</summary>
        private void AssetList<T>(string dir, string label, string hint, System.Action createAction) where T : Object
        {
            var open = GetFoldout(dir);
            var assets = FindAssets<T>(dir);
            EditorGUILayout.BeginHorizontal();
            var next = EditorGUILayout.Foldout(open, $"{label} ×{assets.Count}　—　{hint}", true);
            if (next != open) foldouts[dir] = next;
            if (GUILayout.Button("目录", GUILayout.Width(50)))
                Ping(AssetDatabase.LoadAssetAtPath<Object>(dir));
            EditorGUILayout.EndHorizontal();
            if (!next) return;
            EditorGUI.indentLevel++;
            foreach (var asset in assets)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(asset.name, GUILayout.MinWidth(100));
                if (GUILayout.Button("选中", GUILayout.Width(50))) Ping(asset);
                EditorGUILayout.EndHorizontal();
            }
            if (createAction != null && GUILayout.Button("＋ 新建" + label, GUILayout.Height(20)))
                createAction();
            EditorGUI.indentLevel--;
        }

        // ── 工具 ──

        private static void Ping(Object asset)
        {
            if (asset == null) return;
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        private static List<T> FindAssets<T>(string dir) where T : Object
        {
            var result = new List<T>();
            if (!AssetDatabase.IsValidFolder(dir)) return result;
            foreach (var guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { dir }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) result.Add(asset);
            }
            result.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return result;
        }

        private static void CreateAsset<T>(string dir, string defaultName) where T : Object
        {
            if (!AssetDatabase.IsValidFolder(dir))
            {
                Debug.LogError($"[配置中心] 目录不存在：{dir}，请先用对应模块的「创建示例资产」补齐目录");
                return;
            }
            var path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{defaultName}.asset");
            var asset = ScriptableObject.CreateInstance(typeof(T));
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            Ping(asset);
        }

        private static void FixVisitorAssets() => VisitorConfigSetupUtility.CreateIfMissing();
        private static void FixFurnitureAssets() => FurnitureConfigSetupUtility.CreateIfMissing();

        /// <summary>
        /// 对话侧只需要补一个调参配置——对话内容整表由导表生成，示例资产生成器
        /// （DialogueConfigSetupUtility）已随对话编辑器一起退役：造一份"示例台词"只会和 Excel 打架。
        /// </summary>
        private static void FixDialogueAssets()
        {
            const string path = DialogueTuningPath;
            if (AssetDatabase.LoadAssetAtPath<DialogueTuningConfig>(path) != null) return;
            var asset = ScriptableObject.CreateInstance<DialogueTuningConfig>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            Debug.Log("[配置中心] 已创建对话调参配置：" + path);
        }

        private bool GetFoldout(string key)
        {
            foldouts.TryGetValue(key, out var open);
            return open;
        }

        private Editor GetInlineEditor(Object asset)
        {
            if (inlineEditors.TryGetValue(asset, out var editor) && editor != null) return editor;
            editor = Editor.CreateEditor(asset);
            inlineEditors[asset] = editor;
            return editor;
        }

        private static GUIStyle errorStyle;
        private static GUIStyle ErrorStyle
        {
            get
            {
                if (errorStyle == null)
                {
                    errorStyle = new GUIStyle(EditorStyles.label);
                    errorStyle.normal.textColor = new Color(.95f, .35f, .3f);
                }
                return errorStyle;
            }
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MasterPotion.EditorTools
{
    /// <summary>
    /// 节点编辑器：自定义节点的名称、颜色、尺寸（单元格数）、类型、Pin（由配置推导）与加工配方，
    /// 保存为 NodeDef 资产（也可加载现有资产修改）。支持内联新建配方资产。
    /// </summary>
    public class NodeEditorWindow : EditorWindow
    {
        private enum NodeKind { Resource, Processor, Storage }

        private static readonly string[] KindNames = { "资源型（无输入Pin）", "加工型", "仓库型" };

        private const string NodesFolder = "Assets/GameData/Nodes";
        private const string RecipesFolder = "Assets/GameData/Recipes";
        private const string ConfigPath = "Assets/GameData/GameConfig.asset";

        // 通用
        private NodeDef editTarget;              // 为空 = 新建
        private NodeKind kind = NodeKind.Processor;
        private string nodeName = "新节点";
        private Color cardColor = new Color(0.22f, 0.25f, 0.3f);
        private Vector2Int gridSize = new Vector2Int(3, 3);
        private bool addToToolbar = true;

        // 资源型
        private readonly List<ProductionEntry> productions = new();

        // 加工型
        private readonly List<RecipeDef> recipes = new();
        private int inputBufferCap = 5;
        private int outputBufferCap = 5;
        private bool showRecipeCreator;
        private string newRecipeName = "新配方";
        private float newRecipeTime = 2f;
        private readonly List<ResourceAmount> newRecipeInputs = new();
        private readonly List<ResourceAmount> newRecipeOutputs = new();

        // 仓库型
        private readonly List<ResourceDef> storageResources = new();

        private Vector2 scroll;

        [MenuItem("MasterPotion/节点编辑器", false, 2)]
        public static void Open()
        {
            var win = GetWindow<NodeEditorWindow>("节点编辑器");
            win.minSize = new Vector2(360, 480);
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            DrawEditTargetField();
            EditorGUILayout.Space();
            DrawCommonFields();
            EditorGUILayout.Space();

            switch (kind)
            {
                case NodeKind.Resource: DrawResourceSection(); break;
                case NodeKind.Processor: DrawProcessorSection(); break;
                case NodeKind.Storage: DrawStorageSection(); break;
            }

            EditorGUILayout.Space();
            DrawPinPreview();
            EditorGUILayout.Space();
            DrawSaveSection();

            EditorGUILayout.EndScrollView();
        }

        // ---------- 加载 / 目标 ----------

        private void DrawEditTargetField()
        {
            EditorGUILayout.LabelField("编辑目标", EditorStyles.boldLabel);
            var picked = (NodeDef)EditorGUILayout.ObjectField(
                new GUIContent("现有节点（留空 = 新建）"), editTarget, typeof(NodeDef), false);
            if (picked != editTarget)
            {
                editTarget = picked;
                if (editTarget != null) LoadFrom(editTarget);
            }
            if (editTarget != null)
                EditorGUILayout.HelpBox("正在编辑现有资产，节点类型不可更改；保存会覆盖该资产。", MessageType.Info);
        }

        private void LoadFrom(NodeDef def)
        {
            nodeName = def.displayName;
            cardColor = def.cardColor;
            gridSize = def.gridSize;

            productions.Clear();
            recipes.Clear();
            storageResources.Clear();

            switch (def)
            {
                case ResourceNodeDef r:
                    kind = NodeKind.Resource;
                    foreach (var p in r.productions)
                        productions.Add(new ProductionEntry
                        { resource = p.resource, interval = p.interval, maxBuffer = p.maxBuffer });
                    break;
                case ProcessorNodeDef p:
                    kind = NodeKind.Processor;
                    recipes.AddRange(p.recipes);
                    inputBufferCap = p.inputBufferCap;
                    outputBufferCap = p.outputBufferCap;
                    break;
                case StorageNodeDef s:
                    kind = NodeKind.Storage;
                    storageResources.AddRange(s.resources);
                    break;
            }
        }

        // ---------- 通用字段 ----------

        private void DrawCommonFields()
        {
            EditorGUILayout.LabelField("基本属性", EditorStyles.boldLabel);
            nodeName = EditorGUILayout.TextField("名称", nodeName);
            cardColor = EditorGUILayout.ColorField("卡片颜色", cardColor);

            gridSize = EditorGUILayout.Vector2IntField(
                new GUIContent("尺寸（单元格数）", "卡片占用的画布单元格：宽 x 高"), gridSize);
            gridSize.x = Mathf.Clamp(gridSize.x, 2, 12);
            gridSize.y = Mathf.Clamp(gridSize.y, 2, 12);

            using (new EditorGUI.DisabledScope(editTarget != null))
                kind = (NodeKind)EditorGUILayout.Popup("节点类型", (int)kind, KindNames);
        }

        // ---------- 资源型 ----------

        private void DrawResourceSection()
        {
            EditorGUILayout.LabelField("资源产出（每项生成一个输出Pin）", EditorStyles.boldLabel);
            for (int i = 0; i < productions.Count; i++)
            {
                var p = productions[i];
                EditorGUILayout.BeginHorizontal();
                p.resource = (ResourceDef)EditorGUILayout.ObjectField(p.resource, typeof(ResourceDef), false);
                EditorGUILayout.LabelField("间隔(秒)", GUILayout.Width(52));
                p.interval = EditorGUILayout.FloatField(p.interval, GUILayout.Width(40));
                EditorGUILayout.LabelField("缓存", GUILayout.Width(30));
                p.maxBuffer = EditorGUILayout.IntField(p.maxBuffer, GUILayout.Width(35));
                if (GUILayout.Button("-", GUILayout.Width(22)))
                {
                    productions.RemoveAt(i);
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("+ 添加产出"))
                productions.Add(new ProductionEntry());
        }

        // ---------- 加工型 ----------

        private void DrawProcessorSection()
        {
            EditorGUILayout.LabelField("加工配置（Pin 由配方输入/输出并集推导）", EditorStyles.boldLabel);
            inputBufferCap = EditorGUILayout.IntField("输入缓存上限", inputBufferCap);
            outputBufferCap = EditorGUILayout.IntField("输出缓存上限", outputBufferCap);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("配方列表", EditorStyles.boldLabel);
            for (int i = 0; i < recipes.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                recipes[i] = (RecipeDef)EditorGUILayout.ObjectField(recipes[i], typeof(RecipeDef), false);
                if (GUILayout.Button("-", GUILayout.Width(22)))
                {
                    recipes.RemoveAt(i);
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("+ 添加已有配方槽位"))
                recipes.Add(null);

            EditorGUILayout.Space(4);
            showRecipeCreator = EditorGUILayout.Foldout(showRecipeCreator, "新建配方", true);
            if (showRecipeCreator) DrawRecipeCreator();
        }

        private void DrawRecipeCreator()
        {
            EditorGUI.indentLevel++;
            newRecipeName = EditorGUILayout.TextField("配方名称", newRecipeName);
            newRecipeTime = EditorGUILayout.FloatField("加工耗时(秒)", newRecipeTime);

            DrawAmountList("输入", newRecipeInputs);
            DrawAmountList("输出", newRecipeOutputs);

            if (GUILayout.Button("保存为配方资产并加入列表"))
            {
                var recipe = CreateRecipeAsset();
                if (recipe != null)
                {
                    recipes.Add(recipe);
                    newRecipeInputs.Clear();
                    newRecipeOutputs.Clear();
                    newRecipeName = "新配方";
                }
            }
            EditorGUI.indentLevel--;
        }

        private void DrawAmountList(string label, List<ResourceAmount> list)
        {
            EditorGUILayout.LabelField(label);
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(16);
                a.resource = (ResourceDef)EditorGUILayout.ObjectField(a.resource, typeof(ResourceDef), false);
                EditorGUILayout.LabelField("x", GUILayout.Width(12));
                a.amount = Mathf.Max(1, EditorGUILayout.IntField(a.amount, GUILayout.Width(40)));
                if (GUILayout.Button("-", GUILayout.Width(22)))
                {
                    list.RemoveAt(i);
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(16);
            if (GUILayout.Button($"+ 添加{label}"))
                list.Add(new ResourceAmount());
            EditorGUILayout.EndHorizontal();
        }

        private RecipeDef CreateRecipeAsset()
        {
            if (string.IsNullOrWhiteSpace(newRecipeName))
            {
                EditorUtility.DisplayDialog("节点编辑器", "请填写配方名称。", "好");
                return null;
            }
            if (newRecipeInputs.All(a => a.resource == null) || newRecipeOutputs.All(a => a.resource == null))
            {
                EditorUtility.DisplayDialog("节点编辑器", "配方的输入和输出都至少需要 1 种资源。", "好");
                return null;
            }

            EnsureFolder(RecipesFolder);
            var recipe = ScriptableObject.CreateInstance<RecipeDef>();
            recipe.displayName = newRecipeName;
            recipe.craftTime = Mathf.Max(0.1f, newRecipeTime);
            recipe.inputs = newRecipeInputs.Where(a => a.resource != null)
                .Select(a => new ResourceAmount { resource = a.resource, amount = a.amount }).ToList();
            recipe.outputs = newRecipeOutputs.Where(a => a.resource != null)
                .Select(a => new ResourceAmount { resource = a.resource, amount = a.amount }).ToList();

            string path = AssetDatabase.GenerateUniqueAssetPath(
                $"{RecipesFolder}/{Sanitize(newRecipeName)}.asset");
            AssetDatabase.CreateAsset(recipe, path);
            AssetDatabase.SaveAssets();
            Debug.Log($"节点编辑器: 已创建配方 {path}");
            return recipe;
        }

        // ---------- 仓库型 ----------

        private void DrawStorageSection()
        {
            EditorGUILayout.LabelField("可存储资源（每种生成一对输入/输出Pin，容量无上限）", EditorStyles.boldLabel);
            for (int i = 0; i < storageResources.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                storageResources[i] = (ResourceDef)EditorGUILayout.ObjectField(
                    storageResources[i], typeof(ResourceDef), false);
                if (GUILayout.Button("-", GUILayout.Width(22)))
                {
                    storageResources.RemoveAt(i);
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("+ 添加资源"))
                storageResources.Add(null);
        }

        // ---------- Pin 预览 ----------

        private void DrawPinPreview()
        {
            var inputs = new List<ResourceDef>();
            var outputs = new List<ResourceDef>();
            switch (kind)
            {
                case NodeKind.Resource:
                    foreach (var p in productions)
                        AddUnique(outputs, p.resource);
                    break;
                case NodeKind.Processor:
                    foreach (var r in recipes.Where(r => r != null))
                    {
                        foreach (var i in r.inputs) AddUnique(inputs, i.resource);
                        foreach (var o in r.outputs) AddUnique(outputs, o.resource);
                    }
                    break;
                case NodeKind.Storage:
                    foreach (var r in storageResources)
                    {
                        AddUnique(inputs, r);
                        AddUnique(outputs, r);
                    }
                    break;
            }

            EditorGUILayout.LabelField("Pin 预览", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                $"输入Pin x{inputs.Count}: " + (inputs.Count > 0
                    ? string.Join(", ", inputs.Select(r => r.displayName)) : "无"));
            EditorGUILayout.LabelField(
                $"输出Pin x{outputs.Count}: " + (outputs.Count > 0
                    ? string.Join(", ", outputs.Select(r => r.displayName)) : "无"));
        }

        // ---------- 保存 ----------

        private void DrawSaveSection()
        {
            addToToolbar = EditorGUILayout.ToggleLeft("保存后加入底部工具栏（GameConfig）", addToToolbar);

            if (!GUILayout.Button(editTarget != null ? "保存修改" : "创建节点资产", GUILayout.Height(30)))
                return;

            if (string.IsNullOrWhiteSpace(nodeName))
            {
                EditorUtility.DisplayDialog("节点编辑器", "请填写节点名称。", "好");
                return;
            }

            var def = editTarget != null ? editTarget : CreateNewAsset();
            if (def == null) return;

            def.displayName = nodeName;
            def.cardColor = cardColor;
            def.gridSize = gridSize;
            ApplyKindData(def);
            EditorUtility.SetDirty(def);

            if (addToToolbar) AddToConfig(def);
            AssetDatabase.SaveAssets();

            editTarget = def;
            Debug.Log($"节点编辑器: 已保存节点「{nodeName}」({AssetDatabase.GetAssetPath(def)})" +
                      "。运行中的场景需重新放置节点才会使用新配置。");
        }

        private NodeDef CreateNewAsset()
        {
            EnsureFolder(NodesFolder);
            NodeDef def = kind switch
            {
                NodeKind.Resource => ScriptableObject.CreateInstance<ResourceNodeDef>(),
                NodeKind.Processor => ScriptableObject.CreateInstance<ProcessorNodeDef>(),
                _ => ScriptableObject.CreateInstance<StorageNodeDef>(),
            };
            string path = AssetDatabase.GenerateUniqueAssetPath(
                $"{NodesFolder}/{Sanitize(nodeName)}.asset");
            AssetDatabase.CreateAsset(def, path);
            return def;
        }

        private void ApplyKindData(NodeDef def)
        {
            switch (def)
            {
                case ResourceNodeDef r:
                    r.productions = productions
                        .Where(p => p.resource != null)
                        .Select(p => new ProductionEntry
                        { resource = p.resource, interval = Mathf.Max(0.1f, p.interval), maxBuffer = Mathf.Max(1, p.maxBuffer) })
                        .ToList();
                    break;
                case ProcessorNodeDef p:
                    p.recipes = recipes.Where(r => r != null).Distinct().ToList();
                    p.inputBufferCap = Mathf.Max(1, inputBufferCap);
                    p.outputBufferCap = Mathf.Max(1, outputBufferCap);
                    break;
                case StorageNodeDef s:
                    s.resources = storageResources.Where(r => r != null).Distinct().ToList();
                    break;
            }
        }

        private static void AddToConfig(NodeDef def)
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config == null)
            {
                Debug.LogWarning($"节点编辑器: 未找到 {ConfigPath}，无法加入工具栏。");
                return;
            }
            if (!config.buildableNodes.Contains(def))
            {
                config.buildableNodes.Add(def);
                EditorUtility.SetDirty(config);
            }
        }

        // ---------- 工具 ----------

        private static void AddUnique(List<ResourceDef> list, ResourceDef r)
        {
            if (r != null && !list.Contains(r)) list.Add(r);
        }

        private static string Sanitize(string name)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        private static void EnsureFolder(string path)
        {
            if (path == "Assets" || AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}

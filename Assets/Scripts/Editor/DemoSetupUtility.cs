using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MasterPotion.EditorTools
{
    /// <summary>一键生成示例数据资产 + 一键搭建演示场景。</summary>
    public static class DemoSetupUtility
    {
        private const string Root = "Assets/GameData";

        [MenuItem("MasterPotion/1. 创建示例数据", false, 0)]
        public static void CreateSampleData()
        {
            EnsureFolder(Root);
            EnsureFolder(Root + "/ResourceDefs");
            EnsureFolder(Root + "/Recipes");
            EnsureFolder(Root + "/Nodes");

            // 资源
            var wood = CreateResource("Wood", "木头", new Color(0.55f, 0.36f, 0.2f));
            var stone = CreateResource("Stone", "石头", new Color(0.62f, 0.62f, 0.65f));
            var plank = CreateResource("Plank", "木板", new Color(0.82f, 0.62f, 0.38f));
            var tool = CreateResource("Tool", "工具", new Color(0.35f, 0.62f, 0.88f));

            // 配方
            var plankRecipe = CreateRecipe("PlankRecipe", "锯木板", 2f,
                new[] { (wood, 1) }, new[] { (plank, 2) });
            var toolRecipe = CreateRecipe("ToolRecipe", "打造工具", 3f,
                new[] { (wood, 1), (stone, 1) }, new[] { (tool, 1) });

            // 固定资源产出节点
            var sawmill = CreateAsset<ResourceNodeDef>(Root + "/Nodes/Sawmill.asset");
            sawmill.displayName = "伐木场";
            sawmill.gridSize = new Vector2Int(3, 3);
            sawmill.cardColor = new Color(0.2f, 0.32f, 0.2f);
            sawmill.productions = new List<ProductionEntry>
            {
                new ProductionEntry { resource = wood, interval = 2f, maxBuffer = 5 },
            };
            EditorUtility.SetDirty(sawmill);

            var quarry = CreateAsset<ResourceNodeDef>(Root + "/Nodes/Quarry.asset");
            quarry.displayName = "采石场";
            quarry.gridSize = new Vector2Int(3, 3);
            quarry.cardColor = new Color(0.3f, 0.3f, 0.34f);
            quarry.productions = new List<ProductionEntry>
            {
                new ProductionEntry { resource = stone, interval = 2.5f, maxBuffer = 5 },
            };
            EditorUtility.SetDirty(quarry);

            // 可放置节点：加工坊（两个配方，演示配方精确匹配）+ 仓库
            var workshop = CreateAsset<ProcessorNodeDef>(Root + "/Nodes/Workshop.asset");
            workshop.displayName = "加工坊";
            workshop.gridSize = new Vector2Int(3, 4);
            workshop.cardColor = new Color(0.4f, 0.3f, 0.2f);
            workshop.recipes = new List<RecipeDef> { plankRecipe, toolRecipe };
            EditorUtility.SetDirty(workshop);

            var warehouse = CreateAsset<StorageNodeDef>(Root + "/Nodes/Warehouse.asset");
            warehouse.displayName = "仓库";
            warehouse.gridSize = new Vector2Int(3, 4);
            warehouse.cardColor = new Color(0.24f, 0.28f, 0.4f);
            warehouse.resources = new List<ResourceDef> { wood, stone, plank, tool };
            EditorUtility.SetDirty(warehouse);

            // 全局配置
            var config = CreateAsset<GameConfig>(Root + "/GameConfig.asset");
            config.linkTransferInterval = 1f;
            config.buildableNodes = new List<NodeDef> { workshop, warehouse };
            EditorUtility.SetDirty(config);

            AssetDatabase.SaveAssets();
            Debug.Log("MasterPotion: 示例数据已生成到 " + Root);
        }

        [MenuItem("MasterPotion/2. 搭建演示场景", false, 1)]
        public static void SetupDemoScene()
        {
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog("MasterPotion", "请先退出 Play 模式。", "好");
                return;
            }

            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(Root + "/GameConfig.asset");
            if (config == null)
            {
                EditorUtility.DisplayDialog("MasterPotion", "请先执行菜单「1. 创建示例数据」。", "好");
                return;
            }

            // 相机
            var cam = Camera.main;
            if (cam == null)
            {
                var camGO = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = camGO.AddComponent<Camera>();
                camGO.AddComponent<AudioListener>();
            }
            cam.orthographic = true;
            cam.orthographicSize = 7f;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.13f, 0.16f);
            if (cam.GetComponent<CameraController>() == null)
                cam.gameObject.AddComponent<CameraController>();

            // 管理器
            var root = GameObject.Find("GameRoot");
            if (root == null) root = new GameObject("GameRoot");
            GetOrAdd<SimulationManager>(root);
            GetOrAdd<LinkManager>(root);
            GetOrAdd<InteractionController>(root);
            GetOrAdd<PlacementController>(root);
            GetOrAdd<BoardEditController>(root);

            var board = GetOrAdd<BoardGrid>(root);
            board.initialWidth = 26;
            board.initialHeight = 14;

            var bootstrap = GetOrAdd<Bootstrap>(root);
            bootstrap.config = config;
            bootstrap.presetNodes = new List<Bootstrap.PresetNode>
            {
                new Bootstrap.PresetNode { def = LoadNode("Sawmill"), position = new Vector2(-9.5f, 2.5f) },
                new Bootstrap.PresetNode { def = LoadNode("Quarry"), position = new Vector2(-9.5f, -2.5f) },
            };
            EditorUtility.SetDirty(bootstrap);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("MasterPotion: 场景搭建完成，直接进入 Play 即可。");
        }

        // ---------- helpers ----------

        private static ResourceDef CreateResource(string file, string display, Color color)
        {
            var r = CreateAsset<ResourceDef>($"{Root}/ResourceDefs/{file}.asset");
            r.displayName = display;
            r.color = color;
            EditorUtility.SetDirty(r);
            return r;
        }

        private static RecipeDef CreateRecipe(string file, string display, float time,
            (ResourceDef res, int amt)[] inputs, (ResourceDef res, int amt)[] outputs)
        {
            var r = CreateAsset<RecipeDef>($"{Root}/Recipes/{file}.asset");
            r.displayName = display;
            r.craftTime = time;
            r.inputs = new List<ResourceAmount>();
            foreach (var (res, amt) in inputs)
                r.inputs.Add(new ResourceAmount { resource = res, amount = amt });
            r.outputs = new List<ResourceAmount>();
            foreach (var (res, amt) in outputs)
                r.outputs.Add(new ResourceAmount { resource = res, amount = amt });
            EditorUtility.SetDirty(r);
            return r;
        }

        private static T CreateAsset<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            var so = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(so, path);
            return so;
        }

        private static NodeDef LoadNode(string name) =>
            AssetDatabase.LoadAssetAtPath<NodeDef>($"{Root}/Nodes/{name}.asset");

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        private static void EnsureFolder(string path)
        {
            if (path == "Assets" || AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            string leaf = path.Substring(slash + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}

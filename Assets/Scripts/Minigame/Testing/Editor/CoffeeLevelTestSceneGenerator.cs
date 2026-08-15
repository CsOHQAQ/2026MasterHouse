#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// 创建/刷新 GameTest_Coffee 的固定测试壳（与 CircuitLevelTestSceneGenerator 同构）。
    ///
    /// 除场景外还顺手补两样（都只补缺失）：
    /// - 咖啡本体资产（Prefab / MinigameDef / 默认关卡 / NeedDef）——直接复用资产生成器；
    /// - 「Coffee_直达冲泡」调参关卡（0 障碍 + 0.5 秒磨完，2026-08-15 拍板）：
    ///   调冲咖啡手感时切到它，免得每次都先磨一遍豆子。它不进正式关卡池——
    ///   池只认 MinigameDef.levels 里配的，测试面板的下拉才按目录扫。
    /// </summary>
    public static class CoffeeLevelTestSceneGenerator
    {
        private const string ScenePath = "Assets/Scenes/GameTest_Coffee.unity";
        private const string PrefabPath = "Assets/GameData/Minigames/CoffeeMinigame.prefab";
        private const string DefaultLevelPath = "Assets/GameData/Minigames/CoffeeLevels/Coffee_Default.asset";
        private const string TuningLevelPath = "Assets/GameData/Minigames/CoffeeLevels/Coffee_直达冲泡.asset";

        [MenuItem("MasterHouse/小游戏/重建制作咖啡测试场景")]
        public static void RebuildFromMenu()
        {
            if (!EditorUtility.DisplayDialog("重建制作咖啡测试场景",
                    "会重建 GameTest_Coffee 场景壳；咖啡资产与调参关卡只补缺失、不覆盖已有。",
                    "重建", "取消"))
                return;
            Rebuild();
        }

        /// <summary>供 Unity batchmode 验证与生成调用。</summary>
        public static void Rebuild()
        {
            // 场景依赖 Prefab 与默认关卡；缺了就先跑一遍资产生成器（只补缺失，不会动手调内容）
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null ||
                AssetDatabase.LoadAssetAtPath<CoffeeLevelDef>(DefaultLevelPath) == null)
                CoffeeMinigamePrefabGenerator.CreateIfMissing();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var level = AssetDatabase.LoadAssetAtPath<CoffeeLevelDef>(DefaultLevelPath);
            if (prefab == null || level == null)
            {
                Debug.LogError($"[咖啡关卡测试] 缺少生成依赖：Prefab={prefab != null}，Level={level != null}");
                return;
            }

            EnsureTuningLevel();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "GameTest_Coffee";

            var root = new GameObject("制作咖啡关卡测试入口");
            var bootstrap = root.AddComponent<CoffeeLevelTestBootstrap>();
            bootstrap.level = level;
            bootstrap.minigamePrefab = prefab;
            bootstrap.launchOnStart = true;

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.LogError("[咖啡关卡测试] 场景保存失败：" + ScenePath);
                return;
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[咖啡关卡测试] 已生成：" + ScenePath + "，默认关卡：" + DefaultLevelPath +
                      "。调冲泡手感请在测试面板切到 Coffee_直达冲泡。");
        }

        /// <summary>「直达冲泡」调参关卡：磨豆环节名存实亡，进场半秒后自动进冲咖啡。</summary>
        private static void EnsureTuningLevel()
        {
            if (AssetDatabase.LoadAssetAtPath<CoffeeLevelDef>(TuningLevelPath) != null) return;

            var tuning = ScriptableObject.CreateInstance<CoffeeLevelDef>();
            tuning.ObstacleCount = 0;
            tuning.GrindFillSeconds = 0.5f;
            AssetDatabase.CreateAsset(tuning, TuningLevelPath);
            Debug.Log("[咖啡关卡测试] 已创建调参关卡：" + TuningLevelPath);
        }
    }
}
#endif

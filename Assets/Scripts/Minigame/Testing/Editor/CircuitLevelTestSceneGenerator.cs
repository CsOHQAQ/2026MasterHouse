#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace MasterHouse.EditorTools
{
    /// <summary>创建/刷新 GameTest_Electric 的固定测试壳；不会修改任何 LevelDef 关卡资产。</summary>
    public static class CircuitLevelTestSceneGenerator
    {
        private const string ScenePath = "Assets/Scenes/GameTest_Electric.unity";
        private const string PrefabPath = "Assets/GameData/Minigames/CircuitMinigame.prefab";
        private const string DefaultLevelPath = "Assets/GameData/Levels/General_1_Intro01.asset";

        [MenuItem("MasterHouse/小游戏/重建拉电线关卡测试场景")]
        public static void RebuildFromMenu()
        {
            if (!EditorUtility.DisplayDialog("重建拉电线关卡测试场景",
                    "会重建 GameTest_Electric 场景壳，但不会修改任何 LevelDef 关卡资产。",
                    "重建", "取消"))
                return;
            Rebuild();
        }

        /// <summary>供 Unity batchmode 验证与生成调用。</summary>
        public static void Rebuild()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var level = AssetDatabase.LoadAssetAtPath<LevelDef>(DefaultLevelPath);
            if (prefab == null || level == null)
            {
                Debug.LogError($"[电路关卡测试] 缺少生成依赖：Prefab={prefab != null}，Level={level != null}");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "GameTest_Electric";

            var root = new GameObject("拉电线关卡测试入口");
            var bootstrap = root.AddComponent<CircuitLevelTestBootstrap>();
            bootstrap.level = level;
            bootstrap.minigamePrefab = prefab;
            bootstrap.launchOnStart = true;

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.LogError("[电路关卡测试] 场景保存失败：" + ScenePath);
                return;
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[电路关卡测试] 已生成：" + ScenePath + "，默认关卡：" + DefaultLevelPath);
        }
    }
}
#endif

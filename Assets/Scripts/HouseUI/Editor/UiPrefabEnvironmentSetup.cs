using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// UI Prefab 编辑环境：生成一个带 1920×1080 CanvasScaler 的环境场景并指定为
    /// Editor 的「UI Environment」——之后在 Prefab 模式打开任何 UI Prefab，
    /// 都会摆在与游戏一致的画布里（HouseUIManager 同参数），所见即所得，
    /// 不再出现「Prefab 里调的位置和游戏内不一样」的锚点漂移。
    /// </summary>
    public static class UiPrefabEnvironmentSetup
    {
        private const string ScenePath = "Assets/Scenes/UIPrefabEditEnv.unity";

        [MenuItem("MasterHouse/UI/创建并指定 UI Prefab 编辑环境")]
        public static void CreateAndAssign()
        {
            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (sceneAsset == null)
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                go.layer = 5;
                var canvas = go.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                // 与 HouseUIManager 的运行时画布一致：1920×1080 + Expand
                var scaler = go.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
                EditorSceneManager.MoveGameObjectToScene(go, scene);
                EditorSceneManager.SaveScene(scene, ScenePath);
                EditorSceneManager.CloseScene(scene, true);
                sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            }
            EditorSettings.prefabUIEnvironment = sceneAsset;
            Debug.Log("[UI] 已指定 UI Prefab 编辑环境：" + ScenePath +
                      "。重新打开 UI Prefab 即在 1920×1080 画布内编辑（游戏内同参数）。");
        }
    }
}

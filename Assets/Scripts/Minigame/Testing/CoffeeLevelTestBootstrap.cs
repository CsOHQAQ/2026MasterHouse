using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MasterHouse
{
    /// <summary>
    /// 制作咖啡的手感调参测试入口（与 CircuitLevelTestBootstrap 同构，两套互不干扰）。
    ///
    /// 测试场景直接启动正式 CoffeeMinigame Prefab，确保 Play 时看到的判定、采样与计分
    /// 和访客流程完全一致；本类只补场景壳、重开与结果日志。
    ///
    /// 调冲咖啡手感时切到「Coffee_直达冲泡」关卡（0 障碍 + 0.5 秒磨完），
    /// 免得每次都先磨一遍豆子——零代码的纯资产方案（2026-08-15 拍板）。
    ///
    /// ⚠ 磨豆阶段全屏左键都算切环（正式玩法如此），点本面板的按钮也不例外；
    /// 好在切关/重开本来就会整局重置，不影响调参结论。
    /// </summary>
    public sealed class CoffeeLevelTestBootstrap : MonoBehaviour
    {
        private const string LevelFolder = "Assets/GameData/Minigames/CoffeeLevels";

        [Header("测试内容")]
        [Tooltip("本次 Play 要测试的咖啡关卡（一关 = 一组手感参数）。直接把 CoffeeLevelDef 资产拖到这里即可。")]
        public CoffeeLevelDef level;

        [Tooltip("正式制作咖啡 Prefab。通常保持 CoffeeMinigame.prefab，不要复制测试专用版本。")]
        public GameObject minigamePrefab;

        [Header("测试操作")]
        [Tooltip("进入 Play Mode 后自动开始。关闭后可由 Inspector 右键菜单启动。")]
        public bool launchOnStart = true;

        private GameObject activeInstance;
        private bool hasResult;
        private int lastScore;
        private bool panelVisible = true;
        private bool dropdownOpen;
        private Vector2 dropdownScroll;
        private int selectedLevelIndex = -1;

        [SerializeField, HideInInspector]
        private List<CoffeeLevelDef> availableLevels = new List<CoffeeLevelDef>();

        private void Start()
        {
            EnsureEventSystem();
            EnsureCamera();
            RefreshLevelCatalog();
            if (launchOnStart) Launch();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.C))
            {
                panelVisible = !panelVisible;
                if (!panelVisible) dropdownOpen = false;
            }

            if (Input.GetKeyDown(KeyCode.R))
                Restart();
        }

        [ContextMenu("启动当前关卡")]
        public void Launch()
        {
            if (level == null)
            {
                Debug.LogError("[咖啡关卡测试] 没有配置 CoffeeLevelDef。请在 GameTest_Coffee 场景的测试入口上指定关卡。", this);
                return;
            }

            if (minigamePrefab == null)
            {
                Debug.LogError("[咖啡关卡测试] 没有配置正式的 CoffeeMinigame Prefab。", this);
                return;
            }

            DestroyActiveInstance();
            hasResult = false;

            var canvas = EnsureCanvas();
            activeInstance = Instantiate(minigamePrefab, canvas.transform, false);
            activeInstance.name = minigamePrefab.name + "_LevelTest";

            if (activeInstance.transform is RectTransform rect)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            var minigame = FindMinigame(activeInstance);
            if (minigame == null)
            {
                Debug.LogError("[咖啡关卡测试] Prefab 根节点及子物体中找不到 IMinigame 实现。", activeInstance);
                DestroyActiveInstance();
                return;
            }

            minigame.Launch(level, HandleFinish, HandleAbort);

            // 调参信息（均速/方差）只在测试场景显示：正式 Prefab 里默认隐藏，这里显式打开
            var coffeeView = activeInstance.GetComponent<CoffeeMinigameView>();
            if (coffeeView != null && coffeeView.tuningLabel != null)
                coffeeView.tuningLabel.gameObject.SetActive(true);

            Debug.Log($"[咖啡关卡测试] 已启动 {level.name}。按 R 随时重开。", level);
        }

        [ContextMenu("重开当前关卡")]
        public void Restart()
        {
            Debug.Log($"[咖啡关卡测试] 重开 {(level != null ? level.name : "<未配置>")}。", this);
            Launch();
        }

        private void SwitchToSelectedLevel()
        {
            if (selectedLevelIndex < 0 || selectedLevelIndex >= availableLevels.Count) return;
            var selected = availableLevels[selectedLevelIndex];
            if (selected == null) return;

            level = selected;
            dropdownOpen = false;
            Launch();
        }

        /// <summary>
        /// 测试场景只在 Unity Editor 内使用，因此 Play 时可直接扫描关卡目录；
        /// 目录里的调参关卡（如 Coffee_直达冲泡）不进正式关卡池——池只认 MinigameDef.levels 里配的。
        /// </summary>
        private void RefreshLevelCatalog()
        {
            availableLevels.Clear();

#if UNITY_EDITOR
            var candidates = new List<(string path, CoffeeLevelDef level)>();
            foreach (var guid in AssetDatabase.FindAssets("t:CoffeeLevelDef", new[] { LevelFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var candidate = AssetDatabase.LoadAssetAtPath<CoffeeLevelDef>(path);
                if (candidate != null) candidates.Add((path, candidate));
            }

            candidates.Sort((a, b) => string.Compare(a.path, b.path, StringComparison.OrdinalIgnoreCase));
            foreach (var candidate in candidates)
                availableLevels.Add(candidate.level);
#endif

            // 非 Editor 环境或当前关卡放在扫描目录外时，至少保留场景显式配置的关卡。
            if (level != null && !availableLevels.Contains(level))
                availableLevels.Insert(0, level);

            if (level == null && availableLevels.Count > 0)
                level = availableLevels[0];

            selectedLevelIndex = level != null ? availableLevels.IndexOf(level) : -1;
            if (selectedLevelIndex < 0 && availableLevels.Count > 0)
                selectedLevelIndex = 0;
        }

        private void HandleFinish(int score)
        {
            hasResult = true;
            lastScore = Mathf.Clamp(score, 0, 100);
            Debug.Log($"[咖啡关卡测试] {level.name} 完成，得分 {lastScore}/100。按 R 重开。", level);
        }

        private void HandleAbort()
        {
            hasResult = false;
            Debug.Log($"[咖啡关卡测试] {level.name} 已放弃。按 R 重开。", level);
        }

        private void OnGUI()
        {
            if (!panelVisible) return;

            const float width = 480f;
            float listHeight = dropdownOpen ? Mathf.Min(300f, availableLevels.Count * 30f + 8f) : 0f;
            GUILayout.BeginArea(new Rect(12f, 12f, width, 134f + listHeight), GUI.skin.box);

            GUILayout.Label("制作咖啡测试（C 隐藏面板）", HeaderStyle());
            GUILayout.Label(hasResult
                ? $"当前：{SafeLevelName()}　得分：{lastScore}/100　R 重开"
                : $"当前：{SafeLevelName()}　R 重开");
            GUILayout.Label("磨豆阶段点本面板也算切环；调冲泡手感请切「Coffee_直达冲泡」");

            GUILayout.BeginHorizontal();
            string pendingName = selectedLevelIndex >= 0 && selectedLevelIndex < availableLevels.Count
                ? availableLevels[selectedLevelIndex].name
                : "<没有扫描到关卡>";
            if (GUILayout.Button(pendingName + (dropdownOpen ? " ▲" : " ▼"), GUILayout.Height(30f)))
                dropdownOpen = !dropdownOpen;

            bool previousEnabled = GUI.enabled;
            GUI.enabled = selectedLevelIndex >= 0 && selectedLevelIndex < availableLevels.Count;
            if (GUILayout.Button("切换关卡", GUILayout.Width(100f), GUILayout.Height(30f)))
                SwitchToSelectedLevel();
            GUI.enabled = previousEnabled;
            GUILayout.EndHorizontal();

            if (dropdownOpen)
            {
                dropdownScroll = GUILayout.BeginScrollView(dropdownScroll, GUILayout.Height(listHeight));
                for (int i = 0; i < availableLevels.Count; i++)
                {
                    var candidate = availableLevels[i];
                    if (candidate == null) continue;
                    bool selected = i == selectedLevelIndex;
                    if (GUILayout.Button((selected ? "● " : "　") + candidate.name, GUI.skin.label,
                            GUILayout.Height(28f)))
                    {
                        selectedLevelIndex = i;
                        dropdownOpen = false;
                    }
                }
                GUILayout.EndScrollView();
            }

            GUILayout.EndArea();
        }

        private static GUIStyle HeaderStyle()
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            style.normal.textColor = Color.white;
            return style;
        }

        private string SafeLevelName() => level != null ? level.name : "<未配置 CoffeeLevelDef>";

        private static IMinigame FindMinigame(GameObject root)
        {
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour is IMinigame minigame)
                    return minigame;
            return null;
        }

        private static Canvas EnsureCanvas()
        {
            var canvas = FindObjectOfType<Canvas>();
            if (canvas != null) return canvas.rootCanvas;

            var go = new GameObject("CoffeeLevelTestCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        private static void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        /// <summary>
        /// 空测试场景没有相机时，Game 视图会打「Display 1 No cameras rendering」水印，
        /// 这里建一个只清屏、什么都不画的相机压掉它。Overlay Canvas 不经相机渲染，
        /// 正式场景（OutGameTest）自带相机，没有这个问题——水印是测试场景独有的。
        /// </summary>
        private static void EnsureCamera()
        {
            if (FindObjectOfType<Camera>() != null) return;
            var go = new GameObject("TestCamera", typeof(Camera));
            var cam = go.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 0;
            cam.orthographic = true;
        }

        private void DestroyActiveInstance()
        {
            if (activeInstance == null) return;
            Destroy(activeInstance);
            activeInstance = null;
        }
    }
}

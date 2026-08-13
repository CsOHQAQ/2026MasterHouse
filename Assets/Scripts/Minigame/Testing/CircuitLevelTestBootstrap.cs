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
    /// 修理电路的关卡设计测试入口。
    ///
    /// 测试场景直接启动正式 CircuitMinigame Prefab，确保策划 Play 时看到的
    /// 描线、预算、求解与计分和访客流程完全一致；本类只补场景壳、重开与结果日志。
    /// </summary>
    public sealed class CircuitLevelTestBootstrap : MonoBehaviour
    {
        private const string LevelFolder = "Assets/GameData/Levels";

        [Header("测试内容")]
        [Tooltip("本次 Play 要测试的修理电路关卡。直接把 LevelDef 资产拖到这里即可。")]
        public LevelDef level;

        [Tooltip("正式修理电路 Prefab。通常保持 CircuitMinigame.prefab，不要复制测试专用版本。")]
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
        private List<LevelDef> availableLevels = new List<LevelDef>();

        private void Start()
        {
            EnsureEventSystem();
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
                Debug.LogError("[电路关卡测试] 没有配置 LevelDef。请在 GameTest_Electric 场景的测试入口上指定关卡。", this);
                return;
            }

            if (minigamePrefab == null)
            {
                Debug.LogError("[电路关卡测试] 没有配置正式的 CircuitMinigame Prefab。", this);
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
                Debug.LogError("[电路关卡测试] Prefab 根节点及子物体中找不到 IMinigame 实现。", activeInstance);
                DestroyActiveInstance();
                return;
            }

            minigame.Launch(level, HandleFinish, HandleAbort);
            Debug.Log($"[电路关卡测试] 已启动 {level.name}。按 R 随时重开。", level);
        }

        [ContextMenu("重开当前关卡")]
        public void Restart()
        {
            Debug.Log($"[电路关卡测试] 重开 {(level != null ? level.name : "<未配置>")}。", this);
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
        /// 测试场景只在 Unity Editor 内使用，因此 Play 时可直接扫描策划关卡目录；
        /// 不要求新关卡进入 Resources，也不污染正式小游戏的关卡池配置。
        /// </summary>
        private void RefreshLevelCatalog()
        {
            availableLevels.Clear();

#if UNITY_EDITOR
            var candidates = new List<(string path, LevelDef level)>();
            foreach (var guid in AssetDatabase.FindAssets("t:LevelDef", new[] { LevelFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var candidate = AssetDatabase.LoadAssetAtPath<LevelDef>(path);
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
            Debug.Log($"[电路关卡测试] {level.name} 完成，得分 {lastScore}/100。按 R 重开。", level);
        }

        private void HandleAbort()
        {
            hasResult = false;
            Debug.Log($"[电路关卡测试] {level.name} 已放弃。按 R 重开。", level);
        }

        private void OnGUI()
        {
            if (!panelVisible) return;

            const float width = 480f;
            float listHeight = dropdownOpen ? Mathf.Min(300f, availableLevels.Count * 30f + 8f) : 0f;
            GUILayout.BeginArea(new Rect(12f, 12f, width, 112f + listHeight), GUI.skin.box);

            GUILayout.Label("拉电线关卡测试（C 隐藏面板）", EditorHeaderStyle());
            GUILayout.Label(hasResult
                ? $"当前：{SafeLevelName()}　得分：{lastScore}/100　R 重开"
                : $"当前：{SafeLevelName()}　R 重开");

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

        private static GUIStyle EditorHeaderStyle()
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            style.normal.textColor = Color.white;
            return style;
        }

        private string SafeLevelName() => level != null ? level.name : "<未配置 LevelDef>";

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

            var go = new GameObject("ElectricLevelTestCanvas", typeof(RectTransform), typeof(Canvas),
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

        private void DestroyActiveInstance()
        {
            if (activeInstance == null) return;
            Destroy(activeInstance);
            activeInstance = null;
        }
    }
}

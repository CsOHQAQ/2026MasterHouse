using UnityEngine;
using System.Collections;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace MasterHouse
{
    /// <summary>
    /// Demo 阶段的场景快捷切换面板。
    /// Prefab 放在 Resources 后会自动常驻，也可直接拖入任意场景；L 开关面板。
    /// </summary>
    public sealed class DeveloperSceneSwitcher : MonoBehaviour
    {
        private const string PrefabPath = "Developer/DeveloperSceneSwitcher";
        private const string ElectricScene = "GameTest_Electric";
        private const string CoffeeScene = "GameTest_Coffee";
        private const string OutGameScene = "OutGameTest";

        private static DeveloperSceneSwitcher instance;
        private bool isReturningToOutGame;

        [SerializeField] private GameObject panel;
        [SerializeField] private Button electricButton;
        [SerializeField] private Button coffeeButton;
        [SerializeField] private Button returnButton;
        [SerializeField] private Text electricLabel;
        [SerializeField] private Text coffeeLabel;
        [SerializeField] private Text returnLabel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            EnsureInstance();
        }

        /// <summary>供 Editor Play Mode 桥接调用，兼容关闭 Domain Reload 的快速进 Play 设置。</summary>
        public static void EnsureInstance()
        {
            if (instance != null)
            {
                instance.enabled = true;
                return;
            }

            var prefab = Resources.Load<DeveloperSceneSwitcher>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[开发者场景切换] 缺少 Resources/{PrefabPath} Prefab，改用运行时兜底面板。");
                var fallback = new GameObject("DeveloperSceneSwitcher", typeof(RectTransform), typeof(Canvas));
                fallback.layer = 5;
                fallback.AddComponent<DeveloperSceneSwitcher>().enabled = true;
                return;
            }

            Instantiate(prefab).enabled = true;
        }

        private void Awake()
        {
            // Prefab 的组件启用状态曾被 Unity 保存为关闭；Awake 即使组件关闭仍会执行，
            // 因此在这里自愈，确保热键、按钮绑定与后续 Update 一定可用。
            enabled = true;

            if (instance != null && instance != this)
                Destroy(instance.gameObject);

            instance = this;
            DontDestroyOnLoad(gameObject);
            PrepareUi();
            BindButtons();
            panel.SetActive(false);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.L))
                panel.SetActive(!panel.activeSelf);
        }

        private void PrepareUi()
        {
            var canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 32000;
            canvas.enabled = true;

            var scaler = GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            if (GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();

            if (EventSystem.current == null)
            {
                var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                DontDestroyOnLoad(eventSystem);
            }

            EnsureLayout();
            var panelImage = panel.GetComponent<Image>();
            panelImage.sprite = HouseUIRuntime.WhiteSprite;
            panelImage.color = new Color(0.025f, 0.045f, 0.08f, 0.94f);
            panelImage.type = Image.Type.Sliced;
            StyleButton(electricButton, electricLabel, "电路测试场景");
            StyleButton(coffeeButton, coffeeLabel, "咖啡测试场景");
            StyleButton(returnButton, returnLabel, "返回主场景");
        }

        private void EnsureLayout()
        {
            if (panel != null)
            {
                if (electricButton == null)
                {
                    var buttonTransform = panel.transform.Find("ElectricButton");
                    electricButton = buttonTransform != null ? buttonTransform.GetComponent<Button>() : null;
                }
                if (coffeeButton == null)
                {
                    var buttonTransform = panel.transform.Find("CoffeeButton");
                    coffeeButton = buttonTransform != null ? buttonTransform.GetComponent<Button>() : null;
                }
                if (returnButton == null)
                {
                    var buttonTransform = panel.transform.Find("ReturnButton");
                    returnButton = buttonTransform != null ? buttonTransform.GetComponent<Button>() : null;
                }
                if (electricLabel == null && electricButton != null)
                    electricLabel = electricButton.GetComponentInChildren<Text>(true);
                if (coffeeLabel == null && coffeeButton != null)
                    coffeeLabel = coffeeButton.GetComponentInChildren<Text>(true);
                if (returnLabel == null && returnButton != null)
                    returnLabel = returnButton.GetComponentInChildren<Text>(true);
                if (electricButton != null && coffeeButton != null && returnButton != null) return;
            }

            var panelImage = HouseUIRuntime.Panel(transform, "Panel", new Vector2(0, 1),
                new Vector2(24, -24), new Vector2(270, 184), new Color(0.025f, 0.045f, 0.08f, 0.94f));
            panel = panelImage.gameObject;

            electricButton = CreateButton(panel.transform, "ElectricButton", new Vector2(0, -40));
            electricLabel = electricButton.GetComponentInChildren<Text>();
            coffeeButton = CreateButton(panel.transform, "CoffeeButton", new Vector2(0, -92));
            coffeeLabel = coffeeButton.GetComponentInChildren<Text>();
            returnButton = CreateButton(panel.transform, "ReturnButton", new Vector2(0, -144));
            returnLabel = returnButton.GetComponentInChildren<Text>();
        }

        private static Button CreateButton(Transform parent, string name, Vector2 position)
        {
            var image = HouseUIRuntime.Panel(parent, name, new Vector2(.5f, 1), position,
                new Vector2(238, 44), Color.white);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            HouseUIRuntime.StretchLabel(button.transform, "Label", string.Empty, 23, Color.white,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            return button;
        }

        private static void StyleButton(Button button, Text label, string caption)
        {
            if (button == null)
            {
                Debug.LogError($"[开发者场景切换] 缺少「{caption}」按钮引用。");
                return;
            }

            var image = button.GetComponent<Image>();
            if (image == null)
            {
                Debug.LogError($"[开发者场景切换] 「{caption}」按钮缺少 Image 组件。", button);
                return;
            }
            image.sprite = HouseUIRuntime.WhiteSprite;
            image.color = new Color(0.12f, 0.31f, 0.55f, 0.96f);
            image.type = Image.Type.Sliced;

            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            if (label != null)
            {
                label.font = HouseUIUtil.Font;
                label.fontSize = 23;
                label.fontStyle = FontStyle.Bold;
                label.alignment = TextAnchor.MiddleCenter;
                label.color = Color.white;
                label.raycastTarget = false;
                label.text = caption;
            }
        }

        private void BindButtons()
        {
            // 正式 Prefab 在 On Click() 中序列化了全部三个调用；仅无 Prefab 的兜底布局需要运行时绑定。
            if (electricButton != null && electricButton.onClick.GetPersistentEventCount() == 0)
                electricButton.onClick.AddListener(LoadElectricScene);
            if (coffeeButton != null && coffeeButton.onClick.GetPersistentEventCount() == 0)
                coffeeButton.onClick.AddListener(LoadCoffeeScene);
            if (returnButton != null && returnButton.onClick.GetPersistentEventCount() == 0)
                returnButton.onClick.AddListener(LoadOutGameScene);
        }

        public void LoadElectricScene() => LoadTestScene(ElectricScene);

        public void LoadCoffeeScene() => LoadTestScene(CoffeeScene);

        /// <summary>
        /// 回局外不是普通的换场景：小游戏测试场景是从局外运行态切进来的，
        /// 而局外的 GameManager / HouseUI 都常驻在 DontDestroyOnLoad 中。
        /// 不先清掉它们，OutGameBootstrap 会误认为局外已经启动，留下上一局的 UI 与数据。
        /// </summary>
        public void LoadOutGameScene()
        {
            if (isReturningToOutGame) return;
            StartCoroutine(RestartOutGame());
        }

        private IEnumerator RestartOutGame()
        {
            isReturningToOutGame = true;
            panel.SetActive(false);

            // 保留开发切场器、EventSystem 和全程不断的 BGM；其余局外运行态必须随本局结束。
            // SfxManager 也销毁重建，确保它重新订阅新 GameManager 的事件。
            DestroyPersistent<SfxManager>();
            DestroyPersistent<HouseGmConsole>();
            DestroyPersistent<HouseUIManager>();
            DestroyPersistent<GameManager>();
            DestroyPersistentHouseUiCameras();

            // Destroy 在帧末才真正执行。等一帧后再加载，OutGameBootstrap 才能无歧义地创建一整套新运行态。
            yield return null;
            LoadTestScene(OutGameScene);
            isReturningToOutGame = false;
        }

        private static void DestroyPersistent<T>() where T : Component
        {
            foreach (var component in Resources.FindObjectsOfTypeAll<T>())
            {
                if (component != null && component.gameObject.scene.name == "DontDestroyOnLoad")
                    Destroy(component.gameObject);
            }
        }

        private static void DestroyPersistentHouseUiCameras()
        {
            foreach (var camera in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (camera != null && camera.gameObject.scene.name == "DontDestroyOnLoad" &&
                    camera.gameObject.name == "HouseUICamera")
                    Destroy(camera.gameObject);
            }
        }

        private static void LoadTestScene(string sceneName)
        {
#if UNITY_EDITOR
            var scenePath = $"Assets/Scenes/{sceneName}.unity";
            Debug.Log($"[开发者场景切换] 加载：{scenePath}");
            EditorSceneManager.LoadSceneInPlayMode(scenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError($"[开发者场景切换] Build Settings 中未加入场景：{sceneName}");
                return;
            }

            SceneManager.LoadScene(sceneName);
#endif
        }
    }
}

using UnityEngine;
using UnityEngine.UI;
using F = MasterHouse.HouseUIRuntime; // §16.7 毒点②已断：不再依赖退役中的 OutGameUIFactory

namespace MasterHouse
{
    /// <summary>
    /// GM 调试面板：F1 开关。可增加货币、声望、装饰分（GM 加成项），数值实时同步所有订阅方。
    /// 随进程常驻，在局外 UI 与家具模式中都可用。
    /// </summary>
    public sealed class HouseGmConsole : MonoBehaviour
    {
        /// <summary>「恢复所有状态到初始态」按下后广播；局外 UI 借此重置访客状态并落档。</summary>
        public static event System.Action FullResetRequested;

        /// <summary>过渡桥接：GM 面板读写 Economy 模块（§16.3）；GameManager 由 OutGameBootstrap 保证存在。</summary>
        private static EconomyManager Economy => GameManager.Instance.EconomyManager;

        private GameObject panelRoot;
        private Text valuesLabel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBuild()
        {
            //if (FindObjectOfType<HouseGmConsole>() != null) return;
            //var go = new GameObject("HouseGmConsole", typeof(HouseGmConsole));
            //DontDestroyOnLoad(go);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1)) Toggle();
        }

        private void Toggle()
        {
            if (panelRoot == null) BuildPanel();
            else
            {
                Economy.Changed -= RefreshValues;
                Destroy(panelRoot);
                panelRoot = null;
            }
        }

        private void BuildPanel()
        {
            panelRoot = new GameObject("GmPanel", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            panelRoot.transform.SetParent(transform, false);
            var canvas = panelRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 800;
            var scaler = panelRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;

            var panel = F.Panel(panelRoot.transform, "Body", new Vector2(0, .5f), new Vector2(0, .5f),
                new Vector2(190, 0), new Vector2(330, 500), new Color(.04f, .05f, .07f, .93f));
            F.Outline(panel.gameObject, new Color(.45f, .85f, .8f, .5f), new Vector2(1, -1));
            F.Label(panel.transform, "Title", "GM 面板  <size=13>F1 开关</size>", 22, F.Cyan,
                new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -30), new Vector2(290, 34),
                TextAnchor.MiddleCenter, FontStyle.Bold);
            valuesLabel = F.Label(panel.transform, "Values", string.Empty, 17, F.White,
                new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -84), new Vector2(290, 74), TextAnchor.UpperLeft);

            GmButton(panel.transform, 0, "货币 +1,000", () => Economy.GmAddCurrency(1000));
            GmButton(panel.transform, 1, "货币 +10,000", () => Economy.GmAddCurrency(10000));
            GmButton(panel.transform, 2, "声望 +50", () => Economy.GmAddReputation(50));
            GmButton(panel.transform, 3, "声望 -50", () => Economy.GmAddReputation(-50));
            GmButton(panel.transform, 4, "装饰分 +100", () => Economy.GmAddDecorationBonus(100));
            GmButton(panel.transform, 5, "恢复所有状态到初始态", FullReset);

            Economy.Changed += RefreshValues;
            RefreshValues();
        }

        /// <summary>全量重置：关闭家具模式、流通数值回配置默认、布局回房间默认，并通知局外 UI 收尾。</summary>
        private static void FullReset()
        {
            FurnitureRoomController.CloseActive();
            Economy.ResetToDefaults();
            FurnitureRoomController.ResetSession();
            FullResetRequested?.Invoke();
        }

        private void GmButton(Transform parent, int index, string caption, System.Action action)
        {
            F.Button(parent, "Gm" + index, caption, () => action(),
                new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -160 - index * 52), new Vector2(280, 44),
                new Color(1, 1, 1, .07f), F.White, 18);
        }

        private void RefreshValues()
        {
            if (valuesLabel == null) return;
            valuesLabel.text =
                $"货币　　<color=#D4A46B>◈ {Economy.Data.Currency:N0}</color>\n" +
                $"声望　　<color=#74D8D1>{Economy.Data.Reputation}</color>\n" +
                $"装饰分　<color=#E22D76>{Economy.DecorationScore}</color>";
        }

        private void OnDestroy()
        {
            // 应用退出时各常驻对象的销毁顺序不确定，GameManager 可能先没
            if (GameManager.Instance != null) Economy.Changed -= RefreshValues;
        }
    }
}

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
                new Vector2(190, 0), new Vector2(330, 760), new Color(.04f, .05f, .07f, .93f));
            HouseUIUtil.ApplyPanelSkin(panel); // 全局面板底图（Secondary-bg）
            F.Outline(panel.gameObject, new Color(.45f, .85f, .8f, .5f), new Vector2(1, -1));
            F.Label(panel.transform, "Title", "GM 面板  <size=13>F1 开关</size>", 22, F.Cyan,
                new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -30), new Vector2(290, 34),
                TextAnchor.MiddleCenter, FontStyle.Bold);
            valuesLabel = F.Label(panel.transform, "Values", string.Empty, 17, F.White,
                new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -96), new Vector2(290, 98), TextAnchor.UpperLeft);

            GmButton(panel.transform, 0, "货币 +1,000", () => Economy.GmAddCurrency(1000));
            GmButton(panel.transform, 1, "货币 +10,000", () => Economy.GmAddCurrency(10000));
            GmButton(panel.transform, 2, "声望 +50", () => Economy.GmAddReputation(50));
            GmButton(panel.transform, 3, "声望 -50", () => Economy.GmAddReputation(-50));
            GmButton(panel.transform, 4, "装饰分 +100", () => Economy.GmAddDecorationBonus(100));
            // runSeed 改写入口（访客交付说明 §6.1：存档未落地期间 GM 面板可改写；只影响此后新投放访客的需求）
            GmButton(panel.transform, 5, "访客 runSeed +1", () =>
            {
                var visitor = GameManager.Instance.VisitorManager;
                visitor.SetRunSeed(visitor.Data.RunSeed + 1);
                RefreshValues();
            });
            // 召唤访客：忽略日程立即出现在前台（种族按日程表轮换），验证接待流程用
            GmButton(panel.transform, 6, "召唤一位访客", () =>
            {
                var spawned = GameManager.Instance.VisitorManager.GmSpawnVisitor();
                Debug.Log(spawned != null
                    ? $"[GM] 已召唤访客：{spawned.DisplayName}（实例 {spawned.InstanceId}）"
                    : "[GM] 召唤失败：日程表里没有配置任何种族");
            });
#if UNITY_EDITOR
            // 编辑器专用：给全局仓库注入物资，供访客提交流程验收（局外测试场景与局内隔离，仓库默认为空）
            GmButton(panel.transform, 7, "仓库物资 每种 +5（编辑器）", () =>
            {
                var cargo = GameManager.Instance.PlayerCargo;
                foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:ItemDef"))
                {
                    var item = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemDef>(
                        UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                    if (item != null) cargo.Add(item, 5);
                }
            });
#endif
            // 「给服务中的访客递上仓库首项」那颗临时按钮已随需求交付页面落地删除（2026-08-12）：
            // 提交路径现在由正式界面承担（Hub 点「服务中」的访客 → 交付页拖物品 → 确认交付）。

            GmButton(panel.transform, 8, "恢复所有状态到初始态", FullReset);

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
                new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -206 - index * 52), new Vector2(280, 44),
                new Color(1, 1, 1, .07f), F.White, 18);
        }

        private void RefreshValues()
        {
            if (valuesLabel == null) return;
            valuesLabel.text =
                $"货币　　<color=#D4A46B>◈ {Economy.Data.Currency:N0}</color>\n" +
                $"声望　　<color=#74D8D1>{Economy.Data.Reputation}</color>\n" +
                $"装饰分　<color=#E22D76>{Economy.DecorationScore}</color>\n" +
                $"runSeed　{GameManager.Instance.VisitorManager.Data.RunSeed}";
        }

        private void OnDestroy()
        {
            // 应用退出时各常驻对象的销毁顺序不确定，GameManager 可能先没
            if (GameManager.Instance != null) Economy.Changed -= RefreshValues;
        }
    }
}

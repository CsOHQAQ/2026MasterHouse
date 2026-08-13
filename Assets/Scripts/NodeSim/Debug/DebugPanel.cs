using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MasterHouse
{
    /// <summary>
    /// 调试面板（需求记录·决策 7）：OnGUI 一次性 debug 工具，零 prefab。
    /// 关卡加载/热重载、时间控制、生成列表（含自由模式）、数据展示（PlayerCargo/选中详情）。
    /// 关卡与节点资产用 AssetDatabase 扫描（决策 4：仅编辑器内使用，不考虑打包）。
    /// </summary>
    public class DebugPanel : MonoBehaviour
    {
        private const float PanelWidth = 300f;

        private static readonly float[] TimeScales = { 0.5f, 1f, 2f, 4f, 8f };

        /// <summary>面板屏幕区域（GUI 坐标），世界交互用它判定光标是否被面板挡住。</summary>
        private static Rect activeRect;

        [Tooltip("相机控制（切关后 Focus 对准画布用）；留空自动取主相机上的组件")]
        public CameraController cameraController;

        private GameManager gm;
        private Vector2 scroll;

        /// <summary>关卡列表（同时扫描旧 Level/ 与新 Levels/，策划新建关卡零维护出现）。</summary>
        private readonly List<LevelDef> levelDefs = new List<LevelDef>();

        /// <summary>自由模式的项目全量节点列表。</summary>
        private readonly List<NodeDef> allNodeDefs = new List<NodeDef>();

        private readonly List<KeyValuePair<ItemDef, long>> cargoSnapshot =
            new List<KeyValuePair<ItemDef, long>>();

        /// <summary>IMGUI 不走 EventSystem：世界交互经 InteractionController.IsPointerOverUI 转问这里。</summary>
        public static bool IsPointerOverPanel()
        {
            if (activeRect.width <= 0f) return false;
            var p = Input.mousePosition;
            return activeRect.Contains(new Vector2(p.x, Screen.height - p.y));
        }

        private void Start()
        {
            gm = GameManager.Instance;
            if (gm == null)
            {
                Debug.LogError("场景缺少 GameManager，DebugPanel 停用");
                enabled = false;
                return;
            }
            if (cameraController == null && Camera.main != null)
                cameraController = Camera.main.GetComponent<CameraController>();

            RefreshAssetLists();

            // startLevel 已由 GameManager 自动加载时也对准画布
            if (CurrentLevel != null)
                FocusCanvas(CurrentLevel);
        }

        private void OnDisable()
        {
            activeRect = default;
        }

        /// <summary>玩家正在打开的关卡（同一时刻至多一个）；退出局内后为 null。</summary>
        private LevelData CurrentLevel => gm != null ? gm.LevelManager.ActiveLevel : null;

        private void RefreshAssetLists()
        {
#if UNITY_EDITOR
            levelDefs.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:LevelDef", new[]
                     { "Assets/GameData/Level", "Assets/GameData/Levels" }))
            {
                var def = AssetDatabase.LoadAssetAtPath<LevelDef>(AssetDatabase.GUIDToAssetPath(guid));
                if (def != null) levelDefs.Add(def);
            }
            levelDefs.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            allNodeDefs.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:NodeDef"))
            {
                var def = AssetDatabase.LoadAssetAtPath<NodeDef>(AssetDatabase.GUIDToAssetPath(guid));
                if (def != null) allNodeDefs.Add(def);
            }
            allNodeDefs.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
#endif
        }

        private void OnGUI()
        {
            activeRect = new Rect(8f, 8f, PanelWidth, Screen.height - 16f);
            GUILayout.BeginArea(activeRect, GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);

            DrawLevelSection();
            GUILayout.Space(8f);
            DrawTimeSection();
            GUILayout.Space(8f);
            DrawSpawnSection();
            GUILayout.Space(8f);
            DrawDataSection();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ───────────────── 关卡区 ─────────────────

        private void DrawLevelSection()
        {
            GUILayout.Label("== 关卡 ==");
#if UNITY_EDITOR
            var current = CurrentLevel;
            foreach (var def in levelDefs)
            {
                bool isCurrent = current != null && current.Def == def;
                // 已创建过的关卡即使没打开也常驻（家具照常产出），后缀标出生效状态
                var data = gm.LevelManager.FindLevel(def);
                string suffix = data == null ? "" : data.IsEffective ? "  [生效]" : "  [未生效]";
                if (GUILayout.Button(isCurrent ? $"▶ {def.name}（点击热重载）{suffix}" : $"{def.name}{suffix}"))
                    LoadLevel(def);
            }
            if (levelDefs.Count == 0)
                GUILayout.Label("（Assets/GameData/Level/ 或 Levels/ 下无关卡资产）");

            GUI.enabled = current != null;
            if (GUILayout.Button("关闭关卡（退出局内，数据与产出保留）"))
                gm.LevelManager.CloseLevel();
            GUI.enabled = true;

            if (GUILayout.Button("刷新资产列表"))
                RefreshAssetLists();
#else
            GUILayout.Label("关卡列表仅编辑器内可用（决策 4）");
#endif
        }

        /// <summary>
        /// 点当前关 = **热重载**（决策 3）：丢弃该关全部运行时状态，从 LevelDef 重建。
        /// 点其他关 = **切关**：当前关只是关闭（数据常驻、家具产出继续），再打开目标关。
        /// </summary>
        private void LoadLevel(LevelDef def)
        {
            var lm = gm.LevelManager;
            var current = lm.ActiveLevel;
            if (current != null && current.Def == def)
                lm.DiscardLevel(def);
            else
                lm.CloseLevel();

            var level = lm.OpenLevel(def);
            FocusCanvas(level);
        }

        private void FocusCanvas(LevelData level)
        {
            if (cameraController == null) return;

            bool any = false;
            Vector2Int min = default, max = default;
            foreach (var cell in level.Def.Canvas.CellsAt(level.Def.WorldOrigin))
            {
                if (!any)
                {
                    min = max = cell;
                    any = true;
                    continue;
                }
                min = Vector2Int.Min(min, cell);
                max = Vector2Int.Max(max, cell);
            }
            if (!any) return; // 空画布无从对准

            float s = ViewUtil.GridSize;
            var center = new Vector3(
                (min.x + max.x + 1) * 0.5f * s,
                (min.y + max.y + 1) * 0.5f * s, 0f);
            float halfW = (max.x - min.x + 1) * 0.5f * s;
            float halfH = (max.y - min.y + 1) * 0.5f * s;
            float aspect = (float)Screen.width / Screen.height;
            cameraController.Focus(center, Mathf.Max(halfH, halfW / aspect) * 1.1f);
        }

        // ───────────────── 时间区 ─────────────────

        private void DrawTimeSection()
        {
            GUILayout.Label("== 时间 ==");
            var level = CurrentLevel;
            GUILayout.Label(level != null
                ? $"当前关：{level.Def.name}   Tick：{level.TickCount}   {(level.IsEffective ? "已生效" : "未生效")}"
                : "未打开关卡（相当于退出局内；常驻关卡的家具产出照常推进）");

            GUILayout.BeginHorizontal();
            bool paused = GUILayout.Toggle(gm.IsPaused, "暂停");
            if (paused != gm.IsPaused)
                gm.SetPaused(paused);
            if (GUILayout.Button("单步 tick"))
                gm.StepOneTick();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            foreach (var scale in TimeScales)
            {
                bool active = Mathf.Approximately(gm.TimeScale, scale);
                if (GUILayout.Toggle(active, $"{scale}x", GUI.skin.button) && !active)
                    gm.SetTimeScale(scale);
            }
            GUILayout.EndHorizontal();
        }

        // ───────────────── 生成区 ─────────────────

        private void DrawSpawnSection()
        {
            GUILayout.Label("== 生成 ==");
            // 权限模型：默认真实规则；自由模式只绕过 Controller 层资格校验，合法性仍走 CanPlaceNode
            DebugOptions.FreeMode = GUILayout.Toggle(DebugOptions.FreeMode,
                "自由模式（全部节点、无视上限与预置约束）");

            var level = CurrentLevel;
            if (level == null)
            {
                GUILayout.Label("（先加载关卡）");
                return;
            }
            var placement = PlacementController.Instance;
            if (placement == null)
            {
                GUILayout.Label("（场景缺少 PlacementController）");
                return;
            }
            if (placement.IsPlacing)
                GUILayout.Label("放置中… 左键落子 / Esc·右键退出");

            if (DebugOptions.FreeMode)
            {
#if UNITY_EDITOR
                foreach (var def in allNodeDefs)
                {
                    // 条件节点只能由策划预置，任何模式下都不列出（PlacementController 另有硬拦）
                    if (def.NodeType == ENodeType.Condition) continue;
                    if (GUILayout.Button($"{DisplayName(def)}（{def.NodeType}）"))
                        placement.BeginPlacement(def);
                }
#else
                GUILayout.Label("自由模式列表仅编辑器内可用");
#endif
            }
            else
            {
                foreach (var entry in level.Def.BuildableNodes)
                {
                    if (entry.Node == null) continue;
                    int built = gm.LevelManager.CountNodesOf(level, entry.Node);
                    GUI.enabled = built < entry.MaxCount;
                    if (GUILayout.Button($"{DisplayName(entry.Node)}  {built}/{entry.MaxCount}"))
                        placement.BeginPlacement(entry.Node);
                    GUI.enabled = true;
                }
                if (level.Def.BuildableNodes.Count == 0)
                    GUILayout.Label("（本关无可建节点）");
            }
        }

        // ───────────────── 数据区 ─────────────────

        private void DrawDataSection()
        {
            GUILayout.Label("== 产出（PlayerCargo）==");
            gm.PlayerCargo.GetSnapshot(cargoSnapshot);
            foreach (var pair in cargoSnapshot)
                GUILayout.Label($"{DisplayName(pair.Key)}：{pair.Value}");
            if (cargoSnapshot.Count == 0)
                GUILayout.Label("（暂无产出）");

            GUILayout.Space(8f);
            GUILayout.Label("== 常驻关卡（家具）==");
            foreach (var lv in gm.LevelManager.Levels)
            {
                string furniture = string.IsNullOrEmpty(lv.Def.FurnitureId) ? "未绑定家具" : lv.Def.FurnitureId;
                GUILayout.Label($"{lv.Def.name}（{furniture}）  {(lv.IsEffective ? "生效" : "未生效")}" +
                                $"  产出 {lv.Def.Outputs.Count} 条  Tick {lv.TickCount}");
            }
            if (gm.LevelManager.Levels.Count == 0)
                GUILayout.Label("（尚未创建任何关卡数据）");

            GUILayout.Space(8f);
            GUILayout.Label("== 连线总长 ==");
            var currentLevel = CurrentLevel;
            int linkCount = currentLevel != null ? currentLevel.Links.Count : 0;
            int totalLinkLength = 0;
            if (currentLevel != null)
            {
                foreach (var link in currentLevel.Links)
                    totalLinkLength += link.PathCells.Count;
            }
            GUILayout.Label($"一共连了 {linkCount} 条线，所有线条的总长是 {totalLinkLength} 格");

            GUILayout.Space(8f);
            GUILayout.Label("== 选中详情 ==");
            var ic = InteractionController.Instance;
            if (ic == null)
            {
                GUILayout.Label("（场景缺少 InteractionController）");
                return;
            }

            if (ic.SelectedNode != null)
                DrawNodeDetail(ic.SelectedNode);
            else if (ic.SelectedLink != null)
                DrawLinkDetail(ic.SelectedLink);
            else
                GUILayout.Label("（左键点选节点或链接）");
        }

        private void DrawNodeDetail(NodeData node)
        {
            GUILayout.Label($"节点 #{node.NodeId}  {DisplayName(node.Def)}（{node.Def.NodeType}）");
            GUILayout.Label($"原点 {node.Origin}   可移动:{YesNo(node.CanMove)}   可删除:{YesNo(node.CanDelete)}");
            if (node.IsIllegal)
                GUILayout.Label("状态：位置冲突（冻结中，禁止存档 §4.3）");
            if (node.InputStorage != null)
                GUILayout.Label($"输入暂存：{StorageText(node.InputStorage)}");
            if (node.OutputStorage != null)
                GUILayout.Label($"输出暂存：{StorageText(node.OutputStorage)}");
            if (node.ConditionState != null)
                DrawConditionDetail(node.ConditionState);
            if (node.Def is ProcessorNodeDef processorDef && processorDef.Recipe != null)
                GUILayout.Label(node.RecipeInProgress
                    ? $"配方进度：{node.RecipeProgressTicks}/{processorDef.Recipe.WorkTicks} tick"
                    : "配方：待料");
            for (int i = 0; i < node.Pins.Count; i++)
            {
                var pin = node.Pins[i];
                GUILayout.Label($"Pin{i}  {DirText(pin.RuntimeDirection)}  {DisplayName(pin.RuntimeItemType)}  链接x{pin.Links.Count}");
            }
        }

        /// <summary>条件节点详情：逐条需求的窗口累计 / 需求量与达标情况。</summary>
        private static void DrawConditionDetail(ConditionState state)
        {
            GUILayout.Label($"条件：{(state.Satisfied ? "全部达标" : "未达标")}");
            if (state.Tracks.Count == 0)
            {
                GUILayout.Label("　（未配需求，视为恒达标）");
                return;
            }
            foreach (var track in state.Tracks)
                GUILayout.Label($"　{DisplayName(track.Entry.Item)}  {track.WindowAmount}/{track.Required}" +
                                $"  窗口 {track.WindowTicks} tick{(track.Satisfied ? "  ✓" : "")}");
        }

        private void DrawLinkDetail(LinkData link)
        {
            GUILayout.Label($"链接 #{link.LinkId}  {DisplayName(link.ItemType)}");
            GUILayout.Label($"状态：{StateText(link.State)}   优先级：{link.Priority}"); // 优先级设置入口待定 #16，仅展示
            GUILayout.Label(link.SlotCount > 0
                ? $"持货槽：{DisplayName(link.SlotItem)} x{link.SlotCount}"
                : "持货槽：空");
            GUILayout.Label($"节拍：{link.BeatCounter}/{link.BeatTicks} tick   在途：{link.TransitCounter}/{link.TransitTicks} tick");
            GUILayout.Label($"走线：{link.PathCells.Count} 格");

            if (link.State == ELinkState.Broken &&
                GUILayout.Button("重新布线（删除+同 Pin 重建 A*）"))
                RerouteBrokenLink(link);
        }

        /// <summary>
        /// debug 便利（需求 §三）：对选中的断线链接删除后同 Pin 重建走 A*。
        /// 属面板工具，不违反「无自动修复」原则——玩家玩法仍是手工修线。
        /// </summary>
        private void RerouteBrokenLink(LinkData link)
        {
            var level = CurrentLevel;
            if (level == null) return;

            var fromPin = link.FromPin;
            var toPin = link.ToPin;
            gm.LinkManager.DeleteLink(level, link);
            var recreated = gm.LinkManager.TryCreateLink(level, fromPin, toPin, out var failReason);
            if (recreated == null)
                InteractionController.Instance?.ShowMessage($"重新布线失败：{failReason}");
        }

        // ───────────────── 文案辅助 ─────────────────

        private static string DisplayName(NodeDef def) =>
            def == null ? "?" : string.IsNullOrEmpty(def.DisplayName) ? def.name : def.DisplayName;

        private static string DisplayName(ItemDef item) =>
            item == null ? "未定" : string.IsNullOrEmpty(item.DisplayName) ? item.name : item.DisplayName;

        private static string YesNo(bool value) => value ? "是" : "否";

        private static string StorageText(ItemStorage storage)
        {
            if (storage.Slots.Count == 0) return "空";
            var parts = new List<string>();
            foreach (var slot in storage.Slots) // List 顺序稳定（首次入库序）
                parts.Add(storage.CapPerItem < 0
                    ? $"{DisplayName(slot.Item)} {slot.Count}"
                    : $"{DisplayName(slot.Item)} {slot.Count}/{storage.CapPerItem}");
            return string.Join("  ", parts);
        }

        private static string StateText(ELinkState state)
        {
            switch (state)
            {
                case ELinkState.Idle: return "空闲";
                case ELinkState.InTransit: return "在途";
                case ELinkState.Blocked: return "阻塞（目标无空位）";
                case ELinkState.Broken: return "断线（等待修线）";
                case ELinkState.TypeInvalid: return "类型失效";
                default: return state.ToString();
            }
        }

        private static string DirText(EPinDirection direction)
        {
            switch (direction)
            {
                case EPinDirection.Input: return "输入";
                case EPinDirection.Output: return "输出";
                default: return "未同步";
            }
        }
    }
}

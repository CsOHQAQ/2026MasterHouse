using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 节点放置 Controller（§2/§9）：幽灵预览吸附网格、逐格合法性反馈、点击落子。
    /// 只把输入翻译成对 Manager 的调用，不直接修改任何数据类（§2）。
    /// 建造资格（可建列表/数量上限）在本层校验，自由模式绕过（权限模型）；
    /// 放置合法性永远走 CanPlaceNode，不因自由模式放松。
    /// </summary>
    public class PlacementController : MonoBehaviour
    {
        public static PlacementController Instance { get; private set; }

        private static readonly Color GhostOkColor = new Color(0.4f, 1f, 0.4f, 0.5f);
        private static readonly Color GhostBadColor = new Color(1f, 0.35f, 0.35f, 0.5f);

        private Camera cam;
        private LevelManager levelManager;

        private NodeDef placingDef;

        /// <summary>放置模式中（InteractionController 据此让出左右键）。</summary>
        public bool IsPlacing => placingDef != null;

        private Transform ghostRoot;
        private readonly List<SpriteRenderer> ghostCells = new List<SpriteRenderer>();

        private void Awake()
        {
            Instance = this;
            cam = Camera.main;
        }

        private void Start()
        {
            var gm = GameManager.Instance;
            if (gm == null)
            {
                Debug.LogError("场景缺少 GameManager，PlacementController 停用");
                enabled = false;
                return;
            }
            levelManager = gm.LevelManager;
            levelManager.OnLevelClosed += HandleLevelClosed;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (levelManager != null)
                levelManager.OnLevelClosed -= HandleLevelClosed;
        }

        /// <summary>玩家正在打开的关卡；未进入局内时为 null。</summary>
        private LevelData CurrentLevel => levelManager != null ? levelManager.ActiveLevel : null;

        /// <summary>进入放置模式（调试面板的生成列表调用）。</summary>
        public void BeginPlacement(NodeDef def)
        {
            var level = CurrentLevel;
            if (def == null || level == null) return;

            // 条件节点只能由策划在 LevelDef.PresetNodes 预置；按类型硬拦，自由模式也不放行
            if (def.NodeType == ENodeType.Condition)
            {
                InteractionController.Instance?.ShowMessage("条件节点只能在关卡中预置，不能手动摆放");
                return;
            }

            if (!DebugOptions.FreeMode && !levelManager.CanBuild(level, def))
            {
                InteractionController.Instance?.ShowMessage("无法建造：不在本关可建列表或已达数量上限");
                return;
            }

            CancelPlacement();
            placingDef = def;
            BuildGhost(def);
        }

        public void CancelPlacement()
        {
            placingDef = null;
            if (ghostRoot != null)
                Destroy(ghostRoot.gameObject);
            ghostRoot = null;
            ghostCells.Clear();
        }

        private void Update()
        {
            if (!IsPlacing) return;

            var level = CurrentLevel;
            if (level == null)
            {
                CancelPlacement();
                return;
            }
            if (cam == null)
            {
                cam = Camera.main;
                if (cam == null) return;
            }

            // Esc / 右键：取消放置（放置模式中右键优先级高于删线与相机平移）
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                CancelPlacement();
                return;
            }

            var origin = GridPicker.ScreenToCell(cam, Input.mousePosition);
            bool legal = levelManager.CanPlaceNode(level, placingDef, origin);

            // 幽灵吸附 + 合法性着色
            ghostRoot.position = ViewUtil.CellCorner(origin);
            var color = legal ? GhostOkColor : GhostBadColor;
            foreach (var sr in ghostCells)
                sr.color = color;

            if (Input.GetMouseButtonDown(0) && !InteractionController.IsPointerOverUI())
            {
                if (!legal)
                {
                    InteractionController.Instance?.ShowMessage("此处无法放置：越界或与节点/连线重叠");
                    return;
                }
                // 连续摆放时数量上限可能中途到顶：每次落子前复查资格
                if (!DebugOptions.FreeMode && !levelManager.CanBuild(level, placingDef))
                {
                    InteractionController.Instance?.ShowMessage("已达数量上限");
                    CancelPlacement();
                    return;
                }
                levelManager.PlaceNode(level, placingDef, origin);
                // 保持放置模式便于连续摆放，Esc/右键退出
            }
        }

        private void BuildGhost(NodeDef def)
        {
            ghostRoot = new GameObject($"放置幽灵_{def.name}").transform;
            ghostRoot.SetParent(transform, false);
            float s = ViewUtil.GridSize;
            foreach (var g in def.Shape.Grids)
                ghostCells.Add(VisualAssets.CreateSpriteSquare(ghostRoot, "幽灵格",
                    new Vector3((g.DeltaPosition.x + 0.5f) * s, (g.DeltaPosition.y + 0.5f) * s, 0f),
                    s * 0.98f, GhostOkColor, SortingOrders.DragLine));
        }

        private void HandleLevelClosed(LevelData level) => CancelPlacement();
    }
}

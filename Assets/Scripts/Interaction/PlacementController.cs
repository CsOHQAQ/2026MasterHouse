using UnityEngine;

namespace MasterPotion
{
    /// <summary>
    /// 放置模式：半透明幽灵按画布单元格吸附跟随鼠标，
    /// 必须完整落在画布内且不与其他节点重叠才能放置（非法时幽灵变红）。
    /// 左键放置，右键/Esc 取消。
    /// </summary>
    public class PlacementController : MonoBehaviour
    {
        public static PlacementController Instance { get; private set; }

        /// <summary>放置发生的帧号，供 InteractionController 跳过同帧点击。</summary>
        public static int JustPlacedFrame = -1;

        public bool IsPlacing => currentDef != null;

        private NodeDef currentDef;
        private Transform ghost;
        private SpriteRenderer ghostSprite;
        private Camera cam;

        private void Awake()
        {
            Instance = this;
            cam = Camera.main;
        }

        public void BeginPlacement(NodeDef def)
        {
            CancelPlacement();
            if (BoardEditController.Instance != null) BoardEditController.Instance.SetEditing(false);
            currentDef = def;

            var go = new GameObject("PlacementGhost");
            ghostSprite = go.AddComponent<SpriteRenderer>();
            ghostSprite.sprite = VisualAssets.WhiteSprite;
            ghostSprite.sharedMaterial = VisualAssets.UnlitMaterial;
            ghostSprite.sortingOrder = SortingOrders.DragLine;
            go.transform.localScale = new Vector3(def.WorldSize.x, def.WorldSize.y, 1f);
            ghost = go.transform;
        }

        private void Update()
        {
            if (!IsPlacing) return;

            Vector3 world = cam.ScreenToWorldPoint(Input.mousePosition);
            var origin = BoardGrid.SnapOrigin(world, currentDef.gridSize);
            ghost.position = BoardGrid.AreaCenter(origin, currentDef.gridSize);

            bool valid = BoardGrid.Instance != null &&
                         BoardGrid.Instance.CanPlace(origin, currentDef.gridSize);
            var c = currentDef.cardColor;
            ghostSprite.color = valid
                ? new Color(c.r, c.g, c.b, 0.4f)
                : new Color(0.9f, 0.25f, 0.25f, 0.4f);

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                CancelPlacement();
                return;
            }

            if (Input.GetMouseButtonDown(0) && !InteractionController.IsPointerOverUI() && valid)
            {
                NodeFactory.CreateNodeAt(currentDef, origin);
                JustPlacedFrame = Time.frameCount;
                CancelPlacement();
            }
        }

        public void CancelPlacement()
        {
            currentDef = null;
            if (ghost != null) Destroy(ghost.gameObject);
            ghost = null;
            ghostSprite = null;
        }
    }
}

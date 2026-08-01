using UnityEngine;
/*
namespace MasterPotion
{
    /// <summary>放置模式：半透明幽灵跟随鼠标（0.5 网格吸附），左键放置，右键/Esc 取消。</summary>
    public class PlacementController : MonoBehaviour
    {
        public static PlacementController Instance { get; private set; }

        /// <summary>放置发生的帧号，供 InteractionController 跳过同帧点击。</summary>
        public static int JustPlacedFrame = -1;

        public bool IsPlacing => currentDef != null;

        private NodeDef currentDef;
        private Transform ghost;
        private Camera cam;

        private void Awake()
        {
            Instance = this;
            cam = Camera.main;
        }

        public void BeginPlacement(NodeDef def)
        {
            CancelPlacement();
            currentDef = def;

            var go = new GameObject("PlacementGhost");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = VisualAssets.WhiteSprite;
            sr.sharedMaterial = VisualAssets.UnlitMaterial;
            sr.color = new Color(def.cardColor.r, def.cardColor.g, def.cardColor.b, 0.4f);
            sr.sortingOrder = SortingOrders.DragLine;
            go.transform.localScale = new Vector3(def.size.x, def.size.y, 1f);
            ghost = go.transform;
        }

        private void Update()
        {
            if (!IsPlacing) return;

            Vector3 world = cam.ScreenToWorldPoint(Input.mousePosition);
            world.z = 0f;
            world.x = Mathf.Round(world.x * 2f) * 0.5f;
            world.y = Mathf.Round(world.y * 2f) * 0.5f;
            ghost.position = world;

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                CancelPlacement();
                return;
            }

            if (Input.GetMouseButtonDown(0) && !InteractionController.IsPointerOverUI())
            {
                NodeFactory.CreateNode(currentDef, world);
                JustPlacedFrame = Time.frameCount;
                CancelPlacement();
            }
        }

        public void CancelPlacement()
        {
            currentDef = null;
            if (ghost != null) Destroy(ghost.gameObject);
            ghost = null;
        }
    }
}
*/
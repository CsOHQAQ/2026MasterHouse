using System;
using UnityEngine;

namespace MasterPotion
{
    /// <summary>
    /// 画布编辑模式：运行时动态增删画布单元格。
    /// 左键按下时根据起点格决定本次操作（空格 -> 连续添加；已有格 -> 连续移除），拖动可连续绘制。
    /// 被节点占用的格子拒绝移除；连线会随画布变化自动重新走线。Esc 或再次点击按钮退出。
    /// </summary>
    public class BoardEditController : MonoBehaviour
    {
        public static BoardEditController Instance { get; private set; }

        public bool IsEditing { get; private set; }

        /// <summary>编辑模式开关变化时通知（供 UI 刷新按钮状态）。</summary>
        public event Action<bool> OnModeChanged;

        private Camera cam;
        private SpriteRenderer cursor;
        private bool painting;
        private bool paintAdds; // 本次拖动是添加还是移除

        private void Awake()
        {
            Instance = this;
            cam = Camera.main;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Toggle() => SetEditing(!IsEditing);

        public void SetEditing(bool editing)
        {
            if (IsEditing == editing) return;
            IsEditing = editing;
            painting = false;

            if (editing && PlacementController.Instance != null)
                PlacementController.Instance.CancelPlacement();

            if (!editing && cursor != null)
            {
                Destroy(cursor.gameObject);
                cursor = null;
            }
            OnModeChanged?.Invoke(editing);
        }

        private void Update()
        {
            if (!IsEditing) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                SetEditing(false);
                return;
            }

            var board = BoardGrid.Instance;
            if (board == null) return;

            Vector2 world = cam.ScreenToWorldPoint(Input.mousePosition);
            var cell = BoardGrid.WorldToCell(world);
            bool overUI = InteractionController.IsPointerOverUI();

            UpdateCursor(board, cell, overUI);

            if (Input.GetMouseButtonDown(0) && !overUI)
            {
                painting = true;
                paintAdds = !board.HasCell(cell);
            }
            if (!Input.GetMouseButton(0)) painting = false;

            if (painting && !overUI)
            {
                if (paintAdds) board.AddCell(cell);
                else board.TryRemoveCell(cell);
            }
        }

        private void UpdateCursor(BoardGrid board, Vector2Int cell, bool overUI)
        {
            if (cursor == null)
            {
                var go = new GameObject("BoardEditCursor");
                cursor = go.AddComponent<SpriteRenderer>();
                cursor.sprite = VisualAssets.WhiteSprite;
                cursor.sharedMaterial = VisualAssets.UnlitMaterial;
                cursor.sortingOrder = SortingOrders.CellCursor;
                go.transform.localScale = new Vector3(0.96f, 0.96f, 1f);
            }

            cursor.enabled = !overUI;
            cursor.transform.position = BoardGrid.CellCenter(cell);

            if (!board.HasCell(cell))
                cursor.color = new Color(0.3f, 0.9f, 0.4f, 0.4f);   // 可添加
            else if (board.IsOccupied(cell))
                cursor.color = new Color(0.9f, 0.25f, 0.25f, 0.4f); // 被节点占用，不可移除
            else
                cursor.color = new Color(0.95f, 0.8f, 0.3f, 0.4f);  // 可移除
        }
    }
}

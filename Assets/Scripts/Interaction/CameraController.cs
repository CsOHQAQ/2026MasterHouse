using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 相机 Controller（§9）：右键/中键拖动平移，滚轮以鼠标位置为锚点平滑缩放。
    /// 多小关同场景的聚焦切换交互待定 #13。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraController : MonoBehaviour
    {
        [Header("缩放")]
        [Tooltip("每滚一格的缩放比例（0.1 = 缩放 10%）")]
        [SerializeField] private float zoomStep = 0.1f;

        [Tooltip("正交尺寸下限（越小看得越近）")]
        [SerializeField] private float minOrthoSize = 2f;

        [Tooltip("正交尺寸上限（越大看得越远）")]
        [SerializeField] private float maxOrthoSize = 20f;

        [Tooltip("缩放平滑速度，越大越快到位")]
        [SerializeField] private float zoomSmoothSpeed = 12f;

        private Camera cam;

        /// <summary>缩放目标值，滚轮改它，每帧向它平滑逼近</summary>
        private float targetOrthoSize;

        /// <summary>是否正在拖动平移</summary>
        private bool isDragging;

        /// <summary>拖动起点的世界坐标（拖动期间保持该点始终在鼠标下方）</summary>
        private Vector3 dragOriginWorld;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            if (!cam.orthographic)
                Debug.LogWarning("CameraController 依赖正交相机做缩放，请检查相机设置");
            targetOrthoSize = cam.orthographicSize;
        }

        private void Update()
        {
            HandleZoom();
            HandlePan();

            // TODO 待定 #13：多小关同场景的聚焦切换——
            // 聚焦/离开小关时通知 LevelManager Load/Unload，平移边界也随小关布局定案后补充。
        }

        /// <summary>滚轮缩放：改目标值并平滑逼近，同时保持鼠标下方的世界点不动</summary>
        private void HandleZoom()
        {
            float scroll = Input.mouseScrollDelta.y;
            if (scroll != 0f && !InteractionController.IsPointerOverUI())
            {
                // 乘法缩放：每格固定比例，近处细腻远处大步
                targetOrthoSize = Mathf.Clamp(
                    targetOrthoSize * (1f - scroll * zoomStep),
                    minOrthoSize, maxOrthoSize);
            }

            if (Mathf.Approximately(cam.orthographicSize, targetOrthoSize))
                return;

            // 缩放前后各取一次鼠标世界坐标，用差值回移相机，实现以鼠标为锚点
            Vector3 mouseWorldBefore = cam.ScreenToWorldPoint(Input.mousePosition);

            // 指数平滑（与帧率无关）；相机属表现层，可用 Time.deltaTime（§11 仅约束逻辑层）
            cam.orthographicSize = Mathf.Lerp(
                cam.orthographicSize, targetOrthoSize,
                1f - Mathf.Exp(-zoomSmoothSpeed * Time.deltaTime));
            if (Mathf.Abs(cam.orthographicSize - targetOrthoSize) < 0.001f)
                cam.orthographicSize = targetOrthoSize;

            Vector3 mouseWorldAfter = cam.ScreenToWorldPoint(Input.mousePosition);
            Vector3 offset = mouseWorldBefore - mouseWorldAfter;
            offset.z = 0f;
            transform.position += offset;
        }

        /// <summary>右键/中键拖动平移：拖动期间保持起点世界坐标始终位于鼠标下方</summary>
        private void HandlePan()
        {
            bool panHeld = Input.GetMouseButton(1) || Input.GetMouseButton(2);

            if (!isDragging)
            {
                bool panPressed = Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2);
                if (panPressed && !InteractionController.IsPointerOverUI())
                {
                    isDragging = true;
                    dragOriginWorld = cam.ScreenToWorldPoint(Input.mousePosition);
                }
                return;
            }

            if (!panHeld)
            {
                isDragging = false;
                return;
            }

            Vector3 offset = dragOriginWorld - cam.ScreenToWorldPoint(Input.mousePosition);
            offset.z = 0f;
            transform.position += offset;
        }
    }
}
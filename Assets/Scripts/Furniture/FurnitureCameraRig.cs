using UnityEngine;
using UnityEngine.EventSystems;

namespace MasterHouse
{
    /// <summary>
    /// 家具模式相机：透视相机围绕画面中心做鼠标视差微转 + 缓慢呼吸漂移；
    /// 支持滚轮缩放（朝鼠标位置，1~3.5 倍）与右键/中键拖拽平移，视野钳制在场景图范围内。
    /// 初始视角 = 完整显示整幅背景（宽高同时装下，不放大）。
    /// 场景内容全部位于同一平面附近，只有相机在动，因此背景像素对齐不受影响。
    /// </summary>
    public sealed class FurnitureCameraRig : MonoBehaviour
    {
        private const float MaxYawDegrees = 2.2f;
        private const float MaxPitchDegrees = 1.5f;
        private const float FollowSpeed = 4f;
        private const float MinZoom = 1f;
        private const float MaxZoom = 3.5f;
        private const float ZoomStep = .16f;

        public Camera Camera { get; private set; }

        private Vector3 pivot;          // 场景中心（世界坐标）
        private float baseDistance;     // zoom=1 时完整装下整幅背景的距离
        private float halfWidth;        // 场景半宽（世界单位）
        private float halfHeight;       // 场景半高（世界单位）
        private float zoom = 1f;
        private Vector3 panOffset;
        private Vector2 current;
        private Vector3 lastPanMouse;
        private bool panning;

        public void Init(Vector3 scenePivot, Vector2 sceneHalfExtents, float fieldOfView)
        {
            pivot = scenePivot;
            halfWidth = sceneHalfExtents.x;
            halfHeight = sceneHalfExtents.y;
            Camera = gameObject.AddComponent<Camera>();
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = new Color(.014f, .014f, .022f, 1f);
            Camera.fieldOfView = fieldOfView;
            Camera.nearClipPlane = .5f;
            Camera.farClipPlane = 220f;
            Camera.depth = 60f;
            // 初始 = 原背景完整可见：取「装下全高」与「装下全宽」的较大距离
            var tan = Mathf.Tan(fieldOfView * .5f * Mathf.Deg2Rad);
            var fitHeight = halfHeight / tan;
            var fitWidth = halfWidth / (tan * Mathf.Max(.01f, Camera.aspect));
            baseDistance = Mathf.Max(fitHeight, fitWidth);
            transform.position = pivot + new Vector3(0f, 0f, -baseDistance);
        }

        private void LateUpdate()
        {
            if (Camera == null) return;
            HandleZoom();
            HandlePan();
            ClampPan();

            var mouseX = Screen.width > 0 ? Mathf.Clamp(Input.mousePosition.x / Screen.width - .5f, -.5f, .5f) * 2f : 0f;
            var mouseY = Screen.height > 0 ? Mathf.Clamp(Input.mousePosition.y / Screen.height - .5f, -.5f, .5f) * 2f : 0f;
            var idleYaw = Mathf.Sin(Time.time * .22f) * .35f;
            var idlePitch = Mathf.Cos(Time.time * .17f) * .25f;
            var target = new Vector2(-mouseY * MaxPitchDegrees + idlePitch, mouseX * MaxYawDegrees + idleYaw);
            current = Vector2.Lerp(current, target, Time.deltaTime * FollowSpeed);
            var rotation = Quaternion.Euler(current.x, current.y, 0f);
            transform.position = pivot + panOffset + rotation * new Vector3(0f, 0f, -baseDistance / zoom);
            transform.rotation = rotation;
        }

        /// <summary>滚轮缩放：朝鼠标下的场景点靠近/远离，缩到 1 倍时回正平移。</summary>
        private void HandleZoom()
        {
            var scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) < .01f) return;
            var previous = zoom;
            zoom = Mathf.Clamp(zoom * (1f + scroll * ZoomStep), MinZoom, MaxZoom);
            if (Mathf.Approximately(previous, zoom)) return;
            // 保持鼠标指向的平面点大致不动：把平移中心向该点靠拢
            var mouseWorld = PlanePointUnderMouse();
            panOffset += (mouseWorld - (pivot + panOffset)) * (1f - previous / zoom);
        }

        /// <summary>右键/中键拖拽平移（左键留给家具拖拽）。</summary>
        private void HandlePan()
        {
            var held = Input.GetMouseButton(1) || Input.GetMouseButton(2);
            if (held && !panning)
            {
                // 起手落在 HUD 上时不平移：右键在收纳栏槽位上是「出售」手势（家具库存说明 §5.5），
                // 不挡的话右键出售会顺带把镜头也拽走。只拦**起手**——平移中划过 HUD 照常继续
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
                panning = true;
                lastPanMouse = Input.mousePosition;
            }
            else if (!held)
            {
                panning = false;
            }
            if (!panning || Screen.height <= 0) return;
            var delta = Input.mousePosition - lastPanMouse;
            lastPanMouse = Input.mousePosition;
            var worldPerPixel = VisibleHalfHeight() * 2f / Screen.height;
            panOffset -= new Vector3(delta.x, delta.y, 0f) * worldPerPixel;
        }

        /// <summary>把平移钳在场景图范围内（缩放不足以露边时回正到 0）。</summary>
        private void ClampPan()
        {
            var visibleHalfH = VisibleHalfHeight();
            var visibleHalfW = visibleHalfH * Camera.aspect;
            var limitX = Mathf.Max(0f, halfWidth - visibleHalfW);
            var limitY = Mathf.Max(0f, halfHeight - visibleHalfH);
            panOffset = new Vector3(
                Mathf.Clamp(panOffset.x, -limitX, limitX),
                Mathf.Clamp(panOffset.y, -limitY, limitY), 0f);
        }

        private float VisibleHalfHeight() =>
            baseDistance / zoom * Mathf.Tan(Camera.fieldOfView * .5f * Mathf.Deg2Rad);

        /// <summary>鼠标射线与场景平面（z = pivot.z）的交点。</summary>
        private Vector3 PlanePointUnderMouse()
        {
            var ray = Camera.ScreenPointToRay(Input.mousePosition);
            if (Mathf.Abs(ray.direction.z) < 1e-5f) return pivot + panOffset;
            return ray.GetPoint((pivot.z - ray.origin.z) / ray.direction.z);
        }
    }
}

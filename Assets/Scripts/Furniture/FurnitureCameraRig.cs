using UnityEngine;

namespace MasterPotion
{
    /// <summary>
    /// 家具模式相机：透视相机围绕画面中心做鼠标视差微转 + 缓慢呼吸漂移。
    /// 场景内容全部位于同一平面附近，只有相机在动，因此背景像素对齐不受影响，
    /// 微小的旋转让平面产生 3D 透视感，家具的分层 Z 偏移带来视差。
    /// </summary>
    public sealed class FurnitureCameraRig : MonoBehaviour
    {
        private const float MaxYawDegrees = 2.2f;
        private const float MaxPitchDegrees = 1.5f;
        private const float FollowSpeed = 4f;

        public Camera Camera { get; private set; }

        private Vector3 pivot;
        private float distance;
        private Vector2 current;

        public void Init(Vector3 pivot, float distance, float fieldOfView)
        {
            this.pivot = pivot;
            this.distance = distance;
            Camera = gameObject.AddComponent<Camera>();
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = new Color(.014f, .014f, .022f, 1f);
            Camera.fieldOfView = fieldOfView;
            Camera.nearClipPlane = .5f;
            Camera.farClipPlane = 120f;
            Camera.depth = 60f;
            transform.position = pivot + new Vector3(0f, 0f, -distance);
        }

        private void LateUpdate()
        {
            if (Camera == null) return;
            var mouseX = Screen.width > 0 ? Mathf.Clamp(Input.mousePosition.x / Screen.width - .5f, -.5f, .5f) * 2f : 0f;
            var mouseY = Screen.height > 0 ? Mathf.Clamp(Input.mousePosition.y / Screen.height - .5f, -.5f, .5f) * 2f : 0f;
            var idleYaw = Mathf.Sin(Time.time * .22f) * .35f;
            var idlePitch = Mathf.Cos(Time.time * .17f) * .25f;
            var target = new Vector2(-mouseY * MaxPitchDegrees + idlePitch, mouseX * MaxYawDegrees + idleYaw);
            current = Vector2.Lerp(current, target, Time.deltaTime * FollowSpeed);
            var rotation = Quaternion.Euler(current.x, current.y, 0f);
            transform.position = pivot + rotation * new Vector3(0f, 0f, -distance);
            transform.rotation = rotation;
        }
    }
}

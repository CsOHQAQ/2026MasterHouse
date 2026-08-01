using UnityEngine;
/*
namespace MasterHouse
{
    /// <summary>右键/中键拖动平移，滚轮以鼠标位置为锚点缩放。</summary>
    [RequireComponent(typeof(Camera))]
    public class CameraController : MonoBehaviour
    {
        public float zoomStep = 1.2f;
        public float minZoom = 3f;
        public float maxZoom = 16f;

        private Camera cam;
        private bool panning;
        private Vector3 lastMouse;

        private void Awake() => cam = GetComponent<Camera>();

        private void Update()
        {
            bool panHeld = Input.GetMouseButton(1) || Input.GetMouseButton(2);
            if (panHeld && !panning)
            {
                panning = true;
                lastMouse = Input.mousePosition;
            }
            else if (!panHeld)
            {
                panning = false;
            }

            if (panning)
            {
                var prev = cam.ScreenToWorldPoint(lastMouse);
                var cur = cam.ScreenToWorldPoint(Input.mousePosition);
                transform.position += prev - cur;
                lastMouse = Input.mousePosition;
            }

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f && !InteractionController.IsPointerOverUI())
            {
                var before = cam.ScreenToWorldPoint(Input.mousePosition);
                cam.orthographicSize = Mathf.Clamp(cam.orthographicSize - scroll * zoomStep, minZoom, maxZoom);
                var after = cam.ScreenToWorldPoint(Input.mousePosition);
                transform.position += before - after;
            }
        }
    }
}
*/
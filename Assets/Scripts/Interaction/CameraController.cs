using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 相机 Controller（§9）：视角平移缩放；多小关同场景的聚焦切换交互待定 #13。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraController : MonoBehaviour
    {
        private Camera cam;

        private void Awake()
        {
            cam = GetComponent<Camera>();
        }

        private void Update()
        {
            // TODO：
            // - 右键/中键拖动平移，滚轮以鼠标位置为锚点缩放
            // - 聚焦/离开小关时通知 LevelManager Load/Unload（待定 #13）
        }
    }
}
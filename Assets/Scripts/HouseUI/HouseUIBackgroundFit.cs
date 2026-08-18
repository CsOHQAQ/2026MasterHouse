using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 整屏底图的「铺满不变形」（2026-08-18 跨平台修复）：
    /// 画布用 Expand 之后，非 16:9 屏（Mac 常见的 16:10）上画布会比 1920×1080 更高，
    /// 拉伸铺满的底图就会被竖向拉长。这里改成按贴图原比例**铺满并裁掉多出来的部分**——
    /// 与摄影里的 cover 一个意思，宁可裁边也不变形。
    ///
    /// 非布局件，运行时挂：只改 uvRect（取哪一块贴图），不动 RectTransform 的位置尺寸（§16.2）。
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public sealed class HouseUIBackgroundFit : MonoBehaviour
    {
        private RawImage image;
        private Vector2 lastSize;
        private Texture lastTexture;

        /// <summary>给一张整屏底图挂上 cover 适配；已挂过则复用。</summary>
        public static void Apply(RawImage background)
        {
            if (background == null) return;
            var fit = background.GetComponent<HouseUIBackgroundFit>();
            if (fit == null) fit = background.gameObject.AddComponent<HouseUIBackgroundFit>();
            fit.Refresh();
        }

        private void Awake() => image = GetComponent<RawImage>();

        private void OnEnable() => Refresh();

        private void Update()
        {
            // 分辨率/窗口模式变化、换底图都要重算；每帧只比对两个值，代价可忽略
            var rect = ((RectTransform)transform).rect;
            var size = new Vector2(rect.width, rect.height);
            if (size == lastSize && image != null && image.texture == lastTexture) return;
            Refresh();
        }

        private void Refresh()
        {
            if (image == null) image = GetComponent<RawImage>();
            if (image == null || image.texture == null) return;
            var rect = ((RectTransform)transform).rect;
            if (rect.width <= 1f || rect.height <= 1f) return;
            lastSize = new Vector2(rect.width, rect.height);
            lastTexture = image.texture;

            var boxAspect = rect.width / rect.height;
            var texAspect = image.texture.width / (float)image.texture.height;
            if (boxAspect >= texAspect)
            {
                // 框比图更宽：贴图横向铺满，上下各裁掉一点
                var height = texAspect / boxAspect;
                image.uvRect = new Rect(0f, (1f - height) * .5f, 1f, height);
            }
            else
            {
                var width = boxAspect / texAspect;
                image.uvRect = new Rect((1f - width) * .5f, 0f, width, 1f);
            }
        }
    }
}

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 测量访客立绘的脚底留白（2026-08-18）：立绘四周带透明留白，帧底并不是脚底，
    /// 直接把帧底压在地面坐标上访客就会浮起来。这里逐张量出「每帧底部留白占帧高的比例」，
    /// 写进同名 JSON 的 footPadding，运行时 <see cref="OutGameVisitorActor"/> 拿它当 pivot.y。
    ///
    /// 各张图差别很大（实测 0 ~ 0.14），所以必须按图存、不能用一个常量。
    /// 直接读 PNG 字节自己解码，不依赖贴图的 Read/Write 开关（那个开关会让运行时多占一份内存）。
    /// 素材换了就重跑一次这个菜单。
    /// </summary>
    public static class VisitorSheetFootPaddingTool
    {
        private const string SheetDir = "Assets/Resources/OutGameUI/Visitors";
        /// <summary>低于这个 alpha 视为透明（抗锯齿边缘不算脚）。</summary>
        private const byte AlphaThreshold = 24;

        [MenuItem("Tools/MasterHouse/访客/测量访客立绘脚底留白")]
        public static void Measure()
        {
            var written = 0;
            foreach (var path in Directory.GetFiles(SheetDir, "*.png"))
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(File.ReadAllBytes(path)))
                {
                    Debug.LogWarning("[访客立绘] 解码失败：" + path);
                    Object.DestroyImmediate(texture);
                    continue;
                }
                var jsonPath = Path.ChangeExtension(path, ".json");
                var sheet = ReadSheet(jsonPath, texture);
                sheet.footPadding = MeasureFootPadding(texture, sheet);
                File.WriteAllText(jsonPath, JsonUtility.ToJson(sheet));
                Object.DestroyImmediate(texture);
                written++;
                Debug.Log($"[访客立绘] {Path.GetFileName(path)} 脚底留白 {sheet.footPadding:F3}");
            }
            AssetDatabase.Refresh();
            Debug.Log($"[访客立绘] 已测量并写回 {written} 张的 footPadding。");
        }

        private static OutGameVisitorSheet ReadSheet(string jsonPath, Texture2D texture)
        {
            if (File.Exists(jsonPath))
            {
                var existing = JsonUtility.FromJson<OutGameVisitorSheet>(File.ReadAllText(jsonPath));
                if (existing != null && existing.columns > 0 && existing.rows > 0 && existing.frameCount > 0)
                    return existing;
            }
            // 没有 JSON = 单帧定格图（与运行时 Load 的兜底口径一致）
            return new OutGameVisitorSheet
            {
                frameWidth = texture.width,
                frameHeight = texture.height,
                columns = 1,
                rows = 1,
                frameCount = 1,
            };
        }

        /// <summary>
        /// 取各帧底部留白的**最小值**：只要有一帧的脚探得更低，就按那一帧算，
        /// 否则动画里最低的那一帧会陷进地面。
        /// </summary>
        private static float MeasureFootPadding(Texture2D texture, OutGameVisitorSheet sheet)
        {
            var pixels = texture.GetPixels32();
            var width = texture.width;
            var height = texture.height;
            var frameWidth = width / (float)sheet.columns;
            var frameHeight = height / (float)sheet.rows;
            var paddings = new List<float>();
            for (var index = 0; index < sheet.frameCount; index++)
            {
                var col = index % sheet.columns;
                var row = index / sheet.columns;
                if (row >= sheet.rows) break;
                var x0 = Mathf.RoundToInt(col * frameWidth);
                var x1 = Mathf.RoundToInt((col + 1) * frameWidth);
                // 图片行序自上而下，纹理像素自下而上：帧的底边在纹理里是较小的 y
                var yTop = Mathf.RoundToInt(height - row * frameHeight);
                var yBottom = Mathf.RoundToInt(height - (row + 1) * frameHeight);
                var found = -1;
                for (var y = yBottom; y < yTop && found < 0; y++)
                    for (var x = x0; x < x1; x++)
                        if (pixels[y * width + x].a > AlphaThreshold) { found = y; break; }
                if (found < 0) continue; // 空帧不参与
                paddings.Add((found - yBottom) / frameHeight);
            }
            if (paddings.Count == 0) return 0f;
            var min = paddings[0];
            foreach (var padding in paddings) min = Mathf.Min(min, padding);
            return Mathf.Clamp(min, 0f, .4f);
        }
    }
}

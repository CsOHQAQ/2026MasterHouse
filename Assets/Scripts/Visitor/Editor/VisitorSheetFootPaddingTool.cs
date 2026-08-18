using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 测量访客立绘的四周留白（2026-08-18）：立绘上下都带透明留白，
    /// 帧底不是脚底、帧顶不是头顶。逐张量出两个比例写进同名 JSON：
    /// footPadding（底部留白）运行时当演员的 pivot.y，脚才落在地面点上；
    /// headPadding（顶部留白）用来下压名牌与气泡的挂点，它们才贴着头而不是浮在半空。
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

        [MenuItem("Tools/MasterHouse/访客/测量访客立绘留白")]
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
                sheet.footPadding = MeasurePadding(texture, sheet, fromBottom: true);
                sheet.headPadding = MeasurePadding(texture, sheet, fromBottom: false);
                File.WriteAllText(jsonPath, JsonUtility.ToJson(sheet));
                Object.DestroyImmediate(texture);
                written++;
                Debug.Log($"[访客立绘] {Path.GetFileName(path)} 脚底 {sheet.footPadding:F3} 头顶 {sheet.headPadding:F3}");
            }
            AssetDatabase.Refresh();
            Debug.Log($"[访客立绘] 已测量并写回 {written} 张的留白比例。");
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
        /// 取各帧留白的**最小值**：只要有一帧探得更远，就按那一帧算，
        /// 否则动画里最极端的那一帧会陷进地面（脚）或被名牌压住（头）。
        /// </summary>
        private static float MeasurePadding(Texture2D texture, OutGameVisitorSheet sheet, bool fromBottom)
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
                if (fromBottom)
                {
                    for (var y = yBottom; y < yTop && found < 0; y++)
                        for (var x = x0; x < x1; x++)
                            if (pixels[y * width + x].a > AlphaThreshold) { found = y; break; }
                }
                else
                {
                    for (var y = yTop - 1; y >= yBottom && found < 0; y--)
                        for (var x = x0; x < x1; x++)
                            if (pixels[y * width + x].a > AlphaThreshold) { found = y; break; }
                }
                if (found < 0) continue; // 空帧不参与
                paddings.Add((fromBottom ? found - yBottom : yTop - 1 - found) / frameHeight);
            }
            if (paddings.Count == 0) return 0f;
            var min = paddings[0];
            foreach (var padding in paddings) min = Mathf.Min(min, padding);
            return Mathf.Clamp(min, 0f, .5f);
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 家具布局烘焙器：把「干净背景 + 当前摆放家具」合成到 RenderTexture，
    /// 供 House Hub 的起居室场景图使用——家具摆放完成后，布局变化直接成为背景图。
    /// 采用 Graphics.DrawTexture 按场景像素坐标同步绘制（URP 下临时相机渲染时序不可控，不走相机）。
    /// 锚点公式与 FurnitureRoomController 保持一致（基于摆放配置静态计算，不依赖家具模式开启）。
    /// </summary>
    /// <summary>已摆放家具在场景中的位置信息（供 Hub 背景热点等交互使用）。</summary>
    public readonly struct PlacedFurnitureInfo
    {
        public readonly FurnitureEntry Entry;
        /// <summary>归一化视口矩形（0..1，左下原点），可直接用作全屏 UI 的锚点区间。</summary>
        public readonly Rect ViewportRect;

        public PlacedFurnitureInfo(FurnitureEntry entry, Rect viewportRect)
        {
            Entry = entry;
            ViewportRect = viewportRect;
        }
    }

    public static class FurnitureSceneComposer
    {
        // 家具表并入 Def 体系（§16.7）：统一由 GameManager 加载
        

        private static RenderTexture baked;
        private static bool hasBake;

        /// <summary>当前有效的合成背景；null = 尚未烘焙（Hub 回落到原始美术图）。</summary>
        public static Texture Current => hasBake ? baked : null;

        /// <summary>作废合成图（新游戏 / 读入无布局的存档后，Hub 恢复原始美术图）。</summary>
        public static void ClearBaked()
        {
            hasBake = false;
        }

        /// <summary>按当前布局（会话布局，否则房间默认摆放）同步重新合成背景。回调保留异步形式的兼容签名。</summary>
        public static void RequestBake(Action<Texture> onDone = null)
        {
            var result = Bake();
            onDone?.Invoke(result);
        }

        private static Texture Bake()
        {
            var table = GameManager.Instance.FurnitureTable;
            var rooms = GameManager.Instance.FurnitureRoomTable;
            var room = rooms != null && rooms.rooms.Count > 0 ? rooms.rooms[0] : null;
            if (table == null || room == null || room.background == null) return null;

            var width = Mathf.RoundToInt(room.sceneWidth);
            var height = Mathf.RoundToInt(room.sceneHeight);
            if (baked == null || baked.width != width || baked.height != height)
            {
                if (baked != null) baked.Release();
                baked = new RenderTexture(width, height, 0) { name = "FurnitureSceneBaked" };
                baked.Create();
            }

            // 收集绘制项：背景 + 家具（按层级排序后自后向前绘制）
            var draws = Collect(table, room);
            draws.Sort((a, b) => a.order.CompareTo(b.order));

            var previous = RenderTexture.active;
            Graphics.SetRenderTarget(baked);
            GL.Clear(true, true, Color.black);
            GL.PushMatrix();
            // 像素坐标系（左上原点、Y 向下），与场景像素坐标一一对应
            GL.LoadPixelMatrix(0, width, height, 0);
            DrawSprite(room.background, new Rect(0, 0, width, height));
            foreach (var draw in draws) DrawSprite(draw.entry.sprite, draw.rect);
            GL.PopMatrix();
            RenderTexture.active = previous;

            hasBake = true;
            return baked;
        }

        /// <summary>当前布局中每件家具的场景像素矩形（与合成图一致的锚点数学）。</summary>
        private static List<(FurnitureEntry entry, int order, Rect rect)> Collect(FurnitureTable table, FurnitureRoomEntry room)
        {
            var placements = FurnitureRoomController.CaptureSessionPlacements() ?? room.initialPlacements;
            var result = new List<(FurnitureEntry, int, Rect)>();
            foreach (var placement in placements)
            {
                if (placement == null || !string.IsNullOrEmpty(placement.hostFurnitureId)) continue;
                var entry = table.Find(placement.furnitureId);
                if (entry == null || entry.sprite == null) continue;
                if (!BaseAnchor(room, entry, placement, out var left, out var bottom, out var order)) continue;
                result.Add((entry, order, new Rect(left, bottom - entry.displayHeight, entry.displayWidth, entry.displayHeight)));
            }
            foreach (var placement in placements)
            {
                if (placement == null || string.IsNullOrEmpty(placement.hostFurnitureId)) continue;
                var entry = table.Find(placement.furnitureId);
                if (entry == null || entry.sprite == null) continue;
                if (!HostedAnchor(room, table, placements, placement, entry, out var left, out var bottom, out var order)) continue;
                result.Add((entry, order, new Rect(left, bottom - entry.displayHeight, entry.displayWidth, entry.displayHeight)));
            }
            return result;
        }

        /// <summary>当前布局中每件家具的归一化视口区域（供 Hub 背景热点使用）。</summary>
        public static List<PlacedFurnitureInfo> GetPlacedFurniture()
        {
            var result = new List<PlacedFurnitureInfo>();
            var table = GameManager.Instance.FurnitureTable;
            var rooms = GameManager.Instance.FurnitureRoomTable;
            var room = rooms != null && rooms.rooms.Count > 0 ? rooms.rooms[0] : null;
            if (table == null || room == null) return result;
            foreach (var (entry, _, rect) in Collect(table, room))
            {
                var viewport = new Rect(
                    rect.x / room.sceneWidth,
                    1f - (rect.y + rect.height) / room.sceneHeight,
                    rect.width / room.sceneWidth,
                    rect.height / room.sceneHeight);
                result.Add(new PlacedFurnitureInfo(entry, viewport));
            }
            return result;
        }

        private static void DrawSprite(Sprite sprite, Rect destination)
        {
            var texture = sprite.texture;
            if (texture == null) return;
            var rect = sprite.textureRect;
            var source = new Rect(rect.x / texture.width, rect.y / texture.height,
                rect.width / texture.width, rect.height / texture.height);
            Graphics.DrawTexture(destination, texture, source, 0, 0, 0, 0);
        }

        private static FurnitureGridConfig FindGrid(FurnitureRoomEntry room, string id)
        {
            foreach (var grid in room.grids)
                if (grid != null && grid.id == id) return grid;
            return null;
        }

        private static bool BaseAnchor(FurnitureRoomEntry room, FurnitureEntry entry, FurniturePlacementConfig placement,
            out float left, out float bottom, out int order)
        {
            left = bottom = 0f;
            order = 0;
            var grid = FindGrid(room, placement.gridId);
            if (grid == null) return false;
            left = grid.x + placement.col * grid.cellWidth + (entry.cols * grid.cellWidth - entry.displayWidth) * .5f;
            if (grid.surface == FurnitureSurfaceType.Floor)
            {
                var bottomRow = placement.row + entry.rows;
                bottom = grid.y + bottomRow * grid.cellHeight;
                order = 100 + bottomRow * 10;
            }
            else
            {
                bottom = grid.y + (placement.row + entry.rows) * grid.cellHeight;
                order = 20 + placement.row + entry.rows;
            }
            return true;
        }

        private static bool HostedAnchor(FurnitureRoomEntry room, FurnitureTable table,
            List<FurniturePlacementConfig> placements, FurniturePlacementConfig placement, FurnitureEntry entry,
            out float left, out float bottom, out int order)
        {
            left = bottom = 0f;
            order = 0;
            FurniturePlacementConfig hostPlacement = null;
            foreach (var candidate in placements)
                if (candidate != null && string.IsNullOrEmpty(candidate.hostFurnitureId) &&
                    candidate.furnitureId == placement.hostFurnitureId)
                {
                    hostPlacement = candidate;
                    break;
                }
            if (hostPlacement == null) return false;
            var hostEntry = table.Find(hostPlacement.furnitureId);
            var surface = hostEntry?.tableSurface;
            if (surface == null || !surface.enabled) return false;
            if (!BaseAnchor(room, hostEntry, hostPlacement, out var hostLeft, out var hostBottom, out var hostOrder)) return false;
            var gridX = hostLeft + surface.offsetX;
            var gridY = hostBottom - surface.surfaceHeight - surface.cellHeight;
            left = gridX + placement.col * surface.cellWidth + (entry.cols * surface.cellWidth - entry.displayWidth) * .5f;
            bottom = gridY + surface.cellHeight;
            order = hostOrder + 3;
            return true;
        }
    }
}

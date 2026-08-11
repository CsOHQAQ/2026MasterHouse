using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
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

    /// <summary>
    /// 家具布局烘焙器：把「房间背景 + 当前摆放家具」合成到 RenderTexture，
    /// 供 House Hub 的场景图使用——家具摆放完成后，布局变化直接成为背景图。
    /// **按房间分别烘焙**（家具模式随 Hub 房间动态加载后，各房间布局独立）。
    /// 采用 Graphics.DrawTexture 按场景像素坐标同步绘制（URP 下临时相机渲染时序不可控，不走相机）。
    /// 锚点公式与 FurnitureRoomController 保持一致（基于摆放配置静态计算，不依赖家具模式开启）。
    /// </summary>
    public static class FurnitureSceneComposer
    {
        /// <summary>房间 id → 合成图（惰性创建，尺寸随房间场景尺寸）。</summary>
        private static readonly Dictionary<string, RenderTexture> bakes = new Dictionary<string, RenderTexture>();

        /// <summary>作废全部合成图（新游戏 / 读入无布局的存档后，Hub 恢复原始美术图并按需重烘）。</summary>
        public static void ClearBaked()
        {
            foreach (var texture in bakes.Values)
                if (texture != null) texture.Release();
            bakes.Clear();
        }

        /// <summary>取指定房间的合成图；null = 尚未烘焙。</summary>
        public static Texture BakedFor(int roomIndex)
        {
            var room = RoomAt(roomIndex);
            return room != null && bakes.TryGetValue(room.id, out var texture) ? texture : null;
        }

        /// <summary>取指定房间的合成图，缺失时立即烘焙一张（Hub 进场/切房间用：初始摆放直接可见）。</summary>
        public static Texture EnsureBaked(int roomIndex)
        {
            var existing = BakedFor(roomIndex);
            return existing != null ? existing : Bake(roomIndex);
        }

        /// <summary>按当前布局（会话布局，否则房间默认摆放）同步重新合成指定房间。回调保留异步形式的兼容签名。</summary>
        public static void RequestBake(int roomIndex, Action<Texture> onDone = null)
        {
            var result = Bake(roomIndex);
            onDone?.Invoke(result);
        }

        private static FurnitureRoomEntry RoomAt(int roomIndex)
        {
            var rooms = GameManager.Instance.FurnitureRoomTable;
            if (rooms == null || rooms.rooms.Count == 0) return null;
            return roomIndex >= 0 && roomIndex < rooms.rooms.Count ? rooms.rooms[roomIndex] : rooms.rooms[0];
        }

        private static Texture Bake(int roomIndex)
        {
            var table = GameManager.Instance.FurnitureTable;
            var room = RoomAt(roomIndex);
            if (table == null || room == null || room.background == null) return null;

            var width = Mathf.RoundToInt(room.sceneWidth);
            var height = Mathf.RoundToInt(room.sceneHeight);
            bakes.TryGetValue(room.id, out var baked);
            if (baked == null || baked.width != width || baked.height != height)
            {
                if (baked != null) baked.Release();
                baked = new RenderTexture(width, height, 0) { name = "FurnitureSceneBaked_" + room.id };
                baked.Create();
                bakes[room.id] = baked;
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
            DrawSprite(room.background, new Rect(0, 0, width, height), false);
            foreach (var draw in draws) DrawSprite(draw.entry.sprite, draw.rect, draw.flipped);
            GL.PopMatrix();
            RenderTexture.active = previous;
            return baked;
        }

        /// <summary>当前布局中每件家具的场景像素矩形（与合成图一致的锚点数学）。</summary>
        private static List<(FurnitureEntry entry, int order, Rect rect, bool flipped)> Collect(FurnitureTable table, FurnitureRoomEntry room)
        {
            var placements = FurnitureRoomController.CaptureSessionPlacements(room.id) ?? room.initialPlacements;
            var result = new List<(FurnitureEntry, int, Rect, bool)>();
            foreach (var placement in placements)
            {
                if (placement == null || !string.IsNullOrEmpty(placement.hostFurnitureId)) continue;
                var entry = table.Find(placement.furnitureId);
                if (entry == null || entry.sprite == null) continue;
                if (!BaseAnchor(room, entry, placement, out var left, out var bottom, out var order)) continue;
                result.Add((entry, order, new Rect(left, bottom - entry.displayHeight, entry.displayWidth, entry.displayHeight), placement.flipped));
            }
            foreach (var placement in placements)
            {
                if (placement == null || string.IsNullOrEmpty(placement.hostFurnitureId)) continue;
                var entry = table.Find(placement.furnitureId);
                if (entry == null || entry.sprite == null) continue;
                if (!HostedAnchor(room, table, placements, placement, entry, out var left, out var bottom, out var order)) continue;
                result.Add((entry, order, new Rect(left, bottom - entry.displayHeight, entry.displayWidth, entry.displayHeight), placement.flipped));
            }
            return result;
        }

        /// <summary>指定房间当前布局中每件家具的归一化视口区域（供 Hub 背景热点使用）。</summary>
        public static List<PlacedFurnitureInfo> GetPlacedFurniture(int roomIndex)
        {
            var result = new List<PlacedFurnitureInfo>();
            var table = GameManager.Instance.FurnitureTable;
            var room = RoomAt(roomIndex);
            if (table == null || room == null) return result;
            foreach (var (entry, _, rect, _) in Collect(table, room))
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

        private static void DrawSprite(Sprite sprite, Rect destination, bool flipped)
        {
            var texture = sprite.texture;
            if (texture == null) return;
            var rect = sprite.textureRect;
            var source = new Rect(rect.x / texture.width, rect.y / texture.height,
                rect.width / texture.width, rect.height / texture.height);
            if (flipped) // 左右镜像：源 UV 水平反向
                source = new Rect(source.xMax, source.y, -source.width, source.height);
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

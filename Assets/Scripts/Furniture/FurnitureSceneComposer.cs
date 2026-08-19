using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>已摆放家具在场景中的位置信息（供 Hub 背景热点/前景深度代理等使用）。</summary>
    public readonly struct PlacedFurnitureInfo
    {
        public readonly FurnitureEntry Entry;
        /// <summary>归一化视口矩形（0..1，左下原点），可直接用作全屏 UI 的锚点区间。</summary>
        public readonly Rect ViewportRect;
        /// <summary>烘焙绘制序（同深度并列时的稳定次序）。</summary>
        public readonly int Order;
        /// <summary>水平翻转（深度代理要与烘焙像素重合，必须同向）。</summary>
        public readonly bool Flipped;

        public PlacedFurnitureInfo(FurnitureEntry entry, Rect viewportRect, int order = 0, bool flipped = false)
        {
            Entry = entry;
            ViewportRect = viewportRect;
            Order = order;
            Flipped = flipped;
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
        /// <summary>烘焙分辨率倍数（2026-08-17）：房间推到满屏时按场景像素 1:1 会发糊，×2 接近原画密度。</summary>
        private const float BakeScale = 2f;

        /// <summary>
        /// 房间 id → 合成图。**昆夜合一张**（2026-08-19）：
        /// 曾经昆夜各烘一张、在 Hub 里交叉淡入，但两张里的家具位置不同
        /// （墙脚线不一样，家具要跟着校正），叠在一起就是一件家具出两个。
        /// 现在只烘一张：背景在烘的时候就按夜色权重叠好，家具取插值后的几何，
        /// 任何时刻画面上只有一套家具。
        /// </summary>
        private static readonly Dictionary<string, RenderTexture> bakes = new Dictionary<string, RenderTexture>();

        /// <summary>各房间当前烘图用的夜色权重（差得多了才重烘）。</summary>
        private static readonly Dictionary<string, float> bakedNightAlpha = new Dictionary<string, float>();

        /// <summary>夜色权重变化超过这个幅度才重烘（每帧重烘太浪费，0.02 一档肉眼看不出跳）。</summary>
        private const float NightRebakeStep = .02f;

        /// <summary>夜间房间图的 Resources 路径（与摆放模式同一套素材）。</summary>
        private static Sprite NightBackground(int roomIndex) =>
            Resources.Load<Sprite>($"OutGameUI/RoomNight/room-night-{roomIndex + 1:00}");

        /// <summary>作废全部合成图（新游戏 / 读入无布局的存档后，Hub 恢复原始美术图并按需重烘）。</summary>
        public static void ClearBaked()
        {
            foreach (var texture in bakes.Values)
                if (texture != null) texture.Release();
            bakes.Clear();
            bakedNightAlpha.Clear();
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
            return existing != null ? existing : Bake(roomIndex, FurnitureNightLayout.NightAlphaNow());
        }

        /// <summary>按当前布局（会话布局，否则房间默认摆放）同步重新合成指定房间。回调保留异步形式的兼容签名。</summary>
        public static void RequestBake(int roomIndex, Action<Texture> onDone = null)
        {
            var result = Bake(roomIndex, FurnitureNightLayout.NightAlphaNow());
            onDone?.Invoke(result);
        }

        /// <summary>
        /// 天色推移时重烘（Hub 每帧调）：差不够一档就什么都不做。
        /// 返回 true = 这一帧重烘了，调用方可以顺便刷新引用。
        /// </summary>
        public static bool TickNight(int roomIndex)
        {
            var room = RoomAt(roomIndex);
            if (room == null) return false;
            var want = FurnitureNightLayout.NightAlphaNow();
            if (bakedNightAlpha.TryGetValue(room.id, out var current) &&
                Mathf.Abs(current - want) < NightRebakeStep) return false;
            return Bake(roomIndex, want) != null;
        }

        private static FurnitureRoomEntry RoomAt(int roomIndex)
        {
            var rooms = GameManager.Instance.FurnitureRoomTable;
            if (rooms == null || rooms.rooms.Count == 0) return null;
            return roomIndex >= 0 && roomIndex < rooms.rooms.Count ? rooms.rooms[roomIndex] : rooms.rooms[0];
        }

        private static Texture Bake(int roomIndex, float nightAlpha)
        {
            var table = GameManager.Instance.FurnitureTable;
            var room = RoomAt(roomIndex);
            if (table == null || room == null || room.background == null) return null;
            nightAlpha = Mathf.Clamp01(nightAlpha);
            var nightBackground = nightAlpha > .001f ? NightBackground(roomIndex) : null;

            // 烘焙分辨率放大（2026-08-17）：场景像素 1672 宽的画布推到单房间满屏时只有 ~40% 像素密度，
            // 放大后明显发糊；×2 后接近原画分辨率，家具坐标按同一倍数缩放，几何关系不变。
            var width = Mathf.RoundToInt(room.sceneWidth * BakeScale);
            var height = Mathf.RoundToInt(room.sceneHeight * BakeScale);
            var key = room.id;
            bakes.TryGetValue(key, out var baked);
            if (baked == null || baked.width != width || baked.height != height)
            {
                if (baked != null) baked.Release();
                // ARGB32：需要 alpha 通道（家具层是透明底，2026-08-17）
                baked = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
                {
                    name = "FurnitureSceneBaked_" + key,
                };
                baked.Create();
                bakes[key] = baked;
            }

            // 收集绘制项：背景 + 家具（按层级排序后自后向前绘制）
            var draws = Collect(table, room, nightAlpha);
            // 光影：立式家具（地面/桌面带，order ≥ 100）脚下垫柔和投影，插到该件之下（order-1）
            var shadowSprite = Resources.Load<Sprite>("OutGameUI/soft-shadow");
            if (shadowSprite != null)
            {
                var shadows = new List<(FurnitureEntry entry, int order, Rect rect, bool flipped)>();
                foreach (var draw in draws)
                {
                    if (draw.order < 100 || draw.entry.stackable) continue;
                    // 2026-08-17 加强：投影更宽更实，且**叠两遍**（软影贴图单遍太淡，家具像浮在地上）
                    var shadowW = draw.rect.width * 1.22f;
                    var shadowH = shadowW * .3f;
                    var shadowRect = new Rect(draw.rect.x - (shadowW - draw.rect.width) * .5f,
                        draw.rect.yMax - shadowH * .55f, shadowW, shadowH);
                    shadows.Add((null, draw.order - 2, shadowRect, false));
                    // 第二遍略小、更集中，形成「接地处更深、边缘散开」的层次
                    var coreW = draw.rect.width * .86f;
                    var coreH = coreW * .28f;
                    shadows.Add((null, draw.order - 1,
                        new Rect(draw.rect.x + (draw.rect.width - coreW) * .5f,
                            draw.rect.yMax - coreH * .5f, coreW, coreH), false));
                }
                draws.AddRange(shadows);
            }
            draws.Sort((a, b) => a.order.CompareTo(b.order));

            var previous = RenderTexture.active;
            Graphics.SetRenderTarget(baked);
            // 房间背景重新画进来（2026-08-17）：聚焦单间时这张高清图取代延时帧显示，
            // 延时帧只有 1280 宽，推近了糊；总览时本层淡出、由延时帧的室内光影当家（见 HubSceneBinder 的 LOD）。
            GL.Clear(true, true, Color.clear);
            GL.PushMatrix();
            // 像素坐标系（左上原点、Y 向下），与场景像素坐标一一对应
            // 绘制坐标系仍用**场景像素**口径（家具矩形都按它算），渲染目标分辨率高一档即自动提清晰度
            GL.LoadPixelMatrix(0, room.sceneWidth, room.sceneHeight, 0);
            var full = new Rect(0, 0, room.sceneWidth, room.sceneHeight);
            DrawSprite(room.background, full, false);
            // 夜图在烘的时候就叠好（而不是在 Hub 里再盖一层），
            // 于是家具只会被画一遍，不会昆夜两套叠出重影
            if (nightBackground != null) DrawSprite(nightBackground, full, false, nightAlpha);
            foreach (var draw in draws)
                DrawSprite(draw.entry != null ? draw.entry.sprite : shadowSprite, draw.rect, draw.flipped);
            GL.PopMatrix();
            RenderTexture.active = previous;
            bakedNightAlpha[key] = nightAlpha;
            return baked;
        }

        /// <summary>当前布局中每件家具的场景像素矩形（与合成图一致的锚点数学）。</summary>
        private static List<(FurnitureEntry entry, int order, Rect rect, bool flipped)> Collect(FurnitureTable table, FurnitureRoomEntry room, float nightAlpha)
        {
            var placements = FurnitureRoomController.CaptureSessionPlacements(room.id) ?? room.initialPlacements;
            var result = new List<(FurnitureEntry, int, Rect, bool)>();
            foreach (var placement in placements)
            {
                if (placement == null || placement.IsOnHost) continue;
                var entry = table.Find(placement.furnitureId);
                if (entry == null || entry.sprite == null) continue;
                if (!BaseAnchor(room, entry, placement, nightAlpha, out var left, out var bottom, out var order)) continue;
                result.Add((entry, order, new Rect(left, bottom - entry.displayHeight, entry.displayWidth, entry.displayHeight), placement.flipped));
            }
            foreach (var placement in placements)
            {
                if (placement == null || !placement.IsOnHost) continue;
                var entry = table.Find(placement.furnitureId);
                if (entry == null || entry.sprite == null) continue;
                if (!HostedAnchor(room, table, placements, placement, entry, nightAlpha, out var left, out var bottom, out var order)) continue;
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
            foreach (var (entry, order, rect, flipped) in Collect(table, room, 0f))
            {
                var viewport = new Rect(
                    rect.x / room.sceneWidth,
                    1f - (rect.y + rect.height) / room.sceneHeight,
                    rect.width / room.sceneWidth,
                    rect.height / room.sceneHeight);
                result.Add(new PlacedFurnitureInfo(entry, viewport, order, flipped));
            }
            return result;
        }

        private static void DrawSprite(Sprite sprite, Rect destination, bool flipped, float alpha = 1f)
        {
            var texture = sprite.texture;
            if (texture == null) return;
            var rect = sprite.textureRect;
            var source = new Rect(rect.x / texture.width, rect.y / texture.height,
                rect.width / texture.width, rect.height / texture.height);
            if (flipped) // 左右镜像：源 UV 水平反向
                source = new Rect(source.xMax, source.y, -source.width, source.height);
            if (alpha >= .999f)
            {
                Graphics.DrawTexture(destination, texture, source, 0, 0, 0, 0);
                return;
            }
            // 半透明叠加（夜图按夜色权重压在白天图上）：DrawTexture 的无材质重载不吃颜色，
            // 走带颜色的那个重载
            Graphics.DrawTexture(destination, texture, source, 0, 0, 0, 0,
                new Color(1f, 1f, 1f, Mathf.Clamp01(alpha)));
        }

        private static FurnitureGridConfig FindGrid(FurnitureRoomEntry room, string id)
        {
            foreach (var grid in room.grids)
                if (grid != null && grid.id == id) return grid;
            return null;
        }

        private static bool BaseAnchor(FurnitureRoomEntry room, FurnitureEntry entry, FurniturePlacementConfig placement,
            float nightAlpha, out float left, out float bottom, out int order)
        {
            left = bottom = 0f;
            order = 0;
            var grid = FindGrid(room, placement.gridId);
            if (grid == null) return false;
            // 与摆放模式同一套昆夜几何校正，否则两边位置对不上
            grid = FurnitureNightLayout.Adjust(room, grid, nightAlpha);
            left = grid.x + placement.col * grid.cellWidth + (entry.cols * grid.cellWidth - entry.displayWidth) * .5f;
            if (grid.surface == FurnitureSurfaceType.Floor)
            {
                var bottomRow = placement.row + entry.rows;
                // 2.5D 假透视：与 FurnitureRuntimeGrid.MapX 同口径（横向按底边行向网格中心收拢）
                var farScale = grid.farWidthScale <= 0f ? 1f : grid.farWidthScale;
                var widthScale = Mathf.Lerp(farScale, 1f, grid.rows > 0 ? Mathf.Clamp01((float)bottomRow / grid.rows) : 1f);
                var gridCenter = grid.x + grid.cols * grid.cellWidth * .5f;
                left = gridCenter + (left + entry.displayWidth * .5f - gridCenter) * widthScale - entry.displayWidth * .5f;
                bottom = grid.y + bottomRow * grid.cellHeight;
                // 与 FurnitureRoomController.AnchorOf 同口径：可叠放（地毯）压在立式家具之下
                order = entry.stackable ? 70 + bottomRow : 100 + bottomRow * 10;
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
            float nightAlpha, out float left, out float bottom, out int order)
        {
            left = bottom = 0f;
            order = 0;
            // 按落位坐标认宿主，与 FurnitureRoomController.RestoreState 同口径（§5.4）——
            // 这里是锚点数学的第二份实现（§16.7 已知技术债），改一边必须同步另一边
            FurniturePlacementConfig hostPlacement = null;
            foreach (var candidate in placements)
                if (candidate != null && candidate.OccupiesBaseCell(placement.hostGridId, placement.hostCol, placement.hostRow))
                {
                    hostPlacement = candidate;
                    break;
                }
            if (hostPlacement == null) return false;
            var hostEntry = table.Find(hostPlacement.furnitureId);
            var surface = hostEntry?.tableSurface;
            if (surface == null || !surface.enabled) return false;
            if (!BaseAnchor(room, hostEntry, hostPlacement, nightAlpha, out var hostLeft, out var hostBottom, out var hostOrder)) return false;
            var gridX = hostLeft + surface.offsetX;
            var gridY = hostBottom - surface.surfaceHeight - surface.cellHeight;
            left = gridX + placement.col * surface.cellWidth + (entry.cols * surface.cellWidth - entry.displayWidth) * .5f;
            bottom = gridY + surface.cellHeight;
            order = hostOrder + 3;
            return true;
        }
    }
}

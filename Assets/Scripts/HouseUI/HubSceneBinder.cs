using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// Hub 场景层绑定：房间背景（起居室优先家具烘焙图）、场景说明卡、家具热点、访客舞台挂接、
    /// 观景模式的平移缩放（uvRect）与热点锚点跟随。背景/热点是动态表现件，允许运行时生成（§16.2）。
    /// </summary>
    public sealed class HubSceneBinder
    {
        private HubPage page;
        private RectTransform sceneRoot;
        private RawImage sceneArt;
        private Image sceneWash;
        private OutGameHubSceneOverlayView overlay;
        private OutGameVisitorStage stage;
        private readonly List<(RectTransform rect, Rect viewport)> hotspots =
            new List<(RectTransform, Rect)>();
        private bool panning;
        private Vector3 lastPanPosition;

        private static CodexTable Codex => GameManager.Instance.CodexTable;

        public void Build(OutGameHubView view, HubPage owner)
        {
            page = owner;
            sceneRoot = view.sceneRoot;
            overlay = view.sceneOverlay;
            sceneArt = HouseUIRuntime.StretchTexture(sceneRoot, "SceneArt", Codex.rooms[page.RoomIndex].artPath);
            sceneArt.raycastTarget = false; // 场景图不拦截指针，观景模式拖拽与家具热点都依赖穿透
            ApplySceneArt();
            sceneWash = HouseUIRuntime.StretchPanel(sceneRoot, "SceneWash", new Color(.015f, .02f, .04f, .22f));
            sceneWash.raycastTarget = false;
            BuildHotspots();
            BuildVisitorStage();
            BindOverlay();
        }

        /// <summary>房间切换：背景交叉淡入 + 热点/舞台/说明卡重建。</summary>
        public void SwapRoom()
        {
            if (sceneArt != null)
            {
                var old = sceneArt;
                var next = HouseUIRuntime.StretchTexture(sceneRoot, "SceneArtNext",
                    Codex.rooms[page.RoomIndex].artPath, new Color(1, 1, 1, 0));
                next.raycastTarget = false;
                next.transform.SetAsFirstSibling();
                next.DOFade(1, .5f).SetUpdate(true);
                old.DOFade(0, .5f).SetUpdate(true).OnComplete(() => Object.Destroy(old.gameObject));
                sceneArt = next;
                ApplySceneArt();
            }
            BuildHotspots();
            BuildVisitorStage();
            BindOverlay();
        }

        /// <summary>房间背景优先使用家具布局合成图（背景+当前摆放；缺失时立即烘焙——一进游戏默认家具就可见）。</summary>
        public void ApplySceneArt()
        {
            if (sceneArt == null) return;
            var baked = FurnitureSceneComposer.EnsureBaked(page.RoomIndex);
            if (baked != null) sceneArt.texture = baked;
        }

        /// <summary>观景模式切换的场景侧表现：洗色层显隐、复位画面平移缩放。</summary>
        public void SetImmersiveVisual(bool on)
        {
            panning = false;
            if (sceneWash != null)
            {
                var washGroup = HouseUIUtil.Group(sceneWash.gameObject);
                washGroup.DOKill();
                washGroup.DOFade(on ? 0f : 1f, .25f).SetUpdate(true);
            }
            if (!on && sceneArt != null) sceneArt.uvRect = new Rect(0f, 0f, 1f, 1f);
            UpdateHotspotAnchors();
        }

        /// <summary>观景模式：滚轮以鼠标为中心缩放（1~3.5 倍），按住左键拖拽平移，边界钳制在图内。</summary>
        public void HandleBrowse()
        {
            if (sceneArt == null) return;
            var uv = sceneArt.uvRect;
            var scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > .01f && Screen.width > 0 && Screen.height > 0)
            {
                var zoom = Mathf.Clamp(1f / uv.width + scroll * .12f / uv.width, 1f, 3.5f);
                var size = 1f / zoom;
                var nx = Mathf.Clamp01(Input.mousePosition.x / Screen.width);
                var ny = Mathf.Clamp01(Input.mousePosition.y / Screen.height);
                var pivotX = uv.x + nx * uv.width;
                var pivotY = uv.y + ny * uv.height;
                uv = new Rect(pivotX - nx * size, pivotY - ny * size, size, size);
            }
            if (Input.GetMouseButtonDown(0))
            {
                panning = true;
                lastPanPosition = Input.mousePosition;
            }
            if (Input.GetMouseButtonUp(0)) panning = false;
            if (panning && Screen.width > 0 && Screen.height > 0)
            {
                var delta = Input.mousePosition - lastPanPosition;
                lastPanPosition = Input.mousePosition;
                uv.x -= delta.x / Screen.width * uv.width;
                uv.y -= delta.y / Screen.height * uv.height;
            }
            uv.x = Mathf.Clamp(uv.x, 0f, 1f - uv.width);
            uv.y = Mathf.Clamp(uv.y, 0f, 1f - uv.height);
            sceneArt.uvRect = uv;
            // 热点跟随平移缩放，收起状态下家具依然可悬停/点击
            UpdateHotspotAnchors();
        }

        /// <summary>家具摆放退出后：重新烘焙当前房间背景并重建热点。</summary>
        public void RefreshAfterFurniture()
        {
            FurnitureSceneComposer.RequestBake(page.RoomIndex, _ =>
            {
                ApplySceneArt();
                BuildHotspots();
            });
        }

        /// <summary>背景中的已摆放家具热点：悬停弹提示卡，点击暂接设备面板（3.5c）。按当前房间取布局。</summary>
        private void BuildHotspots()
        {
            if (sceneRoot == null) return;
            var existing = sceneRoot.Find("FurnitureHotspots");
            if (existing != null) Object.Destroy(existing.gameObject);
            hotspots.Clear();
            var root = HouseUIRuntime.Stretch(sceneRoot, "FurnitureHotspots");
            foreach (var info in FurnitureSceneComposer.GetPlacedFurniture(page.RoomIndex))
            {
                var viewport = info.ViewportRect;
                var hotspot = HouseUIRuntime.Rect(root, "Hotspot_" + info.Entry.id,
                    new Vector2(viewport.xMin, viewport.yMin), new Vector2(viewport.xMax, viewport.yMax),
                    Vector2.zero, Vector2.zero);
                hotspots.Add((hotspot, viewport));
                var image = hotspot.gameObject.AddComponent<Image>();
                image.sprite = HouseUIRuntime.WhiteSprite;
                image.color = Color.clear;
                var button = hotspot.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => page.OpenPanel(EHousePanel.Device));

                var card = HouseUIRuntime.Panel(hotspot, "Card", new Vector2(.5f, 1),
                    new Vector2(0, 46), new Vector2(250, 76), new Color(.32f, .06f, .18f, .92f));
                HouseUIRuntime.StretchLabel(card.transform, "Text",
                    $"＋  {info.Entry.displayName}\n<size=13>查看设备</size>", 19, HouseUIUtil.White,
                    TextAnchor.MiddleCenter, FontStyle.Bold);
                var cardGroup = HouseUIUtil.Group(card.gameObject, 0f);
                cardGroup.blocksRaycasts = false;
                cardGroup.interactable = false;

                var trigger = hotspot.gameObject.AddComponent<EventTrigger>();
                var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                enter.callback.AddListener(_ => { cardGroup.DOKill(); cardGroup.DOFade(1f, .16f).SetUpdate(true); });
                trigger.triggers.Add(enter);
                var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                exit.callback.AddListener(_ => { cardGroup.DOKill(); cardGroup.DOFade(0f, .16f).SetUpdate(true); });
                trigger.triggers.Add(exit);
            }
            UpdateHotspotAnchors();
        }

        /// <summary>按当前画面平移缩放（uvRect）换算热点锚点，保证观景模式下热点始终贴住家具。</summary>
        private void UpdateHotspotAnchors()
        {
            if (sceneArt == null) return;
            var uv = sceneArt.uvRect;
            foreach (var (rect, viewport) in hotspots)
            {
                if (rect == null) continue;
                rect.anchorMin = new Vector2((viewport.xMin - uv.x) / uv.width, (viewport.yMin - uv.y) / uv.height);
                rect.anchorMax = new Vector2((viewport.xMax - uv.x) / uv.width, (viewport.yMax - uv.y) / uv.height);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }
        }

        /// <summary>整体重建舞台（GM 重置后访客重新进场）。庆祝/离场等状态表现由舞台层轮询实例状态自驱（§9）。</summary>
        public void RebuildStage() => BuildVisitorStage();

        /// <summary>重建场景访客 NPC 层（仅起居室）。舞台只读 VisitorManager 状态，表现不回写业务（§16.4）。</summary>
        private void BuildVisitorStage()
        {
            if (sceneRoot == null) return;
            stage = null;
            if (page.RoomIndex != 0)
            {
                var existing = sceneRoot.Find("VisitorStage");
                if (existing != null) Object.Destroy(existing.gameObject);
                return;
            }
            stage = OutGameVisitorStage.Build(sceneRoot, sceneArt, page.OnVisitorClicked);
        }

        /// <summary>场景说明卡与设备热点按钮（Prefab 字段可能因手动编辑缺失，逐项判空）。</summary>
        private void BindOverlay()
        {
            if (overlay == null) return;
            var room = Codex.rooms[page.RoomIndex];
            if (overlay.captionHeader != null) overlay.captionHeader.text = "CURRENT ROOM / 04";
            if (overlay.roomName != null) overlay.roomName.text = room.displayName;
            if (overlay.roomNote != null) overlay.roomNote.text = room.note;
            var hotspotLabel = page.RoomIndex == 2 ? "手冲咖啡台" : page.RoomIndex == 3 ? "旧书检索机" : "黑胶唱机";
            if (overlay.hotspotTitle != null) overlay.hotspotTitle.text = "＋  " + hotspotLabel + "\n<size=13>查看设备</size>";
            if (overlay.hotspotButton != null)
                HouseUIUtil.BindButton(overlay.hotspotButton, () => page.OpenPanel(EHousePanel.Device));
        }
    }
}

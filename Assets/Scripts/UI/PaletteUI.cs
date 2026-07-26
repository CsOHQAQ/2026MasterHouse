using UnityEngine;
using UnityEngine.UI;

namespace MasterPotion
{
    /// <summary>运行时构建的底部节点工具栏 + 左上角操作提示。</summary>
    public static class PaletteUI
    {
        public static void Build(GameConfig config)
        {
            var canvasGO = new GameObject("PaletteCanvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGO.AddComponent<GraphicRaycaster>();

            BuildToolbar(canvasGO.transform, config);
            BuildHint(canvasGO.transform);
        }

        private static void BuildToolbar(Transform canvas, GameConfig config)
        {
            var bar = new GameObject("Toolbar",
                typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
            bar.transform.SetParent(canvas, false);

            var rt = (RectTransform)bar.transform;
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0, 12);
            rt.sizeDelta = new Vector2(620, 72);

            bar.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            var layout = bar.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 10;
            layout.padding = new RectOffset(10, 10, 8, 8);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            foreach (var def in config.buildableNodes)
            {
                if (def != null) CreateButton(bar.transform, def);
            }
        }

        private static void CreateButton(Transform parent, NodeDef def)
        {
            var go = new GameObject($"Btn_{def.displayName}",
                typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.25f, 0.28f, 0.34f, 0.95f);

            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = 140;
            le.preferredHeight = 56;

            var label = new GameObject("Label", typeof(RectTransform));
            label.transform.SetParent(go.transform, false);
            var rt = (RectTransform)label.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;

            var text = label.AddComponent<Text>();
            text.font = VisualAssets.DefaultFont;
            text.fontSize = 22;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = def.displayName;

            go.GetComponent<Button>().onClick.AddListener(
                () => PlacementController.Instance.BeginPlacement(def));
        }

        private static void BuildHint(Transform canvas)
        {
            var hint = new GameObject("Hint", typeof(RectTransform));
            hint.transform.SetParent(canvas, false);
            var rt = (RectTransform)hint.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(12, -12);
            rt.sizeDelta = new Vector2(640, 200);

            var text = hint.AddComponent<Text>();
            text.font = VisualAssets.DefaultFont;
            text.fontSize = 20;
            text.alignment = TextAnchor.UpperLeft;
            text.color = new Color(1f, 1f, 1f, 0.75f);
            text.text = "左键拖拽端口: 连线    双击连线: 删除\n" +
                        "左键拖拽卡片: 移动    右/中键拖动: 平移镜头    滚轮: 缩放\n" +
                        "底部按钮: 放置节点 (左键放置, 右键/Esc 取消)";
        }
    }
}

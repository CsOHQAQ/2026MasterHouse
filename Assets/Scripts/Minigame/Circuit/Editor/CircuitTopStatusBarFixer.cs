#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// 一键修复 CircuitTopStatusBar Prefab 中的 Title/Subtitle/Icon 布局。
    /// 用于排查手写 YAML 未被 Unity 正确识别的情况。
    /// </summary>
    public static class CircuitTopStatusBarFixer
    {
        private const string PrefabPath = "Assets/GameData/Minigames/CircuitTopStatusBar.prefab";

        [MenuItem("MasterHouse/小游戏/修复顶部状态条布局")]
        public static void FixTopStatusBar()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogError("找不到 Prefab: " + PrefabPath);
                return;
            }

            // 用 PrefabUtility 打开可编辑副本
            var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var view = contents.GetComponent<CircuitTopStatusBarView>();
                if (view == null)
                {
                    Debug.LogError("Prefab 上找不到 CircuitTopStatusBarView 组件");
                    return;
                }

                var bar = contents.transform as RectTransform;
                bool changed = false;

                // ── Icon ──
                if (view.icon == null)
                {
                    var iconGO = FindOrCreateChild(contents, "Icon");
                    var iconRT = iconGO.GetComponent<RectTransform>();
                    iconRT.anchorMin = new Vector2(0.02f, 0.15f);
                    iconRT.anchorMax = new Vector2(0.12f, 0.85f);
                    iconRT.anchoredPosition = Vector2.zero;
                    iconRT.sizeDelta = Vector2.zero;
                    iconRT.pivot = new Vector2(0.5f, 0.5f);
                    iconRT.localScale = Vector3.one;

                    var iconImg = iconGO.GetComponent<Image>();
                    if (iconImg == null) iconImg = iconGO.AddComponent<Image>();
                    iconImg.color = new Color(0.35f, 0.72f, 0.85f, 1f);
                    iconImg.preserveAspect = true;

                    view.icon = iconImg;
                    changed = true;
                    Debug.Log("[修复] 创建/修复 Icon");
                }

                // ── Title ──
                if (view.titleLabel == null)
                {
                    var titleGO = FindOrCreateChild(contents, "Title");
                    var titleRT = titleGO.GetComponent<RectTransform>();
                    titleRT.anchorMin = new Vector2(0.14f, 0.45f);
                    titleRT.anchorMax = new Vector2(0.40f, 0.80f);
                    titleRT.anchoredPosition = Vector2.zero;
                    titleRT.sizeDelta = Vector2.zero;
                    titleRT.pivot = new Vector2(0.5f, 0.5f);
                    titleRT.localScale = Vector3.one;

                    var titleTxt = titleGO.GetComponent<Text>();
                    if (titleTxt == null) titleTxt = titleGO.AddComponent<Text>();
                    titleTxt.text = "修理电路";
                    titleTxt.fontSize = 32;
                    titleTxt.color = new Color(0.94f, 0.94f, 0.96f, 1f);
                    titleTxt.alignment = TextAnchor.MiddleLeft;
                    titleTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
                    titleTxt.verticalOverflow = VerticalWrapMode.Truncate;
                    titleTxt.raycastTarget = false;

                    view.titleLabel = titleTxt;
                    changed = true;
                    Debug.Log("[修复] 创建/修复 Title");
                }

                // ── Subtitle ──
                if (view.subtitleLabel == null)
                {
                    var subGO = FindOrCreateChild(contents, "Subtitle");
                    var subRT = subGO.GetComponent<RectTransform>();
                    subRT.anchorMin = new Vector2(0.14f, 0.15f);
                    subRT.anchorMax = new Vector2(0.40f, 0.50f);
                    subRT.anchoredPosition = Vector2.zero;
                    subRT.sizeDelta = Vector2.zero;
                    subRT.pivot = new Vector2(0.5f, 0.5f);
                    subRT.localScale = Vector3.one;

                    var subTxt = subGO.GetComponent<Text>();
                    if (subTxt == null) subTxt = subGO.AddComponent<Text>();
                    subTxt.text = "在有限的格子内连通电路！";
                    subTxt.fontSize = 22;
                    subTxt.color = new Color(0.72f, 0.72f, 0.78f, 1f);
                    subTxt.alignment = TextAnchor.MiddleLeft;
                    subTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
                    subTxt.verticalOverflow = VerticalWrapMode.Truncate;
                    subTxt.raycastTarget = false;

                    view.subtitleLabel = subTxt;
                    changed = true;
                    Debug.Log("[修复] 创建/修复 Subtitle");
                }

                // ── 调整旧子对象布局（LinkBudget / PieceBudget / Lit / Progress）──
                var linkGO = FindChild(contents, "LinkBudget");
                if (linkGO != null)
                {
                    var rt = linkGO.GetComponent<RectTransform>();
                    rt.anchorMin = new Vector2(0.55f, 0.52f);
                    rt.anchorMax = new Vector2(0.78f, 0.88f);
                    rt.anchoredPosition = Vector2.zero;
                }

                var pieceGO = FindChild(contents, "PieceBudget");
                if (pieceGO != null)
                {
                    var rt = pieceGO.GetComponent<RectTransform>();
                    rt.anchorMin = new Vector2(0.55f, 0.12f);
                    rt.anchorMax = new Vector2(0.78f, 0.52f);
                    rt.anchoredPosition = Vector2.zero;
                }

                var litGO = FindChild(contents, "Lit");
                if (litGO != null)
                {
                    var rt = litGO.GetComponent<RectTransform>();
                    rt.anchorMin = new Vector2(0.80f, 0.25f);
                    rt.anchorMax = new Vector2(0.96f, 0.75f);
                    rt.anchoredPosition = Vector2.zero;
                }

                var progGO = FindChild(contents, "Progress");
                if (progGO != null)
                {
                    var rt = progGO.GetComponent<RectTransform>();
                    rt.anchorMin = new Vector2(0.02f, 0.78f);
                    rt.anchorMax = new Vector2(0.25f, 0.95f);
                    rt.anchoredPosition = Vector2.zero;
                }

                // ── 调整根节点尺寸 ──
                bar.anchoredPosition = new Vector2(0, -80);
                bar.sizeDelta = new Vector2(-160, 120);

                if (changed)
                {
                    EditorUtility.SetDirty(contents);
                    PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                    AssetDatabase.Refresh();
                    Debug.Log("[修复] CircuitTopStatusBar.prefab 已保存。请在 Unity 中重新导入或运行游戏查看效果。");
                }
                else
                {
                    Debug.Log("[修复] 所有字段已存在且正常，未做修改。");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static GameObject FindOrCreateChild(GameObject parent, string name)
        {
            var found = FindChild(parent, name);
            if (found != null) return found;

            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        private static GameObject FindChild(GameObject parent, string name)
        {
            foreach (Transform child in parent.transform)
                if (child.name == name)
                    return child.gameObject;
            return null;
        }
    }
}
#endif

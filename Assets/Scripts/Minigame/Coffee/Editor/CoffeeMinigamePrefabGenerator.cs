#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// 「制作咖啡」小游戏的 Prefab 与资产生成器（与 CircuitMinigamePrefabGenerator 同一策略）：
    /// **默认只补缺失、绝不覆盖手调**；要恢复默认布局必须从菜单显式确认重建。
    ///
    /// 生成物放在 Assets/GameData/Minigames/ 而不是 Resources 下——
    /// 整条链路（日程表 → NeedDef → MinigameDef → Prefab / 关卡）全是强引用（§8.5）。
    ///
    /// ⚠ 本文件是 §8.5 依赖方向约束的明示例外（authoring 工具，整个类在 UNITY_EDITOR 内）：
    /// 它同时认识 Coffee 内部与宿主层的 MinigameDef / MinigameNeedDef。
    /// 约束真正管的是运行时代码：Minigame/Coffee/ 下除本文件外不得出现任何 Manager 或宿主类型引用。
    /// </summary>
    public static class CoffeeMinigamePrefabGenerator
    {
        private const string Folder = "Assets/GameData/Minigames";
        private const string LevelFolder = Folder + "/CoffeeLevels";
        private const string PrefabPath = Folder + "/CoffeeMinigame.prefab";
        private const string MinigameDefPath = Folder + "/Minigame_制作咖啡.asset";
        private const string DefaultLevelPath = LevelFolder + "/Coffee_Default.asset";
        private const string NeedDefPath = "Assets/GameData/Needs/Need_制作咖啡.asset";

        // 占位配色（无美术阶段；与修理电路的底色同一族，界面观感统一）
        private static readonly Color Backdrop = new Color(0.078f, 0.063f, 0.106f, 0.97f);
        private static readonly Color PanelTint = new Color(1f, 1f, 1f, 0.05f);
        private static readonly Color Ink = new Color(0.94f, 0.94f, 0.96f, 1f);
        private static readonly Color Muted = new Color(0.72f, 0.72f, 0.78f, 1f);
        private static readonly Color ButtonPrimary = new Color(0.24f, 0.62f, 0.44f, 0.95f);
        private static readonly Color ButtonGhost = new Color(1f, 1f, 1f, 0.12f);

        [MenuItem("MasterHouse/小游戏/创建制作咖啡资产（补齐缺失）")]
        public static void CreateIfMissing() => Generate(false);

        [MenuItem("MasterHouse/小游戏/重建制作咖啡 Prefab（覆盖手调）")]
        public static void Rebuild()
        {
            if (!EditorUtility.DisplayDialog("重建制作咖啡 Prefab",
                    "会用默认布局覆盖 " + PrefabPath + " 上的全部手调内容，且不能 Undo。\n\n" +
                    "MinigameDef / 关卡 / NeedDef 资产不受影响（只补缺失）。",
                    "重建", "取消"))
                return;
            Generate(true);
        }

        private static void Generate(bool overwritePrefab)
        {
            EnsureFolder("Assets/GameData", "Minigames");
            EnsureFolder(Folder, "CoffeeLevels");
            var created = new List<string>();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null || overwritePrefab)
            {
                prefab = BuildPrefab();
                created.Add(PrefabPath + (overwritePrefab ? "（重建）" : string.Empty));
            }
            else if (PatchPrefabIfMissing())
            {
                created.Add(PrefabPath + "（补水面节点）");
            }

            var defaultLevel = AssetDatabase.LoadAssetAtPath<CoffeeLevelDef>(DefaultLevelPath);
            if (defaultLevel == null)
            {
                defaultLevel = ScriptableObject.CreateInstance<CoffeeLevelDef>();
                AssetDatabase.CreateAsset(defaultLevel, DefaultLevelPath);
                created.Add(DefaultLevelPath);
            }

            var def = AssetDatabase.LoadAssetAtPath<MinigameDef>(MinigameDefPath);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<MinigameDef>();
                def.minigameId = "coffee";
                def.displayName = "制作咖啡";
                def.prefab = prefab;
                def.levels = new List<MinigameLevelDef> { defaultLevel };
                AssetDatabase.CreateAsset(def, MinigameDefPath);
                created.Add(MinigameDefPath);
            }
            else
            {
                // 只补空引用，不动策划已经配好的
                if (def.prefab == null)
                {
                    def.prefab = prefab;
                    EditorUtility.SetDirty(def);
                    created.Add(MinigameDefPath + "（补 prefab 引用）");
                }
                if (def.levels == null || def.levels.Count == 0)
                {
                    def.levels = new List<MinigameLevelDef> { defaultLevel };
                    EditorUtility.SetDirty(def);
                    created.Add(MinigameDefPath + "（补空关卡池）");
                }
            }

            var need = AssetDatabase.LoadAssetAtPath<MinigameNeedDef>(NeedDefPath);
            if (need == null && AssetDatabase.IsValidFolder("Assets/GameData/Needs"))
            {
                need = ScriptableObject.CreateInstance<MinigameNeedDef>();
                need.needId = "coffee";
                need.description = "想喝一杯现磨的手冲咖啡，拜托你了";
                need.minigame = def;
                AssetDatabase.CreateAsset(need, NeedDefPath);
                created.Add(NeedDefPath);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(created.Count > 0
                ? "[制作咖啡] 已创建：\n" + string.Join("\n", created) +
                  "\n\n本菜单只建小游戏自己的资产。要真正跑起来还缺两样共享内容（都是策划数据，请手动配）：" +
                  "\n① 日程表某一行的「需求」列换成 Need_制作咖啡（当前 9 行都已配了其他需求，挑一行换）" +
                  "\n② Excel/对话表.xlsx 给 Need_制作咖啡 配一行 needTalk、写一个带 StartMinigame 事件的选项，" +
                  "然后跑 Tools/导表/export_config.bat"
                : "[制作咖啡] 资产已齐全，未做修改。");
        }

        // ══════════ Prefab 布局（1920×1080 参考分辨率）══════════

        private static GameObject BuildPrefab()
        {
            var root = new GameObject("CoffeeMinigamePage", typeof(RectTransform), typeof(Image),
                typeof(CoffeeMinigameView), typeof(CoffeeMinigame));
            root.layer = 5;
            var rootRect = (RectTransform)root.transform;
            Stretch(rootRect);
            var backdrop = root.GetComponent<Image>();
            backdrop.color = Backdrop;
            backdrop.raycastTarget = true; // 挡住底下 Hub 页的点击；全屏页没有暴露在外的遮罩可点

            var view = root.GetComponent<CoffeeMinigameView>();

            BuildTopBar(rootRect, view);
            BuildProgressBar(rootRect, view);
            BuildGrind(rootRect, view);
            BuildPour(rootRect, view);
            BuildFooter(rootRect, view);

            bool ok;
            var asset = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out ok);
            if (!ok) Debug.LogError("[制作咖啡] Prefab 保存失败：" + PrefabPath);
            Object.DestroyImmediate(root);
            return asset;
        }

        private static void BuildTopBar(RectTransform parent, CoffeeMinigameView view)
        {
            var bar = Rect(parent, "TopBar", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -52), new Vector2(-160, 72));
            ImageOn(bar, PanelTint);

            view.phaseLabel = Label(bar, "Phase", "① 磨豆子", 30, Ink,
                new Vector2(0, 0), new Vector2(.5f, 1), Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter);
            view.scoreLabel = Label(bar, "Score", "研磨得分 50/50", 28, Ink,
                new Vector2(.5f, 0), new Vector2(1, 1), Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter);
        }

        private static void BuildProgressBar(RectTransform parent, CoffeeMinigameView view)
        {
            var bg = Rect(parent, "ProgressBar", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -112), new Vector2(-560, 22));
            ImageOn(bg, new Color(1f, 1f, 1f, 0.10f));

            // 填充条：代码驱动 anchorMax.x（0~1），初始为空
            var fill = Rect(bg, "Fill", new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, Vector2.zero);
            ImageOn(fill, ButtonPrimary);
            view.progressFill = fill;
        }

        private static void BuildGrind(RectTransform parent, CoffeeMinigameView view)
        {
            var grindRoot = Rect(parent, "GrindRoot", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            view.grindRoot = grindRoot;

            var area = Rect(grindRoot, "GrindArea", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                new Vector2(0, -30), new Vector2(620, 620));
            view.grindArea = area;

            view.grindContentRoot = Rect(area, "GrindContent", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                Vector2.zero, Vector2.zero);

            // 点模板：运行时被隐藏并克隆成环轮廓与障碍弧段（§16.2 动态列表项）
            var template = Rect(view.grindContentRoot, "GrindDotTemplate", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                Vector2.zero, new Vector2(16, 16));
            var templateImage = ImageOn(template, new Color(1f, 1f, 1f, 0.18f));
            templateImage.raycastTarget = false;
            view.grindDotTemplate = templateImage;

            // 指针放模板层之后：兄弟顺序即绘制顺序，指针永远压在点上面
            var pointer = Rect(area, "Pointer", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                Vector2.zero, new Vector2(24, 24));
            var pointerImage = ImageOn(pointer, new Color(0.98f, 0.83f, 0.30f, 1f));
            pointerImage.raycastTarget = false;
            view.pointer = pointer;
            view.pointerImage = pointerImage;
        }

        private static void BuildPour(RectTransform parent, CoffeeMinigameView view)
        {
            var pourRoot = Rect(parent, "PourRoot", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            view.pourRoot = pourRoot;

            // 杯是圆形（2026-08-15 测试反馈）：区域取正方形，判定用内切圆（PourGame.InsideCup），
            // 视觉用内置 Knob 圆形贴图占位，视觉与判定同圆
            var cup = Rect(pourRoot, "CupArea", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                new Vector2(0, -30), new Vector2(460, 460));
            var cupImage = ImageOn(cup, new Color(1f, 1f, 1f, 0.10f));
            cupImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            cupImage.preserveAspect = true;
            cupImage.raycastTarget = false; // 判定走 RectTransformUtility，不吃射线
            view.cupArea = cup;
            view.cupImage = cupImage;

            AddWaterImage(view);

            Label(cup, "Hint", "杯", 40, Muted,
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(120, 60), TextAnchor.MiddleCenter);
        }

        /// <summary>
        /// 节点粒度的「补缺失」：给已存在的 Prefab 补后加的水面节点，不动其他手调内容。
        /// 以后再加新节点，照这个模式扩展本方法即可。
        /// </summary>
        private static bool PatchPrefabIfMissing()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var view = root.GetComponent<CoffeeMinigameView>();
                if (view == null || view.cupArea == null || view.waterImage != null) return false;
                AddWaterImage(view);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>杯内水面：铺满 cupArea，材质由 CoffeeMinigame 运行时创建（Prefab 不挂材质资产）。</summary>
        private static void AddWaterImage(CoffeeMinigameView view)
        {
            var water = Rect(view.cupArea, "Water", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            water.SetAsFirstSibling(); // 兄弟顺序即绘制顺序：压在杯底图之上、提示字之下
            var image = ImageOn(water, Color.white);
            image.raycastTarget = false;
            view.waterImage = image;
        }

        private static void BuildFooter(RectTransform parent, CoffeeMinigameView view)
        {
            view.messageLabel = Label(parent, "Message", string.Empty, 26, Ink,
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 116), new Vector2(-400, 40), TextAnchor.MiddleCenter);

            view.tuningLabel = Label(parent, "Tuning", string.Empty, 20, Muted,
                new Vector2(0, 0), new Vector2(0, 0), new Vector2(300, 56), new Vector2(560, 32), TextAnchor.MiddleLeft);
            // 调参信息只给测试场景看：默认隐藏，由 CoffeeLevelTestBootstrap 显式打开，正式局不显示
            view.tuningLabel.gameObject.SetActive(false);

            view.abortButton = Button(parent, "AbortButton", "放弃", ButtonGhost,
                new Vector2(-140, 64), new Vector2(180, 68));
        }

        // ══════════ 绘制原语（与 CircuitMinigamePrefabGenerator 同一套）══════════

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, .5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 min, Vector2 max,
            Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static Image ImageOn(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private static Text Label(Transform parent, string name, string value, int size, Color color,
            Vector2 min, Vector2 max, Vector2 position, Vector2 dimensions, TextAnchor alignment)
        {
            var text = Rect(parent, name, min, max, position, dimensions).gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            text.text = value;
            return text;
        }

        private static Button Button(Transform parent, string name, string caption, Color color,
            Vector2 position, Vector2 size)
        {
            var rect = Rect(parent, name, new Vector2(1, 0), new Vector2(1, 0), position, size);
            var image = ImageOn(rect, color);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            Label(rect, "Caption", caption, 28, Ink, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                TextAnchor.MiddleCenter);
            return button;
        }

        private static void EnsureFolder(string parent, string leaf)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + leaf))
                AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
#endif

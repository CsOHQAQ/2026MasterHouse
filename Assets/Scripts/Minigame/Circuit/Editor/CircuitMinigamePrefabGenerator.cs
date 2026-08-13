#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// 「修理电路」小游戏的 Prefab 与资产生成器。
    ///
    /// 与局外 OutGameUIPrefabGenerator 同一策略：**默认只补缺失、绝不覆盖手调**；
    /// 要恢复默认布局必须从菜单显式确认重建。
    ///
    /// 生成物放在 Assets/GameData/Minigames/ 而不是 Resources 下——
    /// `MinigameDef.prefab` 是强类型引用（说明文档 §3.6 待确认 #2 拍板），
    /// 整条链路（日程表 → NeedDef → MinigameDef → Prefab / 关卡）全是强引用，
    /// 没有一处按路径字符串加载，所以不需要进 Resources。
    ///
    /// ⚠ **本文件是 §3.1 依赖方向约束的唯一明示例外**：它同时认识 Circuit 内部（要搭 Prefab）
    /// 与宿主层的 MinigameDef / MinigameNeedDef（要把整条链路串起来）。
    /// 这是 authoring 工具的固有属性——它的产物是资产，不是运行时行为，
    /// 打包后根本不存在（整个类在 UNITY_EDITOR 内）。
    /// 约束真正管的是**运行时代码**：`Minigame/Circuit/` 下除本文件外，
    /// 不得出现任何 Manager 或宿主类型的引用。
    /// </summary>
    public static class CircuitMinigamePrefabGenerator
    {
        private const string Folder = "Assets/GameData/Minigames";
        private const string PrefabPath = Folder + "/CircuitMinigame.prefab";
        private const string MinigameDefPath = Folder + "/Minigame_修理电路.asset";
        private const string NeedDefPath = "Assets/GameData/Needs/Need_修理电路.asset";
        private const string SampleLevelPath = "Assets/GameData/Levels/General_1_Intro00.asset";
        private const string SchedulePath = "Assets/Resources/OutGameUI/VisitorScheduleTable.asset";

        // 占位配色（无美术阶段）
        private static readonly Color Backdrop = new Color(0.078f, 0.063f, 0.106f, 0.97f);
        private static readonly Color PanelTint = new Color(1f, 1f, 1f, 0.05f);
        private static readonly Color Ink = new Color(0.94f, 0.94f, 0.96f, 1f);
        private static readonly Color Muted = new Color(0.72f, 0.72f, 0.78f, 1f);
        private static readonly Color ButtonPrimary = new Color(0.24f, 0.62f, 0.44f, 0.95f);
        private static readonly Color ButtonGhost = new Color(1f, 1f, 1f, 0.12f);

        [MenuItem("MasterHouse/小游戏/创建修理电路资产（补齐缺失）")]
        public static void CreateIfMissing() => Generate(false);

        [MenuItem("MasterHouse/小游戏/重建修理电路 Prefab（覆盖手调）")]
        public static void Rebuild()
        {
            if (!EditorUtility.DisplayDialog("重建修理电路 Prefab",
                    "会用默认布局覆盖 " + PrefabPath + " 上的全部手调内容，且不能 Undo。\n\n" +
                    "MinigameDef / NeedDef 资产不受影响（只补缺失）。",
                    "重建", "取消"))
                return;
            Generate(true);
        }

        private static void Generate(bool overwritePrefab)
        {
            EnsureFolder("Assets/GameData", "Minigames");
            var created = new List<string>();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null || overwritePrefab)
            {
                prefab = BuildPrefab();
                created.Add(PrefabPath + (overwritePrefab ? "（重建）" : string.Empty));
            }

            var def = AssetDatabase.LoadAssetAtPath<MinigameDef>(MinigameDefPath);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<MinigameDef>();
                def.minigameId = "circuit";
                def.displayName = "修理电路";
                def.prefab = prefab;
                var sample = AssetDatabase.LoadAssetAtPath<LevelDef>(SampleLevelPath);
                if (sample != null) def.levels = new List<MinigameLevelDef> { sample };
                AssetDatabase.CreateAsset(def, MinigameDefPath);
                created.Add(MinigameDefPath);
            }
            else if (def.prefab == null)
            {
                // 只补空引用，不动策划已经配好的
                def.prefab = prefab;
                EditorUtility.SetDirty(def);
                created.Add(MinigameDefPath + "（补 prefab 引用）");
            }

            var need = AssetDatabase.LoadAssetAtPath<MinigameNeedDef>(NeedDefPath);
            if (need == null && AssetDatabase.IsValidFolder("Assets/GameData/Needs"))
            {
                need = ScriptableObject.CreateInstance<MinigameNeedDef>();
                need.needId = "circuit_intro";
                need.description = "家里的电路坏了，想请你帮忙接一下";
                need.minigame = def;
                AssetDatabase.CreateAsset(need, NeedDefPath);
                created.Add(NeedDefPath);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(created.Count > 0
                ? "[修理电路] 已创建：\n" + string.Join("\n", created) +
                  "\n\n本菜单只建小游戏自己的资产。要真正跑起来还缺两样共享内容：" +
                  "\n① 一条带需求的日程 → 菜单 MasterHouse → 小游戏 → 接通测试链路（只补空缺）" +
                  "\n② 一段能触发小游戏的对话 → 在 Excel/对话表.xlsx 里给 Need_修理电路 配 needTalk"
                : "[修理电路] 资产已齐全，未做修改。");
        }

        // ══════════ 测试链路：补共享内容里的空缺 ══════════

        /// <summary>
        /// 把「日程表 → 需求 → 对话 → 小游戏」这条链路上属于**共享内容**的空缺补上。
        ///
        /// 与上面的生成器分开成两个菜单，是因为这里动的是策划的数据（日程表）而不是小游戏自己的资产。
        /// 所以只做加法、**只填空缺**：日程条目已配需求就跳过。
        ///
        /// **对话那一段本菜单不再插手**（2026-08-14 对话资源重构）：对话内容的唯一源是
        /// Excel/对话表.xlsx，代码生成对话组只会和它打架。小游戏类需求的开局分支请在 Excel 第一页
        /// 给 Need_修理电路 配一行 needTalk、第二页写一个带 StartMinigame 事件的选项。
        /// </summary>
        [MenuItem("MasterHouse/小游戏/接通测试链路（只补空缺）")]
        public static void WireTestPath()
        {
            var def = AssetDatabase.LoadAssetAtPath<MinigameDef>(MinigameDefPath);
            var need = AssetDatabase.LoadAssetAtPath<MinigameNeedDef>(NeedDefPath);
            if (def == null || need == null)
            {
                Debug.LogError("[修理电路] 请先执行「创建修理电路资产（补齐缺失）」，" +
                               "本菜单需要 MinigameDef 与 NeedDef 已经存在。");
                return;
            }

            var log = new List<string>();
            WireScheduleNeed(need, log);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log((log.Count > 0
                          ? "[修理电路] 日程已接通：\n" + string.Join("\n", log)
                          : "[修理电路] 日程条目都已配过需求，未改动") +
                      "\n\n对话内容请在 Excel/对话表.xlsx 里配：第一页给 Need_修理电路 加一行 needTalk，" +
                      "第二页写一个带 StartMinigame 事件的选项，然后跑 Tools/导表/export_config.bat。");
        }

        /// <summary>
        /// 给日程表**第一条没配需求的条目**填上小游戏需求。
        /// 现状是所有条目的需求都空着，而 VisitorManager 会跳过没有需求的条目——
        /// 也就是一个访客都不会投放，游戏根本走不到小游戏这一步。
        /// </summary>
        private static void WireScheduleNeed(MinigameNeedDef need, List<string> log)
        {
            var schedule = AssetDatabase.LoadAssetAtPath<VisitorScheduleTable>(SchedulePath);
            if (schedule == null)
            {
                Debug.LogWarning("[修理电路] 找不到日程表，跳过：" + SchedulePath);
                return;
            }

            for (int i = 0; i < schedule.entries.Count; i++)
            {
                var entry = schedule.entries[i];
                if (entry == null || entry.need != null) continue;
                entry.need = need;
                EditorUtility.SetDirty(schedule);
                log.Add($"{SchedulePath}：第 {i + 1} 条（第 {entry.day} 天 " +
                        $"{entry.appearMinute / 60:00}:{entry.appearMinute % 60:00}）已配上修理电路需求");
                return;
            }

            log.Add(SchedulePath + "：所有条目都已配过需求，未改动");
        }

        // ══════════ Prefab 布局（1920×1080 参考分辨率）══════════

        private static GameObject BuildPrefab()
        {
            var root = new GameObject("CircuitMinigamePage", typeof(RectTransform), typeof(Image),
                typeof(CircuitMinigameView), typeof(CircuitMinigame));
            root.layer = 5;
            var rootRect = (RectTransform)root.transform;
            Stretch(rootRect);
            var backdrop = root.GetComponent<Image>();
            backdrop.color = Backdrop;
            backdrop.raycastTarget = true; // 挡住底下 Hub 页的点击；全屏页没有暴露在外的遮罩可点

            var view = root.GetComponent<CircuitMinigameView>();

            BuildTopBar(rootRect, view);
            BuildPalette(rootRect, view);
            BuildBoard(rootRect, view);
            BuildFooter(rootRect, view);

            bool ok;
            var asset = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out ok);
            if (!ok) Debug.LogError("[修理电路] Prefab 保存失败：" + PrefabPath);
            Object.DestroyImmediate(root);
            return asset;
        }

        private static void BuildTopBar(RectTransform parent, CircuitMinigameView view)
        {
            var bar = Rect(parent, "TopBar", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -52), new Vector2(-160, 72));
            ImageOn(bar, PanelTint);

            view.linkBudgetLabel = Label(bar, "LinkBudget", "导线 0/0", 28, Ink,
                new Vector2(0, 0), new Vector2(.33f, 1), Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter);
            view.pieceBudgetLabel = Label(bar, "PieceBudget", "中转件 0/0", 28, Ink,
                new Vector2(.33f, 0), new Vector2(.66f, 1), Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter);
            view.litLabel = Label(bar, "Lit", "已点亮 0/0", 28, Ink,
                new Vector2(.66f, 0), new Vector2(1, 1), Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter);
        }

        private static void BuildPalette(RectTransform parent, CircuitMinigameView view)
        {
            var panel = Rect(parent, "Palette", new Vector2(0, 0), new Vector2(0, 1), new Vector2(210, -20), new Vector2(260, -200));
            ImageOn(panel, PanelTint);

            Label(panel, "Title", "件库", 26, Muted,
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -30), new Vector2(0, 48), TextAnchor.MiddleCenter);

            var list = Rect(panel, "PaletteRoot", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -70), new Vector2(-24, 0));
            list.pivot = new Vector2(.5f, 1f);
            var layout = list.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10;
            layout.childControlHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            var fitter = list.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            view.paletteRoot = list;

            // 模板：运行时被隐藏并克隆（§16.2 动态列表项）
            var template = Rect(list, "PaletteItemTemplate", Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0, 84));
            // VerticalLayoutGroup 的 childControlHeight 关着，高度得由 LayoutElement 明确给出，
            // 否则条目高度取决于拉伸锚点的解算结果，不同分辨率下会飘
            var layoutElement = template.gameObject.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = 84;
            layoutElement.minHeight = 84;
            var item = template.gameObject.AddComponent<CircuitPaletteItemView>();
            item.background = ImageOn(template, ButtonGhost);
            item.button = template.gameObject.AddComponent<Button>();
            item.button.targetGraphic = item.background;
            item.label = Label(template, "Name", "中转件", 22, Ink,
                new Vector2(0, .45f), new Vector2(1, 1), Vector2.zero, new Vector2(-16, 0), TextAnchor.MiddleLeft);
            item.count = Label(template, "Count", "0/0", 20, Muted,
                new Vector2(0, 0), new Vector2(1, .45f), Vector2.zero, new Vector2(-16, 0), TextAnchor.MiddleLeft);
            view.paletteItemTemplate = item;
        }

        private static void BuildBoard(RectTransform parent, CircuitMinigameView view)
        {
            // 棋盘可用区：左让开件库、右留边、上让开预算条、下让开按钮条。
            // 它的位置与大小由 Prefab 说了算；格子大小由 CircuitBoard 按关卡行列数在运行时算
            var area = Rect(parent, "BoardArea", new Vector2(0, 0), new Vector2(1, 1), new Vector2(180, 10), new Vector2(-760, -220));
            view.boardArea = area;

            view.gridRoot = Rect(area, "GridRoot", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            view.linkRoot = Rect(area, "LinkRoot", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            view.nodeRoot = Rect(area, "NodeRoot", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            view.previewRoot = Rect(area, "PreviewRoot", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            // 兄弟顺序即绘制顺序：格子 → 导线 → 节点 → 预览（预览压最上）
        }

        private static void BuildFooter(RectTransform parent, CircuitMinigameView view)
        {
            view.messageLabel = Label(parent, "Message", string.Empty, 24, new Color(1f, .72f, .35f, 1f),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 116), new Vector2(-400, 40), TextAnchor.MiddleCenter);

            view.finishButton = Button(parent, "FinishButton", "完成", ButtonPrimary,
                new Vector2(-140, 64), new Vector2(200, 68));
            view.abortButton = Button(parent, "AbortButton", "放弃", ButtonGhost,
                new Vector2(-360, 64), new Vector2(180, 68));
        }

        // ══════════ 绘制原语 ══════════

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

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
        private const string TopStatusBarPrefabPath = Folder + "/CircuitTopStatusBar.prefab";
        private const string VisualStylePath = Folder + "/CircuitVisualStyle_Default.asset";
        private const string UIStylePath = Folder + "/CircuitUIStyle_Default.asset";
        private const string MinigameDefPath = Folder + "/Minigame_修理电路.asset";
        private const string NeedDefPath = "Assets/GameData/Needs/Need_修理电路.asset";
        private const string LevelFolder = "Assets/GameData/Levels";
        private const string SampleLevelPath = LevelFolder + "/General_1_Intro00.asset";
        private const string SchedulePath = "Assets/Resources/OutGameUI/VisitorScheduleTable.asset";

        // ── 教程包链路（2026-08-16）：课程包 → 专属 MinigameDef → 专属需求 ──
        private const string LessonPackPath = LevelFolder + "/Pack_电路教程.asset";
        private const string TutorialDefPath = Folder + "/Minigame_电路教程.asset";
        private const string TutorialNeedPath = "Assets/GameData/Needs/Need_电路教程.asset";

        /// <summary>课程包默认收哪些关：资产名里带这个词的就是教程关。仅生成期用一次，运行时不认命名约定。</summary>
        private const string LessonNameMarker = "Intro";

        // 占位配色（无美术阶段）
        private static readonly Color Backdrop = new Color(0.078f, 0.063f, 0.106f, 0.97f);
        private static readonly Color PanelTint = new Color(1f, 1f, 1f, 0.05f);
        private static readonly Color Ink = new Color(0.94f, 0.94f, 0.96f, 1f);
        private static readonly Color Muted = new Color(0.72f, 0.72f, 0.78f, 1f);
        private static readonly Color ButtonPrimary = new Color(0.24f, 0.62f, 0.44f, 0.95f);
        private static readonly Color ButtonGhost = new Color(1f, 1f, 1f, 0.12f);

        private const string NodeFrameGreyPath = "Assets/Developers/taslopece/ArtTest/NodeFrameGrey.png";

        // 音效剪辑（2026-08-20）：复用全局音效素材，但**是独立引用**——
        // 将来给电路做专属素材时换 Prefab 上的引用即可，不会动到家具拿起音 / 基础点击音 / 声望增减音。
        private const string DrawStepClipPath = "Assets/Resources/SoundEffect/2_Pickup_260813_1.mp3";
        private const string NodePlaceClipPath = "Assets/Resources/SoundEffect/2_Putdown_260812.mp3";
        private const string LinkConnectClipPath = "Assets/Resources/SoundEffect/1_Button_260812.mp3";
        private const string BatteryLitClipPath = "Assets/Resources/SoundEffect/4_ScoreGain_260812.mp3";
        private const string BatteryUnlitClipPath = "Assets/Resources/SoundEffect/4_ScoreLose_260813_1.mp3";

        // ── 通关结算正式美术（与咖啡结算同样的层级与入场时序，但使用电路专属资产）──
        private const string ResultArtDir = "Assets/PC ui 2.0/通关结算/";
        private const float ResultArtScale = 1920f / 5120f;
        // 底板文件是带大面积透明留白的横向画布，实际可见的竖向卡只占中段；
        // 因此不能和统计条共用导入像素缩放。底板整体放大，统计条反向缩小，才和参考稿比例一致。
        private const float ResultBoardScale = 1.5f;
        private const float ResultStatsLocalScale = 0.45f;
        private const float ResultStarLocalScale = 0.55f;
        private static readonly Color ResultScrim = new Color(0.04f, 0.03f, 0.06f, 0.68f);
        private static readonly Color ResultInk = new Color(0.22f, 0.34f, 0.55f, 1f);
        private static readonly Color ResultMuted = new Color(0.38f, 0.47f, 0.60f, 1f);

        [MenuItem("MasterHouse/小游戏/创建修理电路资产（补齐缺失）")]
        public static void CreateIfMissing() => Generate(false);

        /// <summary>
        /// 旧 Prefab 的深色小结没有新版结算层需要的序列化引用；只改脚本不会让它凭空长出节点。
        /// 这一段是一次性迁移：域重载后若发现缺新版结算节点，自动调用「补齐缺失」写入 Prefab。
        /// 已迁移的项目不会再保存资产，也不会覆盖旧小结节点或任何手调内容。
        /// </summary>
        [InitializeOnLoadMethod]
        private static void UpgradeResultPopupAfterDomainReload()
        {
            EditorApplication.delayCall += () =>
            {
                // 正在 Play Mode 时 Unity 不应碰资产；退出运行、脚本重载后会自然再执行一次。
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                var view = prefab != null ? prefab.GetComponent<CircuitMinigameView>() : null;
                if (view != null && NeedsResultPopupUpgrade(view))
                    Generate(false);
            };
        }

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

            var visualStyle = AssetDatabase.LoadAssetAtPath<CircuitVisualStyleConfig>(VisualStylePath);
            if (visualStyle == null)
            {
                visualStyle = ScriptableObject.CreateInstance<CircuitVisualStyleConfig>();
                AssetDatabase.CreateAsset(visualStyle, VisualStylePath);
                created.Add(VisualStylePath);
            }

            var uiStyle = AssetDatabase.LoadAssetAtPath<CircuitUIStyleConfig>(UIStylePath);
            if (uiStyle == null)
            {
                uiStyle = ScriptableObject.CreateInstance<CircuitUIStyleConfig>();
                AssetDatabase.CreateAsset(uiStyle, UIStylePath);
                created.Add(UIStylePath);
            }

            var topStatusBarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TopStatusBarPrefabPath);
            if (topStatusBarPrefab == null)
            {
                topStatusBarPrefab = BuildTopStatusBarPrefab();
                created.Add(TopStatusBarPrefabPath);
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null || overwritePrefab)
            {
                prefab = BuildPrefab(visualStyle, uiStyle, topStatusBarPrefab);
                created.Add(PrefabPath + (overwritePrefab ? "（重建）" : string.Empty));
            }
            else if (prefab.GetComponent<CircuitMinigameView>() is { } prefabView &&
                     (prefabView.visualStyle == null || prefabView.uiStyle == null || NeedsAudioFill(prefabView) ||
                      NeedsResultPopupUpgrade(prefabView)))
            {
                // 「补齐缺失」不能为了补一个引用覆盖整份手调 Prefab；只改字段袋里的空引用。
                var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
                try
                {
                    var view = contents.GetComponent<CircuitMinigameView>();
                    if (view.visualStyle == null) view.visualStyle = visualStyle;
                    if (view.uiStyle == null) view.uiStyle = uiStyle;
                    var audioFilled = AssignAudioClips(view);
                    var resultFilled = EnsureResultPopup(view, (RectTransform)contents.transform);
                    prefab = PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                    var notes = new List<string> { "补样式引用" };
                    if (audioFilled) notes.Add("音效引用");
                    if (resultFilled) notes.Add("通关结算弹窗");
                    created.Add(PrefabPath + "（" + string.Join("/", notes) + "）");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
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

        // ══════════ 教程包链路（2026-08-16）══════════

        /// <summary>
        /// 建出「课程包 → Minigame_电路教程 → Need_电路教程」这条链路，**只补缺失、绝不覆盖**。
        ///
        /// 课程包默认收 <c>GameData/Levels</c> 下所有名字带 <see cref="LessonNameMarker"/> 的关卡，按资产名排序。
        /// 这个自动收集**只发生在生成这一刻**：产物是一张写死了顺序的资产，之后策划怎么增删排序都以资产为准，
        /// 运行时不认任何命名约定（见 <see cref="CircuitLessonPackDef"/> 的类注释）。
        ///
        /// 教学文案从各关的 <c>DeveloperNotes</c> 拷一份作为初稿——那是给策划自己看的字段、
        /// 明确不参与运行时，这里只是 authoring 期的一次性复制，方便在此基础上改成对玩家说的话。
        /// </summary>
        [MenuItem("MasterHouse/小游戏/创建电路教程资产（补齐缺失）")]
        public static void CreateTutorialIfMissing()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogError("[电路教程] 找不到 " + PrefabPath +
                               "，请先执行菜单 MasterHouse → 小游戏 → 创建修理电路资产（补齐缺失）。" +
                               "教程与单关共用同一个 Prefab，不另建一套。");
                return;
            }

            var created = new List<string>();

            var pack = AssetDatabase.LoadAssetAtPath<CircuitLessonPackDef>(LessonPackPath);
            if (pack == null)
            {
                pack = ScriptableObject.CreateInstance<CircuitLessonPackDef>();
                pack.DeveloperNotes = "自动收集 " + LevelFolder + " 下名字带 " + LessonNameMarker +
                                      " 的关卡建成的初稿，顺序与文案请按需调整。";
                pack.Lessons = CollectLessons();
                AssetDatabase.CreateAsset(pack, LessonPackPath);
                created.Add($"{LessonPackPath}（收了 {pack.Lessons.Count} 关）");
            }

            var def = AssetDatabase.LoadAssetAtPath<MinigameDef>(TutorialDefPath);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<MinigameDef>();
                def.minigameId = "circuit_tutorial";
                def.displayName = "电路教程";
                def.prefab = prefab;
                def.levels = new List<MinigameLevelDef> { pack };
                // 「必须全亮才能进下一关」⇒ 唯一出口是打穿全程 ⇒ 分数恒为 100。
                // 三档都配 100，让「只有完美一个结局」在资产里也是显式的，而不是留 1/60/100 让人以为有梯度
                def.plainMin = 100;
                def.satisfiedMin = 100;
                def.perfectMin = 100;
                AssetDatabase.CreateAsset(def, TutorialDefPath);
                created.Add(TutorialDefPath);
            }

            var need = AssetDatabase.LoadAssetAtPath<MinigameNeedDef>(TutorialNeedPath);
            if (need == null && AssetDatabase.IsValidFolder("Assets/GameData/Needs"))
            {
                need = ScriptableObject.CreateInstance<MinigameNeedDef>();
                need.needId = "circuit_tutorial";
                need.description = "想从头学一遍怎么修电路";
                need.minigame = def;
                need.level = pack; // 点名课程包：教程不参与关卡池随机
                AssetDatabase.CreateAsset(need, TutorialNeedPath);
                created.Add(TutorialNeedPath);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log((created.Count > 0
                          ? "[电路教程] 已创建：\n" + string.Join("\n", created)
                          : "[电路教程] 资产已齐全，未做修改。") +
                      "\n\n还缺两样共享内容才跑得通：" +
                      "\n① 日程表某一行的「需求」列指向 Need_电路教程" +
                      "\n② 在 Excel/对话表.xlsx 里给 Need_电路教程 配 needTalk，" +
                      "第二页写一个带 StartMinigame 事件的选项，然后跑 Tools/导表/export_config.bat");
        }

        /// <summary>按资产名排序收集教程关，并用 DeveloperNotes 起草教学文案。</summary>
        private static List<CircuitLessonEntry> CollectLessons()
        {
            var levels = new List<LevelDef>();
            foreach (var guid in AssetDatabase.FindAssets("t:LevelDef", new[] { LevelFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var level = AssetDatabase.LoadAssetAtPath<LevelDef>(path);
                if (level == null) continue;
                if (level.name.IndexOf(LessonNameMarker, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                levels.Add(level);
            }
            levels.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            var lessons = new List<CircuitLessonEntry>();
            for (int i = 0; i < levels.Count; i++)
                lessons.Add(new CircuitLessonEntry
                {
                    Level = levels[i],
                    Title = $"第 {i + 1} 课",
                    Brief = levels[i].DeveloperNotes,
                });

            if (lessons.Count == 0)
                Debug.LogWarning("[电路教程] 在 " + LevelFolder + " 下没找到名字带 " + LessonNameMarker +
                                 " 的关卡，课程包建出来是空的。");
            return lessons;
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

        private static GameObject BuildPrefab(CircuitVisualStyleConfig visualStyle, CircuitUIStyleConfig uiStyle,
            GameObject topStatusBarPrefab)
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
            view.visualStyle = visualStyle;
            view.uiStyle = uiStyle;
            view.levelBackground = backdrop;
            AssignAudioClips(view);

            BuildTopBar(rootRect, view, topStatusBarPrefab);
            BuildPalette(rootRect, view, uiStyle);
            BuildBoard(rootRect, view);
            BuildLessonPanel(rootRect, view);
            BuildFooter(rootRect, view);
            BuildSummaryPanel(rootRect, view); // 最后建 = 兄弟顺序最靠后 = 压在所有内容之上

            bool ok;
            var asset = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out ok);
            if (!ok) Debug.LogError("[修理电路] Prefab 保存失败：" + PrefabPath);
            Object.DestroyImmediate(root);
            return asset;
        }

        /// <summary>Prefab 上还有没有空着的音效槽——决定「补齐缺失」要不要为音效开一次 Prefab。</summary>
        private static bool NeedsAudioFill(CircuitMinigameView view) =>
            view.drawStepClip == null || view.nodePlaceClip == null || view.linkConnectClip == null ||
            view.batteryLitClip == null || view.batteryUnlitClip == null;

        /// <summary>
        /// 旧 Prefab 的小结是深色占位卡；新版多了 CanvasGroup、正式底板、统计与星级等引用。
        /// 缺任一项时在旧节点旁新建 ResultPopup 并改写 View 引用，旧节点不删，避免覆盖任何手调。
        /// </summary>
        private static bool NeedsResultPopupUpgrade(CircuitMinigameView view) =>
            view.summaryGroup == null || view.summaryBoard == null || view.summaryLitValue == null ||
            view.summaryWireValue == null || view.summaryScoreValue == null || view.summaryStars == null ||
            view.summaryStars.Length != 3 || view.summaryStars[0] == null || view.summaryStars[1] == null ||
            view.summaryStars[2] == null;

        private static bool EnsureResultPopup(CircuitMinigameView view, RectTransform parent)
        {
            if (!NeedsResultPopupUpgrade(view)) return false;
            BuildSummaryPanel(parent, view);
            return true;
        }

        /// <summary>
        /// 补本小游戏的五个音效剪辑（2026-08-20）：**只补空的**，已手动换过的不动。
        /// 剪辑不进音效表——这些是本小游戏的专属表现，配在自己的 Prefab 上更就近（换音 = 换这里的引用）。
        /// </summary>
        private static bool AssignAudioClips(CircuitMinigameView view)
        {
            var changed = false;
            changed |= FillClip(ref view.drawStepClip, DrawStepClipPath, "描线音");
            changed |= FillClip(ref view.nodePlaceClip, NodePlaceClipPath, "落件音");
            changed |= FillClip(ref view.linkConnectClip, LinkConnectClipPath, "接线音");
            changed |= FillClip(ref view.batteryLitClip, BatteryLitClipPath, "电池满足音");
            changed |= FillClip(ref view.batteryUnlitClip, BatteryUnlitClipPath, "电池失去满足音");
            return changed;
        }

        /// <summary>空引用才填，填不到只警告——留空的后果是「该处静音」，不该拦住整条补齐流程。</summary>
        private static bool FillClip(ref AudioClip slot, string path, string label)
        {
            if (slot != null) return false;
            slot = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (slot != null) return true;
            Debug.LogWarning($"[修理电路] 未找到{label}：{path}（留空 = 该处静音）");
            return false;
        }

        private static GameObject BuildTopStatusBarPrefab()
        {
            var root = new GameObject("CircuitTopStatusBar", typeof(RectTransform), typeof(Image),
                typeof(CircuitTopStatusBarView));
            root.layer = 5;
            var rect = (RectTransform)root.transform;
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(0, -80);
            rect.sizeDelta = new Vector2(-160, 120);

            var bar = root.GetComponent<CircuitTopStatusBarView>();
            bar.background = root.GetComponent<Image>();
            bar.background.color = PanelTint;
            PopulateTopStatusBar(rect, bar);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, TopStatusBarPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static void BuildTopBar(RectTransform parent, CircuitMinigameView view, GameObject topStatusBarPrefab)
        {
            var instance = PrefabUtility.InstantiatePrefab(topStatusBarPrefab, parent) as GameObject;
            if (instance == null)
            {
                Debug.LogError("[修理电路] 顶部状态条 Prefab 实例化失败：" + TopStatusBarPrefabPath);
                return;
            }
            instance.name = "TopBar";
            var bar = (RectTransform)instance.transform;
            bar.anchorMin = new Vector2(0, 1);
            bar.anchorMax = new Vector2(1, 1);
            bar.pivot = new Vector2(.5f, .5f);
            bar.anchoredPosition = new Vector2(0, -80);
            bar.sizeDelta = new Vector2(-160, 120);
            view.topStatusBar = instance.GetComponent<CircuitTopStatusBarView>();
        }

        private static void PopulateTopStatusBar(RectTransform bar, CircuitTopStatusBarView view)
        {
            // 左侧：图标 + 标题 + 副标题
            var iconRect = Rect(bar, "Icon", new Vector2(0.02f, 0.15f), new Vector2(0.12f, 0.85f),
                Vector2.zero, Vector2.zero);
            view.icon = ImageOn(iconRect, new Color(0.35f, 0.72f, 0.85f, 1f));
            view.icon.preserveAspect = true;

            view.titleLabel = Label(bar, "Title", "修理电路", 32, Ink,
                new Vector2(0.14f, 0.45f), new Vector2(0.40f, 0.80f), Vector2.zero, Vector2.zero, TextAnchor.MiddleLeft);

            view.subtitleLabel = Label(bar, "Subtitle", "在有限的格子内连通电路！", 22, Muted,
                new Vector2(0.14f, 0.15f), new Vector2(0.40f, 0.50f), Vector2.zero, Vector2.zero, TextAnchor.MiddleLeft);

            // 课程包进度（左上角小字，单关时隐藏）
            view.progressLabel = Label(bar, "Progress", "第 1/1 关", 22, Muted,
                new Vector2(0.02f, 0.78f), new Vector2(0.25f, 0.95f), Vector2.zero, Vector2.zero, TextAnchor.MiddleLeft);

            // 右侧状态信息
            view.linkBudgetLabel = Label(bar, "LinkBudget", "已消耗格子数 (0/0)", 26, Ink,
                new Vector2(0.55f, 0.52f), new Vector2(0.78f, 0.88f), Vector2.zero, Vector2.zero, TextAnchor.MiddleRight);

            view.pieceBudgetLabel = Label(bar, "PieceBudget", "已使用中转件 (0/0)", 26, Ink,
                new Vector2(0.55f, 0.12f), new Vector2(0.78f, 0.52f), Vector2.zero, Vector2.zero, TextAnchor.MiddleRight);

            view.litLabel = Label(bar, "Lit", "已点亮 0/0", 24, Muted,
                new Vector2(0.80f, 0.25f), new Vector2(0.96f, 0.75f), Vector2.zero, Vector2.zero, TextAnchor.MiddleRight);
        }

        private static void BuildPalette(RectTransform parent, CircuitMinigameView view, CircuitUIStyleConfig uiStyle)
        {
            var panel = Rect(parent, "Palette", new Vector2(0, 0), new Vector2(0, 1), new Vector2(210, -20), new Vector2(260, -200));
            view.palettePanelBackground = ImageOn(panel, uiStyle != null ? uiStyle.palettePanelColor : PanelTint);
            ApplySlicedSprite(view.palettePanelBackground, uiStyle != null ? uiStyle.palettePanelBackgroundSprite : null);

            Label(panel, "Title", "件库", 26, Muted,
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -30), new Vector2(0, 48), TextAnchor.MiddleCenter);

            float contentTopPadding = uiStyle != null ? uiStyle.paletteContentTopPadding : 78f;
            var list = Rect(panel, "PaletteRoot", new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -contentTopPadding), new Vector2(-24, 0));
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
            var template = Rect(list, "PaletteItemTemplate", Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0, 174));
            // VerticalLayoutGroup 的 childControlHeight 关着，高度得由 LayoutElement 明确给出，
            // 否则条目高度取决于拉伸锚点的解算结果，不同分辨率下会飘
            var layoutElement = template.gameObject.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = 174;
            layoutElement.minHeight = 0;
            var item = template.gameObject.AddComponent<CircuitPaletteItemView>();
            item.button = template.gameObject.AddComponent<Button>();
            item.layoutElement = layoutElement;
            var piece = Rect(template, "PieceBackground", new Vector2(.5f, 1), new Vector2(.5f, 1),
                new Vector2(0, -8), new Vector2(160, 96));
            item.background = ImageOn(piece, uiStyle != null ? uiStyle.palettePieceNormalColor : ButtonGhost);
            ApplySlicedSprite(item.background, uiStyle != null ? uiStyle.palettePieceBackgroundSprite : null);
            item.button.targetGraphic = item.background;
            var icon = Rect(piece, "FunctionIcon", Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-24, -24));
            item.functionIcon = icon.gameObject.AddComponent<Image>();
            item.functionIcon.preserveAspect = true;
            item.functionIcon.raycastTarget = false;
            var countRoot = Rect(piece, "CountDigits", new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-8, -8), new Vector2(34, 34));
            countRoot.pivot = Vector2.one;
            item.countDigitRoot = countRoot;
            var digitTemplate = Rect(countRoot, "DigitTemplate", new Vector2(0, .5f), new Vector2(0, .5f),
                new Vector2(17, 0), new Vector2(34, 34));
            item.countDigitTemplate = digitTemplate.gameObject.AddComponent<Image>();
            item.countDigitTemplate.preserveAspect = true;
            item.countDigitTemplate.raycastTarget = false;
            item.countDigitTemplate.gameObject.SetActive(false);
            item.label = Label(template, "Name", "中转件", 22, Ink,
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 26), new Vector2(-16, 30), TextAnchor.MiddleLeft);
            item.count = Label(template, "Count", "0/0", 20, Muted,
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 4), new Vector2(-16, 20), TextAnchor.MiddleLeft);
            view.paletteItemTemplate = item;
        }

        private static void ApplySlicedSprite(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            image.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            image.preserveAspect = false;
        }

        private static void BuildBoard(RectTransform parent, CircuitMinigameView view)
        {
            // 棋盘可用区：左让开件库、右让开教学栏、上让开预算条、下让开按钮条。
            // 它的位置与大小由 Prefab 说了算；格子大小由 CircuitBoard 按关卡行列数在运行时算。
            // 2026-08-16 右边界从 760 收到 540，让出 360 宽给课程包的教学栏——
            // 单关模式下教学栏整体隐藏，那一列就是空白，不额外把棋盘撑回去（布局只有一套，不按模式重排）
            var area = Rect(parent, "BoardArea", new Vector2(0, 0), new Vector2(1, 1), new Vector2(70, 10), new Vector2(-980, -220));
            view.boardArea = area;

            view.gridRoot = Rect(area, "GridRoot", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            view.linkRoot = Rect(area, "LinkRoot", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            view.nodeRoot = Rect(area, "NodeRoot", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            view.previewRoot = Rect(area, "PreviewRoot", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            // 兄弟顺序即绘制顺序：格子 → 导线 → 节点 → 预览（预览压最上）
        }

        /// <summary>
        /// 右侧教学栏（课程包专用）：课程标题 + 教学说明。单关模式下由 CircuitMinigame 整体隐藏。
        /// </summary>
        private static void BuildLessonPanel(RectTransform parent, CircuitMinigameView view)
        {
            var panel = Rect(parent, "LessonPanel", new Vector2(1, 0), new Vector2(1, 1),
                new Vector2(-200, 10), new Vector2(360, -220));
            view.lessonPanelBackground = ImageOn(panel, PanelTint);
            var nodeFrameGrey = AssetDatabase.LoadAssetAtPath<Sprite>(NodeFrameGreyPath);
            ApplySlicedSprite(view.lessonPanelBackground, nodeFrameGrey);
            view.lessonPanel = panel.gameObject;

            view.lessonTitleLabel = Label(panel, "Title", "课程标题", 30, Ink,
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -46), new Vector2(-32, 56), TextAnchor.MiddleLeft);

            // 说明是多行的：换行必须开，否则长句直接溢出到面板外
            view.lessonBriefLabel = Label(panel, "Brief", "教学说明", 24, Muted,
                new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, -46), new Vector2(-32, -140), TextAnchor.UpperLeft);
            view.lessonBriefLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            view.lessonBriefLabel.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private static void BuildFooter(RectTransform parent, CircuitMinigameView view)
        {
            view.messageLabel = Label(parent, "Message", string.Empty, 24, new Color(1f, .72f, .35f, 1f),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 116), new Vector2(-400, 40), TextAnchor.MiddleCenter);

            // 右下＝结束本关的动作；左下＝关卡导航（课程包专用，单关时隐藏）
            Text finishCaption;
            view.finishButton = Button(parent, "FinishButton", "完成", ButtonPrimary,
                new Vector2(-140, 64), new Vector2(200, 68), out finishCaption);
            view.finishButtonLabel = finishCaption;
            view.abortButton = Button(parent, "AbortButton", "放弃", ButtonGhost,
                new Vector2(-360, 64), new Vector2(180, 68));

            view.prevLessonButton = ButtonAt(parent, "PrevLessonButton", "上一关", ButtonGhost,
                new Vector2(0, 0), new Vector2(140, 64), new Vector2(180, 68));
            view.retryLessonButton = ButtonAt(parent, "RetryLessonButton", "重试本关", ButtonGhost,
                new Vector2(0, 0), new Vector2(340, 64), new Vector2(200, 68));
        }

        /// <summary>
        /// 通关结算（单关 / 课程包共用）：复用咖啡结算的全屏遮罩、淡入上浮结构，
        /// 但只引用 PC ui 2.0/通关结算 下的电路正式资产。中间课程关保留「下一关」和 ESC
        /// 继续调整；最后一关和单关只显示 ESC，由运行时把它解释为确认结算并返回。
        /// </summary>
        private static void BuildSummaryPanel(RectTransform parent, CircuitMinigameView view)
        {
            var panel = Rect(parent, "ResultPopup", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            ImageOn(panel, ResultScrim).raycastTarget = true;
            view.summaryPanel = panel.gameObject;
            view.summaryGroup = panel.gameObject.AddComponent<CanvasGroup>();

            var board = ResultArtImage(panel, "Board", ResultArt("游戏结算"),
                new Vector2(.5f, .5f), new Vector2(0, 30));
            board.raycastTarget = true;
            board.rectTransform.localScale = Vector3.one * ResultBoardScale;
            view.summaryBoard = (RectTransform)board.transform;

            var ribbon = ResultArtImage(board.transform, "Ribbon", ResultArt("蓝色缎带"),
                new Vector2(.5f, .5f), new Vector2(0, 104));
            ribbon.raycastTarget = false;
            Label(ribbon.transform, "RibbonTitle", "修理电路", 30, Color.white,
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 10), new Vector2(420, 44),
                TextAnchor.MiddleCenter);

            view.summaryTitleLabel = Label(board.transform, "Title", "第 1/7 关完成！", 40, ResultInk,
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 29), new Vector2(560, 56),
                TextAnchor.MiddleCenter);

            ResultArtImage(board.transform, "Divider", ResultArt("装饰线条"),
                new Vector2(.5f, .5f), new Vector2(0, -11)).raycastTarget = false;

            view.summaryBodyLabel = Label(board.transform, "Detail", "本关电路已全部点亮", 24, ResultMuted,
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, -60), new Vector2(600, 36),
                TextAnchor.MiddleCenter);

            var stats = ResultArtImage(board.transform, "StatsPanel", ResultArt("游戏数据统计面板去底处理-3 1"),
                new Vector2(.5f, .5f), new Vector2(0, -145));
            stats.raycastTarget = false;
            stats.rectTransform.localScale = Vector3.one * ResultStatsLocalScale;
            view.summaryLitValue = ResultStat(stats.transform, "Lit", "点亮", -155f);
            view.summaryWireValue = ResultStat(stats.transform, "Wire", "导线", 15f);
            view.summaryScoreValue = ResultStat(stats.transform, "Score", "得分", 205f);

            var starSprite = ResultArt("星星-单颗");
            view.summaryStars = new Image[3];
            for (int i = 0; i < view.summaryStars.Length; i++)
            {
                var star = ResultArtImage(board.transform, "Star" + i, starSprite,
                    new Vector2(.5f, .5f), new Vector2((i - 1) * 129f, -180f));
                star.raycastTarget = false;
                star.rectTransform.localScale = Vector3.one * ResultStarLocalScale;
                view.summaryStars[i] = star;
            }

            view.summaryStayButton = ResultButton(panel, "EscapeButton", "ESC-默认", "ESC-hover",
                new Vector2(.5f, 0), new Vector2(-140, 135));
            view.summaryContinueButton = ResultButton(panel, "NextButton", "下一关-默认", "下一关-悬停",
                new Vector2(.5f, 0), new Vector2(140, 135));

            panel.gameObject.SetActive(false);
        }

        private static Text ResultStat(Transform parent, string name, string caption, float x)
        {
            Label(parent, name + "Label", caption, 26, ResultMuted,
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(x, 60), new Vector2(150, 28),
                TextAnchor.MiddleCenter);
            return Label(parent, name + "Value", "—", 34, ResultInk,
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(x, -16), new Vector2(150, 34),
                TextAnchor.MiddleCenter);
        }

        private static Sprite ResultArt(string name)
        {
            var path = ResultArtDir + name + ".png";
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
                Debug.LogError("[修理电路] 缺通关结算素材：" + path);
            return sprite;
        }

        private static Image ResultArtImage(Transform parent, string name, Sprite sprite, Vector2 anchor,
            Vector2 position)
        {
            var size = sprite != null
                ? new Vector2(sprite.rect.width, sprite.rect.height) * ResultArtScale
                : new Vector2(160f, 60f);
            var image = ImageOn(Rect(parent, name, anchor, anchor, position, size), Color.white);
            image.sprite = sprite;
            image.preserveAspect = true;
            return image;
        }

        private static Button ResultButton(Transform parent, string name, string normalName, string hoverName,
            Vector2 anchor, Vector2 position)
        {
            var normal = ResultArt(normalName);
            var hover = ResultArt(hoverName);
            var image = ResultArtImage(parent, name, normal, anchor, position);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = hover,
                pressedSprite = hover,
                selectedSprite = normal,
            };
            return button;
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
            text.font = Resources.Load<Font>("Fonts/SourceHanSansOLD-Normal-2") ??
                        Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            text.text = value;
            return text;
        }

        /// <summary>右下角锚点的按钮（结束本关的那两个）。</summary>
        private static Button Button(Transform parent, string name, string caption, Color color,
            Vector2 position, Vector2 size)
        {
            Text ignored;
            return ButtonAt(parent, name, caption, color, new Vector2(1, 0), position, size, out ignored);
        }

        private static Button Button(Transform parent, string name, string caption, Color color,
            Vector2 position, Vector2 size, out Text captionLabel)
            => ButtonAt(parent, name, caption, color, new Vector2(1, 0), position, size, out captionLabel);

        private static Button ButtonAt(Transform parent, string name, string caption, Color color,
            Vector2 anchor, Vector2 position, Vector2 size)
        {
            Text ignored;
            return ButtonAt(parent, name, caption, color, anchor, position, size, out ignored);
        }

        /// <summary>
        /// 任意锚点的按钮。<paramref name="captionLabel"/> 抛出来是给运行时改文案用的
        /// （【完成】要在课程包模式下变成「下一关」/「交卷」）——View 里存显式引用，
        /// 不在运行时 GetComponentInChildren 去猜哪个 Text 是文案。
        /// </summary>
        private static Button ButtonAt(Transform parent, string name, string caption, Color color,
            Vector2 anchor, Vector2 position, Vector2 size, out Text captionLabel)
        {
            var rect = Rect(parent, name, anchor, anchor, position, size);
            var image = ImageOn(rect, color);
            image.color = Color.white;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            captionLabel = Label(rect, "Caption", caption, 28, Ink, Vector2.zero, Vector2.one, Vector2.zero,
                Vector2.zero, TextAnchor.MiddleCenter);
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

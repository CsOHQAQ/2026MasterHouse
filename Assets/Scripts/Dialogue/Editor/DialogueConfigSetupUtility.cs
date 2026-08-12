using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// 对话系统示例资产生成器。默认菜单只补齐缺失资产、不覆盖手工调整（与访客/家具生成器同一策略）。
    ///
    /// 内容来源：退役的 DefDialogueService 里那批占位台词（覆盖 5 个触发点 + 4 档满意度 + 5 句闲逛）。
    /// 设计说明 §2 原本写「迁 VisitorDef.transactionLine 那 4 句」，但 VisitorDef 已随访客系统重做退役，
    /// 那 4 句只剩 git 历史；占位台词覆盖面更全，改用它。
    ///
    /// 生成物：
    ///   Assets/GameData/Dialogue/通用/*.asset            对话组（四个种族共用，示范跨种族复用）
    ///   Assets/GameData/Dialogue/Pool_&lt;race&gt;.asset       种族对话池（挂到 VisitorRaceDef.dialoguePool）
    ///   Assets/Resources/OutGameUI/DialogueTuningConfig  全局调参（必须在 Resources，GameManager 按路径加载）
    /// </summary>
    public static class DialogueConfigSetupUtility
    {
        private const string DialogueDir = "Assets/GameData/Dialogue";
        private const string SharedDir = DialogueDir + "/通用";
        private const string TuningPath = "Assets/Resources/OutGameUI/DialogueTuningConfig.asset";
        private const string RaceDir = "Assets/Resources/OutGameUI/VisitorRaces";

        /// <summary>供 CI / 批处理生成使用；同样只补缺失资产。</summary>
        public static void CreateFromBatch() => CreateIfMissing();

        [MenuItem("MasterHouse/对话系统/创建示例资产（补齐缺失）")]
        public static void CreateIfMissing()
        {
            EnsureFolder(SharedDir);
            var created = new List<string>();

            // ── 全局调参（§12 待确认默认值：打字机 30 字/秒、recent 环长 3）──
            var tuning = AssetDatabase.LoadAssetAtPath<DialogueTuningConfig>(TuningPath);
            if (tuning == null)
            {
                tuning = ScriptableObject.CreateInstance<DialogueTuningConfig>();
                AssetDatabase.CreateAsset(tuning, TuningPath);
                created.Add(TuningPath);
            }

            // ── 初次见面：打招呼 + 分支给出接待/拒绝/再想想（§5「需求是接待之后才提的」）──
            // 接待与拒绝完全由**分支选项上的事件**驱动，UI 不再有硬编码按钮（验收清单第 4 条）
            var firstMeeting = Group(created, "first_meeting", "初次见面：打招呼，分支给出接待/拒绝",
                Line(EDialogueSpeaker.Visitor, "你好，我是{访客名}。今天可以接待我吗？", EDialogueEmotion.Calm),
                Branch(
                    Option("请进，我来招待你。", EBranchNext.End, new AcceptVisitorAction()),
                    Option("抱歉，今天不方便。", EBranchNext.End, new RejectVisitorAction()),
                    Option("让我再想想。", EBranchNext.End)));

            // ── 开始等待服务：程序化需求句走 {需求} 占位符（§9），不是在代码里拼字符串 ──
            var serviceStart = Group(created, "service_start", "开始等待服务：说出需求（{需求} 占位符）",
                Line(EDialogueSpeaker.Visitor, "那就麻烦你了。我想要{需求}。", EDialogueEmotion.Happy),
                Line(EDialogueSpeaker.Player, "我找找看。", EDialogueEmotion.Calm));

            // ── 被拒绝：玩家拒绝与两段超时共用（§5 同口径）──
            var rejected = Group(created, "rejected", "被拒绝：玩家拒绝 / 等搭话超时 / 等交货超时 共用",
                Line(EDialogueSpeaker.Visitor, "……这样啊。那我先走了。", EDialogueEmotion.Sad));

            // ── 完成服务四档 ──
            var doneMismatch = Group(created, "done_mismatch", "完成服务·不对味（此档不进闲逛，直接离开）",
                Line(EDialogueSpeaker.Visitor, "这不是我想要的……我还是走吧。", EDialogueEmotion.Sad));
            var donePlain = Group(created, "done_plain", "完成服务·一般",
                Line(EDialogueSpeaker.Visitor, "唔，「{物品名}」勉强可以吧。", EDialogueEmotion.Calm));
            var doneSatisfied = Group(created, "done_satisfied", "完成服务·满意",
                Line(EDialogueSpeaker.Visitor, "不错不错，我挺喜欢「{物品名}」的。", EDialogueEmotion.Happy));
            var donePerfect = Group(created, "done_perfect", "完成服务·完美（奖励类事件只能加在组末尾或选项上，§5.3 铁律②）",
                Line(EDialogueSpeaker.Visitor, "太完美了！就是「{物品名}」！", EDialogueEmotion.Surprised));

            // ── 闲逛：拆成 5 个单句组而不是一个 5 句组 ──
            // 气泡一次只显示一句，多个候选组才能让 recent 去重环与加权抽取真正发挥作用（§6）
            var wander = new[]
            {
                Group(created, "wander_1", "闲逛台词", Line(EDialogueSpeaker.Visitor, "这间屋子住起来一定很舒服吧。", EDialogueEmotion.Happy)),
                Group(created, "wander_2", "闲逛台词", Line(EDialogueSpeaker.Visitor, "刚才的招待真不错，多谢啦。", EDialogueEmotion.Happy)),
                Group(created, "wander_3", "闲逛台词", Line(EDialogueSpeaker.Visitor, "我再逛一小会儿就回去。", EDialogueEmotion.Calm)),
                Group(created, "wander_4", "闲逛台词", Line(EDialogueSpeaker.Visitor, "窗外的光线真好啊。", EDialogueEmotion.Calm)),
                Group(created, "wander_5", "闲逛台词", Line(EDialogueSpeaker.Visitor, "下次我还会再来的。", EDialogueEmotion.Happy)),
            };

            // ── 四个种族各建一个池，内容先共用同一批组（策划后续按种族分化，只改资产不改代码）──
            foreach (var raceId in new[] { "fox", "crow", "rabbit", "hedgehog" })
            {
                var pool = Pool(created, raceId, firstMeeting, serviceStart, rejected,
                    doneMismatch, donePlain, doneSatisfied, donePerfect, wander);
                AttachPoolToRace(created, raceId, pool);
            }

            AssetDatabase.SaveAssets();
            Debug.Log(created.Count > 0
                ? "[对话] 已创建示例资产：\n" + string.Join("\n", created)
                : "[对话] 对话示例资产已齐全，未做修改。");
        }

        // ══════════ 资产构建 ══════════

        private static DialoguePoolDef Pool(List<string> created, string raceId,
            DialogueGroupDef firstMeeting, DialogueGroupDef serviceStart, DialogueGroupDef rejected,
            DialogueGroupDef doneMismatch, DialogueGroupDef donePlain, DialogueGroupDef doneSatisfied,
            DialogueGroupDef donePerfect, DialogueGroupDef[] wander)
        {
            var path = $"{DialogueDir}/Pool_{raceId}.asset";
            var pool = AssetDatabase.LoadAssetAtPath<DialoguePoolDef>(path);
            if (pool != null) return pool;

            pool = ScriptableObject.CreateInstance<DialoguePoolDef>();
            pool.firstMeeting = Entries(firstMeeting);
            pool.serviceStart = Entries(serviceStart);
            pool.rejected = Entries(rejected);
            pool.doneMismatch = Entries(doneMismatch);
            pool.donePlain = Entries(donePlain);
            pool.doneSatisfied = Entries(doneSatisfied);
            pool.donePerfect = Entries(donePerfect);
            pool.wanderChat = Entries(wander);
            pool.previewMismatch = PreviewLines("（{访客名}皱了皱眉）", EDialogueEmotion.Sad);
            pool.previewPlain = PreviewLines("（{访客名}看了一眼，没什么表情）", EDialogueEmotion.Calm);
            pool.previewSatisfied = PreviewLines("（{访客名}的眼睛亮了一下）", EDialogueEmotion.Happy);
            pool.previewPerfect = PreviewLines("（{访客名}屏住了呼吸）", EDialogueEmotion.Surprised);
            AssetDatabase.CreateAsset(pool, path);
            created.Add(path);
            return pool;
        }

        /// <summary>把池挂到种族上。已挂过的不动（策划可能换了自己的池）。</summary>
        private static void AttachPoolToRace(List<string> created, string raceId, DialoguePoolDef pool)
        {
            var path = $"{RaceDir}/Race_{raceId}.asset";
            var race = AssetDatabase.LoadAssetAtPath<VisitorRaceDef>(path);
            if (race == null || race.dialoguePool != null) return;
            race.dialoguePool = pool;
            EditorUtility.SetDirty(race);
            created.Add(path + "（挂上对话池）");
        }

        private static DialogueGroupDef Group(List<string> created, string id, string note, params DialogueStep[] steps)
        {
            var path = $"{SharedDir}/Group_{id}.asset";
            var group = AssetDatabase.LoadAssetAtPath<DialogueGroupDef>(path);
            if (group != null) return group;
            group = ScriptableObject.CreateInstance<DialogueGroupDef>();
            group.id = id;
            group.note = note;
            group.steps = new List<DialogueStep>(steps);
            AssetDatabase.CreateAsset(group, path);
            created.Add(path);
            return group;
        }

        // ══════════ 内容小工具 ══════════

        private static List<DialogueGroupEntry> Entries(params DialogueGroupDef[] groups)
        {
            var list = new List<DialogueGroupEntry>(groups.Length);
            foreach (var group in groups)
                list.Add(new DialogueGroupEntry { group = group, weight = 1 });
            return list;
        }

        private static List<DialogueLine> PreviewLines(string text, EDialogueEmotion emotion) =>
            new List<DialogueLine>
            {
                new DialogueLine { speaker = EDialogueSpeaker.Narration, text = text, emotion = emotion },
            };

        private static DialogueStep Line(EDialogueSpeaker speaker, string text, EDialogueEmotion emotion) =>
            new DialogueStep
            {
                kind = EDialogueStepKind.Line,
                line = new DialogueLine { speaker = speaker, text = text, emotion = emotion },
            };

        private static DialogueStep Branch(params BranchOption[] options) =>
            new DialogueStep
            {
                kind = EDialogueStepKind.Branch,
                options = new List<BranchOption>(options),
            };

        /// <summary>无条件选项（§4.3 硬校验要求每个分支至少有一个）。</summary>
        private static BranchOption Option(string text, EBranchNext next, params IGameplayAction[] actions) =>
            new BranchOption
            {
                text = text,
                next = next,
                actions = new List<IGameplayAction>(actions),
                conditions = new List<IGameplayCondition>(),
            };

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = path.Substring(0, path.LastIndexOf('/'));
            var leaf = path.Substring(path.LastIndexOf('/') + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}

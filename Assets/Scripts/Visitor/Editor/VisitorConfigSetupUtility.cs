using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// 访客系统示例资产生成器（访客交付说明 §10：现有 4 个动物访客的立绘/序列帧素材保留，改挂到对应种族上作为示例内容）。
    /// 默认菜单只补齐缺失资产、不覆盖手工调整（与家具配置生成器同一策略）。
    /// 生成物：标签森林（Assets/GameData/Tags，现仅供局内物资使用）、示例物资挂 tag、
    /// 4 个种族 + 日程表 + 调参配置（Assets/Resources/OutGameUI，运行时经 Resources 加载）。
    /// 种族的需求权重表已随 tag 需求体系退役（需求重做说明 §9.1），需求改由日程条目逐条配。
    /// </summary>
    public static class VisitorConfigSetupUtility
    {
        private const string TagDir = "Assets/GameData/Tags";
        private const string RaceDir = "Assets/Resources/OutGameUI/VisitorRaces";
        private const string ResourceDir = "Assets/Resources/OutGameUI";
        private const string SchedulePath = ResourceDir + "/VisitorScheduleTable.asset";
        private const string TuningPath = ResourceDir + "/VisitorTuningConfig.asset";

        /// <summary>供 CI / 批处理生成使用；同样只补缺失资产。</summary>
        public static void CreateFromBatch() => CreateIfMissing();

        [MenuItem("MasterHouse/访客系统/创建示例资产（补齐缺失）")]
        public static void CreateIfMissing()
        {
            EnsureFolder(TagDir);
            EnsureFolder(RaceDir);
            var created = new List<string>();

            // ── 标签森林（§4.1）：轴「品类」（名词）与轴「质地」（形容词），示例内容对齐现有局内物资 ──
            // 访客需求已不再用 tag（需求重做说明 §9.1），这片森林现在**只服务于局内 ItemDef.tags**，
            // 随 NodeSim 包一起清理（§9.2）
            var axisCategory = Tag(created, "category", "品类", "品类的", null, ETagGrammarRole.Noun, 0);
            var tagMaterial = Tag(created, "material", "材料", "材料", axisCategory, ETagGrammarRole.Noun, 0);
            var tagWood = Tag(created, "wood", "木料", "木头做的", tagMaterial, ETagGrammarRole.Noun, 0);
            var tagEnergy = Tag(created, "energy", "能源", "能源", axisCategory, ETagGrammarRole.Noun, 1);
            var axisTexture = Tag(created, "texture", "质地", "质地的", null, ETagGrammarRole.Adjective, 1);
            var tagNatural = Tag(created, "natural", "天然", "天然的", axisTexture, ETagGrammarRole.Adjective, 0);
            var tagCrafted = Tag(created, "crafted", "精加工", "精加工的", axisTexture, ETagGrammarRole.Adjective, 1);

            // ── 示例物资挂 tag（§4.2：只在 tags 为空时补，不覆盖策划手配）──
            TagItem(created, "Assets/GameData/Items/木材.asset", tagWood, tagNatural);
            TagItem(created, "Assets/GameData/Items/木板.asset", tagWood, tagCrafted);
            TagItem(created, "Assets/GameData/Items/电力.asset", tagEnergy);

            // ── 种族（§4.3）：沿用原 4 个动物访客的立绘与序列帧素材 ──
            // 性格数值以 tick 计（10 tick/秒、10 tick/游戏分钟）：如 9000 tick = 15 现实分钟 = 900 游戏分钟
            var fox = Race(created, "fox", "狐族", "OutGameUI/Guests/fox", "OutGameUI/Visitors/orange_cat",
                waitTalk: 3000, waitDeliver: 6000, wanderMax: 3600, stayPercent: 30);
            var crow = Race(created, "crow", "鸦族", "OutGameUI/Guests/crow", "OutGameUI/Visitors/rottweiler",
                waitTalk: 1800, waitDeliver: 3600, wanderMax: 2400, stayPercent: 10);
            var rabbit = Race(created, "rabbit", "兔族", "OutGameUI/Guests/rabbit", "OutGameUI/Visitors/xueqiu",
                waitTalk: 4200, waitDeliver: 7200, wanderMax: 6000, stayPercent: 60);
            var hedgehog = Race(created, "hedgehog", "猬族", "OutGameUI/Guests/hedgehog", "OutGameUI/Visitors/wangcai",
                waitTalk: 2400, waitDeliver: 4800, wanderMax: 3000, stayPercent: 20);

            // ── 日程表（§4.4：零随机零上限，谁在第几天几点带什么需求出现由策划配死；加内容请追加表尾）──
            // **这里生成的条目不带需求**：本包只做结构与导表列，需求资产由策划自己建（2026-08-13 访谈定案）。
            // 因此新工程首跑时每条日程都会 LogError 并跳过投放——这是预期状态，
            // 补法是建 NeedDef 资产（菜单 MasterHouse → 访客系统 → 需求编辑器）并填进日程表的「需求」列
            var schedule = AssetDatabase.LoadAssetAtPath<VisitorScheduleTable>(SchedulePath);
            if (schedule == null)
            {
                schedule = ScriptableObject.CreateInstance<VisitorScheduleTable>();
                schedule.entries = new List<VisitorScheduleEntry>
                {
                    Entry(1, 8 * 60 + 30, fox),
                    Entry(1, 9 * 60 + 10, crow),
                    Entry(1, 10 * 60, rabbit),
                    Entry(1, 14 * 60, hedgehog),
                    Entry(2, 8 * 60 + 40, rabbit),
                    Entry(2, 9 * 60 + 30, fox),
                    Entry(2, 13 * 60, crow),
                    Entry(3, 9 * 60, hedgehog),
                    Entry(3, 11 * 60, fox),
                };
                AssetDatabase.CreateAsset(schedule, SchedulePath);
                created.Add(SchedulePath);
            }

            // ── 调参配置（§4.5）：营业时段 + 闲逛节奏 + 氛围邻居名册（自退役的 VisitorTable 迁入）──
            var tuning = AssetDatabase.LoadAssetAtPath<VisitorTuningConfig>(TuningPath);
            if (tuning == null)
            {
                tuning = ScriptableObject.CreateInstance<VisitorTuningConfig>();
                tuning.openMinute = 8 * 60;
                tuning.closeMinute = 22 * 60;
                tuning.bubbleIntervalTicks = 120;
                tuning.bubbleJitterTicks = 40;
                tuning.bubbleHoldTicks = 40;
                tuning.ambientVisitors = new List<AmbientVisitorDef>
                {
                    Ambient("laoda", "老大"), Ambient("laomao", "老猫"), Ambient("longhair_cat", "长毛"),
                    Ambient("panghu", "胖虎"), Ambient("sangbiao", "桑彪"), Ambient("tufu", "土福"),
                };
                AssetDatabase.CreateAsset(tuning, TuningPath);
                created.Add(TuningPath);
            }

            AssetDatabase.SaveAssets();
            Debug.Log(created.Count > 0
                ? "[Visitor] 已创建示例资产：\n" + string.Join("\n", created)
                : "[Visitor] 访客示例资产已齐全，未做修改。");
        }

        private static TagDef Tag(List<string> created, string id, string displayName, string phrase,
            TagDef parent, ETagGrammarRole role, int sortOrder)
        {
            var path = $"{TagDir}/Tag_{id}.asset";
            var tag = AssetDatabase.LoadAssetAtPath<TagDef>(path);
            if (tag != null) return tag;
            tag = ScriptableObject.CreateInstance<TagDef>();
            tag.id = id;
            tag.displayName = displayName;
            tag.phrase = phrase;
            tag.parent = parent;
            tag.grammarRole = role;
            tag.sortOrder = sortOrder;
            AssetDatabase.CreateAsset(tag, path);
            created.Add(path);
            return tag;
        }

        private static void TagItem(List<string> created, string path, params TagDef[] tags)
        {
            var item = AssetDatabase.LoadAssetAtPath<ItemDef>(path);
            if (item == null || (item.tags != null && item.tags.Count > 0)) return;
            item.tags = new List<TagDef>(tags);
            EditorUtility.SetDirty(item);
            created.Add(path + "（补挂 tag）");
        }

        private static VisitorRaceDef Race(List<string> created, string id, string displayName,
            string portraitPath, string sheetPath, int waitTalk, int waitDeliver, int wanderMax,
            int stayPercent)
        {
            var path = $"{RaceDir}/Race_{id}.asset";
            var race = AssetDatabase.LoadAssetAtPath<VisitorRaceDef>(path);
            if (race != null) return race;
            race = ScriptableObject.CreateInstance<VisitorRaceDef>();
            race.raceId = id;
            race.displayName = displayName;
            race.waitTalkTimeoutTicks = waitTalk;
            race.waitDeliverTimeoutTicks = waitDeliver;
            race.wanderMaxTicks = wanderMax;
            race.stayOvernightPercent = stayPercent;
            race.portraits = new List<ExpressionPortrait>
            {
                new ExpressionPortrait { expression = EDialogueEmotion.Calm, portraitPath = portraitPath },
            };
            race.sheetPath = sheetPath;
            AssetDatabase.CreateAsset(race, path);
            created.Add(path);
            return race;
        }

        private static VisitorScheduleEntry Entry(int day, int minute, VisitorRaceDef race) =>
            new VisitorScheduleEntry { day = day, appearMinute = minute, race = race };

        private static AmbientVisitorDef Ambient(string id, string displayName) =>
            new AmbientVisitorDef { id = id, displayName = displayName, sheetPath = "OutGameUI/Visitors/" + id };

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

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// 访客系统示例资产生成器（访客交付说明 §10：现有 4 个动物访客的立绘/序列帧素材保留，改挂到对应种族上作为示例内容）。
    /// 默认菜单只补齐缺失资产、不覆盖手工调整（与家具配置生成器同一策略）。
    /// 生成物：4 个种族 + 日程表 + 调参配置（Assets/Resources/OutGameUI，运行时经 Resources 加载）。
    /// 种族的需求权重表已随 tag 需求体系退役（需求重做说明 §9.1），需求改由日程条目逐条配；
    /// 标签森林与示例物资的生成也已随 TagDef/ItemDef 一并删除（§9.2）。
    /// </summary>
    public static class VisitorConfigSetupUtility
    {
        private const string RaceDir = "Assets/Resources/OutGameUI/VisitorRaces";
        private const string ResourceDir = "Assets/Resources/OutGameUI";
        private const string SchedulePath = ResourceDir + "/VisitorScheduleTable.asset";
        private const string TuningPath = ResourceDir + "/VisitorTuningConfig.asset";

        /// <summary>供 CI / 批处理生成使用；同样只补缺失资产。</summary>
        public static void CreateFromBatch() => CreateIfMissing();

        [MenuItem("MasterHouse/访客系统/创建示例资产（补齐缺失）")]
        public static void CreateIfMissing()
        {
            EnsureFolder(RaceDir);
            var created = new List<string>();

            // ── 种族（§4.3）：沿用原 4 个动物访客的序列帧素材 ──
            // 性格数值以 tick 计（10 tick/秒、10 tick/游戏分钟）：如 9000 tick = 15 现实分钟 = 900 游戏分钟
            // 「默认立绘ID」指向 Excel/立绘表.xlsx 里的行；这里给的四个 ID 与
            // Tools/导表/make_portrait_template.py 生成的初始内容对齐（2026-08-14 立绘 ID 化）
            var fox = Race(created, "fox", "狐族", "fox_平静", "OutGameUI/Visitors/orange_cat",
                waitTalk: 3000, waitDeliver: 6000, wanderMax: 3600);
            var crow = Race(created, "crow", "鸦族", "crow_平静", "OutGameUI/Visitors/rottweiler",
                waitTalk: 1800, waitDeliver: 3600, wanderMax: 2400);
            var rabbit = Race(created, "rabbit", "兔族", "rabbit_平静", "OutGameUI/Visitors/xueqiu",
                waitTalk: 4200, waitDeliver: 7200, wanderMax: 6000);
            var hedgehog = Race(created, "hedgehog", "猬族", "hedgehog_平静", "OutGameUI/Visitors/wangcai",
                waitTalk: 2400, waitDeliver: 4800, wanderMax: 3000);

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

        // 标签森林（Tag / TagItem 两个生成器）已随 TagDef、ItemDef 一并删除：
        // 访客需求早在需求重做时就不用 tag 了（§9.1），最后的消费方 ItemDef.tags 属于局内物资链，
        // 随小游戏框架落地第 2 步整体退役（§9.2）。

        private static VisitorRaceDef Race(List<string> created, string id, string displayName,
            string defaultPortraitId, string sheetPath, int waitTalk, int waitDeliver, int wanderMax)
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
            race.defaultPortraitId = defaultPortraitId;
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

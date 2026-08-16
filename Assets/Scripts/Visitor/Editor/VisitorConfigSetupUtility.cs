using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// 访客系统示例资产生成器（访客交付说明 §10）。
    /// 默认菜单只补齐缺失资产、不覆盖手工调整（与家具配置生成器同一策略）。
    /// 生成物：8 个种族 + 日程表 + 调参配置（Assets/Resources/OutGameUI，运行时经 Resources 加载）。
    /// **这些内容的真相源是 Excel/访客种族表.xlsx 与访客日程表.xlsx**，本工具只是新工程首跑时的兜底；
    /// 改数值请改 Excel 再导表，别改这里（改这里也不会生效——资产已存在时本工具不覆盖）。
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

            // ── 种族（§4.3）：2026-08-16 换成美术交付的 8 个角色 ──
            // 性格数值以 tick 计（10 tick/秒、10 tick/游戏分钟）：如 9000 tick = 15 现实分钟 = 900 游戏分钟
            // 「默认立绘ID」指向 Excel/立绘表.xlsx 里的行（2026-08-14 立绘 ID 化）；
            // 这里的内容与 Excel/访客种族表.xlsx 一致，**真相源仍是 Excel**——本菜单只管新工程首跑时不空手。
            var rabbit = Race(created, "rabbit", "兔族", "rabbit_平静", "OutGameUI/Visitors/rabbit",
                waitTalk: 4200, waitDeliver: 7200, wanderMax: 6000);
            var goat = Race(created, "goat", "羊族", "goat_平静", "OutGameUI/Visitors/goat",
                waitTalk: 3600, waitDeliver: 6600, wanderMax: 5400);
            var wolf = Race(created, "wolf", "狼族", "wolf_平静", "OutGameUI/Visitors/wolf",
                waitTalk: 1800, waitDeliver: 3600, wanderMax: 2400);
            var leopard = Race(created, "leopard", "豹族", "leopard_平静", "OutGameUI/Visitors/leopard",
                waitTalk: 2400, waitDeliver: 4800, wanderMax: 3000);
            var cheetah = Race(created, "cheetah", "猎豹族", "cheetah_平静", "OutGameUI/Visitors/cheetah",
                waitTalk: 2100, waitDeliver: 4200, wanderMax: 2700);
            var ox = Race(created, "ox", "牛族", "ox_平静", "OutGameUI/Visitors/ox",
                waitTalk: 4800, waitDeliver: 8400, wanderMax: 6600);
            var cat = Race(created, "cat", "猫族", "cat_平静", "OutGameUI/Visitors/cat",
                waitTalk: 3000, waitDeliver: 6000, wanderMax: 3600);
            var yak = Race(created, "yak", "牦牛族", "yak_平静", "OutGameUI/Visitors/yak",
                waitTalk: 3900, waitDeliver: 7000, wanderMax: 5600);

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
                    Entry(1, 8 * 60 + 30, rabbit),
                    Entry(1, 9 * 60 + 10, cat),
                    Entry(1, 10 * 60, goat),
                    Entry(1, 14 * 60, wolf),
                    Entry(2, 8 * 60 + 40, ox),
                    Entry(2, 9 * 60 + 30, leopard),
                    Entry(2, 13 * 60, cheetah),
                    Entry(3, 9 * 60, yak),
                    Entry(3, 11 * 60, rabbit),
                    Entry(3, 12 * 60 + 30, cat),
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

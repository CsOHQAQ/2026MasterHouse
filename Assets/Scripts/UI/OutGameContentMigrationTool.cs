#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 3.3 一次性迁移工具：把 OutGameUIData / OutGameUI.DialogueLine 的硬编码内容生成为
    /// VisitorTable / CodexTable 资产（§16.6 内容 Def 化）。
    /// 因需读取本程序集 internal 的旧数据，放在运行时目录并整体用 UNITY_EDITOR 包裹；
    /// 消费代码切换到资产后，本工具随旧硬编码一并删除。
    /// 成就/日记两组内容在旧代码里内联于面板方法（Build/Bind 双份），无单一来源可读，此处为唯一誊抄。
    /// </summary>
    internal static class OutGameContentMigrationTool
    {
        private const string VisitorTablePath = "Assets/Resources/OutGameUI/VisitorTable.asset";
        private const string CodexTablePath = "Assets/Resources/OutGameUI/CodexTable.asset";

        [MenuItem("MasterHouse/局外内容/生成内容表（从旧硬编码迁移）")]
        private static void Generate()
        {
            var created = new List<string>();

            if (AssetDatabase.LoadAssetAtPath<VisitorTable>(VisitorTablePath) == null)
            {
                var table = ScriptableObject.CreateInstance<VisitorTable>();
                FillVisitors(table);
                AssetDatabase.CreateAsset(table, VisitorTablePath);
                created.Add(VisitorTablePath);
            }

            if (AssetDatabase.LoadAssetAtPath<CodexTable>(CodexTablePath) == null)
            {
                var table = ScriptableObject.CreateInstance<CodexTable>();
                FillCodex(table);
                AssetDatabase.CreateAsset(table, CodexTablePath);
                created.Add(CodexTablePath);
            }

            AssetDatabase.SaveAssets();
            Debug.Log(created.Count > 0
                ? "[局外内容] 已生成：" + string.Join("、", created)
                : "[局外内容] 资产已存在，未做修改。如需重新生成，请先删除对应 .asset 再执行本菜单。");
        }

        private static void FillVisitors(VisitorTable table)
        {
            for (var i = 0; i < OutGameUIData.Guests.Length; i++)
            {
                var guest = OutGameUIData.Guests[i];
                table.visitors.Add(new VisitorDef
                {
                    id = guest.id,
                    displayName = guest.name,
                    type = guest.type,
                    special = guest.special,
                    visitHour = guest.visitHour,
                    serviceStart = guest.serviceStart,
                    serviceEnd = guest.serviceEnd,
                    status = guest.status,
                    hint = guest.hint,
                    affinity = guest.affinity,
                    need = guest.need,
                    solution = guest.solution,
                    gift = guest.gift,
                    transactionLine = OutGameUI.DialogueLine(i),
                    portraitPath = guest.portrait,
                    sheetPath = i < OutGameUIData.VisitorSheets.Length ? OutGameUIData.VisitorSheets[i] : "",
                });
            }

            foreach (var raw in OutGameUIData.AmbientVisitors)
            {
                var parts = raw.Split('|');
                var path = parts[0];
                table.ambientVisitors.Add(new AmbientVisitorDef
                {
                    id = path.Substring(path.LastIndexOf('/') + 1),
                    displayName = parts.Length > 1 ? parts[1] : "",
                    sheetPath = path,
                });
            }
        }

        private static void FillCodex(CodexTable table)
        {
            foreach (var room in OutGameUIData.Rooms)
                table.rooms.Add(new RoomDef
                {
                    id = room.id,
                    displayName = room.name,
                    code = room.code,
                    note = room.note,
                    artPath = room.art,
                });

            for (var roomIndex = 0; roomIndex < OutGameUIData.Devices.Length; roomIndex++)
            {
                var roomId = OutGameUIData.Rooms[roomIndex].id;
                foreach (var raw in OutGameUIData.Devices[roomIndex])
                {
                    var parts = raw.Split('|');
                    table.devices.Add(new DeviceDef
                    {
                        roomId = roomId,
                        displayName = parts[0],
                        level = int.TryParse(parts[1].Replace("LV.", ""), out var level) ? level : 1,
                        effect = parts[2],
                        owned = parts.Length > 3 && parts[3] == "1",
                    });
                }
            }

            foreach (var item in OutGameUIData.Furniture)
                table.archives.Add(ArchiveEntry(ECodexArchiveCategory.NarrativeFurniture, item));
            foreach (var item in OutGameUIData.World)
                table.archives.Add(ArchiveEntry(ECodexArchiveCategory.World, item));

            // 成就/日记：旧代码内联在日记面板的 Build/Bind 两处，此处誊抄为唯一来源（id 为新起稳定键）
            table.achievements.Add(new AchievementDef { id = "night-master", displayName = "夜的主人", note = "在深夜完成一次服务" });
            table.achievements.Add(new AchievementDef { id = "first-meet", displayName = "初次相识", note = "录入 3 位访客" });
            table.achievements.Add(new AchievementDef { id = "home-shape", displayName = "家的轮廓", note = "解锁全部房间" });
            table.achievements.Add(new AchievementDef { id = "unknown", displayName = "无人知晓", note = "发现特殊访客的秘密" });

            table.journalEntries.Add(new JournalEntryDef
            {
                id = "0617",
                dateText = "06 / 17 · 雨转晴",
                title = "窗户唱回来的那句话",
                body = "赫墨说“今天糟透了”。琴弦轻轻响了一下，唱回：“但你还是走到了这里。”\n\n关键词：琴弦窗户 / 反向情绪",
            });
            table.journalEntries.Add(new JournalEntryDef
            {
                id = "0616",
                dateText = "06 / 16 · 阴",
                title = "风铃下的纸条",
                body = "米娅没有说再见，只留下一张画着胡萝卜的小纸条。",
            });
        }

        private static CodexEntryDef ArchiveEntry(ECodexArchiveCategory category, OutGameArchiveItem item) =>
            new CodexEntryDef
            {
                category = category,
                id = item.id,
                displayName = item.name,
                type = item.type,
                owner = item.owner,
                note = item.note,
                imagePath = item.image,
            };
    }
}
#endif

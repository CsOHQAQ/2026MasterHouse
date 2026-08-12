using UnityEditor;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 音效表创建工具：只补缺失不覆盖（与访客/对话 SetupUtility 同款约定）。
    /// 剪辑按文件名前缀在 Resources/SoundEffect 下自动匹配——音频源文件带日期后缀（如 1_Button_260812.mp3），
    /// 前缀匹配可容忍换版重导；已手动指定的剪辑不会被改动。
    /// </summary>
    public static class SfxConfigSetupUtility
    {
        private const string AssetPath = "Assets/Resources/OutGameUI/SfxTable.asset";
        private const string ClipFolder = "Assets/Resources/SoundEffect";

        /// <summary>ESfx → 音频文件名前缀（编号沿用音效需求清单）与默认节流间隔。</summary>
        private static readonly (ESfx id, string prefix, float minInterval)[] Mapping =
        {
            (ESfx.UiClick, "1_Button", 0.05f),
            (ESfx.FurniturePickup, "2_Pickup", 0.05f),
            (ESfx.FurniturePlace, "2_Putdown", 0.05f),
            (ESfx.GuestInteract, "3_Interaction", 0.05f),
            (ESfx.ValueGain, "4_ScoreGain", 0.05f),
            (ESfx.ValueLose, "4_ScoreLose", 0.05f),
            (ESfx.PageTransition, "5_Transition", 0.1f),
            (ESfx.VisitorArrive, "6_InfoCome", 0.1f),
            (ESfx.VisitorLeave, "6_InfoLeave", 0.1f),
            (ESfx.Reward, "7_Reward", 0.1f),
            // 逐字音默认用键盘打字剪辑；想换手机打字音（8_TypingPhone）直接改资产上的剪辑引用
            (ESfx.DialogueTyping, "8_TypingKeyboard", 0.055f),
        };

        [MenuItem("MasterHouse/音效系统/创建音效表（补齐缺失）")]
        public static void CreateIfMissing()
        {
            var table = AssetDatabase.LoadAssetAtPath<SfxTable>(AssetPath);
            if (table == null)
            {
                table = ScriptableObject.CreateInstance<SfxTable>();
                AssetDatabase.CreateAsset(table, AssetPath);
            }

            var added = 0;
            var filled = 0;
            foreach (var (id, prefix, minInterval) in Mapping)
            {
                var entry = table.entries.Find(e => e != null && e.id == id);
                if (entry == null)
                {
                    entry = new SfxEntry { id = id, minInterval = minInterval };
                    table.entries.Add(entry);
                    added++;
                }
                if (entry.clip != null) continue;
                entry.clip = FindClip(prefix);
                if (entry.clip != null) filled++;
                else Debug.LogWarning($"[Sfx] 未在 {ClipFolder} 找到前缀「{prefix}」的音频剪辑，条目 {id} 留空待手动指定");
            }

            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Sfx] 音效表就绪：新增条目 {added} 个，自动匹配剪辑 {filled} 个 → {AssetPath}", table);
            Selection.activeObject = table;
        }

        private static AudioClip FindClip(string prefix)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { ClipFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path).StartsWith(prefix, System.StringComparison.Ordinal))
                    return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            }
            return null;
        }
    }
}

using System;
using System.IO;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 全局设置（§16.5）：设置项是「这台机器上玩家的偏好」而非游玩进度，独立为 persistentDataPath 下的
    /// JSON 文件，与存档槽位无关（旧版把设置塞进存档槽是反例）。禁止 PlayerPrefs（待定 #9 硬约定）。
    /// 首次访问时加载；改动处调用 Save() 落盘。
    /// </summary>
    public static class HouseSettings
    {
        [Serializable]
        public sealed class SettingsData
        {
            public int bgmVolume = 64;
            public int sfxVolume = 78;
            public string windowMode = "无边框";
            public bool autoDialogue;
            public bool showInteractionHints = true;
            public bool cameraShake = true;
        }

        private static SettingsData data;

        private static string FilePath => Path.Combine(Application.persistentDataPath, "house-settings.json");

        public static SettingsData Data => data ?? (data = Load());

        public static void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(Data, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HouseSettings] 设置文件写入失败：" + e.Message);
            }
        }

        private static SettingsData Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var loaded = JsonUtility.FromJson<SettingsData>(File.ReadAllText(FilePath));
                    if (loaded != null) return loaded;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HouseSettings] 设置文件读取失败，回落默认值：" + e.Message);
            }
            return new SettingsData();
        }
    }
}

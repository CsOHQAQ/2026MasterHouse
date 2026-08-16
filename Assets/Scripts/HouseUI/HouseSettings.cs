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
            public int masterVolume = 45;
            public int bgmVolume = 45;
            public int sfxVolume = 45;
            public string windowMode = "无边框";
            public bool dayNightEnabled = true;
            public string language = "中文";
            public bool autoDialogue;
            public bool showInteractionHints = true;
            public bool cameraShake = true;
        }

        private static SettingsData data;

        private static string FilePath => Path.Combine(Application.persistentDataPath, "house-settings.json");

        public static SettingsData Data => data ?? (data = Load());

        /// <summary>
        /// 把当前设置作用到运行时（启动与设置页改动时调，2026-08-16 设置页重做）：
        /// 主音量走 AudioListener；音效音量由 SfxManager 播放时自行读取；BGM 音量留给将来的音乐播放器；
        /// 昼夜交替开关由 HouseDayLight 读取；窗口模式映射 Screen.fullScreenMode。
        /// </summary>
        public static void Apply()
        {
            AudioListener.volume = Mathf.Clamp01(Data.masterVolume / 100f);
            switch (Data.windowMode)
            {
                case "全屏": Screen.fullScreenMode = FullScreenMode.ExclusiveFullScreen; break;
                case "窗口": Screen.fullScreenMode = FullScreenMode.Windowed; break;
                default: Screen.fullScreenMode = FullScreenMode.FullScreenWindow; break; // 无边框
            }
        }

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

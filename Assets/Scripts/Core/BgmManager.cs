using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 背景音乐播放器（2026-08-17）：启动即循环播放、全程不停（转场/结算/换页都不打断）。
    /// 音源用裁掉尾部静音的 OGG（Resources/SoundEffect/BGM），loop 无缝衔接；
    /// 音量走设置页「背景音乐」条（HouseSettings.Apply 推送），主音量由 AudioListener 统一收口。
    /// </summary>
    public sealed class BgmManager : MonoBehaviour
    {
        private static BgmManager instance;
        private AudioSource source;

        /// <summary>确保 BGM 常驻播放（HouseUIManager 启动时调；重复调用无害）。</summary>
        public static void Ensure()
        {
            if (instance != null) return;
            var clip = Resources.Load<AudioClip>("SoundEffect/BGM");
            if (clip == null)
            {
                Debug.LogWarning("[Bgm] BGM 音频缺失：Resources/SoundEffect/BGM");
                return;
            }
            var go = new GameObject("BgmManager");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<BgmManager>();
            instance.source = go.AddComponent<AudioSource>();
            instance.source.clip = clip;
            instance.source.loop = true;
            instance.source.playOnAwake = false;
            instance.source.volume = Mathf.Clamp01(HouseSettings.Data.bgmVolume / 100f);
            instance.source.Play();
        }

        /// <summary>「背景音乐」音量 0~1（HouseSettings.Apply 调；未启动时静默忽略）。</summary>
        public static void SetVolume(float value01)
        {
            if (instance != null && instance.source != null)
                instance.source.volume = Mathf.Clamp01(value01);
        }
    }
}

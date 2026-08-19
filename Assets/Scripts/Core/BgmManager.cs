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

        /// <summary>音效表里 Bgm 条目的单条音量倍率（乘在设置音量之上）。</summary>
        private float entryVolume = 1f;

        /// <summary>设置页「背景音乐」条的当前值（0~1）。记着它，压低/恢复时才能算回原音量。</summary>
        private float settingsVolume01 = 1f;

        /// <summary>
        /// 临时压低倍率（1 = 不压低）。小游戏这类「要让玩法音效说话」的场合把 BGM 让开一点。
        /// 有意做成 static：压低请求可能早于 / 晚于 BgmManager 存在（测试场景就没有 BGM），
        /// 记在类上，Ensure 时一并生效，也不会因为实例没起来就把请求丢了。
        /// </summary>
        private static float duckFactor = 1f;

        /// <summary>确保 BGM 常驻播放（HouseUIManager 启动时调；重复调用无害）。
        /// 剪辑与音量倍率配置在音效表（ESfx.Bgm，2026-08-17）——换曲/调音改表不碰代码（§16.6）。</summary>
        public static void Ensure()
        {
            if (instance != null) return;
            var table = Resources.Load<SfxTable>("OutGameUI/SfxTable");
            var entry = table != null ? table.Find(ESfx.Bgm) : null;
            if (entry == null || entry.clip == null)
            {
                Debug.LogWarning("[Bgm] 音效表缺少 Bgm 条目或剪辑（改 Resources/OutGameUI/SfxTable 资产补齐）");
                return;
            }
            var go = new GameObject("BgmManager");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<BgmManager>();
            instance.entryVolume = entry.volume;
            instance.source = go.AddComponent<AudioSource>();
            instance.source.clip = entry.clip;
            instance.source.loop = true;
            instance.source.playOnAwake = false;
            instance.settingsVolume01 = Mathf.Clamp01(HouseSettings.Data.bgmVolume / 100f);
            instance.ApplyVolume();
            instance.source.Play();
        }

        /// <summary>「背景音乐」音量 0~1（HouseSettings.Apply 调；未启动时静默忽略）。</summary>
        public static void SetVolume(float value01)
        {
            if (instance == null || instance.source == null) return;
            instance.settingsVolume01 = Mathf.Clamp01(value01);
            instance.ApplyVolume();
        }

        /// <summary>
        /// 临时压低 / 恢复 BGM（0~1 倍率，1 = 恢复原音量，0 = 全哑；2026-08-20 制作咖啡）。
        /// 与设置页音量是**相乘**关系，互不覆盖——压低期间玩家改设置照样生效，恢复后也不会把设置吃掉。
        /// 谁压低谁负责恢复：调用方的结束 / 放弃 / 销毁三条路都要调 SetDuck(1f)。
        /// </summary>
        public static void SetDuck(float factor01)
        {
            duckFactor = Mathf.Clamp01(factor01);
            if (instance != null) instance.ApplyVolume();
        }

        private void ApplyVolume()
        {
            if (source != null) source.volume = settingsVolume01 * entryVolume * duckFactor;
        }
    }
}

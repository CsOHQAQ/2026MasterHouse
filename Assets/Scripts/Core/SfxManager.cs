using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 全局音效播放器（表现层）：全工程音效的唯一出口，播放点一律 SfxManager.Play(ESfx.Xxx)。
    ///
    /// 分层立场：本类属 View 层——只订阅业务事件、只读业务状态，绝不回写（§16.4 表现层豁免的边界）。
    /// UI 交互音（点击/转场/拖拽）由各 View 直接调 Play；业务事件音（访客到来离开、数值增减）
    /// 由本类订阅 VisitorManager / EconomyManager 的事件统一发声，业务代码不出现任何音频调用。
    ///
    /// 内容数据在 SfxTable 资产（§16.6）：换音/调音量/调节流间隔 = 改 Inspector，不碰代码。
    /// 音量消费 HouseSettings.Data.sfxVolume（0~100，§16.5 设置文件），每次播放实时读取。
    /// 由 OutGameBootstrap 在业务 Manager 就位后拉起；Play 内部有惰性兜底，早于拉起的调用不炸只播。
    /// </summary>
    public sealed class SfxManager : MonoBehaviour
    {
        private const string TablePath = "OutGameUI/SfxTable";

        public static SfxManager Instance { get; private set; }

        private AudioSource source;
        private readonly Dictionary<ESfx, SfxEntry> entries = new Dictionary<ESfx, SfxEntry>();
        private readonly Dictionary<ESfx, float> lastPlayTime = new Dictionary<ESfx, float>();
        private readonly HashSet<ESfx> warned = new HashSet<ESfx>();
        private bool bound;

        /// <summary>创建（或返回已存在的）全局实例，并尝试订阅业务事件。</summary>
        public static SfxManager Ensure()
        {
            if (Instance != null) return Instance;
            var existing = FindObjectOfType<SfxManager>();
            if (existing != null) return existing;
            var go = new GameObject("SfxManager", typeof(SfxManager));
            DontDestroyOnLoad(go);
            return go.GetComponent<SfxManager>();
        }

        /// <summary>播放一个音效。None 静默；表缺条目/缺剪辑只警告一次，不阻断调用方。
        /// bypassThrottle：跳过同 ID 最短间隔节流——打字机逐字音要求「有多少字就响多少声」（音效需求 #8 优化），
        /// 由调用方自己排节奏时用；普通播放点别传。</summary>
        public static void Play(ESfx id, bool bypassThrottle = false)
        {
            if (id == ESfx.None || !Application.isPlaying) return;
            Ensure().PlayInternal(id, bypassThrottle);
        }

        /// <summary>
        /// 播放指定剪辑，clip 为空时回落到全局音效 fallback——家具的专属拿起/放下音（家具表可配）走这里。
        /// 音量吃全局 SFX 音量，不做同 ID 节流（拿起/放下是单发事件）。
        /// </summary>
        public static void PlayOverride(AudioClip clip, ESfx fallback)
        {
            if (!Application.isPlaying) return;
            if (clip == null)
            {
                Play(fallback);
                return;
            }
            var manager = Ensure();
            var volume = Mathf.Clamp01(HouseSettings.Data.sfxVolume / 100f);
            if (volume <= 0f || manager.source == null) return;
            manager.source.PlayOneShot(clip, volume);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // 2D 一次性音（PlayOneShot 自带混音，多音重叠不互切）
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;

            var table = Resources.Load<SfxTable>(TablePath);
            if (table == null)
            {
                Debug.LogError("[Sfx] 音效表缺失（Resources/OutGameUI/SfxTable）：" +
                               "请执行菜单 MasterHouse → 音效系统 → 创建音效表（补齐缺失）");
            }
            else
            {
                foreach (var entry in table.entries)
                    if (entry != null && entry.id != ESfx.None)
                        entries[entry.id] = entry;
            }

            TryBindGameEvents();
        }

        private void OnDestroy()
        {
            UnbindGameEvents();
            if (Instance == this) Instance = null;
        }

        private void PlayInternal(ESfx id, bool bypassThrottle = false)
        {
            if (!entries.TryGetValue(id, out var entry) || entry.clip == null)
            {
                if (warned.Add(id))
                    Debug.LogWarning($"[Sfx] 音效表缺少条目或剪辑：{id}（改 Resources/OutGameUI/SfxTable 资产补齐）");
                return;
            }

            // 同 ID 节流：防同帧多处触发叠爆（打字机逐字音自排节奏，走 bypassThrottle）
            var now = Time.unscaledTime;
            if (!bypassThrottle && lastPlayTime.TryGetValue(id, out var last) && now - last < entry.minInterval) return;
            lastPlayTime[id] = now;

            var volume = Mathf.Clamp01(HouseSettings.Data.sfxVolume / 100f) * entry.volume;
            if (volume <= 0f) return;
            // 有随机变体时在 clip+variants 里随机挑一个（打字机击键声不重复感）
            var clip = entry.clip;
            if (entry.variants != null && entry.variants.Count > 0)
            {
                var pick = Random.Range(0, entry.variants.Count + 1);
                if (pick > 0 && entry.variants[pick - 1] != null) clip = entry.variants[pick - 1];
            }
            source.PlayOneShot(clip, volume);
        }

        // ── 业务事件音（需求 #4 数值变化、#6 访客到来/离开）──

        /// <summary>订阅方向是 Manager → View 的单向广播，与 HubPage 等界面订阅同一批事件，互不知晓。</summary>
        private void TryBindGameEvents()
        {
            if (bound) return;
            var gm = GameManager.Instance;
            if (gm == null || gm.VisitorManager == null || gm.EconomyManager == null) return;
            gm.VisitorManager.InstanceSpawned += OnVisitorSpawned;
            gm.VisitorManager.InstanceDeparted += OnVisitorDeparted;
            gm.EconomyManager.Feedback += OnEconomyFeedback;
            bound = true;
        }

        private void UnbindGameEvents()
        {
            // 应用退出时常驻对象销毁顺序不定，GameManager 可能先没（同 FurnitureRoomController.OnDestroy 的处理）
            if (!bound || GameManager.Instance == null) return;
            var gm = GameManager.Instance;
            if (gm.VisitorManager != null)
            {
                gm.VisitorManager.InstanceSpawned -= OnVisitorSpawned;
                gm.VisitorManager.InstanceDeparted -= OnVisitorDeparted;
            }
            if (gm.EconomyManager != null) gm.EconomyManager.Feedback -= OnEconomyFeedback;
            bound = false;
        }

        private void OnVisitorSpawned(VisitorInstance instance) => PlayInternal(ESfx.VisitorArrive);

        private void OnVisitorDeparted(VisitorInstance instance) => PlayInternal(ESfx.VisitorLeave);

        /// <summary>玩法收支 → 音效的映射：声望增减是「数值变化提示」，货币入账是「获得奖励」（对话奖励发货币）。</summary>
        private void OnEconomyFeedback(EEconomyFeedback feedback)
        {
            switch (feedback)
            {
                case EEconomyFeedback.ReputationGain: PlayInternal(ESfx.ValueGain); break;
                case EEconomyFeedback.CurrencyGain: PlayInternal(ESfx.Reward); break;
                default: PlayInternal(ESfx.ValueLose); break;
            }
        }
    }
}

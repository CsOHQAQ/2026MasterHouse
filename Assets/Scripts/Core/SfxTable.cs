using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 音效 ID：一项对应音效需求清单里的一类反馈（2026-08-12 音效需求——11 个）。
    /// 播放点只写 ID 不写剪辑，换音/调音 = 改 SfxTable 资产，不碰代码（§16.6）。
    /// 显式赋值：ID 会被序列化进 SfxTable 资产，插入新枚举项不得改变已有值。
    /// </summary>
    public enum ESfx
    {
        /// <summary>显式静音：按钮的基础点击音被更具体的动作音取代时用（如访客卡、对话推进）。</summary>
        None = 0,
        /// <summary>需求 1 · 基础点击：普通按钮 / tab 切换 / 翻页箭头 / 菜单按钮统一用。</summary>
        UiClick = 1,
        /// <summary>需求 2 · 家具拾起（开始拖拽）。</summary>
        FurniturePickup = 2,
        /// <summary>需求 2 · 家具放置（落地 / 收回收纳栏）。</summary>
        FurniturePlace = 3,
        /// <summary>需求 3 · 客人交互：点访客卡片或 NPC、对话点击继续。</summary>
        GuestInteract = 4,
        /// <summary>需求 4 · 数值正向提示（声望+ / 任务完成等通用）。</summary>
        ValueGain = 5,
        /// <summary>需求 4 · 数值负向提示（声望- / 任务失败等通用）。</summary>
        ValueLose = 6,
        /// <summary>需求 5 · 页面转场：整页切换 / 进出家具模式 / 切换房间。</summary>
        PageTransition = 7,
        /// <summary>需求 6 · 访客到来通知。</summary>
        VisitorArrive = 8,
        /// <summary>需求 6 · 访客离开通知。</summary>
        VisitorLeave = 9,
        /// <summary>需求 7 · 资源&奖励获得：任务奖励 / 商城购买 / 对话发放物资货币。</summary>
        Reward = 10,
        /// <summary>需求 8 · 对话逐字显示音（打字机）。</summary>
        DialogueTyping = 11,
        /// <summary>背景音乐：全程循环主题曲（由 BgmManager 消费，不走一次性音效通道，2026-08-17）。</summary>
        Bgm = 12,
    }

    /// <summary>音效表单条：ID → 剪辑与播放参数。</summary>
    [Serializable]
    public sealed class SfxEntry
    {
        public ESfx id;
        public AudioClip clip;

        [Tooltip("随机变体（可空）：非空时每次播放在 clip+variants 里随机挑一个——打字机单声击键这类需要不重复感的音效用")]
        public List<AudioClip> variants = new List<AudioClip>();

        [Tooltip("单条音量倍率，乘在全局 SFX 音量（设置文件 sfxVolume）之上")]
        [Range(0f, 2f)] public float volume = 1f;

        [Tooltip("同一 ID 两次播放的最短间隔（秒）：防同帧多处触发叠爆；逐字音的打字节奏也靠它控制")]
        public float minInterval = 0.05f;
    }

    /// <summary>
    /// 音效表（Model，运行时只读，§16.6）：全部音效的唯一登记处，由 SfxManager 加载消费。
    /// 缺失是报错不是回退——请执行菜单 MasterHouse → 音效系统 → 创建音效表（补齐缺失）。
    /// </summary>
    [CreateAssetMenu(menuName = "MasterHouse/音效表", fileName = "SfxTable")]
    public sealed class SfxTable : ScriptableObject
    {
        public List<SfxEntry> entries = new List<SfxEntry>();

        /// <summary>按 ID 找条目（BgmManager 等非 SfxManager 消费方用）；缺失返回 null。</summary>
        public SfxEntry Find(ESfx id)
        {
            foreach (var entry in entries)
                if (entry != null && entry.id == id) return entry;
            return null;
        }
    }
}

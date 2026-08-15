using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 「制作咖啡」的关卡资产：一关 = 一组手感参数（磨豆子 + 冲咖啡两个环节）。
    ///
    /// 改手感 = 改 Inspector；换难度 = 多建一个本资产塞进 MinigameDef 的关卡池
    /// （架构 §15.3「不预设抽象」，与修理电路同例，不加难度字段）。
    /// 纯表现参数（环半径比例、配色等）不在这里——那些在 CoffeeMinigameView 上。
    ///
    /// 计分结构（2026-08-15 访谈拍板）：磨豆子上限 50 分起始即满、撞障碍扣分；
    /// 冲咖啡按速度方差三档定分；总分 = 两环节相加，宿主按 MinigameDef 阈值定满意度档。
    ///
    /// 【务必独占本文件】ScriptableObject 必须与文件同名，否则 .asset 的脚本引用会损坏（见 RETRO）。
    /// </summary>
    [CreateAssetMenu(menuName = "MasterHouse/小游戏关卡·制作咖啡", fileName = "Coffee_")]
    public sealed class CoffeeLevelDef : MinigameLevelDef
    {
        [Header("① 磨豆子（起始满分，撞障碍扣分扣进度，进度满进下一环节）")]
        [Tooltip("正常旋转下进度 0→满 所需秒数（撞障碍的硬直期间不积累，不计入）")]
        [Min(0.5f)] public float GrindFillSeconds = 8f;

        [Tooltip("指针转速（度/秒）")]
        [Min(1f)] public float PointerDegreesPerSecond = 120f;

        [Tooltip("障碍总数（两环合计，随机落环、随机角度；每次开局重新随机）")]
        [Min(0)] public int ObstacleCount = 6;

        [Tooltip("单个障碍占的弧长（度）")]
        [Min(1f)] public float ObstacleArcDegrees = 24f;

        [Tooltip("障碍中心距的额外间隔（度）：任意两个障碍（不分同环异环）中心距 ≥ 弧长 + 此值。\n" +
                 "同环防贴脸；异环保证任意角度至少一环可走——不出无解局")]
        [Min(0f)] public float ObstacleMinGapDegrees = 30f;

        [Tooltip("初始位置安全区（度）：障碍边沿距指针出生角度至少这么多度，两环都算——\n" +
                 "开局不会贴脸挨撞，出生点立即切环也安全。加大会压缩可放障碍的角度空间，" +
                 "配太多障碍可能放不下（控制台会有警告）")]
        [Min(0f)] public float SpawnSafeDegrees = 30f;

        [Tooltip("本环节满分（也是起始分）")]
        [Min(0)] public int GrindMaxScore = 50;

        [Tooltip("撞一次障碍扣的分（最低扣到 0）")]
        [Min(0)] public int HitScorePenalty = 10;

        [Tooltip("撞一次障碍扣的进度（0~1）")]
        [Range(0f, 1f)] public float HitProgressPenalty = 0.15f;

        [Tooltip("撞障碍后的硬直秒数：指针停转、进度不积累、点击无效，结束后从障碍内继续转出（不重复判定）")]
        [Min(0f)] public float HitStunSeconds = 1f;

        [Header("② 冲咖啡（按住左键在杯内匀速移动，按速度方差定档）")]
        [Tooltip("按住且在杯内时进度 0→满 所需秒数（出杯/松手只暂停进度，无额外惩罚）")]
        [Min(0.5f)] public float PourFillSeconds = 6f;

        [Tooltip("速度采样间隔（秒）。速度按「杯径/秒」归一化（杯是圆形，杯径 = 判定圆直径），与分辨率无关；" +
                 "出杯/松手会丢弃未满的采样窗")]
        [Min(0.01f)] public float SpeedSampleSeconds = 0.05f;

        [Tooltip("最低平均速度（杯径/秒）。实测平均速度低于它时，方差改按它为基准算——\n" +
                 "按住不动刷零方差拿优秀的路被堵死（2026-08-15 拍板）。\n" +
                 "调阈值时保持 此值² > 良好方差上限，否则按住不动会溢出到「良好」档（0.3²=0.09 > 0.08）")]
        [Min(0f)] public float MinAverageSpeed = 0.3f;

        [Tooltip("方差 ≤ 此值为「优秀」。单位是(杯径/秒)²，占位值需实测调——测试场景左下角的调参信息会实时显示方差")]
        [Min(0f)] public float ExcellentVarianceMax = 0.02f;

        [Tooltip("方差 ≤ 此值为「良好」，超过则「普通」")]
        [Min(0f)] public float GoodVarianceMax = 0.08f;

        [Tooltip("冲咖啡三档得分：优秀")]
        [Min(0)] public int PourExcellentScore = 50;

        [Tooltip("良好")]
        [Min(0)] public int PourGoodScore = 30;

        [Tooltip("普通")]
        [Min(0)] public int PourPlainScore = 20;
    }
}
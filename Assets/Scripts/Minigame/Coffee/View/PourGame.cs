using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 冲咖啡环节：按住左键在杯内移动，进度均匀增长；结算按移动速度的方差定三档。
    ///
    /// 规则（2026-08-15 访谈拍板）：
    /// - 杯是**圆形**（同日测试反馈）：判定用 cupArea 矩形的内切圆，视觉与判定同圆；
    /// - 进度只在「按住 + 在杯内」时增长；出杯/松手只暂停，无额外惩罚，未满的采样窗一并丢弃；
    /// - 速度按「杯径/秒」归一化（与分辨率无关），按固定间隔采样（不逐帧，免得高帧率下噪声淹掉手感）；
    /// - 方差的基准取 max(实测平均速度, MinAverageSpeed)：均速不达标时按最低均速算方差——
    ///   按住不动时样本全为 0、离基准全是 MinAverageSpeed，方差被顶到 MinAverageSpeed²，
    ///   「原地不动刷零方差拿优秀」的路就此堵死。
    ///
    /// 本类不认识任何 Manager（架构 §8.5 硬约束），时间由根组件按帧喂入 deltaTime。
    /// </summary>
    public sealed class PourGame
    {
        private readonly CoffeeLevelDef level;
        private readonly CoffeeMinigameView view;
        private readonly Camera uiCamera;

        private readonly List<float> samples = new List<float>();
        private float windowTime;
        private float windowDist;
        private bool wasActive;
        private Vector2 lastLocal;

        private float progress;
        private bool complete;

        public float Progress => progress;
        public bool IsComplete => complete;

        /// <summary>本帧是否在有效倒水（按住且在杯内）。纯表现用：驱动冒环节奏与边缘晃动幅度。</summary>
        public bool IsPouring => wasActive;

        /// <summary>最近一次有效倒水点（cupArea 的 uv 坐标 0~1）；从未倒过时为杯心。纯表现用：涡环在这里出生。</summary>
        public Vector2 PourPointUv { get; private set; } = new Vector2(0.5f, 0.5f);

        /// <summary>结算后有效（IsComplete 之前是 0 / null）。</summary>
        public int Score { get; private set; }
        public string GradeName { get; private set; }

        public PourGame(CoffeeMinigameView view, CoffeeLevelDef level, Camera uiCamera)
        {
            this.view = view;
            this.level = level;
            this.uiCamera = uiCamera;
        }

        /// <summary>
        /// 丢掉「上一帧还在倒水」的记忆（2026-08-20 加暂停时补）：
        /// 暂停期间本类不被 Tick，lastLocal 会停在暂停前那一点。玩家在弹窗上把鼠标挪开再继续，
        /// 下一次有效帧就会把这段位移算成一次瞬移，方差被顶得很高、白白掉档。
        /// 调它之后，下一次进入按「刚进入」处理：只重记起点、丢掉没满的采样窗，与出杯再回杯同一条路。
        /// </summary>
        public void DropTracking() => wasActive = false;

        public void Tick(float dt)
        {
            if (complete) return;

            bool active = Input.GetMouseButton(0)
                          && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                                 view.cupArea, Input.mousePosition, uiCamera, out var local)
                          && InsideCup(local);

            if (active)
            {
                if (!wasActive)
                {
                    // 刚进入：只记起点，丢掉上一段未满的采样窗
                    lastLocal = LocalMouse();
                    windowTime = 0f;
                    windowDist = 0f;
                    UpdatePourPointUv();
                }
                else
                {
                    var now = LocalMouse();
                    // 归一化基准 = 杯径（矩形短边，即内切圆直径）
                    float diameter = Mathf.Min(view.cupArea.rect.width, view.cupArea.rect.height);
                    windowDist += (now - lastLocal).magnitude / Mathf.Max(1f, diameter);
                    windowTime += dt;
                    lastLocal = now;
                    UpdatePourPointUv();

                    if (windowTime >= level.SpeedSampleSeconds)
                    {
                        samples.Add(windowDist / windowTime);
                        windowTime = 0f;
                        windowDist = 0f;
                    }
                }

                progress += dt / Mathf.Max(0.1f, level.PourFillSeconds);
            }
            wasActive = active;

            // 「正在搅」的反馈交给液面波纹 + 循环音（2026-08-20 换美术底图时去掉了判定区整体染色）：
            // 杯子已经画在底图里，再给判定区糊一层色只会把水彩笔触盖掉

            if (progress >= 1f)
            {
                progress = 1f;
                complete = true;
                Settle();
            }
        }

        /// <summary>
        /// 实时统计（调参标签用；结算也走这里，保证看到的和算到的是同一套数）。
        /// 方差基准 = max(均速, MinAverageSpeed)，理由见类注释。
        /// </summary>
        public (float mean, float variance, int count) Stats()
        {
            int count = samples.Count;
            if (count == 0)
            {
                // 无有效样本 = 按原地不动算：方差顶到基准的平方
                return (0f, level.MinAverageSpeed * level.MinAverageSpeed, 0);
            }

            float mean = 0f;
            for (int i = 0; i < count; i++) mean += samples[i];
            mean /= count;

            float reference = Mathf.Max(mean, level.MinAverageSpeed);
            float variance = 0f;
            for (int i = 0; i < count; i++)
            {
                float d = samples[i] - reference;
                variance += d * d;
            }
            variance /= count;
            return (mean, variance, count);
        }

        /// <summary>
        /// 最近 windowSeconds 内样本的速度方差（基准公式与 Stats 同款：max(窗口均速, MinAverageSpeed)，
        /// 按住不动同样会被顶到 MinAverageSpeed²，视觉反馈和判分口径一致）。
        /// 纯表现用（驱动液面边缘晃动速度），结算仍走 Stats() 的累计方差。
        /// 尚无样本时返回 0：开局没倒过水，液面该是平静的。
        /// </summary>
        public float RecentVariance(float windowSeconds)
        {
            int count = samples.Count;
            if (count == 0) return 0f;

            int n = Mathf.Clamp(
                Mathf.RoundToInt(windowSeconds / Mathf.Max(0.01f, level.SpeedSampleSeconds)), 1, count);

            float mean = 0f;
            for (int i = count - n; i < count; i++) mean += samples[i];
            mean /= n;

            float reference = Mathf.Max(mean, level.MinAverageSpeed);
            float variance = 0f;
            for (int i = count - n; i < count; i++)
            {
                float d = samples[i] - reference;
                variance += d * d;
            }
            return variance / n;
        }

        private void Settle()
        {
            var (_, variance, _) = Stats();
            if (variance <= level.ExcellentVarianceMax)
            {
                Score = level.PourExcellentScore;
                GradeName = "优秀";
            }
            else if (variance <= level.GoodVarianceMax)
            {
                Score = level.PourGoodScore;
                GradeName = "良好";
            }
            else
            {
                Score = level.PourPlainScore;
                GradeName = "普通";
            }
        }

        /// <summary>杯是圆的：以 cupArea 矩形的内切圆判定，半径 = 短边一半。</summary>
        private bool InsideCup(Vector2 local)
        {
            var rect = view.cupArea.rect;
            float radius = Mathf.Min(rect.width, rect.height) * 0.5f;
            return (local - rect.center).sqrMagnitude <= radius * radius;
        }

        /// <summary>把 lastLocal（局部坐标）换算成 cupArea 的 uv，喂给水面材质当波源。</summary>
        private void UpdatePourPointUv()
        {
            var rect = view.cupArea.rect;
            PourPointUv = new Vector2(
                (lastLocal.x - rect.xMin) / Mathf.Max(1f, rect.width),
                (lastLocal.y - rect.yMin) / Mathf.Max(1f, rect.height));
        }

        private Vector2 LocalMouse()
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                view.cupArea, Input.mousePosition, uiCamera, out var local);
            return local;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 磨豆子环节（渔帆暗涌式钓鱼盘）：指针绕环匀速转，左键切环避障，进度满则完成。
    ///
    /// 规则（2026-08-15 访谈拍板）：
    /// - 起始即满分（GrindMaxScore），撞一次障碍扣 HitScorePenalty 分与 HitProgressPenalty 进度，最低 0 分；
    /// - 撞后硬直 HitStunSeconds：指针停转、进度停积累、点击无效，结束后从障碍内继续转出——
    ///   碰撞只在「进入障碍」的瞬间触发，已在障碍内不再重复判定，这正是「停转片刻再继续」的实现；
    /// - 「进入」包含转入与切环切入两种：切进障碍同样算撞（是玩家自己的失误）；
    /// - 障碍每次开局重新随机（小游戏属 View 层豁免区，允许无种子 Random，架构 §11 豁免表）；
    ///   生成约束见 GenerateObstacles——任意角度至少一环可走（不出无解局）、
    ///   两环各保底一个障碍（数量 ≥ 2 时）、初始位置有安全区（开局不贴脸）。
    ///
    /// 本类不认识任何 Manager（架构 §8.5 硬约束），时间由根组件按帧喂入 deltaTime。
    /// </summary>
    public sealed class GrindGame
    {
        /// <summary>0 = 内环，1 = 外环。</summary>
        private struct Obstacle
        {
            public int Ring;
            public float Center;
        }

        private readonly CoffeeLevelDef level;
        private readonly CoffeeMinigameView view;

        private readonly List<Obstacle> obstacles = new List<Obstacle>();

        /// <summary>运行时生成的点（环轮廓 + 障碍弧段），存角度是为了分辨率变化时重摆。</summary>
        private readonly List<(RectTransform rect, int ring, float angle)> dots =
            new List<(RectTransform, int, float)>();

        private float pointerAngle;
        private int currentRing = 1; // 开局在外环
        private float progress;
        private int score;
        private float stunRemaining;
        private bool insideObstacle; // 「进入沿」判定：只有从外到内的瞬间才算撞
        private bool complete;
        private Vector2 lastAreaSize;

        public int Score => score;
        public float Progress => progress;
        public bool IsComplete => complete;

        /// <summary>撞到障碍（扣分扣进度已生效）。根组件订阅它来闪提示。</summary>
        public event Action Hit;

        public GrindGame(CoffeeMinigameView view, CoffeeLevelDef level)
        {
            this.view = view;
            this.level = level;
        }

        public void Init()
        {
            score = level.GrindMaxScore;
            view.grindDotTemplate.gameObject.SetActive(false);

            // 先定初始位置再撒障碍：生成时直接拒绝落进安全区的候选，
            // 从构造上保证开局不贴脸（比生成后再找空当可靠——那样找不到时只能硬着头皮落子）
            pointerAngle = UnityEngine.Random.Range(0f, 360f);

            GenerateObstacles();
            BuildRingDots();
            BuildObstacleDots();

            insideObstacle = IsInsideObstacle(currentRing, pointerAngle); // 安全区保证下必为 false，留作兜底
            lastAreaSize = view.grindArea.rect.size;
            UpdatePointer();
        }

        // ══════════ 每帧 ══════════

        /// <summary>左键切环。硬直期间输入被吃掉（硬直就是硬直）。</summary>
        public void HandleInput()
        {
            if (complete || stunRemaining > 0f) return;
            if (!Input.GetMouseButtonDown(0)) return;

            currentRing = 1 - currentRing;
            bool now = IsInsideObstacle(currentRing, pointerAngle);
            if (now && !insideObstacle) RegisterHit();
            insideObstacle = now;
            UpdatePointer();
        }

        public void Tick(float dt)
        {
            if (complete) return;

            if (stunRemaining > 0f)
            {
                // 停转、停积累；结束帧只恢复配色，下一帧起从障碍内继续转出（不重复判定）
                stunRemaining -= dt;
                if (stunRemaining <= 0f) UpdatePointer();
                return;
            }

            pointerAngle = Mathf.Repeat(pointerAngle + level.PointerDegreesPerSecond * dt, 360f);

            bool now = IsInsideObstacle(currentRing, pointerAngle);
            if (now && !insideObstacle)
                RegisterHit();
            else
                progress = Mathf.Min(1f, progress + dt / Mathf.Max(0.1f, level.GrindFillSeconds));
            insideObstacle = now;

            UpdatePointer();
            if (progress >= 1f) complete = true;
        }

        /// <summary>分辨率/窗口变化时按新尺寸重摆所有点与指针（与 CircuitMinigame 的尺寸检测同例）。</summary>
        public void RelayoutIfResized()
        {
            var size = view.grindArea.rect.size;
            if ((size - lastAreaSize).sqrMagnitude <= 1f) return;
            lastAreaSize = size;

            foreach (var (rect, ring, angle) in dots)
                rect.anchoredPosition = PointOn(ring, angle);
            UpdatePointer();
        }

        // ══════════ 判定 ══════════

        private void RegisterHit()
        {
            score = Mathf.Max(0, score - level.HitScorePenalty);
            progress = Mathf.Max(0f, progress - level.HitProgressPenalty);
            stunRemaining = level.HitStunSeconds;
            Hit?.Invoke();
        }

        private bool IsInsideObstacle(int ring, float angle)
        {
            float halfArc = level.ObstacleArcDegrees * 0.5f;
            foreach (var o in obstacles)
                if (o.Ring == ring && Mathf.Abs(Mathf.DeltaAngle(angle, o.Center)) <= halfArc)
                    return true;
            return false;
        }

        // ══════════ 生成 ══════════

        /// <summary>
        /// 拒绝采样铺障碍，两条角度约束：
        /// - 任意两个障碍（**不分同环异环**）中心距 ≥ 弧长 + 最小间隔。同环防贴脸；
        ///   异环保证两环障碍不在角度上重叠——任何时刻至少一环可走，不出无解局；
        /// - 障碍边沿距指针出生角度 ≥ SpawnSafeDegrees（两环都算，2026-08-15 测试反馈）：
        ///   开局不贴脸挨撞，出生点立即切环也安全。调用前 pointerAngle 必须已定。
        /// </summary>
        private void GenerateObstacles()
        {
            float minCenterDistance = level.ObstacleArcDegrees + level.ObstacleMinGapDegrees;
            float spawnMinCenterDistance = level.ObstacleArcDegrees * 0.5f + level.SpawnSafeDegrees;

            int tries = 0;
            while (obstacles.Count < level.ObstacleCount && tries < 400)
            {
                tries++;

                // 前两个障碍各占一环（2026-08-15 测试反馈）：纯随机落环可能整环全空，
                // 切环避障就没意义了。障碍总数不足两个时不强制——题面本来就铺不满两环。
                // 只定环不定角度，角度仍是全随机，不引入可感知的布局偏置。
                int ring = level.ObstacleCount >= 2 && obstacles.Count < 2
                    ? obstacles.Count
                    : UnityEngine.Random.Range(0, 2);

                var candidate = new Obstacle
                {
                    Ring = ring,
                    Center = UnityEngine.Random.Range(0f, 360f),
                };

                // 初始位置安全区（不分环，出生点切环也安全）
                if (Mathf.Abs(Mathf.DeltaAngle(candidate.Center, pointerAngle)) < spawnMinCenterDistance)
                    continue;

                bool fits = true;
                foreach (var o in obstacles)
                {
                    if (Mathf.Abs(Mathf.DeltaAngle(candidate.Center, o.Center)) < minCenterDistance)
                    {
                        fits = false;
                        break;
                    }
                }
                if (fits) obstacles.Add(candidate);
            }

            if (obstacles.Count < level.ObstacleCount)
                Debug.LogWarning($"[制作咖啡] 障碍太密放不下：要求 {level.ObstacleCount} 个，" +
                                 $"只放下 {obstacles.Count} 个。请在关卡里调小数量/弧长/间隔");
        }

        // ══════════ 表现 ══════════

        private void BuildRingDots()
        {
            int count = Mathf.Max(8, view.ringDotCount);
            for (int ring = 0; ring < 2; ring++)
                for (int i = 0; i < count; i++)
                    SpawnDot(ring, i * 360f / count, view.ringDotSize, view.ringColor);
        }

        private void BuildObstacleDots()
        {
            foreach (var o in obstacles)
            {
                // 弧段用点串出来（无美术阶段的占位画法），点距约 5°
                int steps = Mathf.Max(2, Mathf.CeilToInt(level.ObstacleArcDegrees / 5f));
                float start = o.Center - level.ObstacleArcDegrees * 0.5f;
                for (int s = 0; s <= steps; s++)
                    SpawnDot(o.Ring, start + level.ObstacleArcDegrees * s / steps,
                        view.obstacleDotSize, view.obstacleColor);
            }
        }

        private void SpawnDot(int ring, float angle, float size, Color color)
        {
            var dot = UnityEngine.Object.Instantiate(view.grindDotTemplate, view.grindContentRoot, false);
            dot.gameObject.SetActive(true);
            dot.color = color;
            dot.raycastTarget = false;
            var rect = (RectTransform)dot.transform;
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = PointOn(ring, angle);
            dots.Add((rect, ring, angle));
        }

        private void UpdatePointer()
        {
            view.pointer.anchoredPosition = PointOn(currentRing, pointerAngle);
            if (view.pointerImage != null)
                view.pointerImage.color = stunRemaining > 0f ? view.pointerStunColor : view.pointerColor;
        }

        private Vector2 PointOn(int ring, float angle)
        {
            var rect = view.grindArea.rect;
            float shortSide = Mathf.Min(rect.width, rect.height);
            float radius = shortSide * (ring == 1 ? view.outerRingRadiusFraction : view.innerRingRadiusFraction);
            float rad = angle * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * radius;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 磨豆子环节（渔帆暗涌式钓鱼盘）：指针在内外两环上走，撞红色障碍扣分，进度满则完成。
    ///
    /// 操作方式有两套，由关卡的 EGrindMode 选（2026-08-19 加入试玩玩法）：
    /// - AutoSpin（原玩法）：指针自己匀速转，左键点击切换内外环避障；
    /// - MouseCrank（试玩）：指针 = 鼠标在盘上的投影（角度取鼠标方位角，半径吸附到最近的环），
    ///   按住左键绕圆心**顺时针**画圈才涨进度——玩家自己「摇磨柄」，同时靠进出圆心变道躲障碍。
    /// 两套共用同一份障碍生成、碰撞判定与扣分规则；切回原玩法只改关卡资产的 GrindMode。
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

        /// <summary>摇柄模式把鼠标换算到圆盘局部坐标用；Overlay 画布传 null（与 PourGame 同例）。</summary>
        private readonly Camera uiCamera;

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

        // ── 摇柄模式状态 ──

        /// <summary>是否已「握住磨柄」：第一次按下左键后**永久**为 true，此后指针一直跟随鼠标。
        /// 松手不脱手是故意的——否则玩家可以松手把鼠标挪过障碍再按下，白嫖过障碍。</summary>
        private bool crankEngaged;

        /// <summary>上一帧鼠标的方位角（度），用来取本帧转过的角度。</summary>
        private float crankMouseAngle;

        /// <summary>上一帧是否在正常跟随：硬直、死区、鼠标解不出来时置 false，
        /// 下一帧只重新对基准、不把这段没跟随的角度一次性补成进度。</summary>
        private bool crankTracking;

        public int Score => score;
        public float Progress => progress;
        public bool IsComplete => complete;

        /// <summary>摇柄模式下是否已握住磨柄（调参标签显示用）。</summary>
        public bool CrankEngaged => crankEngaged;

        /// <summary>是否正处在撞障碍后的硬直里（指针停转、进度冻结）。
        /// 根组件靠它掐研磨循环音——磨盘停转，磨豆声也该停（2026-08-20）。</summary>
        public bool IsStunned => stunRemaining > 0f;

        /// <summary>撞到障碍（扣分扣进度已生效）。根组件订阅它来闪提示。</summary>
        public event Action Hit;

        public GrindGame(CoffeeMinigameView view, CoffeeLevelDef level, Camera uiCamera)
        {
            this.view = view;
            this.level = level;
            this.uiCamera = uiCamera;
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

        /// <summary>左键切环（仅 AutoSpin）。硬直期间输入被吃掉（硬直就是硬直）。
        /// 摇柄模式的左键是「按住摇」——连续状态而非单次事件，读在 Tick 里。</summary>
        public void HandleInput()
        {
            if (complete || stunRemaining > 0f) return;
            if (level.GrindMode != EGrindMode.AutoSpin) return;
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
                // 停转、停积累；结束帧只恢复配色，下一帧起从障碍内继续转出（不重复判定）。
                // 摇柄模式下 crankTracking 已在撞击时置 false：硬直期间鼠标怎么动都不算进度，
                // 硬直结束后指针直接吸回鼠标当前位置，也不补判这段位移
                stunRemaining -= dt;
                if (stunRemaining <= 0f) UpdatePointer();
                return;
            }

            if (level.GrindMode == EGrindMode.MouseCrank) TickCrank();
            else TickAutoSpin(dt);

            UpdatePointer();
            if (progress >= 1f) complete = true;
        }

        /// <summary>原玩法：指针匀速自转，进度按时间涨。</summary>
        private void TickAutoSpin(float dt)
        {
            pointerAngle = Mathf.Repeat(pointerAngle + level.PointerDegreesPerSecond * dt, 360f);

            bool now = IsInsideObstacle(currentRing, pointerAngle);
            if (now && !insideObstacle)
                RegisterHit();
            else
                progress = Mathf.Min(1f, progress + dt / Mathf.Max(0.1f, level.GrindFillSeconds));
            insideObstacle = now;
        }

        /// <summary>
        /// 试玩玩法：指针 = 鼠标在盘上的投影，按住左键顺时针画圈才涨进度。
        ///
        /// 进度只认**净**顺时针转角（逆时针按 CrankBackwardFactor 倒扣），因此来回抖动正负相抵、
        /// 刷不出进度；轴心附近微小位移会被放大成大角度，故设死区，进死区就停跟随。
        /// 不吃 dt：转多少角度算多少进度，与帧率无关（摇得快只是磨得快，这正是想要的手感）。
        /// </summary>
        private void TickCrank()
        {
            if (!TryProjectMouse(out float mouseAngle, out int ring))
            {
                crankTracking = false; // 死区/鼠标解不出来：本帧不跟随，下一帧重新对基准
                return;
            }

            if (!crankEngaged)
            {
                if (!Input.GetMouseButtonDown(0)) return; // 没握住磨柄前，指针停在出生点
                crankEngaged = true;
                // 握柄这一下豁免：指针从出生点直接吸到鼠标处，玩家还没开始摇就先挨一下不合理。
                // 此后松手也不脱手，「松手挪过障碍再按下」的白嫖路子就此堵死（见 crankEngaged 注释）
                insideObstacle = IsInsideObstacle(ring, mouseAngle);
            }

            float delta = crankTracking ? Mathf.DeltaAngle(crankMouseAngle, mouseAngle) : 0f;
            crankMouseAngle = mouseAngle;
            crankTracking = true;

            pointerAngle = mouseAngle;
            currentRing = ring;

            bool now = IsInsideObstacle(currentRing, pointerAngle);
            bool entered = now && !insideObstacle;
            insideObstacle = now;

            if (entered)
            {
                RegisterHit();
                crankTracking = false;
                return;
            }

            if (!Input.GetMouseButton(0)) return; // 没按住就只是挪指针（照样会撞），不涨进度

            float clockwise = -delta; // 屏幕上顺时针 = 方位角减小
            if (clockwise < 0f) clockwise *= level.CrankBackwardFactor;
            progress = Mathf.Clamp01(progress + clockwise / Mathf.Max(1f, level.CrankTotalDegrees));
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

        /// <summary>
        /// 摇柄模式：把鼠标投影到圆盘上——角度取相对圆心的方位角，环按半径吸附到最近的一环
        /// （以两环半径的中线为界，口径与 PointOn 的「短边 × 比例」一致）。
        /// 鼠标解不出局部坐标、或落在轴心死区内时返回 false（该帧不跟随、不积累）。
        /// </summary>
        private bool TryProjectMouse(out float angle, out int ring)
        {
            angle = pointerAngle;
            ring = currentRing;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    view.grindArea, Input.mousePosition, uiCamera, out var local))
                return false;

            var rect = view.grindArea.rect;
            var fromCenter = local - rect.center;
            float shortSide = Mathf.Min(rect.width, rect.height);
            float radiusFraction = fromCenter.magnitude / Mathf.Max(1f, shortSide);
            if (radiusFraction < level.CrankDeadZoneFraction) return false;

            angle = Mathf.Repeat(Mathf.Atan2(fromCenter.y, fromCenter.x) * Mathf.Rad2Deg, 360f);
            float midFraction = (view.innerRingRadiusFraction + view.outerRingRadiusFraction) * 0.5f;
            ring = radiusFraction >= midFraction ? 1 : 0;
            return true;
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

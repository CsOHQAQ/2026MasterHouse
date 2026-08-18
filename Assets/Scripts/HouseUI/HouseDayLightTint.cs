using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 界面昼夜调色（2026-08-18 反馈：弹窗/整屏页的背景也要随时间慢慢变）：
    /// 把 <see cref="HouseDayLight"/> 的色带套到页面底图上——底图走乘法调色（保对比度），
    /// 深夜再叠一层罩色把不吃调色的元素一起压进夜色。做法与 Hub 场景、标题封面一致，
    /// 所以从场景切进商店/设置，灯光是接得上的。
    ///
    /// 入夜后的两件事（2026-08-18 反馈）：
    /// ①「像灯一样在照明」——罩色只盖到底图那一层，文字与线条不被压暗，反而转成暖橘黄；
    /// ②「边框和按钮也需要描边」——底色压暗后细线、框、按钮都发糊，统一补一圈深色描边。
    /// 描边的浓度跟着夜色走，白天为全透明，等于不存在。
    ///
    /// 非布局件，运行时挂（口径同打字机/键位组件，§16.2 例外）：不写 Prefab、不改任何位置尺寸，
    /// 只在自己这一层改颜色。设置里关掉「昼夜交替」时色带恒为正午基准，等于自动失效。
    /// </summary>
    public sealed class HouseDayLightTint : MonoBehaviour
    {
        /// <summary>约定：名字里带这些词的图形算「页面底图」，吃乘法调色。</summary>
        private static readonly string[] BackgroundNames = { "background", "backdrop", "cover", "scrim" };

        /// <summary>约定：名字里带这些词的图形算「线条/边框」，夜里跟着提亮并描边。</summary>
        private static readonly string[] LineNames =
            { "rule", "line", "divider", "row", "separator", "frame", "border", "panel" };

        /// <summary>色带跳变时的追随速度（时钟按 tick 走，插值一下才是「慢慢变」）。</summary>
        private const float FollowSpeed = 2.2f;

        /// <summary>夜间灯光色：暖橘黄。文字往这里靠，越深夜靠得越多。</summary>
        private static readonly Color LampColor = new Color32(0xFF, 0xC2, 0x6B, 0xFF);

        /// <summary>最深夜时文字向灯光色偏移的比例（1 = 完全变成灯光色）。</summary>
        private const float LampStrength = .85f;

        /// <summary>线条只提亮一半：全变橘黄会把分隔线抢成主角。</summary>
        private const float LineLampScale = .5f;

        /// <summary>夜间描边色：深夜蓝黑。alpha 按夜色深浅给，白天为 0（等于没描边）。</summary>
        private static readonly Color LampOutline = new Color(.06f, .07f, .16f, .8f);

        /// <summary>重扫的间隔帧数：分页切换、列表重建出来的新节点靠它兜住。</summary>
        private const int RescanInterval = 15;

        private readonly List<Graphic> targets = new List<Graphic>();
        private readonly List<Color> baseColors = new List<Color>();
        /// <summary>文字 → 它「原本」的颜色。只在第一次见到某个 Text 时记，之后不再覆盖，
        /// 否则会把已经被点亮的颜色当成原色，一轮轮越叠越黄。</summary>
        private readonly Dictionary<Text, Color> textBase = new Dictionary<Text, Color>();
        /// <summary>线条/边框类图形 → 原色：夜里跟着一起提亮，否则细线会沉进夜色里看不见。</summary>
        private readonly Dictionary<Graphic, Color> lineBase = new Dictionary<Graphic, Color>();
        /// <summary>要描边的图形（文字、线条、按钮、边框）。描边与调色是两件事，互不牵连。</summary>
        private readonly List<Outline> outlines = new List<Outline>();
        /// <summary>不吃夜间调色的子树（商店卡片、价格面板这类要保持原色的）；描边照旧。</summary>
        private readonly List<Transform> excluded = new List<Transform>();
        private readonly List<Text> staleTexts = new List<Text>();
        private readonly List<Graphic> staleLines = new List<Graphic>();
        private int rescanCountdown;
        private Image veil;
        private Color tint = Color.white;
        private Color veilColor = Color.clear;
        private bool initialized;

        /// <summary>
        /// 给一层界面挂上昼夜调色。<paramref name="explicitTargets"/> 为空时按名字约定自己找底图。
        /// </summary>
        public static HouseDayLightTint Attach(Transform root, params Graphic[] explicitTargets)
        {
            if (root == null) return null;
            var component = root.GetComponent<HouseDayLightTint>();
            if (component == null) component = root.gameObject.AddComponent<HouseDayLightTint>();
            component.Collect(explicitTargets);
            return component;
        }

        /// <summary>页面在开着的时候换了内容（换分页、重建列表）时立刻重扫。</summary>
        public void Rescan() => CollectDynamic();

        /// <summary>
        /// 排除若干子树，让它们不吃夜间调色（2026-08-18 反馈：商店卡片与价格面板保持原色）。
        /// 已经被点亮过的先还原回原色再摘出去；描边不受影响，边框照旧要咬得出来。
        /// </summary>
        public void Exclude(params Transform[] roots)
        {
            if (roots == null) return;
            foreach (var root in roots)
            {
                if (root == null) continue;
                excluded.Add(root);
                foreach (var text in root.GetComponentsInChildren<Text>(true))
                {
                    if (text == null || !textBase.TryGetValue(text, out var basis)) continue;
                    text.color = basis;
                    textBase.Remove(text);
                }
                foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
                {
                    if (graphic == null || !lineBase.TryGetValue(graphic, out var basis)) continue;
                    graphic.color = basis;
                    lineBase.Remove(graphic);
                }
            }
        }

        private bool IsExcluded(Transform node)
        {
            foreach (var root in excluded)
            {
                if (root == null) continue;
                if (node == root || node.IsChildOf(root)) return true;
            }
            return false;
        }

        private void Collect(Graphic[] explicitTargets)
        {
            targets.Clear();
            baseColors.Clear();
            if (explicitTargets != null && explicitTargets.Length > 0)
            {
                foreach (var graphic in explicitTargets)
                    if (graphic != null) targets.Add(graphic);
            }
            if (targets.Count == 0)
            {
                foreach (var graphic in GetComponentsInChildren<Graphic>(true))
                {
                    if (graphic == null || graphic == veil) continue;
                    if (MatchesAny(graphic, BackgroundNames)) targets.Add(graphic);
                }
            }
            foreach (var graphic in targets) baseColors.Add(graphic.color);
            CollectDynamic();
            initialized = false;
        }

        /// <summary>
        /// 收集要点亮的文字/线条与要描边的图形。运行时新建的行、卡片也要吃到，所以定期重扫。
        /// </summary>
        private void CollectDynamic()
        {
            rescanCountdown = RescanInterval;
            foreach (var text in GetComponentsInChildren<Text>(true))
            {
                if (text == null) continue;
                Stroke(text);
                if (textBase.ContainsKey(text) || IsExcluded(text.transform)) continue;
                textBase[text] = text.color;
            }
            foreach (var graphic in GetComponentsInChildren<Graphic>(true))
            {
                if (graphic == null || graphic == veil || graphic is Text) continue;
                if (IsTarget(graphic)) continue; // 底图自己走乘法调色，不描边
                var interactive = graphic.GetComponent<Selectable>() != null ||
                                  graphic.transform.parent != null &&
                                  graphic.transform.parent.GetComponent<Selectable>() != null;
                if (!interactive && !MatchesAny(graphic, LineNames)) continue;
                Stroke(graphic);
                // 线条、边框、按钮底板夜里一起提亮（2026-08-18「这里也要亮起来」：
                // 页签这类底板不提亮的话，入夜后整条都糊在暗底上看不出边界）
                if (lineBase.ContainsKey(graphic) || IsExcluded(graphic.transform)) continue;
                lineBase[graphic] = graphic.color;
            }
            Prune();
        }

        private bool IsTarget(Graphic graphic)
        {
            foreach (var candidate in targets)
                if (candidate == graphic) return true;
            return false;
        }

        private static bool MatchesAny(Graphic graphic, string[] keys)
        {
            var name = graphic.gameObject.name.ToLowerInvariant();
            foreach (var key in keys)
                if (name.Contains(key)) return true;
            return false;
        }

        private void Prune()
        {
            staleTexts.Clear();
            foreach (var pair in textBase)
                if (pair.Key == null) staleTexts.Add(pair.Key);
            foreach (var dead in staleTexts) textBase.Remove(dead);
            staleLines.Clear();
            foreach (var pair in lineBase)
                if (pair.Key == null) staleLines.Add(pair.Key);
            foreach (var dead in staleLines) lineBase.Remove(dead);
            outlines.RemoveAll(outline => outline == null);
        }

        /// <summary>
        /// 描边（2026-08-18 反馈）：夜里底色压暗后细线、边框、按钮都发糊，补一圈深色把边界咬出来。
        /// 白天描边全透明，等于不存在。
        /// </summary>
        private void Stroke(Graphic graphic)
        {
            var outline = graphic.GetComponent<Outline>();
            if (outline == null)
            {
                outline = graphic.gameObject.AddComponent<Outline>();
                outline.effectDistance = new Vector2(1.4f, -1.4f);
                outline.useGraphicAlpha = true;
            }
            if (!outlines.Contains(outline)) outlines.Add(outline);
        }

        private void LateUpdate()
        {
            if (GameManager.Instance == null) return;
            var (wantTint, wantVeil) = HouseDayLight.Now();
            if (!initialized)
            {
                // 开页当帧就是正确的天色，不要从白色渐变过去（那会闪一下）
                tint = wantTint;
                veilColor = wantVeil;
                initialized = true;
            }
            else
            {
                var step = Mathf.Clamp01(Time.unscaledDeltaTime * FollowSpeed);
                tint = Color.Lerp(tint, wantTint, step);
                veilColor = Color.Lerp(veilColor, wantVeil, step);
            }

            for (var i = 0; i < targets.Count; i++)
            {
                var graphic = targets[i];
                if (graphic == null) continue;
                var basis = baseColors[i];
                // 乘法：白 = 正午原样；alpha 保持页面自己的（淡入淡出还归 CanvasGroup 管）
                graphic.color = new Color(basis.r * tint.r, basis.g * tint.g, basis.b * tint.b, basis.a);
            }
            ApplyVeil();
            ApplyLamp();
        }

        /// <summary>夜色深浅：正午 0、最深夜 1（用色带自身的亮度推，不另立一套时刻表）。</summary>
        private float NightAmount()
        {
            var luma = tint.r * .299f + tint.g * .587f + tint.b * .114f;
            return Mathf.Clamp01(Mathf.InverseLerp(.96f, .5f, luma));
        }

        /// <summary>入夜后文字与线条转暖橘黄、所有描边浮现——观感上像屋里点了灯。</summary>
        private void ApplyLamp()
        {
            if (--rescanCountdown <= 0) CollectDynamic();
            var raw = NightAmount();
            var night = raw * LampStrength;
            var strokeColor = new Color(LampOutline.r, LampOutline.g, LampOutline.b, LampOutline.a * raw);
            foreach (var pair in textBase)
            {
                var text = pair.Key;
                if (text == null) continue;
                var basis = pair.Value;
                var lit = Color.Lerp(basis, LampColor, night);
                text.color = new Color(lit.r, lit.g, lit.b, basis.a);
            }
            foreach (var pair in lineBase)
            {
                var line = pair.Key;
                if (line == null) continue;
                var basis = pair.Value;
                var lit = Color.Lerp(basis, LampColor, night * LineLampScale);
                line.color = new Color(lit.r, lit.g, lit.b, basis.a);
            }
            foreach (var outline in outlines)
                if (outline != null) outline.effectColor = strokeColor;
        }

        /// <summary>
        /// 深夜罩色：需要时才建一层透明图（白天恒透明，不建）。
        /// **压在底图之上、正文之下**——盖到文字上会把字一起压暗，与「点了灯」正好相反。
        /// </summary>
        private void ApplyVeil()
        {
            if (veil == null)
            {
                if (veilColor.a <= .002f) return;
                var go = new GameObject("DayLightVeil", typeof(RectTransform)) { layer = gameObject.layer };
                var rect = (RectTransform)go.transform;
                rect.SetParent(transform, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                veil = go.AddComponent<Image>();
                veil.raycastTarget = false;
            }
            veil.transform.SetSiblingIndex(VeilSiblingIndex());
            veil.color = veilColor;
            veil.enabled = veilColor.a > .002f;
        }

        /// <summary>罩色该插在哪一层：最底那张底图的正上方；找不到底图就压最底。</summary>
        private int VeilSiblingIndex()
        {
            var index = -1;
            foreach (var graphic in targets)
            {
                if (graphic == null || graphic.transform.parent != transform) continue;
                index = Mathf.Max(index, graphic.transform.GetSiblingIndex());
            }
            return Mathf.Clamp(index + 1, 0, transform.childCount - 1);
        }
    }
}

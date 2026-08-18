using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 界面昼夜调色（2026-08-18 反馈：弹窗/整屏页的背景也要随时间慢慢变）：
    /// 把 <see cref="HouseDayLight"/> 的色带套到页面底图上——底图走乘法调色（保对比度），
    /// 深夜再叠一层极淡的罩色把不吃调色的元素一起压进夜色。做法与 Hub 场景、标题封面一致，
    /// 所以从场景切进商店/设置，灯光是接得上的。
    ///
    /// 入夜后文字转成暖橘黄并压在罩色之上（2026-08-18 反馈「像灯一样在照明」）：
    /// 罩色只盖到底图那一层，正文与按钮不被压暗，夜里反而更亮——观感上就是屋里点了灯。
    ///
    /// 非布局件，运行时挂（口径同打字机/键位组件，§16.2 例外）：不写 Prefab、不改任何位置尺寸，
    /// 只在自己这一层改颜色。设置里关掉「昼夜交替」时色带恒为正午基准，等于自动失效。
    /// </summary>
    public sealed class HouseDayLightTint : MonoBehaviour
    {
        /// <summary>约定：名字里带这些词的图形算「页面底图」，吃乘法调色。</summary>
        private static readonly string[] BackgroundNames = { "background", "backdrop", "cover", "scrim" };

        /// <summary>色带跳变时的追随速度（时钟按 tick 走，插值一下才是「慢慢变」）。</summary>
        private const float FollowSpeed = 2.2f;

        /// <summary>夜间灯光色：暖橘黄。文字往这里靠，越深夜靠得越多。</summary>
        private static readonly Color LampColor = new Color32(0xFF, 0xC2, 0x6B, 0xFF);

        /// <summary>最深夜时文字向灯光色偏移的比例（1 = 完全变成灯光色）。</summary>
        private const float LampStrength = .85f;

        /// <summary>重扫文字的间隔帧数：分页切换、列表重建出来的新文字靠它兜住。</summary>
        private const int RescanInterval = 15;

        private readonly List<Graphic> targets = new List<Graphic>();
        private readonly List<Color> baseColors = new List<Color>();
        /// <summary>文字 → 它「原本」的颜色。只在第一次见到某个 Text 时记，之后不再覆盖，
        /// 否则会把已经被点亮的颜色当成原色，一轮轮越叠越黄。</summary>
        private readonly Dictionary<Text, Color> textBase = new Dictionary<Text, Color>();
        private readonly List<Text> stale = new List<Text>();
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

        /// <summary>页面在开着的时候换了内容（换分页、重建列表）时立刻重新收集文字。</summary>
        public void Rescan() => CollectTexts();

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
                    var name = graphic.gameObject.name.ToLowerInvariant();
                    foreach (var key in BackgroundNames)
                        if (name.Contains(key)) { targets.Add(graphic); break; }
                }
            }
            foreach (var graphic in targets) baseColors.Add(graphic.color);
            CollectTexts();
            initialized = false;
        }

        /// <summary>收集页面里的文字。运行时新建的行/卡片也要吃到灯光，所以定期重扫。</summary>
        private void CollectTexts()
        {
            rescanCountdown = RescanInterval;
            foreach (var text in GetComponentsInChildren<Text>(true))
                if (text != null && !textBase.ContainsKey(text)) textBase[text] = text.color;
            stale.Clear();
            foreach (var pair in textBase)
                if (pair.Key == null) stale.Add(pair.Key);
            foreach (var dead in stale) textBase.Remove(dead);
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

        /// <summary>入夜后正文转暖橘黄——观感上像屋里点了灯（2026-08-18 反馈）。</summary>
        private void ApplyLamp()
        {
            if (--rescanCountdown <= 0) CollectTexts();
            var night = NightAmount() * LampStrength;
            foreach (var pair in textBase)
            {
                var text = pair.Key;
                if (text == null) continue;
                var basis = pair.Value;
                var lit = Color.Lerp(basis, LampColor, night);
                text.color = new Color(lit.r, lit.g, lit.b, basis.a);
            }
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

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
    /// 非布局件，运行时挂（口径同打字机/键位组件，§16.2 例外）：不写 Prefab、不改任何位置尺寸，
    /// 只在自己这一层改颜色。设置里关掉「昼夜交替」时色带恒为正午基准，等于自动失效。
    /// </summary>
    public sealed class HouseDayLightTint : MonoBehaviour
    {
        /// <summary>约定：名字里带这些词的图形算「页面底图」，吃乘法调色。</summary>
        private static readonly string[] BackgroundNames = { "background", "backdrop", "cover", "scrim" };

        /// <summary>色带跳变时的追随速度（时钟按 tick 走，插值一下才是「慢慢变」）。</summary>
        private const float FollowSpeed = 2.2f;

        private readonly List<Graphic> targets = new List<Graphic>();
        private readonly List<Color> baseColors = new List<Color>();
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
            initialized = false;
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
        }

        /// <summary>深夜罩色：需要时才建一层压在最上面的透明图（白天恒透明，不建）。</summary>
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
            veil.transform.SetAsLastSibling(); // 页面自己可能又插了节点，罩色始终压最上
            veil.color = veilColor;
            veil.enabled = veilColor.a > .002f;
        }
    }
}

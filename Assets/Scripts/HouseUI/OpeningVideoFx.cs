using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace MasterHouse
{
    /// <summary>
    /// 新游戏开场播片（2026-08-22 一轮测试改进 #1）：New Game 点击后、开场推镜之前播一段视频。
    /// 纯表现件，运行时生成；**点击任意处/任意键立即跳过**（定案）；播完或跳过 → onDone（只调一次）。
    ///
    /// 素材：Resources/OutGameUI/OpeningVideo（VideoClip，Windows 下 H.264 mp4 兼容性最稳）。
    /// **素材未到位时直接跳过、不拦流程**——美术交片后把视频放到该路径即自动生效，不用改代码。
    /// 音频经 VideoPlayer 直出；对接设置项音量体系推迟（改进说明 §1）。
    /// 播片期间 BGM 全哑（2026-08-22 反馈）：SetDuck(0) 起播压下、结束/销毁两条路都恢复（幂等）。
    /// </summary>
    public sealed class OpeningVideoFx : MonoBehaviour
    {
        private const string ClipPath = "OutGameUI/OpeningVideo";

        private VideoPlayer player;
        private RawImage screen;
        private RenderTexture surface;
        private Action onDone;
        private bool finished;

        public static void Play(HouseUIManager ui, Action onDone)
        {
            var clip = Resources.Load<VideoClip>(ClipPath);
            if (clip == null)
            {
                // 播片素材还没交（#1 允许先落框架）：静默走下一步，只留一条 Log 指路
                Debug.Log($"[HouseUI] 开场播片素材缺失（Resources/{ClipPath}），本次跳过播片；" +
                          "美术交片后放到该路径即自动生效");
                onDone?.Invoke();
                return;
            }

            // 播片一开层就把 BGM 压到全哑（不是停播：与设置音量相乘，恢复时不吃玩家设置），
            // 视频自己的音轨独占听觉；跳过/播完/层被销毁三条路都会恢复
            BgmManager.SetDuck(0f);

            var layer = new GameObject("OpeningVideoLayer", typeof(RectTransform), typeof(Image));
            layer.layer = 5;
            var rect = (RectTransform)layer.transform;
            rect.SetParent(ui.PageRoot, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.SetAsLastSibling();
            var backdrop = layer.GetComponent<Image>();
            backdrop.color = Color.black;  // 比例不满屏时露黑边，而不是露标题页
            backdrop.raycastTarget = true; // 播片期间挡住底下的一切点击

            var screenGo = new GameObject("Screen", typeof(RectTransform), typeof(RawImage));
            screenGo.layer = 5;
            var screenRect = (RectTransform)screenGo.transform;
            screenRect.SetParent(rect, false);
            screenRect.anchorMin = Vector2.zero;
            screenRect.anchorMax = Vector2.one;
            screenRect.offsetMin = screenRect.offsetMax = Vector2.zero;
            var fitter = screenGo.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent; // 等比适配，不拉变形
            fitter.aspectRatio = clip.width / (float)Math.Max(1, (int)clip.height);

            var fx = layer.AddComponent<OpeningVideoFx>();
            fx.onDone = onDone;
            fx.screen = screenGo.GetComponent<RawImage>();
            fx.screen.raycastTarget = false;
            fx.screen.color = Color.clear; // 首帧就绪前保持全黑，防白闪

            fx.surface = new RenderTexture((int)clip.width, (int)clip.height, 0);
            var video = layer.AddComponent<VideoPlayer>();
            fx.player = video;
            video.playOnAwake = false;
            video.clip = clip;
            video.renderMode = VideoRenderMode.RenderTexture;
            video.targetTexture = fx.surface;
            video.isLooping = false;
            video.audioOutputMode = VideoAudioOutputMode.Direct;
            video.loopPointReached += _ => fx.Finish();
            video.prepareCompleted += _ =>
            {
                if (fx == null || fx.finished) return;
                fx.screen.texture = fx.surface;
                fx.screen.color = Color.white;
                video.Play();
            };
            video.errorReceived += (_, message) =>
            {
                Debug.LogError("[HouseUI] 开场播片解码失败，跳过播片：" + message);
                if (fx != null) fx.Finish();
            };
            video.Prepare();
        }

        private void Update()
        {
            if (finished) return;
            // 点击任意处/任意键立即跳过（#1 定案）。Prepare 阶段也允许跳——解码卡住时玩家不至于被锁在黑屏上
            if (Input.GetMouseButtonDown(0) || Input.anyKeyDown) Finish();
        }

        private void Finish()
        {
            if (finished) return;
            finished = true;
            var done = onDone;
            onDone = null;
            if (player != null) player.Stop();
            BgmManager.SetDuck(1f); // 在 done() 之前恢复：下一段开场推镜就该有 BGM 了
            Destroy(gameObject);
            done?.Invoke();
        }

        private void OnDestroy()
        {
            // 层被外力销毁（未走 Finish）也得把 BGM 还回去；SetDuck 幂等，Finish 那条路重复调无害
            BgmManager.SetDuck(1f);
            if (surface != null)
            {
                surface.Release();
                Destroy(surface);
            }
        }
    }
}

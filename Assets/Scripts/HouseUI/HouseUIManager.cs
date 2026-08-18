using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 局外界面壳（§16.3 HouseUI 模块）：Canvas 生命周期、整页路由、叠加层栈与 ESC 退栈
    /// （先弹栈、再问当前页）、Toast。页面绑定逻辑一页一文件（HousePage 派生类）；
    /// 本壳不做任何布局搭建（§16.2）。由 OutGameBootstrap 拉起（3.9 起为局外界面唯一实现）。
    /// </summary>
    public sealed class HouseUIManager : MonoBehaviour
    {
        public static HouseUIManager Instance { get; private set; }

        /// <summary>页面与叠加层的父节点（Canvas 根）。</summary>
        public RectTransform PageRoot => (RectTransform)transform;

        /// <summary>壳的 Canvas（家具模式打开期间整体禁用渲染）。</summary>
        public Canvas Canvas { get; private set; }

        private HousePage currentPage;
        private readonly List<IHouseOverlay> overlayStack = new List<IHouseOverlay>();

        private RectTransform toastRoot;
        private Tween toastTween;

        public static HouseUIManager Build()
        {
            var existing = FindObjectOfType<HouseUIManager>();
            if (existing != null) return existing;

            var go = new GameObject("HouseUI", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(HouseUIManager));
            DontDestroyOnLoad(go);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            // Expand（2026-08-18 跨平台修复）：画布尺寸永远 >= 1920×1080，宽高各自「只放大不缩小」。
            // 原来的 MatchWidthOrHeight .5 会在非 16:9 屏（Mac 常见的 16:10）上把画布缩成
            // 1822×1139 这类中间尺寸，于是所有按 1920×1080 写死的坐标横竖都对不上位。
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            Debug.Log("[HouseUI] 局外界面壳已创建。", go);
            return go.GetComponent<HouseUIManager>();
        }

        private void Awake()
        {
            Instance = this;
            Canvas = GetComponent<Canvas>();
            HouseSettings.Apply(); // 启动即作用设置（主音量/窗口模式；2026-08-16 设置页重做）
            BgmManager.Ensure();   // BGM 常驻循环（2026-08-17：全程不停，音量随设置）
        }

        private void Start()
        {
            if (EventSystem.current == null)
            {
                var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                DontDestroyOnLoad(eventSystem);
            }
            ShowPage(new TitlePage());
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>整页替换：清空叠加层 → 旧页退场销毁 → 新页进场。</summary>
        public void ShowPage(HousePage page)
        {
            // 音效需求 #5：页与页之间的切换响转场音；启动进标题页（无旧页）不响
            if (currentPage != null) SfxManager.Play(ESfx.PageTransition);
            while (overlayStack.Count > 0) PopOverlay();
            currentPage?.Hide();
            currentPage = page;
            page.Show(this);
        }

        public void PushOverlay(IHouseOverlay overlay) => overlayStack.Add(overlay);

        /// <summary>某叠加层当前是否处于栈顶（设置层在确认弹窗压顶时挂起自身热键用）。</summary>
        public bool IsTopOverlay(IHouseOverlay overlay) =>
            overlayStack.Count > 0 && overlayStack[overlayStack.Count - 1] == overlay;

        public void PopOverlay()
        {
            if (overlayStack.Count == 0) return;
            var top = overlayStack[overlayStack.Count - 1];
            overlayStack.RemoveAt(overlayStack.Count - 1);
            top.Close();
        }

        private void Update()
        {
            if (currentPage == null) return;

            currentPage.OnUpdate();

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (overlayStack.Count > 0)
                {
                    // 顶层可先消费 ESC（如商店先关获得弹窗，再按一次才退店）——递归返回语义
                    if (overlayStack[overlayStack.Count - 1].ConsumeEscape()) return;
                    PopOverlay();
                    return;
                }
                if (currentPage.OnEscape()) return;
            }

            // 叠加层压住页面输入（面板/对话打开时页面快捷键失效，与旧壳一致）
            if (overlayStack.Count == 0) currentPage.HandleInput();
        }

        /// <summary>顶部 Toast（transient 反馈件，非布局内容，允许运行时构建）。</summary>
        public void ShowToast(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (toastRoot != null) Destroy(toastRoot.gameObject);

            var go = new GameObject("Toast", typeof(RectTransform), typeof(Image));
            go.layer = 5;
            toastRoot = (RectTransform)go.transform;
            toastRoot.SetParent(PageRoot, false);
            toastRoot.SetAsLastSibling();
            toastRoot.anchorMin = toastRoot.anchorMax = new Vector2(.5f, 1);
            toastRoot.anchoredPosition = new Vector2(0, -168);
            toastRoot.sizeDelta = new Vector2(470, 58);
            // 美术皮肤（PC ui/common/Toast）：缺失时回退旧底色
            var toastImage = go.GetComponent<Image>();
            var toastSkin = Resources.Load<Sprite>("OutGameUI/common/Toast");
            if (toastSkin != null)
            {
                toastImage.sprite = toastSkin;
                toastImage.color = Color.white;
                toastRoot.sizeDelta = new Vector2(640, 52); // 贴合素材长条比例
            }
            else
            {
                toastImage.color = new Color(.12f, .035f, .1f, .94f);
            }

            var labelGo = new GameObject("ToastText", typeof(RectTransform), typeof(Text));
            labelGo.layer = 5;
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.SetParent(toastRoot, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var label = labelGo.GetComponent<Text>();
            label.font = HouseUIUtil.Font;
            label.fontSize = 16;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = HouseUIUtil.White;
            label.text = "●  " + message;

            var group = HouseUIUtil.Group(go, 0);
            toastTween?.Kill();
            toastTween = DOTween.Sequence().SetTarget(group).SetUpdate(true)
                .Append(group.DOFade(1, .18f))
                .Join(toastRoot.DOAnchorPosY(-132, .28f).SetEase(Ease.OutCubic))
                .AppendInterval(3f)
                .Append(group.DOFade(0, .25f))
                .OnComplete(() =>
                {
                    if (toastRoot != null) Destroy(toastRoot.gameObject);
                    toastRoot = null;
                });
        }
    }
}

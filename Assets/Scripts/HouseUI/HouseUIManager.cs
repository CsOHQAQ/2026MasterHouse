using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 局外界面壳（§16.3 HouseUI 模块，取代旧 OutGameUI 的界面框架职责）：
    /// Canvas 生命周期、整页路由、叠加层栈与 ESC 退栈（先弹栈、再问当前页）、Toast。
    /// 页面绑定逻辑一页一文件（HousePage 派生类）；本壳不做任何布局搭建（§16.2）。
    /// 3.5 期间与旧 OutGameUI 并行共存，由 OutGameBootstrap 的开关二选一拉起。
    /// </summary>
    public sealed class HouseUIManager : MonoBehaviour
    {
        public static HouseUIManager Instance { get; private set; }

        /// <summary>页面与叠加层的父节点（Canvas 根）。</summary>
        public RectTransform PageRoot => (RectTransform)transform;

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
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;
            Debug.Log("[HouseUI] 局外界面壳已创建。", go);
            return go.GetComponent<HouseUIManager>();
        }

        private void Awake()
        {
            Instance = this;
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
            while (overlayStack.Count > 0) PopOverlay();
            currentPage?.Hide();
            currentPage = page;
            page.Show(this);
        }

        public void PushOverlay(IHouseOverlay overlay) => overlayStack.Add(overlay);

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

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (overlayStack.Count > 0)
                {
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
            go.GetComponent<Image>().color = new Color(.12f, .035f, .1f, .94f);

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

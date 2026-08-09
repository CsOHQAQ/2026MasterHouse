using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// House Hub 主页面：只做组装与状态协调（房间切换/观景模式/家具模式/访客选择/时钟闸门），
    /// 各区块绑定逻辑在六个 Binder 文件里（§16.3 每页绑定独立成文件）。
    /// 面板栈与对话层归 3.5c，本页对应入口暂以 Toast 占位。
    /// </summary>
    public sealed class HubPage : HousePage
    {
        protected override string PrefabPath => OutGamePrefabResourcePaths.Hub;

        private readonly string notice;

        private OutGameHubView view;
        private readonly HubTopBarBinder topBar = new HubTopBarBinder();
        private readonly HubTaskCardBinder taskCard = new HubTaskCardBinder();
        private readonly HubGuestRailBinder guestRail = new HubGuestRailBinder();
        private readonly HubRightDockBinder rightDock = new HubRightDockBinder();
        private readonly HubRoomNavBinder roomNav = new HubRoomNavBinder();
        private readonly HubSceneBinder scene = new HubSceneBinder();

        private Text immersiveLabel;
        private bool immersive;
        private bool roomTransitioning;
        private bool furnitureModeOpen;

        /// <summary>当前房间下标（列表顺序 = 导航顺序）。</summary>
        public int RoomIndex { get; private set; }

        /// <summary>当前选中的访客下标（任务卡与对话层共用）。</summary>
        public int GuestIndex { get; private set; }

        /// <summary>对话快捷栏/档案「放入房间」共用的会话级摆放选择（纯表现，与真实家具布局无数据联系）。</summary>
        internal static string PlacedFurnitureId = "whale";

        public HubPage(string notice = "欢迎回家。本周有 4 位访客。")
        {
            this.notice = notice;
        }

        protected override void OnEnter()
        {
            view = Root != null ? Root.GetComponent<OutGameHubView>() : null;
            if (view == null || view.sceneRoot == null || view.chromeRoot == null ||
                view.topBar == null || view.taskCard == null || view.guestRail == null ||
                view.rightDock == null || view.roomNavigation == null || view.sceneOverlay == null)
            {
                Debug.LogError("[HouseUI] Hub Prefab 缺失或槽位不完整，页面无法呈现（§16.2 不回退代码布局）：" +
                               OutGamePrefabResourcePaths.Hub);
                return;
            }

            scene.Build(view, this);
            topBar.Bind(view.topBar, this, view.chromeRoot);
            taskCard.Bind(view.taskCard, this);
            guestRail.Bind(view.guestRail, this);
            rightDock.Bind(view.rightDock, this, view.chromeRoot);
            roomNav.Bind(view.roomNavigation, this);
            BuildImmersiveToggle(view.chromeRoot);
            if (view.footer != null)
                view.footer.text = "NEW LIFE, NEW HOME · UI/UX CONCEPT                                      ESC 返回 · ← → 切换房间 · I 仓库";
            HouseUIUtil.ApplyFallbackFont(Root);
            AnimateHubIn();

            // 时间只在 Hub 内流动（§16.4 闸门）：进场开、退场关；家具模式不退页，时钟照走
            GameManager.Instance.HouseClockManager.SetRunning(true);
            HouseGmConsole.FullResetRequested += OnGmFullReset;
            UI.ShowToast(notice);
        }

        protected override void OnExit()
        {
            HouseGmConsole.FullResetRequested -= OnGmFullReset;
            GameManager.Instance.HouseClockManager.SetRunning(false);
            topBar.Dispose();
        }

        /// <summary>GM「恢复初始态」：面板本体已重置经济与家具会话，这里补访客/时钟归零与表现重建。</summary>
        private void OnGmFullReset()
        {
            var gm = GameManager.Instance;
            gm.VisitorManager.ResetNew();
            gm.HouseClockManager.ResetNew();
            GuestIndex = 0;
            if (view == null) return;
            scene.RefreshAfterFurniture();
            scene.RebuildStage();
            guestRail.Refresh();
            taskCard.Refresh();
            Toast("GM · 已恢复所有状态到初始态");
        }

        public override bool OnEscape()
        {
            if (furnitureModeOpen) return true; // 家具模式接管输入，壳不动作
            if (immersive)
            {
                SetImmersive(false);
                return true;
            }
            BackToTitle(); // 旧壳此处先写档；存档功能移除（§16.5 豁免）
            return true;
        }

        public override void HandleInput()
        {
            if (view == null || furnitureModeOpen) return;
            if (immersive)
            {
                scene.HandleBrowse();
                return;
            }
            if (Input.GetKeyDown(KeyCode.LeftArrow)) SelectRoom((RoomIndex + 3) % 4);
            if (Input.GetKeyDown(KeyCode.RightArrow)) SelectRoom((RoomIndex + 1) % 4);
            if (Input.GetKeyDown(KeyCode.I)) OpenPanel(EHousePanel.Inventory);
        }

        public override void OnUpdate()
        {
            if (view == null || furnitureModeOpen) return;
            topBar.Tick();
        }

        // ── 供各 Binder 与场景层回调的页面动作 ──

        public void Toast(string message) => UI.ShowToast(message);

        public void BackToTitle() => UI.ShowPage(new TitlePage());

        /// <summary>打开已迁移的系统面板（叠加层压栈；ESC/遮罩/返回弹栈）。</summary>
        public void OpenPanel(EHousePanel panel)
        {
            if (furnitureModeOpen) return;
            if (immersive) SetImmersive(false);
            PanelHost.Open(UI, this, panel);
        }

        /// <summary>Hub 内设置：复用标题设置 Prefab 的叠加层（§16.8）。</summary>
        public void OpenSettings()
        {
            if (furnitureModeOpen) return;
            if (immersive) SetImmersive(false);
            SettingsOverlay.Open(UI);
        }

        public void SelectRoom(int index)
        {
            if (view == null) return;
            if (index == RoomIndex || roomTransitioning)
            {
                var rooms = GameManager.Instance.CodexTable.rooms;
                if (index == RoomIndex) Toast($"当前位于{rooms[index].displayName} · {rooms[index].note}");
                return;
            }
            // 进出卧室走开关门过渡，其余房间直接交叉淡入（与旧壳一致）
            var usesDoor = index == 1 || RoomIndex == 1;
            if (!usesDoor)
            {
                SwapRoom(index);
                Toast(index == 2 ? "镜头聚焦至厨房料理台" : index == 3 ? "视角旋转 90° · 已进入书房" : "已回到起居室");
                return;
            }

            roomTransitioning = true;
            var transition = HouseUIRuntime.Stretch(Root, "RoomDoorTransition");
            transition.SetAsLastSibling();
            var left = HouseUIRuntime.Panel(transition, "LeftDoor", new Vector2(0, .5f),
                new Vector2(-480, 0), new Vector2(960, 1080), HouseUIUtil.Hex("251820"));
            var right = HouseUIRuntime.Panel(transition, "RightDoor", new Vector2(1, .5f),
                new Vector2(480, 0), new Vector2(960, 1080), HouseUIUtil.Hex("251820"));
            DOTween.Sequence().SetTarget(transition).SetUpdate(true)
                .Append(left.rectTransform.DOAnchorPosX(480, .42f).SetEase(Ease.InCubic))
                .Join(right.rectTransform.DOAnchorPosX(-480, .42f).SetEase(Ease.InCubic))
                .AppendCallback(() => SwapRoom(index))
                .Append(left.rectTransform.DOAnchorPosX(-480, .72f).SetEase(Ease.OutCubic))
                .Join(right.rectTransform.DOAnchorPosX(480, .72f).SetEase(Ease.OutCubic))
                .OnComplete(() =>
                {
                    roomTransitioning = false;
                    if (transition != null) Object.Destroy(transition.gameObject);
                });
        }

        public void SelectGuest(int index)
        {
            var visitor = GameManager.Instance.VisitorManager;
            var visitors = GameManager.Instance.VisitorTable.visitors;
            if (visitor.Data.States[index].Served)
            {
                Toast(visitors[index].displayName + " 已完成接待并离开旅店");
                return;
            }
            // 服务时间窗口由 VisitorManager 整数分钟判定（§16.4）；窗口外访客留在屋内，暂不开放服务
            if (!visitor.CanServe(index))
            {
                var guest = visitors[index];
                var clock = GameManager.Instance.HouseClockManager.Data;
                Toast($"{guest.displayName} 的可服务时间是 {guest.ServiceWindowText} · 现在 {clock.TimeText}，TA 先在屋里歇着");
                return;
            }
            GuestIndex = index;
            taskCard.Refresh();
            DialogueOverlay.Open(UI, this);
        }

        /// <summary>对话层内切换本周访客：关当前层重开（选中语义与旧壳一致）。</summary>
        public void SwitchDialogueGuest(int index)
        {
            GuestIndex = index;
            taskCard.Refresh();
            UI.PopOverlay();
            DialogueOverlay.Open(UI, this);
        }

        /// <summary>完成服务：业务结算归 VisitorManager，这里只做表现刷新与提示（存档移除，§16.5）。</summary>
        public void ServeSelectedGuest()
        {
            var gm = GameManager.Instance;
            if (!gm.VisitorManager.Serve(GuestIndex)) return;
            var name = gm.VisitorTable.visitors[GuestIndex].displayName;
            scene.NotifyServed(GuestIndex);
            UI.PopOverlay();
            guestRail.Refresh();
            taskCard.Refresh();
            Toast($"{name} 的服务已完成 · ◈ +{gm.EconomyManager.ServiceCurrencyReward} · 声望 +{gm.EconomyManager.ServiceReputationReward}");
        }

        /// <summary>拒绝接待：业务结算归 VisitorManager。</summary>
        public void RefuseSelectedGuest()
        {
            var gm = GameManager.Instance;
            if (!gm.VisitorManager.Refuse(GuestIndex)) return;
            var name = gm.VisitorTable.visitors[GuestIndex].displayName;
            scene.NotifyRefused(GuestIndex);
            UI.PopOverlay();
            guestRail.Refresh();
            taskCard.Refresh();
            Toast($"已婉拒 {name} 的委托 · 声望 -{gm.EconomyManager.RefuseReputationPenalty}");
        }

        /// <summary>周结算：业务（扣声望/清状态/时钟跳次日）整体归 VisitorManager；表现整体刷新。</summary>
        public void EndWeek()
        {
            var gm = GameManager.Instance;
            var missed = gm.VisitorManager.EndWeek();
            UI.PopOverlay();
            guestRail.Refresh();
            taskCard.Refresh();
            scene.RebuildStage(); // 新的一周 → 访客整体刷新，重新从大门进场
            Toast(missed > 0
                ? $"本周结束 · {missed} 项服务未完成，声望 -{missed * gm.EconomyManager.FailReputationPenalty}"
                : "本周结束 · 所有访客服务全部完成！新的一周开始了");
        }

        /// <summary>点击场景中的访客 NPC（观景模式下先展开界面）。</summary>
        public void OnVisitorClicked(int index)
        {
            if (furnitureModeOpen || roomTransitioning) return;
            if (immersive) SetImmersive(false);
            SelectGuest(index);
        }

        /// <summary>家具模式：世界空间独立舞台，打开期间禁用整个壳 Canvas，退出回调恢复并重烘焙背景。</summary>
        public void OpenFurnitureMode()
        {
            if (furnitureModeOpen) return;
            furnitureModeOpen = true;
            UI.Canvas.enabled = false;
            var opened = FurnitureRoomController.Open(() =>
            {
                furnitureModeOpen = false;
                UI.Canvas.enabled = true;
                // 旧壳此处落档；存档功能移除（§16.5），仅重烘焙背景与热点
                scene.RefreshAfterFurniture();
            });
            if (!opened)
            {
                furnitureModeOpen = false;
                UI.Canvas.enabled = true;
                Toast("家具配置表缺失：请先执行菜单 MasterHouse → 家具系统 → 创建配置表");
            }
        }

        private void SwapRoom(int index)
        {
            RoomIndex = index;
            scene.SwapRoom();
            roomNav.Refresh();
        }

        /// <summary>收起/展开四周 UI。收起后进入观景模式：拖拽平移背景、滚轮缩放。</summary>
        private void SetImmersive(bool on)
        {
            immersive = on;
            foreach (Transform child in view.chromeRoot)
            {
                if (child == view.sceneRoot || child.name == "ImmersiveToggle") continue;
                var group = HouseUIUtil.Group(child.gameObject);
                group.DOKill();
                group.DOFade(on ? 0f : 1f, .25f).SetUpdate(true);
                group.blocksRaycasts = !on;
                group.interactable = !on;
            }
            scene.SetImmersiveVisual(on);
            if (immersiveLabel != null)
                immersiveLabel.text = on ? "展开界面\n<size=12>ESC</size>" : "收起界面";
        }

        /// <summary>「收起界面」开关按钮：模板 Prefab 实例化，缺失报错（§16.2）。</summary>
        private void BuildImmersiveToggle(Transform root)
        {
            var prefab = Resources.Load<GameObject>(OutGamePrefabResourcePaths.HubImmersiveToggle);
            if (prefab == null)
            {
                Debug.LogError("[HouseUI] Prefab 缺失，收起界面按钮无法呈现（§16.2）：" + OutGamePrefabResourcePaths.HubImmersiveToggle);
                return;
            }
            var instance = Object.Instantiate(prefab, root, false);
            instance.name = "ImmersiveToggle";
            if (instance.transform is RectTransform rect)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(1, 0);
                rect.anchoredPosition = new Vector2(-110, 56);
                rect.localScale = Vector3.one;
            }
            var toggleView = instance.GetComponent<OutGameHubImmersiveToggleView>();
            if (toggleView == null || toggleView.button == null)
            {
                Debug.LogError("[HouseUI] 收起界面按钮 Prefab 缺少视图组件：OutGameHubImmersiveToggleView");
                Object.Destroy(instance);
                return;
            }
            HouseUIUtil.BindButton(toggleView.button, () => SetImmersive(!immersive));
            immersiveLabel = toggleView.label;
            HouseUIUtil.ApplyFallbackFont(instance.transform);
        }

        /// <summary>Hub 各区块错峰浮入（Tween 目标用 CanvasGroup/容器 rect，避开按钮 hover 的按目标清杀）。</summary>
        private void AnimateHubIn()
        {
            foreach (Transform child in view.chromeRoot)
            {
                if (child == view.sceneRoot || child.name == "Scene") continue;
                var rt = child as RectTransform;
                if (rt == null) continue;
                var group = HouseUIUtil.Group(child.gameObject, 0);
                var target = rt.anchoredPosition;
                var delay = Random.Range(.03f, .22f);
                rt.anchoredPosition = target + new Vector2(0, child.name == "RoomNav" ? -35 : 22);
                group.DOFade(1, .3f).SetUpdate(true).SetDelay(delay);
                rt.DOAnchorPos(target, .42f).SetEase(Ease.OutCubic).SetUpdate(true).SetDelay(delay);
            }
        }
    }
}

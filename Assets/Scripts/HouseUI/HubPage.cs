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

        /// <summary>当前选中的访客实例 id（任务卡与对话层共用；-1 = 未选中）。</summary>
        public int SelectedInstanceId { get; private set; } = -1;

        /// <summary>对话快捷栏/档案「放入房间」共用的会话级摆放选择（纯表现，与真实家具布局无数据联系）。</summary>
        internal static string PlacedFurnitureId = "whale";

        public HubPage(string notice = "欢迎回家。")
        {
            this.notice = notice;
        }

        /// <summary>当前选中的访客实例；未选中或已离场时回落到首位在场实例（可为 null）。</summary>
        public VisitorInstance SelectedInstance
        {
            get
            {
                var visitor = GameManager.Instance.VisitorManager;
                var selected = visitor.Find(SelectedInstanceId);
                if (selected != null) return selected;
                return visitor.Data.Instances.Count > 0 ? visitor.Data.Instances[0] : null;
            }
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

            // 时间只在 Hub 内流动（§16.4 闸门）：进场开、退场关；家具模式不退页，时钟照走。
            // 闸门是原因集合（对话设计说明 §8），本页只负责自己那条原因，不碰模态对话框那条
            GameManager.Instance.HouseClockManager.SetStopReason(EClockStopReason.OffHubPage, false);
            HouseGmConsole.FullResetRequested += OnGmFullReset;
            // 访客实例动态增删（§9）：进离场/状态变化时刷新访客卡与任务卡
            var visitor = GameManager.Instance.VisitorManager;
            visitor.InstanceSpawned += OnVisitorListChanged;
            visitor.InstanceChanged += OnVisitorListChanged;
            visitor.InstanceDeparted += OnVisitorListChanged;
            // 对话框的开合由业务驱动而非玩家点击驱动：接待成功会自动接上【开始等待服务】，
            // 超时/拒绝会自动播【被拒绝】——UI 侧只管跟着开关（对话设计说明 §7）
            var dialogue = GameManager.Instance.DialogueManager;
            dialogue.PlaybackStarted += OnDialogueStarted;
            dialogue.PlaybackEnded += OnDialogueEnded;
            UI.ShowToast(notice);
        }

        protected override void OnExit()
        {
            var visitor = GameManager.Instance.VisitorManager;
            visitor.InstanceSpawned -= OnVisitorListChanged;
            visitor.InstanceChanged -= OnVisitorListChanged;
            visitor.InstanceDeparted -= OnVisitorListChanged;
            var dialogue = GameManager.Instance.DialogueManager;
            dialogue.PlaybackStarted -= OnDialogueStarted;
            dialogue.PlaybackEnded -= OnDialogueEnded;
            HouseGmConsole.FullResetRequested -= OnGmFullReset;
            GameManager.Instance.HouseClockManager.SetStopReason(EClockStopReason.OffHubPage, true);
            topBar.Dispose();
        }

        private void OnVisitorListChanged(VisitorInstance instance)
        {
            if (view == null) return;
            guestRail.Refresh();
            taskCard.Refresh();
        }

        private void OnDialogueStarted() => DialogueOverlay.Open(UI);

        private void OnDialogueEnded() => DialogueOverlay.CloseFromPlaybackEnded();

        /// <summary>GM「恢复初始态」：面板本体已重置经济与家具会话，这里补访客/时钟归零与表现重建。</summary>
        private void OnGmFullReset()
        {
            var gm = GameManager.Instance;
            gm.VisitorManager.ResetNew();
            gm.HouseClockManager.ResetNew();
            gm.DialogueManager.ResetNew(); // 清 recent 环与待播队列，并强制收掉可能开着的对话框
            SelectedInstanceId = -1;
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
            SfxManager.Play(ESfx.PageTransition); // 音效需求 #5：切换房间即转场

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

        /// <summary>
        /// 选中在场访客并搭话。
        ///
        /// 接待/拒绝**不再由 UI 决定**——它们是【初次见面】对话末尾分支选项上的事件（§7），
        /// 这里只负责把「玩家点了这位访客」翻译成一次对话请求，剩下的全在对话内容里。
        /// 旧版那套「按访客状态硬生成接待/拒绝/递物品按钮」的 debug 驱动层已随对话系统落地删除
        /// （访客交付说明 §8 的临时许可到此为止）。
        /// </summary>
        public void SelectGuest(int instanceId)
        {
            var instance = GameManager.Instance.VisitorManager.Find(instanceId);
            if (instance == null)
            {
                Toast("这位访客已经离开了");
                return;
            }
            SelectedInstanceId = instanceId;
            taskCard.Refresh();
            // 音效需求 #3：点访客卡/NPC 的交互音在此统一发（两条点击路径都汇到这里；访客卡按钮的基础点击音已关避免叠响）
            SfxManager.Play(ESfx.GuestInteract);

            switch (instance.State)
            {
                case EVisitorState.FrontDesk:
                    // 播【初次见面】：对话框由 DialogueManager.PlaybackStarted 事件拉起（见 OnDialogueStarted）
                    GameManager.Instance.VisitorManager.RequestFirstMeeting(instanceId);
                    break;
                case EVisitorState.Serving:
                    // 【外部依赖】选物品并交付属于「需求交付页面」，由单独的落地文档定义（§7）。
                    // 那个页面就位前，这里只提示需求；提交路径的验收暂由 GM 面板（F1）的调试按钮承担。
                    Toast($"{instance.DisplayName} 在等：{instance.BuildNeedSentence()}");
                    break;
                default:
                    Toast($"{instance.DisplayName} 正心满意足地在屋里逛着。");
                    break;
            }
        }

        /// <summary>
        /// 结束今天（§7 日结）：场上有未处理访客（前台/服务中）时不可用，须逐个处理；闲逛中的不阻塞。
        /// 成功后弹当日结算面板（只展示不惩罚），时间已跳到次日开门时刻。
        /// </summary>
        public void TryEndDay()
        {
            var gm = GameManager.Instance;
            if (gm.VisitorManager.HasBlockingVisitors)
            {
                Toast("还有访客在等待接待或服务中 · 请逐个完成服务或拒绝后再结束今天");
                return;
            }
            var endedDay = gm.HouseClockManager.Data.Day;
            var summary = gm.VisitorManager.EndDay();
            if (summary == null) return;
            guestRail.Refresh();
            taskCard.Refresh();
            DaySettleOverlay.Open(UI, endedDay, summary);
        }

        /// <summary>点击场景中的访客 NPC（观景模式下先展开界面）。</summary>
        public void OnVisitorClicked(int instanceId)
        {
            if (furnitureModeOpen || roomTransitioning) return;
            if (immersive) SetImmersive(false);
            SelectGuest(instanceId);
        }

        /// <summary>家具模式：世界空间独立舞台，打开期间禁用整个壳 Canvas，退出回调恢复并重烘焙背景。</summary>
        public void OpenFurnitureMode()
        {
            if (furnitureModeOpen) return;
            furnitureModeOpen = true;
            UI.Canvas.enabled = false;
            var opened = FurnitureRoomController.Open(RoomIndex, () => // 家具模式随 Hub 当前房间动态加载
            {
                furnitureModeOpen = false;
                UI.Canvas.enabled = true;
                // 旧壳此处落档；存档功能移除（§16.5），仅重烘焙背景与热点
                scene.RefreshAfterFurniture();
                SfxManager.Play(ESfx.PageTransition); // 音效需求 #5：退出家具模式
            });
            if (!opened)
            {
                furnitureModeOpen = false;
                UI.Canvas.enabled = true;
                Toast("家具配置表缺失：请先执行菜单 MasterHouse → 家具系统 → 创建配置表");
            }
            else
            {
                SfxManager.Play(ESfx.PageTransition); // 音效需求 #5：进入家具模式
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
            HouseUIUtil.ApplyPanelSkin(toggleView.button.targetGraphic as UnityEngine.UI.Image, .8f, 2.5f); // 收起界面按钮换 common 框
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

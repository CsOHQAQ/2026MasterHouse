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
        private bool furnitureModeOpen;

        /// <summary>当前房间下标（四宫格：由场景相机的视口中心决定，见 HubSceneBinder.DetectCurrentRoom）。</summary>
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
            // 离开 Hub 时丢弃未消化的小游戏请求，免得下次进来冷不丁弹一局出来
            MinigameOverlay.DiscardPending();
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

        /// <summary>
        /// 对话播放结束：先收对话框，**再**消化小游戏的待打开请求（小游戏说明 §3.7）。
        /// 顺序不能反——StartMinigameAction 只登记意图不当场开页，就是为了等这一刻：
        /// 对话层已经退栈，小游戏才压得进一个干净的栈顶。
        /// </summary>
        private void OnDialogueEnded()
        {
            DialogueOverlay.CloseFromPlaybackEnded();
            MinigameOverlay.ConsumePending(UI);
        }

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
            // 四宫格相机常开（观景/普通模式都可滚轮缩放、拖拽平移）；叠加层开着时壳不派发本方法，天然不抢滚轮
            scene.HandleCamera();
            if (immersive) return;
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

        /// <summary>房间导航/方向键：相机平滑推到目标房间（四宫格连续世界，2026-08-13；旧的开关门/交叉淡入转场随之退役）。</summary>
        public void SelectRoom(int index)
        {
            if (view == null) return;
            var rooms = GameManager.Instance.CodexTable.rooms;
            if (index == RoomIndex)
            {
                Toast($"当前位于{rooms[index].displayName} · {rooms[index].note}");
                scene.FocusRoom(index); // 已在此房间：仍推一次镜头，把缩放/平移复位到满屏
                return;
            }
            SfxManager.Play(ESfx.PageTransition); // 音效需求 #5：切换房间即转场
            scene.FocusRoom(index);
            Toast($"镜头推向{rooms[index].displayName}");
        }

        /// <summary>场景相机的视口中心换了房间（HubSceneBinder 回调）：同步下标并刷新导航高亮。</summary>
        public void NotifyCameraRoomChanged(int index)
        {
            RoomIndex = index;
            roomNav.Refresh();
        }

        /// <summary>
        /// 访客被拖到某房间后松手（舞台层回调）：翻译成业务动作（§8 同口径）。
        /// 返回业务是否接受这个落点——false 时舞台把演员弹回起手位置。
        ///
        /// 被拒时**一定要给出理由**：拖不动的规则（前台不可搬、服务中锁房、一房一客）都是玩法约束，
        /// 演员默默弹回去只会让玩家以为是操作没成功。
        /// </summary>
        public bool OnVisitorDropped(int instanceId, int roomIndex)
        {
            var visitor = GameManager.Instance.VisitorManager;
            var instance = visitor.Find(instanceId);
            if (instance == null) return false;
            var fromRoom = instance.RoomIndex;
            var wasAwaiting = instance.State == EVisitorState.AwaitingRoom;

            if (!visitor.MoveVisitorToRoom(instanceId, roomIndex))
            {
                Toast(RejectReason(instance, roomIndex));
                return false;
            }

            var rooms = GameManager.Instance.CodexTable.rooms;
            if (wasAwaiting) Toast($"已把{instance.DisplayName}安排进{rooms[roomIndex].displayName}");
            else if (fromRoom != roomIndex) Toast($"已把{instance.DisplayName}带到{rooms[roomIndex].displayName}");
            return true;
        }

        /// <summary>拖拽被业务拒绝的原因文案（与 VisitorManager.MoveVisitorToRoom 的裁决表一一对应，§5.2）。</summary>
        private static string RejectReason(VisitorInstance instance, int roomIndex)
        {
            var visitor = GameManager.Instance.VisitorManager;
            switch (instance.State)
            {
                case EVisitorState.FrontDesk:
                    return $"{instance.DisplayName}还在门口等着被接待 · 先点他交谈";
                case EVisitorState.Serving:
                    return $"{instance.DisplayName}正在等需求被满足 · 服务中不能换房";
                case EVisitorState.AwaitingRoom:
                case EVisitorState.Wandering:
                    if (roomIndex < VisitorManager.FirstGuestRoomIndex || roomIndex > VisitorManager.LastGuestRoomIndex)
                        return "起居室是大堂，不能当客房 · 请拖进卧室/厨房/书房";
                    if (visitor.IsRoomOccupied(roomIndex))
                    {
                        var rooms = GameManager.Instance.CodexTable.rooms;
                        return $"{rooms[roomIndex].displayName}已经住了人 · 一间房只招待一位客人";
                    }
                    return "这里放不下";
                default:
                    return "这位访客已经离开了";
            }
        }

        /// <summary>
        /// 选中在场访客并搭话。
        ///
        /// 接待/拒绝**不再由 UI 决定**——它们是【初次见面】对话末尾分支选项上的事件（§7），
        /// 这里只负责把「玩家点了这位访客」翻译成一次对话请求，剩下的全在对话内容里。
        /// 旧版那套「按访客状态硬生成接待/拒绝/递物品按钮」的 debug 驱动层已随对话系统落地删除
        /// （访客交付说明 §8 的临时许可到此为止）。
        ///
        /// 2026-08-14 对话重构后**说哪一类由 VisitorManager.RequestTalk 决定**，UI 只负责在
        /// 「这一下点击不该有对话」时给一句提示。四种没有对话的情形各有各的说法（见 NoTalkHint）。
        /// </summary>
        public void SelectGuest(int instanceId)
        {
            var visitors = GameManager.Instance.VisitorManager;
            var instance = visitors.Find(instanceId);
            if (instance == null)
            {
                Toast("这位访客已经离开了");
                return;
            }
            SelectedInstanceId = instanceId;
            taskCard.Refresh();
            // 音效需求 #3：点访客卡/NPC 的交互音在此统一发（两条点击路径都汇到这里；访客卡按钮的基础点击音已关避免叠响）
            SfxManager.Play(ESfx.GuestInteract);

            // 对话框由 DialogueManager.PlaybackStarted 事件拉起（见 OnDialogueStarted）
            if (!visitors.RequestTalk(instanceId)) Toast(NoTalkHint(visitors, instance));
        }

        /// <summary>点了但没有对话时的提示文案（与 VisitorManager.CanInteract 的判据一一对应）。</summary>
        private static string NoTalkHint(VisitorManager visitors, VisitorInstance instance)
        {
            switch (instance.State)
            {
                case EVisitorState.FrontDesk:
                    if (visitors.FrontDeskHead != instance)
                        return $"{instance.DisplayName}还在后面排队 · 先招呼前面那位";
                    if (visitors.HasAwaitingRoomVisitor)
                        return "还有一位客人在等房间 · 先把他安顿好再接待下一位";
                    return "客房都住满了 · 等有人离开再接待";
                case EVisitorState.AwaitingRoom:
                    // 这一态没有对话，唯一的推进方式是把人拖进空房（拒绝也不给：接待时已经保证有房）
                    return $"把{instance.DisplayName}拖进一间空客房，他安顿好才会说出需求";
                case EVisitorState.Serving:
                    return $"{instance.DisplayName}还在安顿 · 等他开口再来";
                default:
                    return $"{instance.DisplayName} 正心满意足地在屋里逛着。";
            }
        }

        /// <summary>
        /// 结束今天（§7 日结）：只有「等待分配房间」的访客会阻塞（2026-08-14 第 11 题）——
        /// 前台的到点自动清场，服务中的原样跨天。成功后弹当日结算面板（只展示不惩罚），
        /// 时间已跳到次日开门时刻。
        /// </summary>
        public void TryEndDay()
        {
            var gm = GameManager.Instance;
            if (gm.VisitorManager.HasBlockingVisitors)
            {
                Toast("还有客人在等房间 · 把他拖进一间空客房再结束今天");
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
            if (furnitureModeOpen) return;
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
            }, () => StoreOverlay.Open(UI, OpenFurnitureMode)); // 「购买家具」：开商店，关店后递归退回摆放模式
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

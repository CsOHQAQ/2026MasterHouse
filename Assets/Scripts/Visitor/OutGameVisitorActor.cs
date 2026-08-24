using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using F = MasterHouse.HouseUIRuntime;

namespace MasterHouse
{
    /// <summary>
    /// 场景中的单个访客 NPC 表现层演员，分两类：
    /// ①业务访客（对应 VisitorInstance）：状态由舞台层按业务实例同步驱动（SyncBusinessState），
    ///   前台等待 → 接待后进屋 → 服务完成庆祝并闲逛 → 离场；点击触发对话，闲逛台词经句子气泡展示。
    /// ②串门邻居（ambient）：进门 → 在门口排队等待，由玩家点击后选择「请进屋 / 请回吧」；请进后游走一段时间自行离开。
    /// 只负责表现与点击转发，不持有任何业务结算（§16.4 表现层豁免：允许 deltaTime 与无种子随机，结果不回写业务）。
    /// 位置使用场景归一化坐标（0~1 视口），由舞台层换算成锚点。
    /// </summary>
    internal sealed class OutGameVisitorActor : MonoBehaviour
    {
        private enum ActorState { Hidden, Arriving, Waiting, Wandering, Celebrating, Leaving, Gone }

        private const float BaseHeight = 205f;     // 最近处（画面下缘）的显示高度，越远按深度缩小
        private const float FarScale = .6f;
        /// <summary>名牌底边与**头顶**的间距（名牌 pivot 在底边，所以这个数就是肉眼看到的空隙）。</summary>
        private const float CardOffsetY = 22f;
        private const float NearY = .04f;          // 深度带：y 越小离镜头越近
        private const float FarY = .34f;

        private string displayName;
        private bool ambient;
        private Texture2D awaitTexture;
        private OutGameVisitorSheet awaitSheet;
        private Texture2D walkTexture;
        private OutGameVisitorSheet walkSheet;
        private Texture2D celebrateTexture;
        private OutGameVisitorSheet celebrateSheet;
        private OutGameVisitorSheetAnimator animator;
        private RectTransform spriteRect;
        private CanvasGroup group;
        private CanvasGroup cardGroup;
        private RectTransform cardRect;
        private Text cardLabel;
        private CanvasGroup choiceGroup;
        private OutGameVisitorBubble bubble;
        private bool choiceOpen;
        private float choiceTimer;
        private Action onClicked;
        private Action onGone;

        /// <summary>
        /// 「现在点他有没有对话」的业务判据（舞台层注入，邻居为 null）。
        /// **表现层不自己推**：可交互性不只看 EVisitorState——前台要看队首/有没有空房，
        /// 服务中还要看安顿完没有（NeedPromptTick），这些在演员这边一概看不见。
        /// 判据一律走 VisitorManager.NoTalkReason，与访客卡、Toast 同一个来源（2026-08-19）。
        /// </summary>
        private Func<VisitorManager.ENoTalkReason> noTalkReason;

        /// <summary>上一次轮询到的判据；null = 还没轮询过（邻居恒为 null）。变了才重刷名牌文案。</summary>
        private VisitorManager.ENoTalkReason? talkReason;

        private ActorState state = ActorState.Hidden;
        private bool spawnInside; // 常驻回填：不走进门流程，直接在屋内/前台淡入
        private Vector2 doorPoint;
        private Vector2 waitPoint;
        /// <summary>所在房间的访客活动区（房间表可配；由舞台层注入，游走落点与拖拽钳制共用）。</summary>
        private Func<int, Vector2> randomWalkPoint;
        /// <summary>所在房间的访客入口区（房间表可配）：离场时走向本房间的门口范围。</summary>
        private Func<int, Rect> entryArea;
        private Vector2 moveTarget;
        private bool moving;
        private bool facingRight;
        private bool reacting;
        private bool usingWalkSheet;
        private bool movementReachedTarget;
        private bool movementAnimationFinished;
        private int finishWalkAfterLoop;
        private WalkVisualPhase walkVisualPhase;
        private float walkSpeed;      // 视口单位/秒
        private float stateTimer;     // 游走停顿 / 生成延迟
        private float patienceTimer;  // 邻居在门口的耐心，耗尽自行离开
        private float departTimer = -1f; // 仅邻居使用：请进屋后的停留倒计时；业务访客离场由业务层驱动
        private float bobPhase;
        private float reactHopTimer;

        private enum WalkVisualPhase { None, Rising, Walking, Settling }

        /// <summary>业务访客当前同步到的业务状态；-1 = 尚未同步/氛围邻居。</summary>
        private int businessState = -1;

        /// <summary>房间内归一化坐标（0~1，相对所在房间的背景图），供舞台层换算世界锚点与深度排序。</summary>
        public Vector2 ScenePosition { get; private set; }

        /// <summary>所在房间（Hub 四宫格下标；表现层副本，业务真相在 VisitorInstance.RoomIndex）。</summary>
        public int RoomIndex { get; private set; }

        public bool IsInteractable => state == ActorState.Waiting || state == ActorState.Wandering;

        /// <summary>邻居是否还在门口排队（含正走向排队点的路上），供舞台层动态分配队位。</summary>
        public bool IsQueuingAtDoor => ambient && (state == ActorState.Arriving || state == ActorState.Waiting);

        /// <summary>是否已进入离场流程（舞台层判重用）。</summary>
        public bool IsLeaving => state == ActorState.Leaving || state == ActorState.Gone;

        public static OutGameVisitorActor Create(Transform parent, string actorId, string actorName, string sheetBase,
            bool isAmbient, float spawnDelay, Vector2 door, Vector2 wait,
            Func<int, Vector2> randomWalkPoint, Func<int, Rect> entryArea,
            Action clicked, Action gone, bool spawnInside = false, int startRoom = 0,
            Func<VisitorManager.ENoTalkReason> noTalkReason = null)
        {
            var awaitMeta = OutGameVisitorSheet.Load(sheetBase + "_await_sheet", out var awaitTex);
            if (awaitMeta == null)
            {
                Debug.LogWarning("[OutGameVisitorActor] 访客序列帧缺失：" + sheetBase);
                return null;
            }
            var rect = F.Rect(parent, "Visitor_" + actorId, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            // 立绘底部有一段透明留白，帧底 ≠ 脚底：pivot 抬到脚底那一行，
            // 可见的脚才落在地面坐标上（2026-08-18 反馈「访客还是有些高」）
            rect.pivot = new Vector2(.5f, Mathf.Clamp(awaitMeta.footPadding, 0f, .4f));
            var actor = rect.gameObject.AddComponent<OutGameVisitorActor>();
            actor.displayName = actorName;
            actor.ambient = isAmbient;
            actor.awaitTexture = awaitTex;
            actor.awaitSheet = awaitMeta;
            actor.walkSheet = OutGameVisitorSheet.Load(sheetBase + "_walk_sheet", out actor.walkTexture);
            actor.celebrateSheet = OutGameVisitorSheet.Load(sheetBase + "_attack_sheet", out actor.celebrateTexture);
            actor.doorPoint = door;
            actor.waitPoint = wait;
            actor.randomWalkPoint = randomWalkPoint;
            actor.entryArea = entryArea;
            actor.onClicked = clicked;
            actor.onGone = gone;
            actor.noTalkReason = noTalkReason;
            actor.spawnInside = spawnInside;
            actor.RoomIndex = startRoom; // 主楼场景（2026-08-16）：访客进场落在接待室，而不是默认房间 0
            actor.walkSpeed = .042f * UnityEngine.Random.Range(.85f, 1.15f);
            actor.stateTimer = Mathf.Max(0f, spawnDelay);
            actor.BuildHierarchy();
            return actor;
        }

        private void BuildHierarchy()
        {
            group = F.Group(gameObject, 0f);
            ScenePosition = doorPoint;

            var sprite = F.Stretch(transform, "Sprite");
            spriteRect = sprite;
            var image = sprite.gameObject.AddComponent<RawImage>();
            image.raycastTarget = false;
            animator = sprite.gameObject.AddComponent<OutGameVisitorSheetAnimator>();
            animator.Play(awaitTexture, awaitSheet, 12f, true);

            // 透明点击区（与家具热点一致：clear Image 承接指针）
            var click = gameObject.AddComponent<Image>();
            click.sprite = F.WhiteSprite;
            click.color = Color.clear;
            var button = gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(OnClickSelf);

            // 头顶气泡（在名牌卡之前创建，悬停时名牌盖在气泡上）：
            // 「···」可交互提示 + 闲聊文字两套视觉合一（#5），气泡以自身底边贴近演员头顶，稍微右移避免挡住脸。
            bubble = OutGameVisitorBubble.Create(transform, new Vector2(18, 4));

            // 头顶悬停卡：访客名 + 当前状态
            var card = F.Panel(transform, "Card", new Vector2(.5f, 1), new Vector2(.5f, 1),
                new Vector2(0, CardOffsetY), new Vector2(240, 68), new Color(.32f, .06f, .18f, .92f));
            cardRect = card.rectTransform;
            // 以底边做挂点：anchoredPosition.y 就是名牌与头顶的真实空隙，
            // 不会再因名牌自身高度而多压半个身位（口径与气泡一致）
            cardRect.pivot = new Vector2(.5f, 0f);
            F.Outline(card.gameObject, new Color(.85f, .15f, .45f, .5f), new Vector2(1, -1));
            cardLabel = F.Label(card.transform, "Text", "", 17, F.White, TextAnchor.MiddleCenter, FontStyle.Bold);
            cardGroup = F.Group(card.gameObject, 0f);
            cardGroup.blocksRaycasts = false;
            cardGroup.interactable = false;

            // 黄色感叹号标记已随 #5 退役：「此刻点他有对话」改由「···」气泡常驻承担（见 RefreshTalkState）
            if (ambient) BuildChoicePopup();

            var trigger = gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ =>
            {
                if (choiceOpen) return;
                cardGroup.DOKill();
                cardGroup.DOFade(1f, .16f).SetUpdate(true);
            });
            trigger.triggers.Add(enter);
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => { cardGroup.DOKill(); cardGroup.DOFade(0f, .16f).SetUpdate(true); });
            trigger.triggers.Add(exit);

            UpdateStatusCard();
            ApplyDepth();
        }

        /// <summary>邻居的去留选择弹窗：请进屋 → 进入游走；请回吧 → 直接离开。</summary>
        private void BuildChoicePopup()
        {
            var choice = F.Panel(transform, "Choice", new Vector2(.5f, 1), new Vector2(.5f, 1),
                new Vector2(0, 62), new Vector2(268, 62), new Color(.04f, .045f, .085f, .95f));
            F.Outline(choice.gameObject, new Color(.85f, .15f, .45f, .5f), new Vector2(1, -1));
            F.Button(choice.transform, "Admit", "请进屋", Admit,
                new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(69, 0), new Vector2(114, 44), F.Wine, F.White, 17);
            F.Button(choice.transform, "Expel", "请回吧", Expel,
                new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-69, 0), new Vector2(114, 44), new Color(1, 1, 1, .08f), F.White, 17);
            choiceGroup = F.Group(choice.gameObject, 0f);
            choiceGroup.blocksRaycasts = false;
            choiceGroup.interactable = false;
        }

        /// <summary>
        /// 轮询业务判据（每帧）：判据变了刷名牌文案；「此刻点他有对话」由「···」气泡常驻承担
        /// （#5 取代原黄色感叹号），与访客卡同判据（VisitorManager.NoTalkReason）。
        /// 「安顿 → 开口示意」在业务上**不是状态切换**（两段都是 Serving，区别只在 NeedPromptTick），
        /// SyncBusinessState 那条路根本收不到通知，所以只能这样轮询。
        /// </summary>
        private void RefreshTalkState()
        {
            if (bubble == null) return;
            if (noTalkReason == null)
            {
                // 邻居：在门口等去留决定时可点（去留弹窗），同样用「···」示意；弹窗已开就不再示意
                if (ambient)
                    bubble.SetTalkReady(state == ActorState.Waiting && !choiceOpen);
                return;
            }
            var reason = noTalkReason();
            if (talkReason != reason)
            {
                talkReason = reason;
                UpdateStatusCard();
            }
            // 还在进场/庆祝/离场的路上就先不召唤：那时点了也被 IsInteractable 拦下
            bubble.SetTalkReady(IsInteractable && reason == VisitorManager.ENoTalkReason.None);
        }

        private void OnClickSelf()
        {
            if (!IsInteractable) return;
            if (!ambient)
            {
                onClicked?.Invoke();
                return;
            }
            if (state == ActorState.Waiting) ToggleChoice(!choiceOpen);
            else PokeReaction();
        }

        private void ToggleChoice(bool open)
        {
            if (choiceGroup == null) return;
            choiceOpen = open;
            choiceTimer = 6f; // 一会儿没决定就自动收起
            choiceGroup.DOKill();
            choiceGroup.DOFade(open ? 1f : 0f, .18f).SetUpdate(true);
            choiceGroup.blocksRaycasts = open;
            choiceGroup.interactable = open;
            if (open && cardGroup != null)
            {
                cardGroup.DOKill();
                cardGroup.DOFade(0f, .12f).SetUpdate(true);
            }
        }

        /// <summary>玩家决定：让这只邻居进屋。开始屋内游走，一段时间后自行离开。</summary>
        public void Admit()
        {
            if (!IsQueuingAtDoor) return;
            ToggleChoice(false);
            departTimer = UnityEngine.Random.Range(22f, 45f);
            EnterWandering(.2f);
        }

        /// <summary>玩家决定：把这只邻居赶出去。加速走回门口消失。</summary>
        public void Expel()
        {
            if (!IsQueuingAtDoor) return;
            ToggleChoice(false);
            walkSpeed *= 1.3f;
            EnterLeaving();
        }

        // ── 玩家拖拽换房已整体退役（2026-08-22 #8）：分房改走 HubPage 的强制分配模式 ──

        /// <summary>直接落位到某房间（业务同步用；不走进门流程）。</summary>
        public void TeleportToRoom(int roomIndex, Vector2 localPosition)
        {
            RoomIndex = roomIndex;
            ScenePosition = localPosition;
            StopMoving();
            UpdateStatusCard();
        }

        /// <summary>舞台层动态分配的门口排队点（队伍前移时演员会挪过去）。</summary>
        public void SetWaitPoint(Vector2 point)
        {
            if (!IsQueuingAtDoor) return;
            if ((waitPoint - point).sqrMagnitude < 1e-6f) return;
            waitPoint = point;
            moveTarget = point;
            StartMoving();
        }

        private void OnDestroy()
        {
            DOTween.Kill(this);
            if (cardGroup != null) cardGroup.DOKill();
            if (choiceGroup != null) choiceGroup.DOKill();
        }

        /// <summary>
        /// 业务访客状态同步（舞台层每帧调用，只读业务不回写，§16.4）：
        /// 前台等待 → 服务中（进屋）→ 闲逛（庆祝后游走）；实例离场时舞台层调 BeginDepart。
        /// </summary>
        public void SyncBusinessState(EVisitorState next)
        {
            if (ambient || IsLeaving) return;
            if (businessState == (int)next) return;
            var first = businessState < 0;
            businessState = (int)next;
            if (state == ActorState.Hidden)
            {
                UpdateStatusCard();
                return; // 尚未进场：出场时 BeginInside/BeginArrive 按业务状态落位
            }
            switch (next)
            {
                case EVisitorState.FrontDesk:
                    break; // 初始状态：进门 → 前台等待，由 Arriving→Waiting 流程呈现
                case EVisitorState.AwaitingRoom:
                    break; // 接待完仍在门口排队等分房（§5.3），站位不动——分配模式点了空房才算数
                case EVisitorState.Serving:
                    EnterWandering(.2f); // 分房落定：走进那间客房（房间由舞台层按 instance.RoomIndex 同步）
                    break;
                case EVisitorState.Wandering:
                    if (first) EnterWandering(.3f); // 重建舞台时已在闲逛：直接游走，不补庆祝
                    else Celebrate();              // 完成服务：庆祝一次，然后继续闲逛
                    break;
                case EVisitorState.AwaitingFarewell:
                    // 停留到点转过来：人还在自己房间里，照旧游走，只是头顶亮起「！」等玩家来道别。
                    // 不在这里走向门口——真正离场由【告别】对话里的 Leave 事件触发 BeginDepart
                    if (first) EnterWandering(.3f);
                    break;
            }
            UpdateStatusCard();
        }

        /// <summary>业务访客离场（拒绝/超时/闲逛结束/日结清场共用）：走向门口消失。</summary>
        public void BeginDepart()
        {
            if (IsLeaving) return;
            if (state == ActorState.Hidden)
            {
                onGone?.Invoke();
                Destroy(gameObject);
                return;
            }
            walkSpeed *= 1.25f;
            EnterLeaving();
        }

        /// <summary>
        /// 闲逛冒泡（§9：复用头顶那一套气泡，不新建第二套）。
        /// 台词文字恢复显示（#5，2026-08-22）：业务层把选好格式好的那句话连同展示时长一起递过来。
        /// </summary>
        public void ShowBubble(string line, float holdSeconds)
        {
            if (bubble != null) bubble.ShowLine(line, holdSeconds);
        }

        /// <summary>完成服务：播放一次庆祝动作，然后继续游走。</summary>
        private void Celebrate()
        {
            StopMoving();
            if (celebrateSheet != null)
            {
                state = ActorState.Celebrating;
                animator.Play(celebrateTexture, celebrateSheet, 14f, false, () =>
                {
                    animator.Play(awaitTexture, awaitSheet, 12f, true);
                    if (state == ActorState.Celebrating) EnterWandering(.5f);
                });
            }
            else
            {
                EnterWandering(.5f);
            }
            UpdateStatusCard();
        }

        private void Update()
        {
            var dt = Time.unscaledDeltaTime;
            RefreshTalkState(); // 业务判据 → 头顶「···」提示与名牌文案
            if (choiceOpen)
            {
                choiceTimer -= dt;
                if (choiceTimer <= 0f || !IsQueuingAtDoor) ToggleChoice(false);
            }
            switch (state)
            {
                case ActorState.Hidden:
                    stateTimer -= dt;
                    if (stateTimer <= 0f)
                    {
                        if (spawnInside) BeginInside();
                        else BeginArrive();
                    }
                    break;
                case ActorState.Arriving:
                    moveTarget = waitPoint; // 队位/前台位可能被舞台层实时调整
                    if (MoveTowards(dt))
                    {
                        StopMoving();
                        state = ActorState.Waiting;
                        if (ambient) patienceTimer = UnityEngine.Random.Range(45f, 75f);
                        UpdateStatusCard();
                    }
                    break;
                case ActorState.Waiting:
                    if (moving && MoveTowards(dt)) StopMoving(); // 队伍前移
                    if (ambient)
                    {
                        patienceTimer -= dt;
                        if (patienceTimer <= 0f) { ToggleChoice(false); EnterLeaving(); } // 等太久，自己走了
                    }
                    // 业务访客：在前台一直等（是否超时由业务层判定，表现不自作主张）
                    break;
                case ActorState.Wandering:
                    if (ambient && departTimer > 0f)
                    {
                        departTimer -= dt;
                        if (departTimer <= 0f) { EnterLeaving(); break; }
                    }
                    if (moving)
                    {
                        if (MoveTowards(dt))
                        {
                            StopMoving();
                            stateTimer = UnityEngine.Random.Range(2.5f, 6f);
                        }
                    }
                    else
                    {
                        stateTimer -= dt;
                        if (stateTimer <= 0f) PickNextWaypoint();
                    }
                    break;
                case ActorState.Celebrating:
                    break; // 等待庆祝动画播完回调
                case ActorState.Leaving:
                    if (MoveTowards(dt))
                    {
                        StopMoving();
                        state = ActorState.Gone;
                        group.DOFade(0f, .45f).SetTarget(this).SetUpdate(true)
                            .OnComplete(() =>
                            {
                                onGone?.Invoke();
                                Destroy(gameObject);
                            });
                    }
                    break;
            }
            ApplyDepth();
            ApplyBobbing(dt);
            // 只有可交互时才拦截指针，避免隐身/离场中的透明点击区挡住下层家具热点
            group.blocksRaycasts = IsInteractable;
        }

        private void BeginArrive()
        {
            state = ActorState.Arriving;
            ScenePosition = doorPoint;
            moveTarget = waitPoint;
            StartMoving();
            group.DOFade(1f, .4f).SetTarget(this).SetUpdate(true);
            UpdateStatusCard();
        }

        /// <summary>常驻回填：重建舞台时访客已在场，按业务状态直接落位淡入（门口排队位或屋内游走点）。</summary>
        private void BeginInside()
        {
            group.DOFade(1f, .4f).SetTarget(this).SetUpdate(true);
            // 「等待分配房间」与「前台等待接待」都站在起居室入口区排队（需求重做说明 §5.3），落位口径一致
            if (!ambient && (businessState == (int)EVisitorState.FrontDesk ||
                             businessState == (int)EVisitorState.AwaitingRoom))
            {
                ScenePosition = waitPoint;
                state = ActorState.Waiting;
                UpdateStatusCard();
                return;
            }
            ScenePosition = RandomWalkPoint();
            EnterWandering(UnityEngine.Random.Range(.5f, 3f));
        }

        private void EnterWandering(float idleDelay)
        {
            state = ActorState.Wandering;
            StopMoving();
            stateTimer = idleDelay;
            UpdateStatusCard();
        }

        private void EnterLeaving()
        {
            state = ActorState.Leaving;
            // 离场走向**当前所在房间**的入口区（每个房间的门位在房间表里配）；未注入时回落进场门点
            if (entryArea != null)
            {
                var area = entryArea(RoomIndex);
                moveTarget = new Vector2(
                    UnityEngine.Random.Range(area.xMin, area.xMax),
                    UnityEngine.Random.Range(area.yMin, area.yMax));
            }
            else
            {
                moveTarget = doorPoint;
            }
            StartMoving();
            UpdateStatusCard();
        }

        /// <summary>所在房间活动区内随机取落点（活动区按房间美术红框配置在房间表）。</summary>
        private Vector2 RandomWalkPoint()
        {
            // 可走梯形的取样逻辑在舞台层（与家具地面网格同源，2026-08-16）；无委托时回落旧矩形
            if (randomWalkPoint != null) return randomWalkPoint(RoomIndex);
            var area = Rect.MinMaxRect(.04f, .03f, .96f, .35f);
            return new Vector2(
                UnityEngine.Random.Range(area.xMin, area.xMax),
                UnityEngine.Random.Range(area.yMin, area.yMax));
        }

        private void PickNextWaypoint()
        {
            // 随机挑一个与当前位置有一定距离的落点，避免原地抖动
            for (var attempt = 0; attempt < 6; attempt++)
            {
                var candidate = RandomWalkPoint();
                if ((candidate - ScenePosition).sqrMagnitude < .01f) continue;
                moveTarget = candidate;
                StartMoving();
                return;
            }
            stateTimer = 2f;
        }

        /// <summary>点击游走中的邻居：跳一下逗个乐，有攻击表的顺便做一次动作（状态切换演示）。</summary>
        private void PokeReaction()
        {
            if (state != ActorState.Wandering && state != ActorState.Waiting) return;
            StopMoving();
            stateTimer = Mathf.Max(stateTimer, 1.5f);
            reactHopTimer = .55f;
            if (celebrateSheet != null && !reacting)
            {
                reacting = true;
                animator.Play(celebrateTexture, celebrateSheet, 14f, false, () =>
                {
                    reacting = false;
                    animator.Play(awaitTexture, awaitSheet, 12f, true);
                });
            }
        }

        private void StartMoving()
        {
            if (moving) return;
            moving = true;
            movementReachedTarget = false;
            movementAnimationFinished = false;
            finishWalkAfterLoop = 0;
            if (usingWalkSheet) ReturnToAwaitAnimation();
        }

        private void StopMoving()
        {
            moving = false;
            movementReachedTarget = false;
            movementAnimationFinished = false;
            finishWalkAfterLoop = 0;
            ReturnToAwaitAnimation();
        }

        private void EnsureMovementAnimation()
        {
            if (usingWalkSheet || walkSheet == null || reacting || state == ActorState.Celebrating) return;
            usingWalkSheet = true;
            movementAnimationFinished = false;
            var fps = walkSheet.TransitionPlaybackFps;
            if (walkSheet.HasMovementWindow && walkSheet.moveStartFrame > 0)
            {
                walkVisualPhase = WalkVisualPhase.Rising;
                animator.PlayRange(walkTexture, walkSheet, fps, 0, walkSheet.moveStartFrame - 1, false, () =>
                {
                    if (usingWalkSheet && moving && !reacting) BeginWalkLoop();
                });
            }
            else
            {
                BeginWalkLoop();
            }
        }

        private void BeginWalkLoop()
        {
            if (walkSheet == null) return;
            walkVisualPhase = WalkVisualPhase.Walking;
            var first = walkSheet.HasMovementWindow ? walkSheet.moveStartFrame : 0;
            var last = walkSheet.HasMovementWindow ? walkSheet.moveEndFrame : walkSheet.frameCount - 1;
            var loopFirst = walkSheet.HasMovementWindow ? walkSheet.WalkLoopStartFrame : first;
            if (loopFirst > first)
            {
                animator.PlayRange(walkTexture, walkSheet, walkSheet.PlaybackFps,
                    first, loopFirst - 1, false, () =>
                    {
                        if (usingWalkSheet && moving && !movementReachedTarget &&
                            walkVisualPhase == WalkVisualPhase.Walking)
                            BeginWalkCycle(loopFirst, last);
                    });
                return;
            }
            BeginWalkCycle(loopFirst, last);
        }

        private void BeginWalkCycle(int first, int last)
        {
            animator.PlayRange(walkTexture, walkSheet, walkSheet.PlaybackFps, first, last, true);
        }

        private void BeginWalkOutro()
        {
            if (walkSheet == null || !walkSheet.HasMovementWindow)
            {
                movementAnimationFinished = true;
                return;
            }
            walkVisualPhase = WalkVisualPhase.Settling;
            if (walkSheet.moveEndFrame + 1 < walkSheet.frameCount)
            {
                animator.PlayRange(walkTexture, walkSheet, walkSheet.TransitionPlaybackFps,
                    walkSheet.moveEndFrame + 1, walkSheet.frameCount - 1, false, FinishMovementAnimation);
            }
            else if (walkSheet.reverseIntroOnStop && walkSheet.moveStartFrame > 0)
            {
                animator.PlayRange(walkTexture, walkSheet, walkSheet.TransitionPlaybackFps,
                    walkSheet.moveStartFrame - 1, 0, false, FinishMovementAnimation);
            }
            else
            {
                movementAnimationFinished = true;
            }
        }

        private void FinishMovementAnimation()
        {
            if (usingWalkSheet) movementAnimationFinished = true;
        }

        private void ReturnToAwaitAnimation()
        {
            if (!usingWalkSheet) return;
            usingWalkSheet = false;
            walkVisualPhase = WalkVisualPhase.None;
            if (!reacting && animator != null)
                animator.Play(awaitTexture, awaitSheet, 12f, true);
        }

        /// <summary>朝 moveTarget 匀速移动一帧；到达返回 true。速度随深度缩放，远处走得慢，近大远小不穿帮。</summary>
        private bool MoveTowards(float dt)
        {
            EnsureMovementAnimation();
            if (movementAnimationFinished) return true;
            var delta = moveTarget - ScenePosition;
            if (Mathf.Abs(delta.x) > .003f) facingRight = delta.x > 0f;
            if (movementReachedTarget)
            {
                if (walkVisualPhase == WalkVisualPhase.Walking && animator.CompletedLoops >= finishWalkAfterLoop)
                    BeginWalkOutro();
                return false;
            }
            if (walkSheet != null && walkSheet.HasMovementWindow && walkVisualPhase != WalkVisualPhase.Walking)
                return false;
            var step = walkSpeed * Mathf.Lerp(1f, .55f, DepthT()) * dt;
            if (delta.magnitude <= step)
            {
                ScenePosition = moveTarget;
                if (walkSheet != null && walkSheet.HasMovementWindow)
                {
                    movementReachedTarget = true;
                    if (walkSheet.reverseIntroOnStop || walkSheet.stopImmediatelyAtTarget)
                        BeginWalkOutro();
                    else
                        finishWalkAfterLoop = animator.CompletedLoops + 1;
                    return false;
                }
                return true;
            }
            ScenePosition += delta.normalized * step;
            return false;
        }

        private float DepthT() => Mathf.InverseLerp(NearY, FarY, ScenePosition.y);

        /// <summary>按深度更新显示尺寸与左右翻转；少数 walk 素材可在 JSON 中单独反转朝向约定。</summary>
        private void ApplyDepth()
        {
            var scale = Mathf.Lerp(1f, FarScale, DepthT());
            var height = BaseHeight * scale;
            var rect = (RectTransform)transform;
            rect.sizeDelta = new Vector2(height * animator.CurrentAspect, height);
            if (spriteRect != null)
            {
                var mirrorForRight = facingRight;
                if (walkSheet != null && walkSheet.invertFacing &&
                    (usingWalkSheet || walkSheet.keepWalkFacingWhenIdle))
                    mirrorForRight = !mirrorForRight;
                spriteRect.localScale = new Vector3(mirrorForRight ? -1f : 1f, 1f, 1f);
            }
            // 名牌/气泡挂在矩形上边缘，但立绘头顶上方还有一段透明留白，
            // 按它下压挂点，名牌才是贴着头而不是浮在半空（2026-08-18 反馈）
            var headDrop = Mathf.Clamp01(awaitSheet != null ? awaitSheet.headPadding : 0f) * height;
            if (cardRect != null) cardRect.anchoredPosition = new Vector2(0f, CardOffsetY - headDrop);
            if (bubble != null) bubble.SetHeadDrop(headDrop);
        }

        /// <summary>没有 walk 素材时才用小幅跳动兜底；有真实序列帧时脚底保持稳定。</summary>
        private void ApplyBobbing(float dt)
        {
            if (spriteRect == null) return;
            if (reactHopTimer > 0f)
            {
                reactHopTimer -= dt;
                spriteRect.anchoredPosition = new Vector2(0f, Mathf.Abs(Mathf.Sin(reactHopTimer * 11f)) * 16f);
                return;
            }
            if (moving && walkSheet == null && state != ActorState.Hidden)
            {
                bobPhase += dt * 9f;
                spriteRect.anchoredPosition = new Vector2(0f, Mathf.Abs(Mathf.Sin(bobPhase)) * 7f);
            }
            else
            {
                bobPhase = 0f;
                spriteRect.anchoredPosition = Vector2.zero;
            }
        }

        private void UpdateStatusCard()
        {
            if (cardLabel == null) return;
            string status;
            if (ambient)
            {
                status = state switch
                {
                    ActorState.Arriving => "溜了进来",
                    ActorState.Waiting => "在门口张望 · 点击决定去留",
                    ActorState.Wandering => "串门中 · 点它逗一逗",
                    ActorState.Leaving => "心满意足地回去了",
                    _ => "",
                };
            }
            else if (state == ActorState.Leaving || state == ActorState.Gone)
            {
                status = "正在离开";
            }
            else if (state == ActorState.Celebrating)
            {
                status = "服务完成！";
            }
            else
            {
                // 措辞判据走 VisitorManager.NoTalkReason，与 Toast、访客卡同一个来源。
                // 原先按 EVisitorState 硬写，服务中一律「点击交谈」——安顿中的客人名牌这么说、
                // 点下去却吃一句「还在安顿」，名牌和 Toast 自相矛盾（2026-08-19 反馈）
                status = talkReason switch
                {
                    VisitorManager.ENoTalkReason.None => businessState switch
                    {
                        (int)EVisitorState.Serving => "有话要说 · 点击交谈",
                        (int)EVisitorState.AwaitingFarewell => "准备离开 · 点击道别",
                        _ => "在门口等待接待 · 点击交谈",
                    },
                    VisitorManager.ENoTalkReason.NotFrontOfQueue => "门口排队中 · 等前面那位",
                    VisitorManager.ENoTalkReason.SomeoneAwaitingRoom => "门口等着 · 先安顿上一位",
                    VisitorManager.ENoTalkReason.NoFreeRoom => "门口等着 · 客房已住满",
                    VisitorManager.ENoTalkReason.AwaitingRoom => "等待安排房间 · 点击一间空房",
                    VisitorManager.ENoTalkReason.SettlingIn => "正在安顿 · 等他开口再来",
                    VisitorManager.ENoTalkReason.Wandering => "心满意足 · 屋内闲逛中",
                    _ => "刚刚进门", // talkReason 尚未轮询（建层的头一帧）
                };
                // 业务访客标注所在房间（四宫格拖拽换房后一眼可辨）；邻居只待起居室不标
                status = RoomLabel() + status;
            }
            cardLabel.text = displayName + "\n<size=13>" + status + "</size>";
        }

        /// <summary>所在房间前缀，如「@卧室 · 」；图鉴表异常时退空串不阻断。</summary>
        private string RoomLabel()
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.CodexTable == null) return string.Empty;
            var rooms = gm.CodexTable.rooms;
            if (RoomIndex < 0 || RoomIndex >= rooms.Count) return string.Empty;
            return "@" + rooms[RoomIndex].displayName + " · ";
        }
    }
}

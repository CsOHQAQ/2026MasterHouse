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
        private const float NearY = .04f;          // 深度带：y 越小离镜头越近
        private const float FarY = .34f;

        private string displayName;
        private bool ambient;
        private Texture2D awaitTexture;
        private OutGameVisitorSheet awaitSheet;
        private Texture2D celebrateTexture;
        private OutGameVisitorSheet celebrateSheet;
        private OutGameVisitorSheetAnimator animator;
        private RectTransform spriteRect;
        private CanvasGroup group;
        private CanvasGroup cardGroup;
        private Text cardLabel;
        private CanvasGroup choiceGroup;
        private OutGameVisitorBubble bubble;
        private bool choiceOpen;
        private float choiceTimer;
        private Action onClicked;
        private Action onGone;

        private ActorState state = ActorState.Hidden;
        private bool spawnInside; // 常驻回填：不走进门流程，直接在屋内/前台淡入
        private Vector2 doorPoint;
        private Vector2 waitPoint;
        private Vector2[] waypoints;
        private Vector2 moveTarget;
        private bool moving;
        private bool facingRight;
        private bool reacting;
        private float walkSpeed;      // 视口单位/秒
        private float stateTimer;     // 游走停顿 / 生成延迟
        private float patienceTimer;  // 邻居在门口的耐心，耗尽自行离开
        private float departTimer = -1f; // 仅邻居使用：请进屋后的停留倒计时；业务访客离场由业务层驱动
        private float bobPhase;
        private float reactHopTimer;

        /// <summary>业务访客当前同步到的业务状态；-1 = 尚未同步/氛围邻居。</summary>
        private int businessState = -1;

        /// <summary>场景归一化坐标（0~1，相对整幅背景图），供舞台层换算锚点与深度排序。</summary>
        public Vector2 ScenePosition { get; private set; }

        public bool IsInteractable => state == ActorState.Waiting || state == ActorState.Wandering;

        /// <summary>邻居是否还在门口排队（含正走向排队点的路上），供舞台层动态分配队位。</summary>
        public bool IsQueuingAtDoor => ambient && (state == ActorState.Arriving || state == ActorState.Waiting);

        /// <summary>是否已进入离场流程（舞台层判重用）。</summary>
        public bool IsLeaving => state == ActorState.Leaving || state == ActorState.Gone;

        public static OutGameVisitorActor Create(Transform parent, string actorId, string actorName, string sheetBase,
            bool isAmbient, float spawnDelay, Vector2 door, Vector2 wait, Vector2[] wanderPoints,
            Action clicked, Action gone, bool spawnInside = false)
        {
            var awaitMeta = OutGameVisitorSheet.Load(sheetBase + "_await_sheet", out var awaitTex);
            if (awaitMeta == null)
            {
                Debug.LogWarning("[OutGameVisitorActor] 访客序列帧缺失：" + sheetBase);
                return null;
            }
            var rect = F.Rect(parent, "Visitor_" + actorId, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            rect.pivot = new Vector2(.5f, 0f); // 底边中心落在地面坐标上
            var actor = rect.gameObject.AddComponent<OutGameVisitorActor>();
            actor.displayName = actorName;
            actor.ambient = isAmbient;
            actor.awaitTexture = awaitTex;
            actor.awaitSheet = awaitMeta;
            actor.celebrateSheet = OutGameVisitorSheet.Load(sheetBase + "_attack_sheet", out actor.celebrateTexture);
            actor.doorPoint = door;
            actor.waitPoint = wait;
            actor.waypoints = wanderPoints;
            actor.onClicked = clicked;
            actor.onGone = gone;
            actor.spawnInside = spawnInside;
            actor.walkSpeed = .055f * UnityEngine.Random.Range(.85f, 1.15f);
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

            // 情绪/台词气泡（在名牌卡之前创建，悬停时名牌盖在气泡上）
            bubble = OutGameVisitorBubble.Create(transform, new Vector2(30, 24), ProvideEmote);

            // 头顶悬停卡：访客名 + 当前状态
            var card = F.Panel(transform, "Card", new Vector2(.5f, 1), new Vector2(.5f, 1),
                new Vector2(0, 52), new Vector2(240, 68), new Color(.32f, .06f, .18f, .92f));
            F.Outline(card.gameObject, new Color(.85f, .15f, .45f, .5f), new Vector2(1, -1));
            cardLabel = F.Label(card.transform, "Text", "", 17, F.White, TextAnchor.MiddleCenter, FontStyle.Bold);
            cardGroup = F.Group(card.gameObject, 0f);
            cardGroup.blocksRaycasts = false;
            cardGroup.interactable = false;

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

        /// <summary>舞台层动态分配的门口排队点（队伍前移时演员会挪过去）。</summary>
        public void SetWaitPoint(Vector2 point)
        {
            if (!IsQueuingAtDoor) return;
            if ((waitPoint - point).sqrMagnitude < 1e-6f) return;
            waitPoint = point;
            moveTarget = point;
            moving = true;
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
                case EVisitorState.Serving:
                    EnterWandering(.2f); // 接待成功：走进屋内（单房间阶段以游走区代表房间，§9）
                    break;
                case EVisitorState.Wandering:
                    if (first) EnterWandering(.3f); // 重建舞台时已在闲逛：直接游走，不补庆祝
                    else Celebrate();              // 完成服务：庆祝一次，然后继续闲逛
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

        /// <summary>闲逛台词冒泡（§9：扩展既有气泡显示句子，不新建第二套气泡）。</summary>
        public void ShowLine(string text, float holdSeconds)
        {
            if (bubble != null && !string.IsNullOrEmpty(text))
                bubble.ShowSentence(text, holdSeconds);
        }

        /// <summary>完成服务：播放一次庆祝动作，然后继续游走。</summary>
        private void Celebrate()
        {
            moving = false;
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
                        state = ActorState.Waiting;
                        if (ambient) patienceTimer = UnityEngine.Random.Range(45f, 75f);
                        UpdateStatusCard();
                    }
                    break;
                case ActorState.Waiting:
                    if (moving && MoveTowards(dt)) moving = false; // 队伍前移
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
                            moving = false;
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
            moving = true;
            group.DOFade(1f, .4f).SetTarget(this).SetUpdate(true);
            UpdateStatusCard();
        }

        /// <summary>常驻回填：重建舞台时访客已在场，按业务状态直接落位淡入（前台位或屋内游走点）。</summary>
        private void BeginInside()
        {
            group.DOFade(1f, .4f).SetTarget(this).SetUpdate(true);
            if (!ambient && businessState == (int)EVisitorState.FrontDesk)
            {
                ScenePosition = waitPoint;
                state = ActorState.Waiting;
                UpdateStatusCard();
                return;
            }
            ScenePosition = waypoints != null && waypoints.Length > 0
                ? waypoints[UnityEngine.Random.Range(0, waypoints.Length)]
                : waitPoint;
            EnterWandering(UnityEngine.Random.Range(.5f, 3f));
        }

        private void EnterWandering(float idleDelay)
        {
            state = ActorState.Wandering;
            moving = false;
            stateTimer = idleDelay;
            UpdateStatusCard();
        }

        private void EnterLeaving()
        {
            state = ActorState.Leaving;
            moveTarget = doorPoint;
            moving = true;
            UpdateStatusCard();
        }

        private void PickNextWaypoint()
        {
            if (waypoints == null || waypoints.Length == 0) return;
            // 随机挑一个与当前位置有一定距离的落点，避免原地抖动
            for (var attempt = 0; attempt < 6; attempt++)
            {
                var candidate = waypoints[UnityEngine.Random.Range(0, waypoints.Length)];
                if ((candidate - ScenePosition).sqrMagnitude < .01f) continue;
                moveTarget = candidate;
                moving = true;
                return;
            }
            stateTimer = 2f;
        }

        /// <summary>点击游走中的邻居：跳一下逗个乐，有攻击表的顺便做一次动作（状态切换演示）。</summary>
        private void PokeReaction()
        {
            if (state != ActorState.Wandering && state != ActorState.Waiting) return;
            moving = false;
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

        /// <summary>朝 moveTarget 匀速移动一帧；到达返回 true。速度随深度缩放，远处走得慢，近大远小不穿帮。</summary>
        private bool MoveTowards(float dt)
        {
            var delta = moveTarget - ScenePosition;
            if (Mathf.Abs(delta.x) > .003f) facingRight = delta.x > 0f;
            var step = walkSpeed * Mathf.Lerp(1f, .55f, DepthT()) * dt;
            if (delta.magnitude <= step)
            {
                ScenePosition = moveTarget;
                return true;
            }
            ScenePosition += delta.normalized * step;
            return false;
        }

        private float DepthT() => Mathf.InverseLerp(NearY, FarY, ScenePosition.y);

        /// <summary>按深度更新显示尺寸与左右翻转（素材默认朝左，向右走时镜像）。</summary>
        private void ApplyDepth()
        {
            var scale = Mathf.Lerp(1f, FarScale, DepthT());
            var height = BaseHeight * scale;
            var rect = (RectTransform)transform;
            rect.sizeDelta = new Vector2(height * animator.CurrentAspect, height);
            if (spriteRect != null)
                spriteRect.localScale = new Vector3(facingRight ? -1f : 1f, 1f, 1f);
        }

        /// <summary>行走时的小幅跳动（素材没有走路动画，用节奏跳动代替步态）；被逗时原地跳一下。</summary>
        private void ApplyBobbing(float dt)
        {
            if (spriteRect == null) return;
            if (reactHopTimer > 0f)
            {
                reactHopTimer -= dt;
                spriteRect.anchoredPosition = new Vector2(0f, Mathf.Abs(Mathf.Sin(reactHopTimer * 11f)) * 16f);
                return;
            }
            if (moving && state != ActorState.Hidden)
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

        /// <summary>情绪气泡内容：随状态给出随机符号，返回空串表示当前不冒泡。</summary>
        private string ProvideEmote()
        {
            string[] pool;
            if (ambient)
            {
                pool = state switch
                {
                    ActorState.Waiting => new[] { "？", "！", "…" },
                    ActorState.Wandering => new[] { "♪", "…", "★" },
                    ActorState.Leaving => new[] { "…" },
                    _ => null,
                };
            }
            else
            {
                pool = businessState switch
                {
                    (int)EVisitorState.FrontDesk => new[] { "？", "…" },
                    (int)EVisitorState.Serving => new[] { "！", "…" },
                    (int)EVisitorState.Wandering => new[] { "♥", "♪", "★" },
                    _ => state == ActorState.Leaving ? new[] { "…" } : null,
                };
            }
            if (pool == null || choiceOpen || state == ActorState.Hidden) return "";
            return pool[UnityEngine.Random.Range(0, pool.Length)];
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
                status = businessState switch
                {
                    (int)EVisitorState.FrontDesk => "在前台等待接待 · 点击交谈",
                    (int)EVisitorState.Serving => "等待服务 · 点击递上物品",
                    (int)EVisitorState.Wandering => "心满意足 · 屋内闲逛中",
                    _ => "刚刚进门",
                };
            }
            cardLabel.text = displayName + "\n<size=13>" + status + "</size>";
        }
    }
}

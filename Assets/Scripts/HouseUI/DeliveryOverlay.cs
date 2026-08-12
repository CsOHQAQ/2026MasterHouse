using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 需求交付页（交付页落地说明全文）：给「服务中」的访客做的交付界面——
    /// 左访客、中交付框、右仓库；玩家从仓库拖一件物品到交付框，访客即时给出预览反应，
    /// 再决定确认交付 / 拒绝交付 / 稍后再说。
    ///
    /// 三条不变量，改动时别破坏：
    ///   ①**拖拽不预扣库存**：库存只在 VisitorManager.Submit 里扣，所以「稍后再说」无需任何回滚；
    ///   ②**交付框是纯 UI 临时状态**：不写进 VisitorInstance、不进存档，重进页面就是空的；
    ///   ③**预览与结算共用一份判定**：档位一律走 VisitorManager.Preview（内部与 Submit 同一个 Evaluate），
    ///     本类不得自己算分——预览显示「完美」交出去变「满意」是最难查的那种 bug（§5.1）。
    ///
    /// 页面是叠加层而不是整页路由：ESC / 点遮罩 =「稍后再说」，走壳的统一退栈（§3）。
    /// 打烊后本页照常可用——玩家清场时需要它（§6）。
    /// </summary>
    public sealed class DeliveryOverlay : IHouseOverlay
    {
        /// <summary>预览档位 → 立绘差分（2026-08-12 访谈定：文档只说「换表情」，映射在此写死）。</summary>
        private static readonly EDialogueEmotion[] EmotionBySatisfaction =
        {
            EDialogueEmotion.Sad,       // 不对味
            EDialogueEmotion.Confused,  // 一般
            EDialogueEmotion.Happy,     // 满意
            EDialogueEmotion.Surprised, // 完美
        };

        private readonly HouseUIManager ui;
        private readonly RectTransform root;
        private readonly DeliveryPageView view;
        private readonly int instanceId;
        private readonly Camera uiCamera;

        private readonly List<KeyValuePair<ItemDef, long>> snapshot = new List<KeyValuePair<ItemDef, long>>();
        private readonly List<DeliveryItemView> rows = new List<DeliveryItemView>();
        private readonly List<ItemDef> rowItems = new List<ItemDef>();

        /// <summary>交付框里的候选物品；null = 空框（§4：纯 UI 状态，不落地）。</summary>
        private ItemDef candidate;

        private Sequence bubbleSequence;
        private bool closing;

        private DeliveryOverlay(HouseUIManager ui, RectTransform root, DeliveryPageView view, int instanceId)
        {
            this.ui = ui;
            this.root = root;
            this.view = view;
            this.instanceId = instanceId;
            var canvas = ui.Canvas;
            uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        }

        private static VisitorManager Visitors => GameManager.Instance.VisitorManager;

        // ══════════ 开关 ══════════

        /// <summary>
        /// 打开交付页（由 HubPage.SelectGuest 在访客处于「服务中」时调用）。
        /// 访客不在服务中就不该开——那时该走的是【初次见面】对话。
        /// </summary>
        public static void Open(HouseUIManager ui, int instanceId)
        {
            var instance = Visitors.Find(instanceId);
            if (instance == null || instance.State != EVisitorState.Serving)
            {
                Debug.LogWarning($"[HouseUI] 交付页只对「服务中」的访客开放（实例 {instanceId}），本次请求已忽略");
                return;
            }

            var prefab = Resources.Load<GameObject>(OutGamePrefabResourcePaths.DeliveryPage);
            if (prefab == null)
            {
                Debug.LogError("[HouseUI] 交付页 Prefab 缺失，无法打开（§16.2 不回退代码布局）：" +
                               OutGamePrefabResourcePaths.DeliveryPage);
                return;
            }
            var instanceGo = Object.Instantiate(prefab, ui.PageRoot, false);
            instanceGo.name = "DeliveryLayer";
            var view = instanceGo.GetComponent<DeliveryPageView>();
            if (view == null || view.dropZone == null || view.cargoContent == null || view.dragLayer == null)
            {
                Debug.LogError("[HouseUI] 交付页 Prefab 缺少视图组件或槽位不完整：DeliveryPageView" +
                               "（旧结构 Prefab 请删除后由生成器重建）");
                Object.Destroy(instanceGo);
                return;
            }

            var rect = (RectTransform)instanceGo.transform;
            rect.SetAsLastSibling();
            var overlay = new DeliveryOverlay(ui, rect, view, instanceId);

            // 闸门（§5.3）：挑物品期间场上不该继续掉时间——尤其「等交货超时」正是这位访客自己的倒计时
            GameManager.Instance.HouseClockManager.SetStopReason(EClockStopReason.DeliveryPage, true);

            // 先压栈再绑定：绑定过程中任何一条「访客没了就关页」的退路都要能弹到自己，
            // 而不是把别人的叠加层弹掉
            ui.PushOverlay(overlay);
            overlay.BindStatic(instance);
            overlay.BuildCargoList();
            overlay.RefreshCandidate();
            HouseUIUtil.ApplyFallbackFont(instanceGo.transform);
            overlay.AnimateIn();
        }

        /// <summary>
        /// 由壳在弹栈时调用。三条来路（ESC / 点遮罩 / 稍后再说）都是「什么都不发生」：
        /// 库存没动过、访客仍在「服务中」、可以再次进入。
        /// 确认交付与拒绝交付也经此路关闭，但它们在弹栈**之后**才调业务（见 ConfirmDelivery / RejectDelivery）。
        /// </summary>
        public void Close()
        {
            if (closing) return;
            closing = true;
            bubbleSequence?.Kill();
            // 退出应用时各常驻对象的销毁顺序不确定，GameManager 可能先没
            if (GameManager.Instance != null)
                GameManager.Instance.HouseClockManager.SetStopReason(EClockStopReason.DeliveryPage, false);
            FadeOutAndDestroy();
        }

        // ══════════ 绑定 ══════════

        /// <summary>与交付框内容无关的静态部分（只在打开时做一次）。</summary>
        private void BindStatic(VisitorInstance instance)
        {
            if (view.scrimButton != null)
            {
                // 遮罩直接绑，不走 HouseUIUtil.BindButton——那个会挂 hover 缩放动效，
                // 整屏遮罩跟着鼠标缩放是纯噪声
                view.scrimButton.onClick.RemoveAllListeners();
                view.scrimButton.onClick.AddListener(ui.PopOverlay);
            }
            if (view.laterButton != null) HouseUIUtil.BindButton(view.laterButton, ui.PopOverlay);
            if (view.laterLabel != null) view.laterLabel.text = "稍后再说";
            if (view.confirmButton != null) HouseUIUtil.BindButton(view.confirmButton, ConfirmDelivery);
            if (view.confirmLabel != null) view.confirmLabel.text = "确认交付";
            if (view.rejectButton != null) HouseUIUtil.BindButton(view.rejectButton, RejectDelivery);
            if (view.rejectLabel != null)
                // 拒绝代价直接写在按钮上（§3.1），所以不做二次确认（§7 待确认默认值）。
                // 取的是**服务中档**——这位访客已经被接待过了（§5.2）
                view.rejectLabel.text = $"拒绝交付　声望 -{GameManager.Instance.EconomyManager.ServiceFailedReputationPenalty}";

            if (view.guestName != null) view.guestName.text = instance.DisplayName;
            if (view.needSentence != null)
                // 程序化需求句走 INeedPhraseBuilder（与对话里的 {需求} 同一个组装器，§3.1）。
                // 只说自然语言——**不显示 tag 列表与必要项标记**，「哪一项更重要」靠预览反应去试（§3.1 有意的信息设计）
                view.needSentence.text = instance.BuildNeedSentence();
            if (view.bubbleGroup != null) view.bubbleGroup.alpha = 0f;
        }

        /// <summary>
        /// 建仓库列表（模板 Prefab + 运行时实例化，§16.2）。只列数量 > 0 的条目，
        /// 顺序取 GetSnapshot 的稳定序（按资产名，§11.2）。
        ///
        /// 只在打开时建一次即可：页面开着的时候闸门关着，局内产线不产出、库存不会变（§5.3）。
        /// </summary>
        private void BuildCargoList()
        {
            var template = Resources.Load<GameObject>(OutGamePrefabResourcePaths.DeliveryItemRow);
            if (template == null)
            {
                Debug.LogError("[HouseUI] 仓库条目模板缺失（§16.2）：" + OutGamePrefabResourcePaths.DeliveryItemRow);
                return;
            }

            GameManager.Instance.PlayerCargo.GetSnapshot(snapshot);
            rows.Clear();
            rowItems.Clear();
            foreach (var pair in snapshot)
            {
                if (pair.Key == null || pair.Value <= 0) continue; // 数量为 0 的条目不出现（验收清单）
                var rowGo = Object.Instantiate(template, view.cargoContent, false);
                var row = rowGo.GetComponent<DeliveryItemView>();
                if (row == null)
                {
                    Object.Destroy(rowGo);
                    continue;
                }
                BindRow(row, pair.Key, pair.Value);
                rows.Add(row);
                rowItems.Add(pair.Key);
            }

            if (view.cargoEmptyLabel != null) view.cargoEmptyLabel.gameObject.SetActive(rows.Count == 0);
            if (view.cargoScroll != null) view.cargoScroll.verticalNormalizedPosition = 1f;
        }

        private void BindRow(DeliveryItemView row, ItemDef item, long count)
        {
            if (row.itemName != null) row.itemName.text = item.DisplayName;
            if (row.count != null) row.count.text = "×" + count;
            ApplyItemIcon(row.icon, item);

            var drag = row.GetComponent<DeliveryDragSource>();
            if (drag == null) drag = row.gameObject.AddComponent<DeliveryDragSource>();
            drag.Bind(view.dragLayer, view.dropZone, uiCamera, () => PutIntoBox(item));
        }

        /// <summary>
        /// 物品图标：ItemDef.icon 为空时按 DisplayColor 画占位色块。
        /// 缺图**不报错**——美术尚未接入是明示的过渡态，不是配置事故（2026-08-12 访谈）。
        /// </summary>
        private static void ApplyItemIcon(Image target, ItemDef item)
        {
            if (target == null) return;
            if (item != null && item.icon != null)
            {
                target.sprite = item.icon;
                target.color = Color.white;
                target.preserveAspect = true;
                return;
            }
            target.sprite = null;
            target.color = item != null ? item.DisplayColor : Color.gray;
        }

        // ══════════ 交付框 ══════════

        /// <summary>
        /// 放入交付框。已有物品则替换——原物品「回到列表」是空操作，
        /// 因为库存本就没扣，列表始终显示完整库存（§4）。
        /// </summary>
        private void PutIntoBox(ItemDef item)
        {
            if (closing || item == null) return;
            candidate = item;
            RefreshCandidate();
        }

        /// <summary>按交付框当前内容刷新：立绘表情、页内气泡、奖励预期、确认按钮可用性、列表选中态。</summary>
        private void RefreshCandidate()
        {
            var instance = Visitors.Find(instanceId);
            if (instance == null)
            {
                // 理论上不可达（页面开着时闸门关着，访客不会超时离场），但状态机的手不止一只
                ui.PopOverlay();
                return;
            }

            var hasItem = candidate != null;
            var satisfaction = hasItem ? Visitors.Preview(instanceId, candidate) : EServeSatisfaction.Mismatch;

            // 交付框
            if (view.dropItemName != null) view.dropItemName.text = hasItem ? candidate.DisplayName : string.Empty;
            if (view.dropItemIcon != null)
            {
                view.dropItemIcon.gameObject.SetActive(hasItem);
                if (hasItem) ApplyItemIcon(view.dropItemIcon, candidate);
            }
            if (view.dropHint != null) view.dropHint.gameObject.SetActive(!hasItem);

            // 立绘：空框用默认表情、不播任何预览单句（§7 待确认默认值）
            ApplyPortrait(instance, hasItem ? EmotionBySatisfaction[(int)satisfaction] : EDialogueEmotion.Calm);

            // 奖励预期：档名 + 货币 + 声望；框空时「——」（§3.1）
            if (view.rewardPreview != null)
            {
                if (!hasItem)
                {
                    view.rewardPreview.text = "预期：——";
                }
                else
                {
                    var reward = GameManager.Instance.EconomyManager.RewardFor(satisfaction);
                    view.rewardPreview.text =
                        $"预期：{ServeSatisfactionText.NameOf(satisfaction)} · " +
                        $"<color=#D4A46B>◈+{reward.currency:N0}</color> · " +
                        $"<color=#74D8D1>声望+{reward.reputation}</color>";
                }
            }

            if (view.confirmButton != null) view.confirmButton.interactable = hasItem; // 框空时禁用（§4）

            // 预览单句（绝不结算：池里存的是单句，从类型上就挂不了事件与分支）
            if (hasItem) ShowBubble(GameManager.Instance.DialogueManager.PreviewLine(instance, candidate, satisfaction));
            else HideBubble();

            RefreshRowSelection();
        }

        private void ApplyPortrait(VisitorInstance instance, EDialogueEmotion emotion)
        {
            if (view.portrait == null || instance.Race == null) return;
            // 差分缺失时 GetPortraitPath 内部回落平静并打 Warning，不阻断表现（对话说明 §4.1）
            var path = instance.Race.GetPortraitPath(emotion);
            var texture = string.IsNullOrEmpty(path) ? null : Resources.Load<Texture2D>(path);
            view.portrait.texture = texture;
            view.portrait.gameObject.SetActive(texture != null);
            if (texture == null) return;
            // RawImage 没有保持宽高比的开关：高度用 Prefab 手调值，宽度按贴图真实比例回算（与对话层同一套做法）
            var rect = view.portrait.rectTransform;
            var height = rect.sizeDelta.y;
            rect.sizeDelta = new Vector2(height * texture.width / (float)texture.height, height);
        }

        private void RefreshRowSelection()
        {
            for (var i = 0; i < rows.Count && i < rowItems.Count; i++)
            {
                var row = rows[i];
                if (row == null || row.background == null) continue;
                var selected = candidate != null && rowItems[i] == candidate;
                // 只做「这件在框里」的指示，**不置灰**——库存没扣，暗示它被消耗掉是误导
                row.background.color = selected ? HouseUIUtil.Hex("E22D76", .35f) : new Color(1, 1, 1, .06f);
            }
        }

        // ══════════ 页内气泡（§7 待确认默认值：立绘下方，停留时长复用气泡时长）══════════

        private void ShowBubble(string text)
        {
            if (view.bubbleGroup == null || view.bubbleText == null) return;
            if (string.IsNullOrEmpty(text))
            {
                HideBubble();
                return;
            }
            view.bubbleText.text = text;
            bubbleSequence?.Kill();
            bubbleSequence = DOTween.Sequence().SetTarget(view.bubbleGroup).SetUpdate(true)
                .Append(view.bubbleGroup.DOFade(1f, .18f))
                .AppendInterval(BubbleHoldSeconds)
                .Append(view.bubbleGroup.DOFade(0f, .3f));
        }

        private void HideBubble()
        {
            if (view.bubbleGroup == null) return;
            bubbleSequence?.Kill();
            view.bubbleGroup.alpha = 0f;
        }

        /// <summary>
        /// 气泡停留秒数：复用 VisitorTuningConfig 的气泡时长（tick）÷ tick 频率。
        /// 业务参数以 tick 配（§11.3），表现层换算成秒——本页气泡是表现件，用秒计时不违反守则。
        /// </summary>
        private static float BubbleHoldSeconds
        {
            get
            {
                var tuning = GameManager.Instance.VisitorTuning;
                var ticks = tuning != null ? tuning.bubbleHoldTicks : 40;
                var config = GameConfig.Instance;
                var perSecond = config != null ? Mathf.Max(1, config.TicksPerSecond) : 10;
                return Mathf.Max(1f, ticks / (float)perSecond);
            }
        }

        // ══════════ 三个出口（§4）══════════

        /// <summary>
        /// 确认交付。**先弹栈关页、再调业务**，顺序是必须的：
        ///   ①Submit 内部会同步请求【完成服务】对话并压栈，若先 Submit，栈顶就成了对话层，
        ///     随后的 PopOverlay 会把对话框弹掉而不是交付页；
        ///   ②闸门交接（§5.3）由「同一调用栈内完成」保证——本页闸门刚落、对话闸门就起，
        ///     中间插不进任何 tick（tick 只在 GameManager.Update 里推进），不会有「闸门全开」的一瞬。
        /// </summary>
        private void ConfirmDelivery()
        {
            if (closing || candidate == null) return;
            var item = candidate;
            ui.PopOverlay();
            // Submit 内部：扣库存 → 评分（与预览同一个 Evaluate）→ 经济结算 → 请求【完成服务】对话
            //             → 不对味直接离场，其余三档转闲逛
            if (!Visitors.Submit(instanceId, item))
                Debug.LogWarning($"[HouseUI] 交付未生效：实例 {instanceId} 不在「服务中」，或仓库里没有「{item.DisplayName}」");
        }

        /// <summary>拒绝交付：按**服务中档**扣声望（比前台谢客更重，§5.2），走【被拒绝】对话后离场。</summary>
        private void RejectDelivery()
        {
            if (closing) return;
            ui.PopOverlay(); // 同 ConfirmDelivery：先关页再调业务
            if (!Visitors.Reject(instanceId))
                Debug.LogWarning($"[HouseUI] 拒绝未生效：实例 {instanceId} 不在可拒绝状态");
        }

        // ══════════ 动效 ══════════

        private void AnimateIn()
        {
            var group = HouseUIUtil.Group(root.gameObject, 0);
            group.DOFade(1, .25f).SetUpdate(true);
            if (view.dropZone != null)
            {
                var resting = view.dropZone.anchoredPosition;
                view.dropZone.anchoredPosition = resting + new Vector2(0, -30);
                view.dropZone.DOAnchorPos(resting, .38f).SetEase(Ease.OutCubic).SetUpdate(true);
            }
            if (view.portrait != null)
            {
                var resting = view.portrait.rectTransform.anchoredPosition;
                view.portrait.rectTransform.anchoredPosition = resting + new Vector2(-80, 0);
                view.portrait.rectTransform.DOAnchorPos(resting, .42f).SetEase(Ease.OutCubic).SetUpdate(true);
            }
        }

        private void FadeOutAndDestroy()
        {
            if (root == null) return;
            var group = HouseUIUtil.Group(root.gameObject);
            group.blocksRaycasts = false;
            group.DOFade(0, .2f).SetUpdate(true).OnComplete(() =>
            {
                if (root == null) return;
                HouseUIUtil.KillTweensUnder(root);
                Object.Destroy(root.gameObject);
            });
        }
    }
}

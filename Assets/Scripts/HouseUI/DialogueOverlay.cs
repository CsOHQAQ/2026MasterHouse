using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 访客事务对话层（叠加层）。**对话系统未落地期间的临时 debug 驱动层（访客交付说明 §8 明示许可）**：
    /// 台词经对话接缝取单句，三个业务动作（接待/拒绝/提交物品）直接调 VisitorManager 的公开方法，
    /// 用于验证访客状态机；对话系统接入时删除本层的 debug 交互、保留壳与 Prefab。
    /// 按访客状态重绑内容：前台等待 → 初次见面 + 接待/拒绝；服务中 → 需求句 + 仓库物品提交栏 + 拒绝。
    /// Prefab 缺失是报错（§16.2）；底部原「结束本周」按钮改绑「结束今天」（周制退役，§10）。
    /// </summary>
    public sealed class DialogueOverlay : IHouseOverlay
    {
        private readonly RectTransform root;
        private bool closing;

        private DialogueOverlay(RectTransform root)
        {
            this.root = root;
        }

        public static void Open(HouseUIManager ui, HubPage page)
        {
            var prefab = Resources.Load<GameObject>(OutGamePrefabResourcePaths.DialogueView);
            if (prefab == null)
            {
                Debug.LogError("[HouseUI] 对话层 Prefab 缺失，无法打开（§16.2 不回退代码布局）：" + OutGamePrefabResourcePaths.DialogueView);
                return;
            }
            var instance = Object.Instantiate(prefab, ui.PageRoot, false);
            instance.name = "DialogueLayer";
            var view = instance.GetComponent<OutGameDialogueView>();
            if (view == null)
            {
                Debug.LogError("[HouseUI] 对话层 Prefab 缺少视图组件：OutGameDialogueView");
                Object.Destroy(instance);
                return;
            }
            var rect = (RectTransform)instance.transform;
            rect.SetAsLastSibling();
            var overlay = new DialogueOverlay(rect);
            BindContent(view, ui, page);
            HouseUIUtil.ApplyFallbackFont(instance.transform);

            // 入场动效：整层淡入 + 立绘/对话框滑入（Tween 目标避开按钮 transform）
            var group = HouseUIUtil.Group(rect.gameObject, 0);
            group.DOFade(1, .28f).SetUpdate(true);
            if (view.characterCard != null)
            {
                view.characterCard.anchoredPosition = new Vector2(330, 40);
                view.characterCard.DOAnchorPosX(390, .55f).SetEase(Ease.OutCubic).SetUpdate(true);
            }
            if (view.dialogueBox != null)
            {
                view.dialogueBox.anchoredPosition = new Vector2(80, 110);
                view.dialogueBox.DOAnchorPosY(190, .5f).SetEase(Ease.OutCubic).SetUpdate(true);
            }
            ui.PushOverlay(overlay);
        }

        public void Close()
        {
            if (closing || root == null) return;
            closing = true;
            var group = HouseUIUtil.Group(root.gameObject);
            group.blocksRaycasts = false;
            group.DOFade(0, .2f).SetUpdate(true).OnComplete(() =>
            {
                if (root == null) return;
                HouseUIUtil.KillTweensUnder(root);
                Object.Destroy(root.gameObject);
            });
        }

        private static void BindContent(OutGameDialogueView view, HouseUIManager ui, HubPage page)
        {
            var gm = GameManager.Instance;
            var visitor = gm.VisitorManager;
            var guest = page.SelectedInstance;

            if (view.sceneArt != null)
            {
                var baked = FurnitureSceneComposer.Current;
                view.sceneArt.texture = baked != null ? (Texture)baked : Resources.Load<Texture2D>("OutGameUI/house-hub-v2");
            }
            if (view.closeButton != null) HouseUIUtil.BindButton(view.closeButton, ui.PopOverlay);

            if (guest == null)
            {
                if (view.dialogueText != null)
                    view.dialogueText.text = "<size=31>暂无访客在场</size>\n\n访客会按日程表在营业时段到访。";
                BindEndDayButton(view, ui, page);
                DisableButton(view.serveButton, view.serveLabel, "暂无访客");
                DisableButton(view.refuseButton, view.refuseLabel, "暂无访客");
                return;
            }

            if (view.portrait != null) view.portrait.texture = Resources.Load<Texture2D>(guest.Race.GetPortraitPath());
            if (view.portraitTag != null)
                view.portraitTag.text = "VISITOR / " + (string.IsNullOrEmpty(guest.Race.raceId) ? "GUEST" : guest.Race.raceId.ToUpperInvariant());

            BindOnStageSwitcher(view, page);

            // 状态相关：台词 + 动作按钮（debug 驱动，§8）
            var stateLine = guest.State switch
            {
                // 初次见面（§8）：玩家交互「前台等待」访客时触发，状态不变
                EVisitorState.FrontDesk => visitor.RequestFirstMeeting(guest.InstanceId),
                EVisitorState.Serving => guest.BuildNeedSentence(),
                _ => "（哼着歌在屋里闲逛……）",
            };
            var stateTag = guest.State switch
            {
                EVisitorState.FrontDesk => "前台等待接待",
                EVisitorState.Serving => "服务中 · 等待物品",
                _ => "闲逛中 · " + ServeSatisfactionText.NameOf(guest.Satisfaction),
            };
            if (view.dialogueText != null)
                view.dialogueText.text = $"<size=15>{stateTag}</size>\n<size=31>{guest.DisplayName}</size>\n\n{stateLine}";

            if (view.needButton != null)
            {
                var needGuest = guest;
                HouseUIUtil.BindButton(view.needButton, () => page.Toast(
                    needGuest.State == EVisitorState.FrontDesk ? "接待后才会说出需求。" : needGuest.BuildNeedSentence()));
            }

            if (view.serveButton != null)
            {
                switch (guest.State)
                {
                    case EVisitorState.FrontDesk:
                        if (view.serveLabel != null) view.serveLabel.text = "接待（开始服务）";
                        HouseUIUtil.BindButton(view.serveButton, page.AcceptSelectedGuest);
                        view.serveButton.interactable = true;
                        break;
                    case EVisitorState.Serving:
                        DisableButton(view.serveButton, view.serveLabel, "从下方选择物品提交 ↓");
                        break;
                    default:
                        DisableButton(view.serveButton, view.serveLabel, "服务已完成");
                        break;
                }
            }
            if (view.refuseButton != null)
            {
                // 拒绝在「前台等待」与「服务中」都可用（§5，打烊后玩家必须能手动清场）
                var canRefuse = guest.State == EVisitorState.FrontDesk || guest.State == EVisitorState.Serving;
                if (view.refuseLabel != null)
                    view.refuseLabel.text = $"拒绝接待 <size=13>声望 -{gm.EconomyManager.RefuseReputationPenalty}</size>";
                HouseUIUtil.BindButton(view.refuseButton, page.RefuseSelectedGuest);
                view.refuseButton.interactable = canRefuse;
            }

            BindCargoSlots(view, page, guest);
            BindEndDayButton(view, ui, page);
        }

        /// <summary>在场访客切换栏（原「本周访客」四槽位，§10 改为「当前在场访客」）。</summary>
        private static void BindOnStageSwitcher(OutGameDialogueView view, HubPage page)
        {
            if (view.weekTitle != null) view.weekTitle.text = "ON STAGE / 在场访客";
            var instances = GameManager.Instance.VisitorManager.Data.Instances;
            var selected = page.SelectedInstance;
            for (var i = 0; i < 4; i++)
            {
                var hasGuest = i < instances.Count;
                var target = hasGuest ? instances[i] : null;
                if (view.weekGuestLabels != null && i < view.weekGuestLabels.Length && view.weekGuestLabels[i] != null)
                    view.weekGuestLabels[i].text = hasGuest
                        ? target.DisplayName + "\n<size=13>" + StateShort(target.State) + "</size>"
                        : "—\n<size=13>空</size>";
                if (view.weekGuestBackgrounds != null && i < view.weekGuestBackgrounds.Length && view.weekGuestBackgrounds[i] != null)
                    view.weekGuestBackgrounds[i].color = hasGuest && selected != null && target.InstanceId == selected.InstanceId
                        ? new Color(.45f, .08f, .28f, .75f)
                        : new Color(1, 1, 1, .035f);
                if (view.weekGuestButtons != null && i < view.weekGuestButtons.Length && view.weekGuestButtons[i] != null)
                {
                    var button = view.weekGuestButtons[i];
                    button.interactable = hasGuest;
                    if (hasGuest)
                    {
                        var instanceId = target.InstanceId;
                        HouseUIUtil.BindButton(button, () => page.SwitchDialogueGuest(instanceId));
                    }
                }
            }
        }

        private static string StateShort(EVisitorState state) => state switch
        {
            EVisitorState.FrontDesk => "前台等待",
            EVisitorState.Serving => "服务中",
            EVisitorState.Wandering => "闲逛中",
            _ => "离开中",
        };

        /// <summary>
        /// 仓库物品提交栏（debug，§8 明示许可）：原「家具快捷栏」五槽位改列 PlayerCargo 存货，
        /// 服务中点击即提交结算（服务一次性、交错照扣，§5）。对话系统接入时删除。
        /// </summary>
        private static void BindCargoSlots(OutGameDialogueView view, HubPage page, VisitorInstance guest)
        {
            if (view.furnitureTitle != null) view.furnitureTitle.text = "CARGO / 提交物品（debug）";
            var snapshot = new List<KeyValuePair<ItemDef, long>>();
            GameManager.Instance.PlayerCargo.GetSnapshot(snapshot);
            var serving = guest.State == EVisitorState.Serving;
            for (var i = 0; i < 5; i++)
            {
                var hasItem = i < snapshot.Count && snapshot[i].Value > 0;
                var item = hasItem ? snapshot[i].Key : null;
                if (view.furnitureLabels != null && i < view.furnitureLabels.Length && view.furnitureLabels[i] != null)
                    view.furnitureLabels[i].text = hasItem ? $"{item.DisplayName} ×{snapshot[i].Value}" : "—";
                if (view.furnitureBackgrounds != null && i < view.furnitureBackgrounds.Length && view.furnitureBackgrounds[i] != null)
                    view.furnitureBackgrounds[i].color = hasItem && serving
                        ? new Color(.48f, .08f, .28f, .72f)
                        : new Color(1, 1, 1, .035f);
                if (view.furnitureButtons != null && i < view.furnitureButtons.Length && view.furnitureButtons[i] != null)
                {
                    var button = view.furnitureButtons[i];
                    button.interactable = hasItem && serving;
                    if (hasItem && serving)
                    {
                        var chosen = item;
                        HouseUIUtil.BindButton(button, () => page.SubmitItemToSelectedGuest(chosen));
                    }
                }
            }
        }

        /// <summary>「结束今天」入口（§7 日结；周制退役后原「结束本周」按钮改绑）。可用性校验在 HubPage.TryEndDay。</summary>
        private static void BindEndDayButton(OutGameDialogueView view, HouseUIManager ui, HubPage page)
        {
            if (view.endWeekButton == null) return;
            var label = view.endWeekButton.GetComponentInChildren<Text>();
            if (label != null) label.text = "结束今天 →";
            HouseUIUtil.BindButton(view.endWeekButton, () =>
            {
                ui.PopOverlay(); // 先关对话层，避免日结后残留过期的访客状态
                page.TryEndDay();
            });
        }

        private static void DisableButton(Button button, Text label, string text)
        {
            if (button == null) return;
            if (label != null) label.text = text;
            button.onClick.RemoveAllListeners();
            button.interactable = false;
        }
    }
}

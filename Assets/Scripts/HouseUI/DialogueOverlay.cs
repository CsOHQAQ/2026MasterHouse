using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 访客事务对话层（叠加层）：台词读 VisitorDef.transactionLine——这里就是 3.7 对话接缝的落点，
    /// 自研对话系统进场时替换「取内容/走流程」的实现，不动访客业务与本层壳。
    /// 服务/拒绝/周结算动作转发给 HubPage（业务在 VisitorManager）。Prefab 缺失是报错（§16.2）。
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
            var visitors = GameManager.Instance.VisitorTable.visitors;
            var states = GameManager.Instance.VisitorManager.Data.States;
            var guest = visitors[page.GuestIndex];

            if (view.sceneArt != null)
            {
                var baked = FurnitureSceneComposer.Current;
                view.sceneArt.texture = baked != null ? (Texture)baked : Resources.Load<Texture2D>("OutGameUI/house-hub-v2");
            }
            if (view.closeButton != null) HouseUIUtil.BindButton(view.closeButton, ui.PopOverlay);
            if (view.portrait != null) view.portrait.texture = Resources.Load<Texture2D>(guest.portraitPath);
            if (view.portraitTag != null)
                view.portraitTag.text = "VISITOR / " + (guest.special ? "SPECIAL" : "WEEK 01");

            for (var i = 0; i < visitors.Count; i++)
            {
                var index = i;
                var item = visitors[i];
                if (view.weekGuestLabels != null && i < view.weekGuestLabels.Length && view.weekGuestLabels[i] != null)
                    view.weekGuestLabels[i].text = item.displayName + "\n<size=13>" + (item.special ? "特殊事件 · 可打断" : "一般事件 · 无先后") + "</size>";
                if (view.weekGuestBackgrounds != null && i < view.weekGuestBackgrounds.Length && view.weekGuestBackgrounds[i] != null)
                    view.weekGuestBackgrounds[i].color = i == page.GuestIndex ? new Color(.45f, .08f, .28f, .75f) : new Color(1, 1, 1, .035f);
                if (view.weekGuestButtons != null && i < view.weekGuestButtons.Length && view.weekGuestButtons[i] != null)
                    HouseUIUtil.BindButton(view.weekGuestButtons[i], () => page.SwitchDialogueGuest(index));
            }

            if (view.dialogueText != null)
                view.dialogueText.text = $"<size=15>{guest.type}{(guest.special ? " · 硬植入事件" : " · 无接待顺序")}</size>\n<size=31>{guest.displayName}</size>     <size=15>信赖 {guest.affinity}%</size>\n\n{guest.transactionLine}";
            if (view.needButton != null)
                HouseUIUtil.BindButton(view.needButton, () =>
                {
                    ui.PopOverlay();
                    PanelHost.Open(ui, page, EHousePanel.Archive);
                });

            var served = states[page.GuestIndex].Served;
            if (view.serveButton != null)
            {
                if (view.serveLabel != null) view.serveLabel.text = served ? "事件已完成" : "回应访客事件";
                HouseUIUtil.BindButton(view.serveButton, page.ServeSelectedGuest);
                view.serveButton.interactable = !served;
            }
            if (view.refuseButton != null)
            {
                if (view.refuseLabel != null)
                    view.refuseLabel.text = $"拒绝接待 <size=13>声望 -{GameManager.Instance.EconomyManager.RefuseReputationPenalty}</size>";
                HouseUIUtil.BindButton(view.refuseButton, page.RefuseSelectedGuest);
                view.refuseButton.interactable = !served;
            }

            // 家具快捷栏：与真实家具布局无数据联系（纯表现，旧壳已知状态），内容读档案 Def
            var furnitureArchives = new List<CodexEntryDef>();
            GameManager.Instance.CodexTable.GetArchives(ECodexArchiveCategory.NarrativeFurniture, furnitureArchives);
            for (var i = 0; i < furnitureArchives.Count && i < 5; i++)
            {
                var item = furnitureArchives[i];
                if (view.furnitureLabels != null && i < view.furnitureLabels.Length && view.furnitureLabels[i] != null)
                    view.furnitureLabels[i].text = item.displayName;
                if (view.furnitureBackgrounds != null && i < view.furnitureBackgrounds.Length && view.furnitureBackgrounds[i] != null)
                    view.furnitureBackgrounds[i].color = HubPage.PlacedFurnitureId == item.id
                        ? new Color(.48f, .08f, .28f, .72f)
                        : new Color(1, 1, 1, .035f);
                if (view.furnitureButtons != null && i < view.furnitureButtons.Length && view.furnitureButtons[i] != null)
                {
                    var itemId = item.id;
                    var itemName = item.displayName;
                    var backgrounds = view.furnitureBackgrounds;
                    HouseUIUtil.BindButton(view.furnitureButtons[i], () =>
                    {
                        HubPage.PlacedFurnitureId = itemId;
                        for (var j = 0; j < furnitureArchives.Count && j < backgrounds.Length; j++)
                            if (backgrounds[j] != null)
                                backgrounds[j].color = furnitureArchives[j].id == itemId
                                    ? new Color(.48f, .08f, .28f, .72f)
                                    : new Color(1, 1, 1, .035f);
                        page.Toast("已摆放：" + itemName);
                    });
                }
            }
            if (view.endWeekButton != null) HouseUIUtil.BindButton(view.endWeekButton, page.EndWeek);
        }
    }
}

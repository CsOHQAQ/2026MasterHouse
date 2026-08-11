using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 访客事务对话层（GVN/视觉小说式，布局与美术烘在 DialogueView Prefab，§16.2）。
    /// **对话系统未落地期间的临时 debug 驱动层（访客交付说明 §8 明示许可）**：
    /// 台词经对话接缝取单句；右侧选项列按访客状态生成——接待/拒绝/提交物品/结束今天，
    /// 直接调 VisitorManager 的公开方法验证状态机；对话系统接入时替换选项生成与台词流程，保留壳与 Prefab。
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
                Debug.LogError("[HouseUI] 对话层 Prefab 缺少视图组件：OutGameDialogueView（旧版 Prefab 请删除后由生成器重建）");
                Object.Destroy(instance);
                return;
            }
            var rect = (RectTransform)instance.transform;
            rect.SetAsLastSibling();
            var overlay = new DialogueOverlay(rect);
            BindContent(view, ui, page);
            HouseUIUtil.ApplyFallbackFont(instance.transform);

            // 入场动效：整层淡入 + 对话条上滑 + 立绘左滑（Tween 目标避开按钮 transform）
            var group = HouseUIUtil.Group(rect.gameObject, 0);
            group.DOFade(1, .28f).SetUpdate(true);
            if (view.dialogueBar != null)
            {
                var resting = view.dialogueBar.anchoredPosition;
                view.dialogueBar.anchoredPosition = resting + new Vector2(0, -70);
                view.dialogueBar.DOAnchorPos(resting, .45f).SetEase(Ease.OutCubic).SetUpdate(true);
            }
            if (view.portrait != null)
            {
                var resting = view.portrait.rectTransform.anchoredPosition;
                view.portrait.rectTransform.anchoredPosition = resting + new Vector2(-90, 0);
                view.portrait.rectTransform.DOAnchorPos(resting, .5f).SetEase(Ease.OutCubic).SetUpdate(true);
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

            // 场景/立绘/GUEST 标题为 Prefab 烘焙的静态美术；此处只绑文本与选项
            if (view.closeButton != null) HouseUIUtil.BindButton(view.closeButton, ui.PopOverlay);

            string speaker;
            string line;
            if (guest == null)
            {
                speaker = string.Empty;
                line = "现在没有访客在场。访客会按日程表在营业时段到访。";
            }
            else
            {
                speaker = guest.DisplayName;
                line = guest.State switch
                {
                    // 初次见面（§8）：玩家交互「前台等待」访客时触发，状态不变
                    EVisitorState.FrontDesk => visitor.RequestFirstMeeting(guest.InstanceId),
                    EVisitorState.Serving => guest.BuildNeedSentence(),
                    _ => "（哼着歌在屋里闲逛……）",
                };
            }
            if (view.speakerName != null) view.speakerName.text = speaker;
            if (view.dialogueText != null) view.dialogueText.text = line;
            if (view.guestTitle != null) view.guestTitle.text = "GUEST";
            if (view.escHint != null) view.escHint.text = "ESC  返回";

            BindOptions(view, page, guest);
        }

        /// <summary>
        /// 右侧选项列（debug 驱动，§8）：按访客状态生成——
        /// 前台等待：接待 / 查看需求 / 拒绝；服务中：递上仓库物品（前 4 项）/ 拒绝。
        /// </summary>
        private static void BindOptions(OutGameDialogueView view, HubPage page, VisitorInstance guest)
        {
            var gm = GameManager.Instance;
            var options = new List<(string label, UnityAction action)>();

            if (guest != null && guest.State == EVisitorState.FrontDesk)
            {
                options.Add(("接待（开始服务）", page.AcceptSelectedGuest));
                options.Add(("查看需求家具", () => page.Toast("接待后才会说出需求。")));
                options.Add(($"拒绝接待  声望 -{gm.EconomyManager.RefuseReputationPenalty}", page.RefuseSelectedGuest));
            }
            else if (guest != null && guest.State == EVisitorState.Serving)
            {
                // 仓库物品提交（debug：取存量前 4 项；服务一次性、交错照扣，§5）
                var snapshot = new List<KeyValuePair<ItemDef, long>>();
                gm.PlayerCargo.GetSnapshot(snapshot);
                var listed = 0;
                foreach (var pair in snapshot)
                {
                    if (pair.Value <= 0 || listed >= 4) continue;
                    var item = pair.Key;
                    options.Add(($"递上「{item.DisplayName}」×{pair.Value}", () => page.SubmitItemToSelectedGuest(item)));
                    listed++;
                }
                if (listed == 0)
                    options.Add(("（仓库里没有可递上的物品）", () => page.Toast("仓库是空的——GM 面板（F1）可注入物资")));
                options.Add(($"拒绝接待  声望 -{gm.EconomyManager.RefuseReputationPenalty}", page.RefuseSelectedGuest));
            }
            // 「结束今天」不进对话选项：日结入口在 Hub 右侧 dock（对话里出现打断演出且语义不属于访客交谈）

            for (var i = 0; i < view.optionButtons.Length; i++)
            {
                var button = view.optionButtons[i];
                if (button == null) continue;
                if (i >= options.Count)
                {
                    button.gameObject.SetActive(false);
                    continue;
                }
                button.gameObject.SetActive(true);
                if (view.optionLabels != null && i < view.optionLabels.Length && view.optionLabels[i] != null)
                    view.optionLabels[i].text = options[i].label;
                HouseUIUtil.BindButton(button, options[i].action);
            }
        }
    }
}

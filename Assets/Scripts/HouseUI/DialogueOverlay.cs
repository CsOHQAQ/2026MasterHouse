using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 访客对话层（GVN/视觉小说式，布局与美术烘在 DialogueView Prefab，§16.2）。
    ///
    /// 本类只做**表现与输入**：把 DialogueManager 的播放状态画出来，把点击翻译成 Advance / ChooseOption /
    /// Interrupt。选哪段对话、执行什么事件、什么时候结束，全在 DialogueManager 里（§11.4 View 只读）。
    ///
    /// 开关由业务事件驱动而不是由玩家点击驱动：
    ///   DialogueManager.PlaybackStarted → HubPage 调 Open；PlaybackEnded → 退栈关闭。
    /// 这样「接待 → 自动接上开始等待服务对话」这类连播不需要 UI 侧知情。
    ///
    /// 旧版这里是访客状态机的临时 debug 驱动层（按状态硬生成接待/拒绝/递物品按钮，
    /// 访客交付说明 §8 明示许可），已随本次落地删除。
    /// </summary>
    public sealed class DialogueOverlay : IHouseOverlay
    {
        /// <summary>当前打开的对话层；null = 没开。用于避免连播时重复实例化。</summary>
        private static DialogueOverlay current;

        /// <summary>说话人名配色（§4.1 三种样式的区分之一）。访客沿用 Prefab 原色。</summary>
        private static readonly Color VisitorNameColor = HouseUIUtil.Hex("E22D76");
        private static readonly Color PlayerNameColor = HouseUIUtil.Hex("74D8D1");

        private readonly HouseUIManager ui;
        private readonly RectTransform root;
        private readonly OutGameDialogueView view;
        private readonly DialogueTypewriter typewriter;
        private readonly List<DialogueOptionView> optionInstances = new List<DialogueOptionView>();

        private GameObject optionTemplate;
        private bool closing;

        private DialogueOverlay(HouseUIManager ui, RectTransform root, OutGameDialogueView view,
            DialogueTypewriter typewriter)
        {
            this.ui = ui;
            this.root = root;
            this.view = view;
            this.typewriter = typewriter;
        }

        /// <summary>对话层是否已打开。</summary>
        public static bool IsOpen => current != null;

        // ══════════ 开关 ══════════

        /// <summary>打开对话层（由 HubPage 响应 DialogueManager.PlaybackStarted 调用）。已开着则只刷新内容。</summary>
        public static void Open(HouseUIManager ui)
        {
            if (current != null)
            {
                current.Refresh();
                return;
            }

            var prefab = Resources.Load<GameObject>(OutGamePrefabResourcePaths.DialogueView);
            if (prefab == null)
            {
                Debug.LogError("[HouseUI] 对话层 Prefab 缺失，无法打开（§16.2 不回退代码布局）：" +
                               OutGamePrefabResourcePaths.DialogueView);
                return;
            }
            var instance = Object.Instantiate(prefab, ui.PageRoot, false);
            instance.name = "DialogueLayer";
            var view = instance.GetComponent<OutGameDialogueView>();
            if (view == null)
            {
                Debug.LogError("[HouseUI] 对话层 Prefab 缺少视图组件：OutGameDialogueView" +
                               "（旧版 Prefab 请删除后由生成器重建——本次落地改了字段结构）");
                Object.Destroy(instance);
                return;
            }

            var rect = (RectTransform)instance.transform;
            rect.SetAsLastSibling();
            var typewriter = instance.AddComponent<DialogueTypewriter>(); // 非布局件，运行时挂（§16.2 例外说明见该类注释）
            var overlay = new DialogueOverlay(ui, rect, view, typewriter);
            current = overlay;

            overlay.BindStatic();
            overlay.Refresh();
            HouseUIUtil.ApplyFallbackFont(instance.transform);
            overlay.AnimateIn();

            GameManager.Instance.DialogueManager.ContentChanged += overlay.Refresh;
            ui.PushOverlay(overlay);
        }

        /// <summary>播放结束后收框（由 HubPage 响应 DialogueManager.PlaybackEnded 调用）。</summary>
        public static void CloseFromPlaybackEnded()
        {
            // 走正常退栈：此时 DialogueManager.IsPlaying 已是 false，Close() 不会再触发中断补执行
            if (current != null) current.ui.PopOverlay();
        }

        /// <summary>
        /// 由壳在弹栈时调用。两条来路：
        ///   ①玩家按 ESC / 点关闭 → 此时对话还在播 ⇒ 通知 DialogueManager 走中断语义（§5.2）；
        ///   ②播放正常结束后 CloseFromPlaybackEnded 主动退栈 ⇒ 已经不在播，不做中断。
        /// </summary>
        public void Close()
        {
            if (closing) return;
            closing = true;
            if (current == this) current = null;

            var dialogue = GameManager.Instance != null ? GameManager.Instance.DialogueManager : null;
            if (dialogue != null) dialogue.ContentChanged -= Refresh;
            if (typewriter != null) typewriter.Stop();

            FadeOutAndDestroy();

            // 放在销毁之后：中断可能立刻带出队列里的下一段对话（PlaybackStarted → 重新 Open 一个新实例），
            // 先把自己摘干净再通知，避免新旧两层互相干扰
            if (dialogue != null && dialogue.IsPlaying) dialogue.Interrupt();
        }

        // ══════════ 绑定与刷新 ══════════

        /// <summary>绑定与内容无关的静态部分（只在打开时做一次）。</summary>
        private void BindStatic()
        {
            if (view.closeButton != null) HouseUIUtil.BindButton(view.closeButton, ui.PopOverlay);
            if (view.advanceButton != null) HouseUIUtil.BindButton(view.advanceButton, OnAdvanceClicked);
            if (view.guestTitle != null) view.guestTitle.text = "GUEST";
            if (view.escHint != null) view.escHint.text = "ESC  返回";
        }

        /// <summary>把 DialogueManager 的当前状态画出来。内容每变一次调一次。</summary>
        private void Refresh()
        {
            if (closing || view == null) return;
            var dialogue = GameManager.Instance.DialogueManager;
            var line = dialogue.CurrentLine;

            // 停在分支上时 CurrentLine 为 null：保留上一句正文不动，只换选项列——
            // 玩家需要一边看着刚说完的话一边选。
            if (line != null) ApplySpeaker(line, dialogue);

            if (view.continueArrow != null)
                view.continueArrow.gameObject.SetActive(!dialogue.IsAtBranch);

            RebuildOptions(dialogue);
        }

        /// <summary>三种说话人样式（§4.1）：访客=立绘+名字条；玩家=无立绘、名字条换色；旁白=居中无框。</summary>
        private void ApplySpeaker(DialogueLine line, DialogueManager dialogue)
        {
            var visitor = dialogue.CurrentVisitor;
            var isNarration = line.speaker == EDialogueSpeaker.Narration;
            var isVisitor = line.speaker == EDialogueSpeaker.Visitor;
            var text = dialogue.CurrentText;

            if (view.dialogueBar != null) view.dialogueBar.gameObject.SetActive(!isNarration);
            if (view.portrait != null) view.portrait.gameObject.SetActive(isVisitor);
            if (view.nameplate != null) view.nameplate.gameObject.SetActive(!isNarration);
            if (view.narrationText != null) view.narrationText.gameObject.SetActive(isNarration);

            if (isVisitor && view.portrait != null && visitor != null && visitor.Race != null)
            {
                // 差分缺失时 GetPortraitPath 内部回落平静并打 Warning，不阻断播放（§4.1）
                var path = visitor.Race.GetPortraitPath(line.emotion);
                view.portrait.texture = string.IsNullOrEmpty(path) ? null : Resources.Load<Texture2D>(path);
            }

            if (view.speakerName != null)
            {
                view.speakerName.text = isVisitor ? (visitor != null ? visitor.DisplayName : "访客") : "我";
                // 访客名沿用 Prefab 的粉；玩家换青，一眼分得清谁在说话
                view.speakerName.color = isVisitor ? VisitorNameColor : PlayerNameColor;
            }

            // 打字机接管正文：换句时重开，未显完点击立即全文（§5.1）
            var target = isNarration ? view.narrationText : view.dialogueText;
            if (target != null) typewriter.Play(target, text, dialogue.TypewriterCharsPerSecond);
        }

        /// <summary>重建选项列：模板 Prefab 运行时实例化（§16.2），数量无上限、由布局组自动排布。</summary>
        private void RebuildOptions(DialogueManager dialogue)
        {
            foreach (var option in optionInstances)
                if (option != null)
                    Object.Destroy(option.gameObject);
            optionInstances.Clear();

            var options = dialogue.CurrentOptions;
            if (options == null || options.Count == 0 || view.optionsRoot == null) return;

            if (optionTemplate == null)
            {
                optionTemplate = Resources.Load<GameObject>(OutGamePrefabResourcePaths.DialogueOption);
                if (optionTemplate == null)
                {
                    Debug.LogError("[HouseUI] 对话选项模板 Prefab 缺失（§16.2）：" +
                                   OutGamePrefabResourcePaths.DialogueOption);
                    return;
                }
            }

            for (var i = 0; i < options.Count; i++)
            {
                var option = options[i];
                if (option == null) continue;
                var instance = Object.Instantiate(optionTemplate, view.optionsRoot, false);
                instance.name = "Option" + i;
                var optionView = instance.GetComponent<DialogueOptionView>();
                if (optionView == null)
                {
                    Debug.LogError("[HouseUI] 对话选项模板缺少视图组件：DialogueOptionView");
                    Object.Destroy(instance);
                    continue;
                }

                var enabled = dialogue.IsOptionEnabled(option);
                if (optionView.label != null)
                {
                    optionView.label.text = dialogue.FormatOptionText(option);
                    // 不满足条件的选项**置灰保留可见**，让玩家知道存在别的可能（§12 待确认默认值）
                    optionView.label.color = enabled ? HouseUIUtil.White : new Color(1, 1, 1, .35f);
                }
                if (optionView.button != null)
                {
                    optionView.button.interactable = enabled;
                    if (enabled)
                    {
                        var index = i; // 闭包捕获：不能直接用循环变量
                        HouseUIUtil.BindButton(optionView.button, () => OnOptionClicked(index));
                    }
                }
                optionInstances.Add(optionView);
            }

            HouseUIUtil.ApplyFallbackFont(view.optionsRoot);
        }

        // ══════════ 输入 ══════════

        private void OnAdvanceClicked()
        {
            if (closing) return;
            var dialogue = GameManager.Instance.DialogueManager;
            if (dialogue.IsAtBranch) return;                 // 分支必须选，点空白不推进
            if (!typewriter.IsComplete)                      // 未显完 ⇒ 立即全文（§5.1）
            {
                typewriter.SkipToEnd();
                return;
            }
            dialogue.Advance();
        }

        private void OnOptionClicked(int index)
        {
            if (closing) return;
            GameManager.Instance.DialogueManager.ChooseOption(index);
        }

        // ══════════ 动效 ══════════

        private void AnimateIn()
        {
            // 整层淡入 + 对话条上滑 + 立绘左滑（Tween 目标避开按钮 transform）
            var group = HouseUIUtil.Group(root.gameObject, 0);
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
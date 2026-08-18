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

        /// <summary>
        /// 2.0 配色（§4.1 三种说话人样式的区分之一）：访客名用设计图那支墨蓝（与底部键位条同色），
        /// 玩家名换暖褐——纸底上跟蓝对比最清楚。旁白句名字留空、正文改居中，见 ApplySpeaker。
        /// </summary>
        private static readonly Color VisitorNameColor = HouseUIUtil.Hex("5676A6");
        private static readonly Color PlayerNameColor = HouseUIUtil.Hex("9C6238");

        /// <summary>选项文字三态：默认铅灰（压在米白纸条上）、选中白（压在蓝水彩条上）、条件不满足置灰。</summary>
        private static readonly Color OptionNormalColor = HouseUIUtil.Hex("5C5C5A");
        private static readonly Color OptionSelectedColor = Color.white;
        private static readonly Color OptionDisabledColor = HouseUIUtil.Hex("5C5C5A", .35f);

        private readonly HouseUIManager ui;
        private readonly RectTransform root;
        private readonly OutGameDialogueView view;
        private readonly DialogueTypewriter typewriter;

        /// <summary>Prefab 里预摆的选项槽位（阶梯排布手调定稿）；数量不够时克隆最后一个向下延伸。</summary>
        private readonly List<DialogueOptionView> optionSlots = new List<DialogueOptionView>();
        private readonly List<DialogueOptionView> optionClones = new List<DialogueOptionView>();
        /// <summary>本分支当前可见且可选的选项下标（滚轮/回车用）。</summary>
        private readonly List<int> enabledOptions = new List<int>();
        private readonly List<DialogueOptionView> shownOptions = new List<DialogueOptionView>();
        private int selectedOption = -1;

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

            // 拉框失败必须回执给 DialogueManager：它在广播 PlaybackStarted **之前**就关了营业闸门，
            // 这里两个 return 出口若不通知，ModalDialogue 那条停走原因就再也没人清——时间永久冻住。
            var prefab = Resources.Load<GameObject>(OutGamePrefabResourcePaths.DialogueView);
            if (prefab == null)
            {
                Debug.LogError("[HouseUI] 对话层 Prefab 缺失，无法打开（§16.2 不回退代码布局）：" +
                               OutGamePrefabResourcePaths.DialogueView);
                GameManager.Instance.DialogueManager.AbortForMissingUi();
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
                GameManager.Instance.DialogueManager.AbortForMissingUi();
                return;
            }

            var rect = (RectTransform)instance.transform;
            rect.SetAsLastSibling();
            var typewriter = instance.AddComponent<DialogueTypewriter>(); // 非布局件，运行时挂（§16.2 例外说明见该类注释）
            var overlay = new DialogueOverlay(ui, rect, view, typewriter);
            current = overlay;

            // 键盘/滚轮输入（空格推进、滚轮切选项、回车确认）；挂当层，随层销毁
            var hotkeys = instance.AddComponent<DialogueHotkeys>();
            hotkeys.Bind(overlay.CycleSelection, overlay.ConfirmSelection);

            // 清掉 EventSystem 选中残留（比如打开对话用的那个场景访客按钮）——
            // 否则回车会触发它的 uGUI Submit，隔空再点一次、叠开新对话
            if (UnityEngine.EventSystems.EventSystem.current != null)
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);

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
            // 走正常退栈：此时 DialogueManager.IsPlaying 已是 false，Close() 不会再走中断语义
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
            // 推进按钮不响基础点击音：推进音（音效需求 #3）在 OnAdvanceClicked 里按「是否真的推进了」发声
            if (view.advanceButton != null) HouseUIUtil.BindButton(view.advanceButton, OnAdvanceClicked, ESfx.None);
            // 2.0 底部键位条是**真按钮**不是纯提示：点它们等价于滚轮/空格，鼠标党不必去够键盘
            if (view.cycleButton != null) HouseUIUtil.BindButton(view.cycleButton, () => CycleSelection(1));
            if (view.confirmButton != null) HouseUIUtil.BindButton(view.confirmButton, ConfirmSelection, ESfx.None);

            // 选项槽位在 Prefab 里预摆（§16.2 布局真相源），这里只收集引用
            optionSlots.Clear();
            if (view.optionsRoot != null)
                view.optionsRoot.GetComponentsInChildren(true, optionSlots);
            if (optionSlots.Count == 0)
                Debug.LogError("[HouseUI] DialogueView Prefab 里没有选项槽位（OptionsRoot 下应预摆 DialogueOptionView）；" +
                               "旧结构 Prefab 请删除后由生成器重建");
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

            RebuildOptions(dialogue);
        }

        /// <summary>
        /// 三种说话人样式：访客/玩家 = 立绘（按立绘ID）+ 名字；旁白 = 无立绘、名字留空 + 正文居中。
        ///
        /// 2026-08-19（2.0 设计图）：旁白从「整条对白板隐藏、另起一段居中无框文本」改成**复用同一块板**。
        /// 原因有二——2.0 的名字凸台是烘在 `对白底板` 素材里的，切不出「只藏名字」的版本；
        /// 而新底图是明亮水彩外景，无框墨字直接压在天空/建筑上根本读不清。
        /// 靠「没有名字 + 正文居中」照样和访客句、玩家句区分得开。
        ///
        /// 2026-08-19（羊族定为玩家）：**立绘只看当前立绘ID、名字与配色只看「谁在说」**，
        /// 两者都不再由 speaker 枚举直接决定，见下面两段与 DialogueManager.IsCurrentLineFromPlayer。
        /// </summary>
        private void ApplySpeaker(DialogueLine line, DialogueManager dialogue)
        {
            var isNarration = line.speaker == EDialogueSpeaker.Narration;
            var text = dialogue.CurrentText;

            if (view.portrait != null)
            {
                // 立绘显隐**不看说话人、只看对话表里配的立绘ID**（2026-08-19，羊族定为玩家）：
                // 老板同样是有脸的角色，按 §4.1 原口径「玩家句不显示立绘」会把表里配好的 goat_* 差分全吃掉。
                // 唯一的例外是旁白——它是环境描写、没有说话的人，让承接来的上一张脸留在屏幕上
                // 只会读成「还是他在说」。
                // 立绘ID 由 DialogueManager 承接过了（台词留空 = 沿用上一句，首句回落种族默认脸），
                // 这里只负责查图。查不到不补日志——导表期已硬校验过 ID 存在性。
                var portraits = GameManager.Instance.PortraitTable;
                var texture = isNarration || portraits == null
                    ? null
                    : portraits.TextureOf(dialogue.CurrentPortraitId);
                // 没图就整个隐掉而不是留个空位：RawImage 不带贴图会画成一块白板
                view.portrait.gameObject.SetActive(texture != null);
                view.portrait.texture = texture;
                if (texture != null)
                {
                    // RawImage 没有保持宽高比的开关：高度用 Prefab 手调值，宽度按贴图真实比例回算，避免拉伸
                    var portraitRect = view.portrait.rectTransform;
                    var height = portraitRect.sizeDelta.y;
                    portraitRect.sizeDelta = new Vector2(height * texture.width / (float)texture.height, height);
                }
            }

            if (view.speakerName != null)
            {
                // 名字与配色都按「这句是谁在说」走 DialogueManager 的判定，而不是按 speaker 枚举硬分：
                // 老板的台词在对话表里大多写成 visitor（历史原因，那时玩家句会藏脸），
                // 名字得认回玩家名「嘻洋羊」而不是访客名，见 DialogueManager.IsCurrentLineFromPlayer。
                view.speakerName.text = dialogue.CurrentSpeakerName;
                // 访客名墨蓝、玩家名暖褐，一眼分得清谁在说话
                view.speakerName.color = dialogue.IsCurrentLineFromPlayer ? PlayerNameColor : VisitorNameColor;
            }

            // 打字机接管正文：换句时重开，未显完点击立即全文（§5.1）
            if (view.dialogueText != null)
            {
                view.dialogueText.alignment = isNarration ? TextAnchor.MiddleCenter : TextAnchor.UpperLeft;
                typewriter.Play(view.dialogueText, text, dialogue.TypewriterCharsPerSecond);
            }
        }

        /// <summary>
        /// 重建选项列：绑定 Prefab 里预摆的槽位，多余槽位隐藏。
        ///
        /// 2026-08-19（2.0 设计图）改为**底对齐**：槽位在 Prefab 里自下而上摆，最下一个紧贴对白板顶沿；
        /// N 个选项占用最下面 N 格，第一个选项落在最上。这样选项越多越往上长、永远压不到对白板上——
        /// 1.0 那种「从上往下排、不够往下克隆」在新版式里会直接怼进对白板。
        /// 选项数超出预摆槽位时，从最上一格继续按槽距向上克隆。
        /// </summary>
        private void RebuildOptions(DialogueManager dialogue)
        {
            foreach (var clone in optionClones)
                if (clone != null)
                    Object.Destroy(clone.gameObject);
            optionClones.Clear();
            foreach (var slot in optionSlots)
                if (slot != null)
                    slot.gameObject.SetActive(false);
            shownOptions.Clear();
            enabledOptions.Clear();
            selectedOption = -1;

            var options = dialogue.CurrentOptions;
            if (options == null || options.Count == 0 || optionSlots.Count == 0) return;

            // 底对齐要先知道总数：空洞的行不占格
            var total = 0;
            foreach (var candidate in options)
                if (candidate != null)
                    total++;
            if (total == 0) return;

            for (var i = 0; i < options.Count; i++)
            {
                var option = options[i];
                if (option == null) continue;
                var optionView = SlotFor(shownOptions.Count, total);
                if (optionView == null) continue;
                optionView.gameObject.SetActive(true);

                var enabled = dialogue.IsOptionEnabled(option);
                // 文字配色统一在 ApplySelectionVisual 里按「选中 / 可选 / 置灰」三态刷
                if (optionView.label != null) optionView.label.text = dialogue.FormatOptionText(option);
                if (optionView.button != null)
                {
                    optionView.button.interactable = enabled;
                    if (enabled)
                    {
                        var index = i; // 闭包捕获：不能直接用循环变量
                        // 选项也算对话交互（音效需求 #3），点击音关掉、在 OnOptionClicked 里发交互音
                        HouseUIUtil.BindButton(optionView.button, () => OnOptionClicked(index), ESfx.None);
                        var shownIndex = shownOptions.Count;
                        BindHoverSync(optionView, shownIndex);
                        enabledOptions.Add(shownIndex);
                    }
                }
                shownOptions.Add(optionView);
            }

            // 默认选中第一个可选项（滚轮/回车操作的起点；悬停会把选中同步到鼠标位置）
            if (enabledOptions.Count > 0) selectedOption = enabledOptions[0];
            ApplySelectionVisual();
            HouseUIUtil.ApplyFallbackFont(view.optionsRoot);
        }

        /// <summary>
        /// 取第 index 个选项（自上而下数）该用的槽位。槽位数组的下标 0 是**最下面**那一格
        /// （Prefab 里自下而上创建，GetComponentsInChildren 按层序返回），所以底对齐的映射是
        /// slot = total - 1 - index：共 3 个选项时依次落在 slot2 / slot1 / slot0。
        /// 选项数超出预摆格数时，从最上一格继续按槽距向上克隆。
        /// </summary>
        private DialogueOptionView SlotFor(int index, int total)
        {
            var slot = total - 1 - index;
            if (slot >= 0 && slot < optionSlots.Count) return optionSlots[slot];

            var top = optionSlots[optionSlots.Count - 1];
            var step = optionSlots.Count >= 2
                ? top.GetComponent<RectTransform>().anchoredPosition -
                  optionSlots[optionSlots.Count - 2].GetComponent<RectTransform>().anchoredPosition
                : new Vector2(0, 94);
            var clone = Object.Instantiate(top.gameObject, top.transform.parent, false);
            clone.name = "OptionClone" + index;
            var rect = (RectTransform)clone.transform;
            rect.anchoredPosition = ((RectTransform)top.transform).anchoredPosition +
                                    step * (slot - optionSlots.Count + 1);
            var optionView = clone.GetComponent<DialogueOptionView>();
            optionClones.Add(optionView);
            return optionView;
        }

        /// <summary>悬停时把「当前选中」同步到鼠标所在项，让滚轮和鼠标以最后交互为准。</summary>
        private void BindHoverSync(DialogueOptionView optionView, int shownIndex)
        {
            var trigger = optionView.gameObject.GetComponent<UnityEngine.EventSystems.EventTrigger>();
            if (trigger == null) trigger = optionView.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            trigger.triggers.Clear();
            var entry = new UnityEngine.EventSystems.EventTrigger.Entry
            {
                eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter,
            };
            entry.callback.AddListener(_ =>
            {
                selectedOption = shownIndex;
                ApplySelectionVisual();
            });
            trigger.triggers.Add(entry);
        }

        /// <summary>
        /// 刷选中态：选中项亮蓝水彩条 + 白字，其余回米白纸条 + 铅灰字，条件不满足的置灰。
        /// 底图切换交给 DialogueOptionView.SetSelected（两张素材主体不等大，靠显隐叠放而不是 SpriteSwap）。
        /// </summary>
        private void ApplySelectionVisual()
        {
            for (var i = 0; i < shownOptions.Count; i++)
            {
                var optionView = shownOptions[i];
                if (optionView == null) continue;
                var selected = i == selectedOption;
                optionView.SetSelected(selected);
                if (optionView.label == null) continue;
                // 不满足条件的选项**置灰保留可见**，让玩家知道存在别的可能（§12 待确认默认值）
                var interactable = optionView.button == null || optionView.button.interactable;
                optionView.label.color = !interactable ? OptionDisabledColor
                    : selected ? OptionSelectedColor : OptionNormalColor;
            }
        }

        /// <summary>滚轮切换选中项（向下滚 = 往下一项）。分支未亮选项时无事发生。</summary>
        private void CycleSelection(int direction)
        {
            if (closing || enabledOptions.Count == 0) return;
            var at = enabledOptions.IndexOf(selectedOption);
            var next = at < 0 ? 0 : ((at + direction) % enabledOptions.Count + enabledOptions.Count) % enabledOptions.Count;
            selectedOption = enabledOptions[next];
            ApplySelectionVisual();
        }

        /// <summary>回车/空格确认当前选中项；不在分支上时回落为推进对白。</summary>
        private void ConfirmSelection()
        {
            if (closing) return;
            var dialogue = GameManager.Instance.DialogueManager;
            if (!dialogue.IsAtBranch)
            {
                OnAdvanceClicked();
                return;
            }
            if (selectedOption < 0 || selectedOption >= shownOptions.Count) return;
            var chosen = shownOptions[selectedOption];
            if (chosen != null && chosen.button != null && chosen.button.interactable)
                chosen.button.onClick.Invoke();
        }

        // ══════════ 输入 ══════════

        private void OnAdvanceClicked()
        {
            if (closing) return;
            var dialogue = GameManager.Instance.DialogueManager;
            if (dialogue.IsAtBranch) return;                 // 分支必须选，点空白不推进（无效点击不响）
            SfxManager.Play(ESfx.GuestInteract);             // 音效需求 #3：对话点击继续（跳全文与推进同响）
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
            SfxManager.Play(ESfx.GuestInteract); // 音效需求 #3：选定分支选项（键盘回车确认也汇到这里）
            // 点完清掉 uGUI 选中态，免得之后的回车对这颗按钮再触发一次 Submit
            if (UnityEngine.EventSystems.EventSystem.current != null)
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
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
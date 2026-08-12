using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 对白文本演出（运行时挂到对话层实例，不改 Prefab 资产）：
    /// ①关键词标红（下划线由 DialogueKeywordUnderline 顶点特效绘制，Legacy Text 不支持 u 标签）；
    /// ②逐字打字机；③单段最多 50 字，超长按标点拆成多段；
    /// ④段间显示继续箭头（位置自适应贴在已显示文本的最后一行下方），末段（后续是选项）不显示；
    /// ⑤空格 / 鼠标左键：动效中→整段立即显示；本段显示完→推进下一段；全部演完→回调（亮出选项）。
    /// 表现层组件：允许 deltaTime；打字机音效在 OnCharRevealed 留挂点，音频素材就位后接入。
    /// </summary>
    public sealed class DialogueTextPlayer : MonoBehaviour
    {
        private const float CharsPerSecond = 30f;
        private const int MaxCharsPerSegment = 50;
        private const string KeywordColor = "#E22D76";
        private static readonly char[] SplitPunctuations = { '。', '！', '？', '…', '；', '，', '.', '!', '?', ';', ',' };

        /// <summary>一段富文本中的连续片段：keyword=true 的片段标红并画下划线。</summary>
        private struct Run
        {
            public string Text;
            public bool Keyword;
        }

        private Text label;
        private Image arrow;
        private Action onAllShown;
        private readonly List<List<Run>> segments = new List<List<Run>>();
        private int segmentIndex;
        private float shownChars;
        private bool segmentDone;
        private bool finished = true;
        private float arrowBobPhase;

        // ── 选项选择阶段（文字演完后）：滚轮/悬停切换，空格确认，以最后交互为准 ──
        private readonly List<Button> optionButtons = new List<Button>();
        private int selectedOption = -1;
        private int finishedFrame = -1;
        private int playFrame = -1;

        /// <summary>开始播放一段对白。keywords 为需要标红下划线的词（按长度优先匹配）。</summary>
        public void Play(Text targetLabel, Image continueArrow, string text, IReadOnlyList<string> keywords, Action allShown)
        {
            label = targetLabel;
            arrow = continueArrow;
            onAllShown = allShown;
            segments.Clear();
            optionButtons.Clear();
            selectedOption = -1;
            segmentIndex = 0;
            shownChars = 0f;
            segmentDone = false;
            finished = false;
            // 开播当帧忽略推进输入：上一层对话的确认按键/点击不泄漏进本层
            // （否则键盘与鼠标因触发帧序不同，一个逐字一个直接跳满，表现不一致）
            playFrame = Time.frameCount;

            if (label == null || string.IsNullOrEmpty(text))
            {
                Finish();
                return;
            }
            if (label.GetComponent<DialogueKeywordUnderline>() == null)
                label.gameObject.AddComponent<DialogueKeywordUnderline>();

            BuildSegments(text, keywords);
            if (segments.Count == 0)
            {
                Finish();
                return;
            }
            label.text = string.Empty;
            RefreshText();
        }

        /// <summary>文字演完后由对话层传入当前可选项：滚轮切换、空格/回车确认对应选项。</summary>
        public void SetOptions(List<Button> buttons)
        {
            optionButtons.Clear();
            if (buttons != null)
                foreach (var button in buttons)
                    if (button != null) optionButtons.Add(button);
            selectedOption = optionButtons.Count > 0 ? 0 : -1;
            ApplySelectionVisual();
        }

        /// <summary>鼠标悬停某选项时同步选中（悬停即选中，规格：以最后的交互方式为准）。</summary>
        public void NotifyOptionHover(Button button)
        {
            var index = optionButtons.IndexOf(button);
            if (index < 0) return;
            selectedOption = index;
            ApplySelectionVisual();
        }

        private void Update()
        {
            if (finished)
            {
                HandleOptionInput();
                return;
            }
            var advance = Time.frameCount > playFrame &&
                          (Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0));

            if (!segmentDone)
            {
                var total = SegmentLength(segments[segmentIndex]);
                shownChars = advance ? total : Mathf.Min(total, shownChars + Time.unscaledDeltaTime * CharsPerSecond);
                // 打字机音效挂点：每显示一个新字符时播放（音频素材就位后接入）
                // OnCharRevealed();
                RefreshText();
                if (shownChars >= total)
                {
                    segmentDone = true;
                    if (segmentIndex >= segments.Count - 1) Finish(); // 末段演完直接收尾（后续是选项，不显示箭头）
                }
            }
            else if (advance)
            {
                segmentIndex++;
                shownChars = 0f;
                segmentDone = false;
                RefreshText();
            }
            UpdateArrow();
        }

        private void Finish()
        {
            finished = true;
            finishedFrame = Time.frameCount; // 结束当帧的空格不能顺手确认选项
            if (arrow != null) arrow.gameObject.SetActive(false);
            var callback = onAllShown;
            onAllShown = null;
            callback?.Invoke();
        }

        /// <summary>选项阶段输入：滚轮上下切换（循环）、空格/回车确认当前选中项。</summary>
        private void HandleOptionInput()
        {
            if (optionButtons.Count == 0 || selectedOption < 0) return;
            if (Time.frameCount == finishedFrame) return;
            var scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > .01f)
            {
                var step = scroll < 0f ? 1 : -1; // 向下滚 = 选下一条
                selectedOption = (selectedOption + step + optionButtons.Count) % optionButtons.Count;
                ApplySelectionVisual();
            }
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                var chosen = optionButtons[selectedOption];
                if (chosen != null && chosen.interactable) chosen.onClick.Invoke();
            }
        }

        /// <summary>选中项显示悬停粉刷（SpriteSwap 的 highlighted 图），其余回默认黑刷。</summary>
        private void ApplySelectionVisual()
        {
            for (var i = 0; i < optionButtons.Count; i++)
            {
                var button = optionButtons[i];
                if (button == null || !(button.targetGraphic is Image image)) continue;
                var state = button.spriteState;
                var sprite = i == selectedOption ? state.highlightedSprite : state.selectedSprite;
                if (sprite == null) sprite = image.sprite;
                image.sprite = sprite;
            }
        }

        // ── 分段与富文本 ──

        /// <summary>关键词切分 + 按 50 字上限分段（优先在标点后断开；策划规范单段两行 50 字内）。</summary>
        private void BuildSegments(string text, IReadOnlyList<string> keywords)
        {
            var sorted = new List<string>();
            if (keywords != null)
                foreach (var keyword in keywords)
                    if (!string.IsNullOrEmpty(keyword) && !sorted.Contains(keyword))
                        sorted.Add(keyword);
            sorted.Sort((a, b) => b.Length.CompareTo(a.Length)); // 长词优先，避免短词截断长词

            var current = new List<Run>();
            var currentLength = 0;
            var cursor = 0;
            while (cursor < text.Length)
            {
                // 命中关键词：整词进当前段（不跨段拆关键词）
                string hit = null;
                foreach (var keyword in sorted)
                    if (cursor + keyword.Length <= text.Length &&
                        string.CompareOrdinal(text, cursor, keyword, 0, keyword.Length) == 0)
                    {
                        hit = keyword;
                        break;
                    }
                if (hit != null)
                {
                    current.Add(new Run { Text = hit, Keyword = true });
                    currentLength += hit.Length;
                    cursor += hit.Length;
                }
                else
                {
                    var ch = text[cursor];
                    if (current.Count > 0 && !current[current.Count - 1].Keyword)
                    {
                        var last = current[current.Count - 1];
                        last.Text += ch;
                        current[current.Count - 1] = last;
                    }
                    else
                    {
                        current.Add(new Run { Text = ch.ToString(), Keyword = false });
                    }
                    currentLength++;
                    cursor++;
                }
                // 超长拆段：到上限后在标点处断开（当前字符是标点则立即断）
                if (currentLength >= MaxCharsPerSegment && cursor < text.Length &&
                    Array.IndexOf(SplitPunctuations, text[cursor - 1]) >= 0)
                {
                    segments.Add(current);
                    current = new List<Run>();
                    currentLength = 0;
                }
                else if (currentLength >= MaxCharsPerSegment + 12)
                {
                    // 一直没等到标点的硬断，避免单段无限变长
                    segments.Add(current);
                    current = new List<Run>();
                    currentLength = 0;
                }
            }
            if (current.Count > 0) segments.Add(current);
        }

        private static int SegmentLength(List<Run> runs)
        {
            var total = 0;
            foreach (var run in runs) total += run.Text.Length;
            return total;
        }

        /// <summary>按已显示字符数重建富文本（关键词整段包色，避免截断标签）。</summary>
        private void RefreshText()
        {
            var runs = segments[segmentIndex];
            var visible = Mathf.FloorToInt(shownChars);
            var builder = new System.Text.StringBuilder();
            var used = 0;
            foreach (var run in runs)
            {
                if (used >= visible) break;
                var take = Mathf.Min(run.Text.Length, visible - used);
                var piece = take == run.Text.Length ? run.Text : run.Text.Substring(0, take);
                if (run.Keyword) builder.Append("<color=").Append(KeywordColor).Append(">").Append(piece).Append("</color>");
                else builder.Append(piece);
                used += take;
            }
            label.text = builder.ToString();
        }

        /// <summary>继续箭头：仅段间等待时显示，位置自适应贴在已显示文本最后一行下方（轻微上下浮动）。</summary>
        private void UpdateArrow()
        {
            if (arrow == null) return;
            var waiting = segmentDone && !finished;
            if (arrow.gameObject.activeSelf != waiting) arrow.gameObject.SetActive(waiting);
            if (!waiting) return;
            arrowBobPhase += Time.unscaledDeltaTime * 5f;
            var labelRect = label.rectTransform;
            var localY = labelRect.rect.yMax - label.preferredHeight - 26f + Mathf.Sin(arrowBobPhase) * 3f;
            arrow.rectTransform.position = labelRect.TransformPoint(new Vector3(labelRect.rect.xMin + 16f, localY, 0f));
        }
    }
}

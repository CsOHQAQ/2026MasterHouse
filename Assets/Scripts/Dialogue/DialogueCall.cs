using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 一次函数调用（条件或事件）：函数名 + 参数字符串数组。
    ///
    /// Excel 里写成 `HasEmptyRoom` / `DayAtLeast(3)` / `Log("你好, 世界")` 这样的调用串，
    /// **导入期**（DialogueCsvImporter）解析成本结构存进 DialogueTable（第 14 题定案·乙）：
    /// 运行时直接查 DialogueFuncs 的字典调用，既不重复解析字符串、也不做任何反射。
    ///
    /// 写错的函数名与参数个数在导表期就被校验器拦下并指到 Excel 行号，不用跑进游戏才发现。
    /// </summary>
    [Serializable]
    public sealed class DialogueCall
    {
        [Tooltip("函数名，对应 DialogueFuncs 里注册的那一条")]
        public string func;

        [Tooltip("参数（原样存字符串，取值时由 DialogueArgs 按需转换）")]
        public List<string> args = new List<string>();

        /// <summary>参数访问器（省得每个函数体里重复判空与越界）。</summary>
        public DialogueArgs Args => new DialogueArgs(args);

        /// <summary>回写成 Excel 里的调用串（导出底稿与报错文案用）。</summary>
        public override string ToString()
        {
            if (args == null || args.Count == 0) return func ?? string.Empty;
            return $"{func}({string.Join(",", args)})";
        }
    }

    /// <summary>参数取值助手：越界与格式错一律回落默认值，不抛异常（内容驱动的系统不该被一格配错打死）。</summary>
    public readonly struct DialogueArgs
    {
        private readonly List<string> values;

        public DialogueArgs(List<string> values) => this.values = values;

        public int Count => values != null ? values.Count : 0;

        public string Str(int index, string fallback = "") =>
            values != null && index >= 0 && index < values.Count ? values[index] : fallback;

        public int Int(int index, int fallback = 0) =>
            int.TryParse(Str(index), out var value) ? value : fallback;

        /// <summary>满意度档位：认英文 key（disappointed/plain/fine/perfect），也认中文（失望/一般/还行/完美）。</summary>
        public EServeSatisfaction Satisfaction(int index, EServeSatisfaction fallback = EServeSatisfaction.Perfect)
        {
            var raw = Str(index).Trim();
            if (raw.Length == 0) return fallback;
            switch (raw.ToLowerInvariant())
            {
                case "disappointed": case "mismatch": return EServeSatisfaction.Mismatch;
                case "plain": return EServeSatisfaction.Plain;
                case "fine": case "satisfied": return EServeSatisfaction.Satisfied;
                case "perfect": return EServeSatisfaction.Perfect;
            }
            switch (raw)
            {
                case "失望": case "不对味": return EServeSatisfaction.Mismatch;
                case "一般": return EServeSatisfaction.Plain;
                case "还行": case "满意": return EServeSatisfaction.Satisfied;
                case "完美": return EServeSatisfaction.Perfect;
            }
            return fallback;
        }

        /// <summary>访客状态：认枚举名（FrontDesk / AwaitingRoom / Serving / Wandering）。</summary>
        public EVisitorState VisitorState(int index, EVisitorState fallback = EVisitorState.FrontDesk) =>
            Enum.TryParse(Str(index).Trim(), true, out EVisitorState value) ? value : fallback;
    }

    /// <summary>
    /// 调用串的语法解析（**导入器与校验器共用同一份**，保证「导表期能过」等价于「运行时能跑」）。
    ///
    /// 语法极简，刻意不是表达式语言（§12 明确不做）：
    ///   Name                 无参
    ///   Name(a,b)            两个参数，两侧空白自动去掉
    ///   Name("a,b")          双引号保护含逗号的参数；内部的 "" 表示一个字面双引号
    /// 多条之间在 Excel 里用 `;` 分隔，隐式 AND（条件）/ 顺序执行（事件）。
    /// </summary>
    public static class DialogueCallParser
    {
        /// <summary>拆分一格里的多条调用（`;` 分隔，忽略引号内的分号）。空格返回空列表。</summary>
        public static List<string> SplitCalls(string raw)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(raw)) return result;

            var current = new StringBuilder();
            var inQuote = false;
            foreach (var c in raw)
            {
                if (c == '"') { inQuote = !inQuote; current.Append(c); continue; }
                // 中文分号也认：策划切输入法时很容易打成全角
                if (!inQuote && (c == ';' || c == '；'))
                {
                    if (current.Length > 0) result.Add(current.ToString());
                    current.Clear();
                    continue;
                }
                current.Append(c);
            }
            if (current.Length > 0) result.Add(current.ToString());

            for (var i = result.Count - 1; i >= 0; i--)
            {
                result[i] = result[i].Trim();
                if (result[i].Length == 0) result.RemoveAt(i);
            }
            return result;
        }

        /// <summary>
        /// 解析单条调用串。语法错误时返回 false 并给出中文原因（调用方负责拼上 Excel 行号）。
        /// **只做语法**——函数名是否存在、参数个数对不对由 DialogueFuncs 校验。
        /// </summary>
        public static bool TryParse(string raw, out DialogueCall call, out string error)
        {
            call = null;
            error = null;
            if (string.IsNullOrWhiteSpace(raw)) { error = "调用串是空的"; return false; }

            var text = raw.Trim();
            // 全角括号一并认下：策划切输入法时常见
            text = text.Replace('（', '(').Replace('）', ')').Replace('，', ',');

            var open = text.IndexOf('(');
            if (open < 0)
            {
                if (text.IndexOf(')') >= 0) { error = $"「{raw}」有右括号却没有左括号"; return false; }
                call = new DialogueCall { func = text, args = new List<string>() };
                return true;
            }

            if (!text.EndsWith(")")) { error = $"「{raw}」的左括号没有配对的右括号（右括号必须在最后）"; return false; }

            var name = text.Substring(0, open).Trim();
            if (name.Length == 0) { error = $"「{raw}」缺少函数名"; return false; }

            var inner = text.Substring(open + 1, text.Length - open - 2);
            if (!TrySplitArgs(inner, out var args, out var argError))
            {
                error = $"「{raw}」的参数有问题：{argError}";
                return false;
            }

            call = new DialogueCall { func = name, args = args };
            return true;
        }

        private static bool TrySplitArgs(string inner, out List<string> args, out string error)
        {
            args = new List<string>();
            error = null;
            if (string.IsNullOrWhiteSpace(inner)) return true;

            var current = new StringBuilder();
            var inQuote = false;
            for (var i = 0; i < inner.Length; i++)
            {
                var c = inner[i];
                if (c == '"')
                {
                    if (inQuote && i + 1 < inner.Length && inner[i + 1] == '"') { current.Append('"'); i++; continue; }
                    inQuote = !inQuote;
                    continue;
                }
                if (!inQuote && c == ',') { args.Add(current.ToString().Trim()); current.Clear(); continue; }
                current.Append(c);
            }
            if (inQuote) { error = "双引号没有闭合"; return false; }
            args.Add(current.ToString().Trim());
            return true;
        }
    }
}

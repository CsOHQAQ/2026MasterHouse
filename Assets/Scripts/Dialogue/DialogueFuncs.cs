using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>条件函数：纯查询，**不得有任何副作用**（选项置灰会在每次重绘时反复求值）。</summary>
    public delegate bool DialogueConditionFunc(GameplayContext ctx, DialogueArgs args);

    /// <summary>事件函数：一次性的状态转换或结算。</summary>
    public delegate void DialogueActionFunc(GameplayContext ctx, DialogueArgs args);

    /// <summary>条件注册项：函数体 + 给校验器和填表说明用的元信息。</summary>
    public sealed class DialogueConditionDef
    {
        public readonly DialogueConditionFunc Fn;
        public readonly string Label;    // 中文名（报错与文档用）
        public readonly string ArgsHint; // 参数说明，无参写空串
        public readonly int ArgCount;

        public DialogueConditionDef(DialogueConditionFunc fn, string label, string argsHint = "", int argCount = 0)
        {
            Fn = fn; Label = label; ArgsHint = argsHint; ArgCount = argCount;
        }
    }

    /// <summary>事件注册项：函数体 + 元信息 + 奖励标记。</summary>
    public sealed class DialogueActionDef
    {
        public readonly DialogueActionFunc Fn;
        public readonly string Label;
        public readonly string ArgsHint;
        public readonly int ArgCount;

        /// <summary>
        /// 奖励类事件（对话设计说明 §5.3 铁律②）：**只允许放在所在路径的最后一个事件位**。
        /// 中途给奖励 + 玩家 ESC = 这段视为没播过、再抽到又领一次。校验器据此给警告。
        /// </summary>
        public readonly bool IsReward;

        public DialogueActionDef(DialogueActionFunc fn, string label, string argsHint = "", int argCount = 0,
            bool isReward = false)
        {
            Fn = fn; Label = label; ArgsHint = argsHint; ArgCount = argCount; IsReward = isReward;
        }
    }

    /// <summary>
    /// 对话可用的条件与事件（2026-08-14 重构定案，取代原来 16 个 [SerializeReference] 多态类）。
    ///
    /// **零反射**：Excel 里写函数名，导入期解析成 DialogueCall 存进表，运行时查下面两张字典直接调委托。
    /// 没有 TypeCache 扫描、没有 Delegate.CreateDelegate、没有 IL2CPP 裁剪风险。
    /// 代价是「加一条判定要改这个文件」——但加一条**新的判定逻辑**本来就是写代码，
    /// 不是策划改内容，不违反 CLAUDE.md「加内容 = 加资产行、不碰代码」（那条管的是台词与数值）。
    ///
    /// **这两张字典是「工程里有哪些条件/事件」的唯一真相源**：导表校验、Excel 模板的数据校验下拉、
    /// 资产校验器全部从这里取，一处维护三处生效。加一条 = 加一行。
    ///
    /// 字典只做 key 查询、从不按枚举顺序遍历业务逻辑（导出下拉时显式排序），不违反确定性守则 §11.2。
    /// </summary>
    public static class DialogueFuncs
    {
        // ══════════ 条件 ══════════

        public static readonly Dictionary<string, DialogueConditionDef> Conditions =
            new Dictionary<string, DialogueConditionDef>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["HasEmptyRoom"] = new DialogueConditionDef(
                    (ctx, a) => ctx?.VisitorManager != null && ctx.VisitorManager.HasFreeRoom,
                    "还有空客房（只看房，不看队列）"),

                ["CanAcceptGuest"] = new DialogueConditionDef(
                    (ctx, a) => ctx?.VisitorManager != null && ctx.VisitorManager.CanAcceptGuest,
                    "现在能接待新客人（有空房 且 没有别人正在等分房）"),

                ["RoomHasNeedFurniture"] = new DialogueConditionDef(
                    (ctx, a) =>
                    {
                        var visitor = ctx?.Visitor;
                        // 不是条件类需求时一律 false：小游戏类的验收走分数，不该被这条误判成通过
                        if (visitor == null || !(visitor.Need is ConditionNeedDef need)) return false;
                        // 族与精确 id 是 OR（家具族体系说明 §4.2）：配了族就任意配色都算，配了 id 就只认那一件
                        return FurniturePlacementQuery.RoomHasAnyFamily(visitor.RoomIndex, need.familyIds) ||
                               FurniturePlacementQuery.RoomHasAny(visitor.RoomIndex, need.furnitureIds);
                    },
                    "所住房间里摆着他要的家具之一（条件类需求的验收判据；按族配则任意配色都算）"),

                ["DayAtLeast"] = new DialogueConditionDef(
                    (ctx, a) => ctx?.Clock != null && ctx.Clock.Data.Day >= a.Int(0, 1),
                    "第 N 天或更晚", "天数", 1),

                ["CurrencyAtLeast"] = new DialogueConditionDef(
                    (ctx, a) => ctx?.Economy != null && ctx.Economy.Data.Currency >= a.Int(0),
                    "货币不少于 N", "数量", 1),

                ["ReputationAtLeast"] = new DialogueConditionDef(
                    (ctx, a) => ctx?.Economy != null && ctx.Economy.Data.Reputation >= a.Int(0),
                    "声望不少于 N", "数量", 1),

                ["SatisfactionAtLeast"] = new DialogueConditionDef(
                    (ctx, a) => ctx?.Visitor != null && ctx.Visitor.Satisfaction >= a.Satisfaction(0),
                    "本次满意度不低于指定档（结算之后才有意义）",
                    "档位 disappointed/plain/fine/perfect", 1),

                ["VisitorStateIs"] = new DialogueConditionDef(
                    (ctx, a) => ctx?.Visitor != null && ctx.Visitor.State == a.VisitorState(0),
                    "访客处于指定状态",
                    "状态 FrontDesk/AwaitingRoom/Serving/Wandering", 1),
            };

        // ══════════ 事件 ══════════
        //
        // 两条铁律（对话设计说明 §5.3）：
        //   ① 事件只做**一次性的状态转换与结算**，绝不承担「必须发生的后续推进」。
        //      拒绝事件只置状态，离场由状态机自己走完——把「离场」挂在对话末尾的话，
        //      玩家一按 ESC 访客就永远卡在场上。（ExecuteOnInterrupt 那个补丁已于本次重构删除，
        //      因为八个事件里从来没有一个用过它，而状态机自洽才是根本。）
        //   ② 奖励类事件只允许放在所在路径的最后一个事件位（IsReward = true 的那些）。

        public static readonly Dictionary<string, DialogueActionDef> Actions =
            new Dictionary<string, DialogueActionDef>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["Accept"] = new DialogueActionDef(
                    (ctx, a) =>
                    {
                        if (ctx?.VisitorManager == null) { Warn("接待", "上下文缺 VisitorManager"); return; }
                        if (!ctx.VisitorManager.Accept(ctx.VisitorInstanceId))
                            Debug.LogWarning($"[对话事件] 接待未生效：实例 {ctx.VisitorInstanceId} 不在前台，" +
                                             "或现在接待不了（客房住满 / 已有人在等分房）；" +
                                             "这个选项应当挂上 CanAcceptGuest 条件");
                    },
                    "接待（转「等待分配房间」，此时不说需求）"),

                ["Reject"] = new DialogueActionDef(
                    (ctx, a) =>
                    {
                        if (ctx?.VisitorManager == null) { Warn("拒绝", "上下文缺 VisitorManager"); return; }
                        if (!ctx.VisitorManager.Reject(ctx.VisitorInstanceId))
                            Debug.LogWarning($"[对话事件] 拒绝未生效：实例 {ctx.VisitorInstanceId} 不在可拒绝状态" +
                                             "（前台 / 服务中才可以；等待分房的客人必须先分房）");
                    },
                    "拒绝（扣声望并离场）"),

                ["CompleteNeed"] = new DialogueActionDef(
                    (ctx, a) =>
                    {
                        if (ctx?.VisitorManager == null) { Warn("完成需求结算", "上下文缺 VisitorManager"); return; }
                        if (!ctx.VisitorManager.CompleteNeed(ctx.VisitorInstanceId, a.Satisfaction(0)))
                            Debug.LogWarning($"[对话事件] 完成需求结算未生效：实例 {ctx.VisitorInstanceId} 不在「服务中」");
                    },
                    "完成需求结算（条件类固定完美；小游戏类由分数定档）",
                    "档位 disappointed/plain/fine/perfect，留空=perfect", 1, isReward: true),

                ["StartMinigame"] = new DialogueActionDef(
                    (ctx, a) =>
                    {
                        var visitor = ctx?.Visitor;
                        var who = visitor != null ? visitor.DisplayName : "访客";
                        var need = visitor != null ? visitor.Need as MinigameNeedDef : null;
                        if (need == null)
                        {
                            Debug.LogWarning($"[对话事件] 开始小游戏：{who} 的需求不是小游戏类" +
                                             $"（当前 {(visitor?.Need != null ? visitor.Need.DisplayId : "无需求")}）；" +
                                             "请检查这条对话组的「需求ID」是不是挂错了");
                            Toast("这位客人要的不是小游戏");
                            return;
                        }
                        if (need.minigame == null)
                        {
                            Debug.LogError($"[对话事件] 开始小游戏：需求「{need.DisplayId}」没有配 minigame 引用，无法开局", need);
                            Toast("这条需求还没配小游戏");
                            return;
                        }
                        // 只登记意图、不当场开页面：本事件在播放推进过程中执行，此时对话层还在栈顶，
                        // 当场 PushOverlay 会让随后的收框弹掉小游戏层而不是对话层。
                        // need.level 是需求点名的关卡，为 null 时宿主回落关卡池抽取（§8.4）——
                        // 打哪一关是**需求资产**的事，对话表里不需要多写参数。
                        MinigameOverlay.Request(need.minigame, visitor.InstanceId, need.DisplayId, need.level);
                    },
                    "开始小游戏（小游戏类需求的开局口）"),

                ["AddCurrency"] = new DialogueActionDef(
                    (ctx, a) =>
                    {
                        if (ctx?.Economy == null) { Warn("增减货币", "上下文缺 EconomyManager"); return; }
                        var applied = ctx.Economy.AddCurrency(a.Int(0));
                        // 计入当日结算累计（日结面板「对话奖励」行）；缺 VisitorManager 只影响统计，入账已生效不回滚
                        ctx.VisitorManager?.RecordDialogueReward(applied, 0);
                    },
                    "增减货币（正给负扣，下限 0）", "数量", 1, isReward: true),

                ["AddReputation"] = new DialogueActionDef(
                    (ctx, a) =>
                    {
                        if (ctx?.Economy == null) { Warn("增减声望", "上下文缺 EconomyManager"); return; }
                        var applied = ctx.Economy.AddReputation(a.Int(0));
                        ctx.VisitorManager?.RecordDialogueReward(0, applied);
                    },
                    "增减声望（正给负扣，下限 0）", "数量", 1, isReward: true),

                ["Log"] = new DialogueActionDef(
                    (ctx, a) =>
                    {
                        var who = ctx?.Visitor != null ? ctx.Visitor.DisplayName : "（无访客）";
                        Debug.Log($"[对话调试] {who}：{a.Str(0)}");
                    },
                    "往 Console 打一条日志（自查分支走向用，不影响业务）", "文本", 1),
            };

        // ══════════ 查询（运行时、导入器与校验器共用）══════════

        /// <summary>求值一条条件；函数名未注册时报错并按**不通过**处理（宁可少说话，不可乱结算）。</summary>
        public static bool Evaluate(DialogueCall call, GameplayContext ctx)
        {
            if (call == null || string.IsNullOrEmpty(call.func)) return true;
            if (!Conditions.TryGetValue(call.func, out var def))
            {
                Debug.LogError($"[对话条件] 未注册的条件函数「{call.func}」，按不通过处理；" +
                               "请检查对话表里的拼写，或在 DialogueFuncs.Conditions 里补一行");
                return false;
            }
            try { return def.Fn(ctx, call.Args); }
            catch (System.Exception e)
            {
                Debug.LogError($"[对话条件] {call.func} 求值抛异常，按不通过处理：\n{e}");
                return false;
            }
        }

        /// <summary>多条之间 AND；空列表 = 无条件通过。</summary>
        public static bool EvaluateAll(List<DialogueCall> calls, GameplayContext ctx)
        {
            if (calls == null || calls.Count == 0) return true;
            foreach (var call in calls)
                if (!Evaluate(call, ctx))
                    return false;
            return true;
        }

        /// <summary>
        /// 执行一条事件并吞掉异常：内容驱动的系统里，一条配错的事件不该把整段对话打死在半路
        /// （那会连 ESC 都退不出去，因为播放状态卡在异常点上）。
        /// </summary>
        public static void Execute(DialogueCall call, GameplayContext ctx)
        {
            if (call == null || string.IsNullOrEmpty(call.func)) return;
            if (!Actions.TryGetValue(call.func, out var def))
            {
                Debug.LogError($"[对话事件] 未注册的事件函数「{call.func}」，已跳过；" +
                               "请检查对话表里的拼写，或在 DialogueFuncs.Actions 里补一行");
                return;
            }
            try { def.Fn(ctx, call.Args); }
            catch (System.Exception e)
            {
                Debug.LogError($"[对话事件] {call.func} 执行抛异常，已跳过该事件继续播放：\n{e}");
            }
        }

        public static void ExecuteAll(List<DialogueCall> calls, GameplayContext ctx)
        {
            if (calls == null) return;
            foreach (var call in calls) Execute(call, ctx);
        }

        /// <summary>这条事件是不是奖励类（校验器判「奖励必须在路径末尾」用）。未注册时按不是处理。</summary>
        public static bool IsReward(DialogueCall call) =>
            call != null && !string.IsNullOrEmpty(call.func) &&
            Actions.TryGetValue(call.func, out var def) && def.IsReward;

        private static void Warn(string what, string why) =>
            Debug.LogWarning($"[对话事件] {what}：{why}，已跳过");

        private static void Toast(string message)
        {
            // 不用 ?. ——Unity 的「已销毁但引用非 null」要靠重载的 == 才判得出来
            var ui = HouseUIManager.Instance;
            if (ui != null) ui.ShowToast(message);
        }
    }
}

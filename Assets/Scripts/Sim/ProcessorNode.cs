using System.Collections.Generic;
using System.Linq;

namespace MasterPotion
{
    /// <summary>
    /// 加工节点。当前配方由「已连接输入端口的资源种类集合」与配方输入集合精确匹配决定：
    /// 恰好匹配一个配方 -> 执行该配方；匹配 0 个或多个 -> 视为不满足需求，所有入链停止传输。
    /// </summary>
    public class ProcessorNode : NodeBase
    {
        private ProcessorNodeDef PDef => (ProcessorNodeDef)Def;

        private readonly ResourceBuffer inputBuffer = new();
        private RecipeDef activeRecipe;
        private bool recipeConflict;
        private bool crafting;
        private float progress;

        public RecipeDef ActiveRecipe => activeRecipe;
        public float Progress01 =>
            crafting && activeRecipe != null && activeRecipe.craftTime > 0f
                ? progress / activeRecipe.craftTime : 0f;

        public override bool CanAcceptInput(ResourceDef r)
        {
            if (activeRecipe == null) return false;                          // 无匹配配方或配方冲突
            if (activeRecipe.inputs.All(i => i.resource != r)) return false; // 当前配方不需要这种资源
            return inputBuffer.Get(r) < PDef.inputBufferCap;
        }

        public override void ReceiveInput(ResourceDef r) => inputBuffer.Add(r);

        public override void OnConnectionsChanged()
        {
            var connected = new HashSet<ResourceDef>(
                InputPorts.Where(p => p.IsConnected).Select(p => p.Resource));

            var matches = PDef.recipes
                .Where(rec => rec != null && connected.SetEquals(rec.InputTypes))
                .ToList();

            recipeConflict = matches.Count > 1;
            var newRecipe = matches.Count == 1 ? matches[0] : null;

            if (newRecipe != activeRecipe && crafting)
            {
                // 配方被切换：中断当前加工并退还已投入的原料
                foreach (var input in activeRecipe.inputs)
                    inputBuffer.Add(input.resource, input.amount);
                crafting = false;
                progress = 0f;
            }
            activeRecipe = newRecipe;
        }

        public override void SimTick(float dt)
        {
            if (crafting)
            {
                progress += dt;
                if (progress >= activeRecipe.craftTime)
                {
                    if (HasRoomForOutputs(activeRecipe))
                    {
                        foreach (var o in activeRecipe.outputs)
                            outputBuffer.Add(o.resource, o.amount);
                        crafting = false;
                        progress = 0f;
                    }
                    else
                    {
                        progress = activeRecipe.craftTime; // 输出缓存满，完成品滞留
                    }
                }
            }

            if (!crafting && activeRecipe != null &&
                HasAllInputs(activeRecipe) && HasRoomForOutputs(activeRecipe))
            {
                foreach (var i in activeRecipe.inputs)
                    inputBuffer.TryRemove(i.resource, i.amount);
                crafting = true;
                progress = 0f;
            }
        }

        private bool HasAllInputs(RecipeDef r) =>
            r.inputs.All(i => inputBuffer.Get(i.resource) >= i.amount);

        private bool HasRoomForOutputs(RecipeDef r) =>
            r.outputs.All(o => outputBuffer.Get(o.resource) + o.amount <= PDef.outputBufferCap);

        protected override string BuildInfoText()
        {
            string status;
            if (recipeConflict) status = "配方冲突!";
            else if (activeRecipe == null)
                status = InputPorts.Any(p => p.IsConnected) ? "无匹配配方" : "未连接";
            else
                status = activeRecipe.displayName + (crafting ? $"  {(int)(Progress01 * 100)}%" : "  待料");

            return status
                   + "\n入: " + inputBuffer.ToDisplayString()
                   + "\n出: " + outputBuffer.ToDisplayString();
        }
    }
}

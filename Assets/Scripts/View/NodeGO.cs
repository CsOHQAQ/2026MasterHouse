using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 节点表现层（§10 View 类）：占格色块 + Pin 标记 + 头顶数据文字。
    /// View 只读：仅读取绑定的 NodeData 渲染，写操作必须经 Manager（§2）。
    /// 位置/暂存/进度/非法态/中转 Pin 运行时同步均每帧轮询（§2.1 连续量）。
    /// </summary>
    public class NodeGO : MonoBehaviour
    {
        // 占位配色（无美术阶段）：四类节点底色
        private static readonly Color ResourceColor = new Color(0.30f, 0.62f, 0.36f);
        private static readonly Color ProcessorColor = new Color(0.30f, 0.50f, 0.75f);
        private static readonly Color StorageColor = new Color(0.78f, 0.63f, 0.26f);
        private static readonly Color TransitColor = new Color(0.58f, 0.44f, 0.72f);
        private static readonly Color UnknownColor = Color.magenta; // NodeType 未配置的警示色

        /// <summary>非法临时态 tint（§4.3 最简着色提示，正式交互待定 #14）。</summary>
        private static readonly Color IllegalTint = new Color(0.85f, 0.20f, 0.20f);

        public NodeData Data { get; private set; }

        private readonly List<SpriteRenderer> shapeCells = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> pinMarks = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> pinDots = new List<SpriteRenderer>();
        private TextMesh headText;
        private Color baseColor;
        private readonly StringBuilder textBuilder = new StringBuilder();

        public void Bind(NodeData data)
        {
            Data = data;
            baseColor = BaseColorOf(data.Def.NodeType);
            BuildShape();
            BuildPins();
            BuildHeadText();
            UpdateVisual();
        }

        private void Update()
        {
            if (Data == null) return;
            UpdateVisual();
        }

        // ───────────────── 构建（Bind 时一次性）─────────────────

        private void BuildShape()
        {
            float s = ViewUtil.GridSize;
            foreach (var g in Data.Def.Shape.Grids)
            {
                var sr = VisualAssets.CreateSpriteSquare(transform,
                    $"格({g.DeltaPosition.x},{g.DeltaPosition.y})",
                    new Vector3((g.DeltaPosition.x + 0.5f) * s, (g.DeltaPosition.y + 0.5f) * s, 0f),
                    s * 0.98f, baseColor, SortingOrders.Card);
                shapeCells.Add(sr);
            }
        }

        private void BuildPins()
        {
            float s = ViewUtil.GridSize;
            for (int i = 0; i < Data.Pins.Count; i++)
            {
                var layout = Data.Def.Pins[i];
                var toward = Direction4.ToOffset(layout.Facing);
                // Pin 标记贴在所在格朝向外侧的边缘上
                var pos = new Vector3(
                    (layout.LocalCell.x + 0.5f + toward.x * 0.5f) * s,
                    (layout.LocalCell.y + 0.5f + toward.y * 0.5f) * s, 0f);
                pinMarks.Add(VisualAssets.CreateSpriteSquare(transform, $"Pin{i}",
                    pos, 0.32f * s, Color.gray, SortingOrders.Pin));
                // 内部小点标方向：白=输出、黑=输入、灰=未同步（中转 §6.3）
                pinDots.Add(VisualAssets.CreateSpriteSquare(transform, $"Pin{i}方向点",
                    pos, 0.12f * s, Color.gray, SortingOrders.Pin + 1));
            }
        }

        private void BuildHeadText()
        {
            // 文字锚在形状包围盒顶边中央上方
            int minX = 0, maxX = 0, maxY = 0;
            bool first = true;
            foreach (var g in Data.Def.Shape.Grids)
            {
                if (first)
                {
                    minX = maxX = g.DeltaPosition.x;
                    maxY = g.DeltaPosition.y;
                    first = false;
                    continue;
                }
                if (g.DeltaPosition.x < minX) minX = g.DeltaPosition.x;
                if (g.DeltaPosition.x > maxX) maxX = g.DeltaPosition.x;
                if (g.DeltaPosition.y > maxY) maxY = g.DeltaPosition.y;
            }

            float s = ViewUtil.GridSize;
            var pos = new Vector3((minX + maxX + 1) * 0.5f * s, (maxY + 1) * s + 0.15f * s, 0f);
            headText = VisualAssets.CreateWorldText(transform, "头顶信息", pos,
                "", 0.28f * s, TextAnchor.LowerCenter, Color.white, SortingOrders.Text);
        }

        // ───────────────── 每帧刷新（§2.1 连续量轮询）─────────────────

        private void UpdateVisual()
        {
            transform.position = ViewUtil.CellCorner(Data.Origin);

            var color = Data.IsIllegal ? Color.Lerp(baseColor, IllegalTint, 0.65f) : baseColor;
            foreach (var sr in shapeCells)
                sr.color = color;

            for (int i = 0; i < pinMarks.Count; i++)
            {
                var pin = Data.Pins[i];
                pinMarks[i].color = pin.RuntimeItemType != null
                    ? pin.RuntimeItemType.DisplayColor
                    : Color.gray;
                pinDots[i].color = pin.RuntimeDirection == EPinDirection.Output ? Color.white
                    : pin.RuntimeDirection == EPinDirection.Input ? Color.black
                    : new Color(0.45f, 0.45f, 0.45f);
            }

            UpdateHeadText();
        }

        private void UpdateHeadText()
        {
            var sb = textBuilder;
            sb.Length = 0;
            sb.Append(DisplayNameOf(Data.Def));

            switch (Data.Def.NodeType)
            {
                case ENodeType.Resource:
                    sb.Append("\n存 ");
                    AppendStorage(sb, Data.OutputStorage);
                    break;
                case ENodeType.Processor:
                    sb.Append("\n入 ");
                    AppendStorage(sb, Data.InputStorage);
                    sb.Append("  出 ");
                    AppendStorage(sb, Data.OutputStorage);
                    sb.Append('\n');
                    AppendRecipeProgress(sb);
                    break;
                case ENodeType.Transit:
                    sb.Append("\n存 ");
                    AppendStorage(sb, Data.OutputStorage);
                    break;
                // Storage：漏斗无暂存（§7），累计产出看调试面板的 PlayerCargo
            }

            if (Data.IsIllegal)
                sb.Append("\n[位置冲突]");

            // 测试场景节点量小，接受每帧构建字符串的开销
            headText.text = sb.ToString();
        }

        private void AppendRecipeProgress(StringBuilder sb)
        {
            var recipe = ((ProcessorNodeDef)Data.Def).Recipe;
            if (recipe == null)
            {
                sb.Append("无配方"); // 待定 #3：先按策划配单条配方
                return;
            }
            if (!Data.RecipeInProgress)
            {
                sb.Append("待料");
                return;
            }
            int percent = recipe.WorkTicks > 0
                ? Data.RecipeProgressTicks * 100 / recipe.WorkTicks
                : 100;
            sb.Append("加工 ").Append(percent).Append('%');
        }

        private static void AppendStorage(StringBuilder sb, ItemStorage storage)
        {
            if (storage == null)
            {
                sb.Append('-');
                return;
            }
            bool any = false;
            foreach (var slot in storage.Slots) // List 顺序稳定（首次入库序）
            {
                if (slot.Count <= 0) continue;
                if (any) sb.Append(' ');
                sb.Append(DisplayNameOf(slot.Item)).Append('x').Append(slot.Count);
                any = true;
            }
            if (!any) sb.Append('空');
        }

        private static Color BaseColorOf(ENodeType type)
        {
            switch (type)
            {
                case ENodeType.Resource: return ResourceColor;
                case ENodeType.Processor: return ProcessorColor;
                case ENodeType.Storage: return StorageColor;
                case ENodeType.Transit: return TransitColor;
                default: return UnknownColor;
            }
        }

        private static string DisplayNameOf(NodeDef def) =>
            string.IsNullOrEmpty(def.DisplayName) ? def.name : def.DisplayName;

        private static string DisplayNameOf(ItemDef item) =>
            item == null ? "?"
            : string.IsNullOrEmpty(item.DisplayName) ? item.name : item.DisplayName;
    }
}

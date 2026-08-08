using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 链接表现层（§10 View 类）：折线渲染 + 状态配色（§6.5）。View 只读（§2）。
    /// 空闲/在途=物资色，阻塞=灰，断线/类型失效=红；状态与走线每帧轮询（§2.1 连续量）。
    /// </summary>
    public class LinkGO : MonoBehaviour
    {
        private static readonly Color BlockedColor = new Color(0.55f, 0.55f, 0.55f);
        private static readonly Color InvalidColor = new Color(0.85f, 0.20f, 0.20f);

        public LinkData Data { get; private set; }

        public LineRenderer LineRenderer;

        /// <summary>当前折线对应的途径格引用：检测理线改道后重建折线（§5 后补理线的兼容位）。</summary>
        private List<Vector2Int> boundPath;

        public void Bind(LinkData data)
        {
            Data = data;

            if (LineRenderer == null)
                LineRenderer = gameObject.AddComponent<LineRenderer>();
            LineRenderer.sharedMaterial = VisualAssets.UnlitMaterial;
            LineRenderer.widthMultiplier = 0.16f * ViewUtil.GridSize;
            LineRenderer.numCornerVertices = 2;
            LineRenderer.numCapVertices = 2;
            LineRenderer.useWorldSpace = true;
            LineRenderer.sortingOrder = SortingOrders.Link;
            RebuildPolyline();

            var pulseGO = new GameObject("脉冲");
            pulseGO.transform.SetParent(transform, false);
            pulseGO.AddComponent<TransferPulseGO>().Bind(data);
        }

        private void Update()
        {
            if (Data == null) return;

            if (!ReferenceEquals(boundPath, Data.PathCells) ||
                LineRenderer.positionCount != Data.PathCells.Count)
                RebuildPolyline();

            var color = StateColor(Data);
            LineRenderer.startColor = color;
            LineRenderer.endColor = color;
        }

        private void RebuildPolyline()
        {
            boundPath = Data.PathCells;
            LineRenderer.positionCount = boundPath.Count;
            for (int i = 0; i < boundPath.Count; i++)
                LineRenderer.SetPosition(i, ViewUtil.CellCenter(boundPath[i]));
        }

        /// <summary>状态配色（§6.5）。类型失效的警示图标暂缓，先与断线同色。</summary>
        private static Color StateColor(LinkData link)
        {
            switch (link.State)
            {
                case ELinkState.Blocked:
                    return BlockedColor;
                case ELinkState.Broken:
                case ELinkState.TypeInvalid:
                    return InvalidColor;
                default:
                    return link.ItemType != null ? link.ItemType.DisplayColor : Color.white;
            }
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MasterHouse
{
    /// <summary>
    /// 件库条目的「拖到棋盘上摆放」手势。**只做事件转发**，不碰任何玩法数据：
    /// 拖的是哪种件、落点合不合法，全由 <see cref="CircuitMinigame"/> / <see cref="CircuitBoard"/> 判定。
    ///
    /// 运行时由 <see cref="CircuitMinigame"/> 挂到每个克隆出来的条目上，模板 Prefab 上不挂——
    /// 条目本来就是代码克隆的，两处都放等于同一件事有两个来源（§16.2 禁止双实现）。
    ///
    /// **与「先点件库再点棋盘」并存**：UGUI 只有在指针于同一个对象上按下并抬起时才发 onClick，
    /// 拖到棋盘上松手不会触发件库条目的 onClick，两条路径不会把同一次操作摆两遍。
    /// </summary>
    public sealed class CircuitPaletteDragHandler : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private NodeDef def;
        private Action<NodeDef, PointerEventData> onBegin;
        private Action<PointerEventData> onMove;
        private Action<PointerEventData> onEnd;

        public void Init(NodeDef nodeDef, Action<NodeDef, PointerEventData> begin,
            Action<PointerEventData> move, Action<PointerEventData> end)
        {
            def = nodeDef;
            onBegin = begin;
            onMove = move;
            onEnd = end;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            // 右键在棋盘上是删线/取消选中，不该在件库上被解读成拖件
            if (eventData.button != PointerEventData.InputButton.Left) return;
            onBegin?.Invoke(def, eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            onMove?.Invoke(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            onEnd?.Invoke(eventData);
        }
    }
}

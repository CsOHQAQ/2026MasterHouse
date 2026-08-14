using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// Hub 访客列表绑定：语义为「当前在场访客」（访客交付说明 §10，周制退役）。
    /// Prefab 内既有 4 张卡位，按在场实例（InstanceId 升序）填充，超出卡位的实例暂不上墙、空卡隐藏；
    /// 实例进离场/状态变化时由 HubPage 订阅业务事件整体刷新。
    /// </summary>
    public sealed class HubGuestRailBinder
    {
        private OutGameHubGuestRailView view;
        private HubPage page;

        public void Bind(OutGameHubGuestRailView rail, HubPage owner)
        {
            view = rail;
            page = owner;
            Refresh();
        }

        /// <summary>在场实例变化后整体重绑（数量少，全量刷新）。</summary>
        public void Refresh()
        {
            if (view == null) return;
            var visitor = GameManager.Instance.VisitorManager;
            var instances = visitor.Data.Instances;
            view.title.text = "VISITOR EVENTS / 当前在场访客";
            view.remaining.text = visitor.CountOnStage.ToString("00");
            for (var i = 0; i < view.cards.Length; i++)
            {
                var card = view.cards[i];
                if (card == null) continue;
                if (i >= instances.Count)
                {
                    card.gameObject.SetActive(false);
                    continue;
                }
                card.gameObject.SetActive(true);
                var instance = instances[i];
                var instanceId = instance.InstanceId;
                // 小卡跟对话无关，顶的永远是种族的默认脸（2026-08-14 立绘 ID 化：ID 查立绘表）
                var portraits = GameManager.Instance.PortraitTable;
                card.portrait.texture = portraits != null && instance.Race != null
                    ? portraits.TextureOf(instance.Race.defaultPortraitId)
                    : null;
                // 所在房间跟着显示（四宫格拖拽换房后靠这里一眼找到人）
                card.eventLabel.text = $"VISITOR {instance.InstanceId:00} · {RoomLabel(instance)}";
                card.guestName.text = instance.DisplayName;
                card.status.text = StatusText(visitor, instance);
                card.typeLabel.text = visitor.WantsAttention(instance) ? "！" : TypeMark(instance.State);
                HouseUIUtil.ApplyPanelSkin(card.background, .8f, 2.5f); // 访客卡换 common 框（半透明）
                card.eventLabel.color = card.guestName.color = card.status.color = HouseUIUtil.White;
                // 点击音关掉：访客卡响的是交互音（音效需求 #3），由 SelectGuest 统一发声
                HouseUIUtil.BindButton(card.button, () => page.SelectGuest(instanceId), ESfx.None);
            }
        }

        /// <summary>
        /// 房间标注。「等待分配房间」显示「待分房」而不是「起居室」（需求重做说明 §10）——
        /// 他人是在起居室没错，但玩家要看的是「这位还没有房间」，标成起居室会读成「已经住下了」。
        /// </summary>
        private static string RoomLabel(VisitorInstance instance)
        {
            if (instance.State == EVisitorState.AwaitingRoom) return "待分房";
            var rooms = GameManager.Instance.CodexTable.rooms;
            var roomIndex = instance.RoomIndex;
            return roomIndex >= 0 && roomIndex < rooms.Count ? rooms[roomIndex].displayName : "屋内";
        }

        /// <summary>
        /// 状态文案。点了没有对话的客人，卡上就得说清楚为什么，否则玩家只会觉得「点了没反应」。
        /// **判据一律走 `VisitorManager.NoTalkReason`**，与 Hub 的 Toast 同一个来源——
        /// 卡上和 Toast 各写一套 switch 迟早会漂开（曾经就漂过：卡上说「腾不出房间」，
        /// 实际原因却是别人在等分房）。
        /// </summary>
        private static string StatusText(VisitorManager visitor, VisitorInstance instance)
        {
            var reason = visitor.NoTalkReason(instance);
            if (reason == VisitorManager.ENoTalkReason.None)
                return instance.State == EVisitorState.Serving ? "有话要说 · 点击交谈" : "门口等待接待 · 点击交谈";

            return reason switch
            {
                VisitorManager.ENoTalkReason.NotFrontOfQueue => "门口排队中 · 等前面那位",
                VisitorManager.ENoTalkReason.SomeoneAwaitingRoom => "门口等着 · 先安顿上一位",
                VisitorManager.ENoTalkReason.NoFreeRoom => "门口等着 · 客房已住满",
                VisitorManager.ENoTalkReason.AwaitingRoom => "待分房 · 拖进一间空房",
                VisitorManager.ENoTalkReason.SettlingIn => "正在安顿 · 稍等一会儿",
                _ => instance.State == EVisitorState.Wandering ? "心满意足 · 屋内闲逛" : "正在离开",
            };
        }

        private static string TypeMark(EVisitorState state) => state switch
        {
            EVisitorState.FrontDesk => "待",
            EVisitorState.AwaitingRoom => "房",
            EVisitorState.Serving => "服",
            EVisitorState.Wandering => "逛",
            _ => "…",
        };
    }
}

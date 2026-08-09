using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 商城面板绑定（§16.8 P0 经济链路消费出口）：三值钱包 + 家具货架。
    /// 解禁（声望）与购买（货币）是两道独立的门，逻辑全在 EconomyManager，这里只做展示与转发。
    /// </summary>
    public static class MarketPanelBinder
    {
        public static void Bind(MarketPanelView view, HubPage page)
        {
            if (view == null) return;
            Refresh(view, page);
        }

        private static void Refresh(MarketPanelView view, HubPage page)
        {
            var economy = GameManager.Instance.EconomyManager;
            if (view.walletText != null)
                view.walletText.text =
                    $"<size=13>流通数值</size>\n<size=28><color=#E3A869>◈ {economy.Data.Currency:N0}</color></size>\n<color=#74D8D1>声望 {economy.Data.Reputation}</color>    <color=#E22D76>装饰分 {economy.DecorationScore}</color>";

            if (view.cardsRoot == null) return;
            for (var i = view.cardsRoot.childCount - 1; i >= 0; i--)
                Object.Destroy(view.cardsRoot.GetChild(i).gameObject);

            var table = Resources.Load<FurnitureTable>("OutGameUI/FurnitureTable");
            if (table == null || table.entries.Count == 0)
            {
                Debug.LogError("[HouseUI] 家具配置表缺失：请执行菜单 MasterHouse → 家具系统 → 创建配置表");
                return;
            }
            var template = Resources.Load<GameObject>(OutGamePrefabResourcePaths.MarketCard);
            if (template == null)
            {
                Debug.LogError("[HouseUI] 商城卡模板 Prefab 缺失（§16.2）：" + OutGamePrefabResourcePaths.MarketCard);
                return;
            }

            for (var i = 0; i < table.entries.Count; i++)
            {
                var entry = table.entries[i];
                if (entry == null) continue;
                var instance = Object.Instantiate(template, view.cardsRoot, false);
                instance.name = "Market_" + entry.id;
                var card = instance.GetComponent<MarketCardView>();
                if (card == null) continue;
                var rect = (RectTransform)instance.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
                rect.anchoredPosition = new Vector2(-472 + i % 5 * 236, 130 - i / 5 * 235);

                var owned = economy.IsFurnitureOwned(entry.id);
                var revealed = economy.IsFurnitureRevealed(entry);
                if (card.label != null)
                {
                    if (!revealed)
                        // 文档：未解禁 Item 在商城/图鉴中呈「？」状态
                        card.label.text = $"<size=42>？</size>\n<size=14>声望 {entry.unlockReputation} 解禁</size>";
                    else if (owned)
                        card.label.text = $"\n\n\n<size=17>{entry.displayName}</size>\n<size=14><color=#9AE2B8>已拥有</color></size>";
                    else
                        card.label.text = $"\n\n\n<size=17>{entry.displayName}</size>\n<color=#E3A869>◈ {entry.price}</color>";
                }
                if (card.background != null)
                    card.background.color = !revealed ? new Color(.06f, .05f, .08f, .85f)
                        : owned ? new Color(.05f, .07f, .06f, .8f)
                        : new Color(.1f, .04f, .09f, .86f);
                if (card.thumb != null)
                {
                    var showThumb = revealed && entry.sprite != null;
                    card.thumb.gameObject.SetActive(showThumb);
                    if (showThumb)
                    {
                        card.thumb.sprite = entry.sprite;
                        card.thumb.color = owned ? new Color(1, 1, 1, .45f) : Color.white;
                    }
                }
                HouseUIUtil.BindButton(card.button, () =>
                {
                    if (!economy.IsFurnitureRevealed(entry))
                    {
                        page.Toast($"声望达到 {entry.unlockReputation} 后解禁（当前 {economy.Data.Reputation}）");
                        return;
                    }
                    if (economy.IsFurnitureOwned(entry.id))
                    {
                        page.Toast($"「{entry.displayName}」已拥有，可在「家具摆放」中使用");
                        return;
                    }
                    if (economy.TryPurchaseFurniture(entry) == FurniturePurchaseResult.Success)
                    {
                        // 旧壳此处落档；存档功能移除（§16.5）
                        page.Toast($"已购入「{entry.displayName}」 · ◈ -{entry.price}");
                        Refresh(view, page);
                    }
                    else
                    {
                        page.Toast("货币不足：完成客人服务可以获得 ◈");
                    }
                });
            }
            HouseUIUtil.ApplyFallbackFont(view.cardsRoot);
        }
    }
}

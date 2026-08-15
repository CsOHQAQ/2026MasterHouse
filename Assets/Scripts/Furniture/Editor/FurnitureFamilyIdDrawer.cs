using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// `[FurnitureFamilyId]` 的属性抽屉：把 string 字段画成「从家具族表读出全部族」的下拉。
    ///
    /// 结构与 <see cref="FurnitureIdDrawer"/> 一一对应（缺表退回手填 + 警告、失联值原样置顶不静默吞掉），
    /// 差别只在两处：读的是族表，而**门槛按整族取最低**——条件类需求配族的语义是
    /// 「随便什么颜色都行」，那么玩家能不能做到，取决于这一族里**最便宜、最早解禁**的那个配色。
    /// </summary>
    [CustomPropertyDrawer(typeof(FurnitureFamilyIdAttribute))]
    public sealed class FurnitureFamilyIdDrawer : PropertyDrawer
    {
        private const string FamilyTablePath = "Assets/Resources/OutGameUI/FurnitureFamilyTable.asset";
        private const string FurnitureTablePath = "Assets/Resources/OutGameUI/FurnitureTable.asset";

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var line = EditorGUIUtility.singleLineHeight;
            if (property.propertyType != SerializedPropertyType.String) return line * 2 + 2;
            return NeedsHint(property) ? line * 2 + 2 : line;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.HelpBox(position, "[FurnitureFamilyId] 只能标在 string 字段上", MessageType.Error);
                return;
            }

            EditorGUI.BeginProperty(position, label, property);

            var line = EditorGUIUtility.singleLineHeight;
            var first = new Rect(position.x, position.y, position.width, line);
            var second = new Rect(position.x, position.y + line + 2, position.width, line);

            var table = AssetDatabase.LoadAssetAtPath<FurnitureFamilyTable>(FamilyTablePath);
            if (table == null)
            {
                EditorGUI.BeginChangeCheck();
                var manual = EditorGUI.TextField(first, label, property.stringValue);
                if (EditorGUI.EndChangeCheck()) property.stringValue = manual;
                Hint(second, $"未找到家具族表（{FamilyTablePath}），暂时手填族 id");
                EditorGUI.EndProperty();
                return;
            }

            var current = property.stringValue ?? string.Empty;
            var ids = new List<string>();
            var labels = new List<string>();

            var missing = !string.IsNullOrEmpty(current) && table.Find(current) == null;
            if (missing)
            {
                ids.Add(current);
                labels.Add(Escape($"⚠ {current}（不在家具族表）"));
            }
            ids.Add(string.Empty);
            labels.Add("（未选择）");

            var furniture = AssetDatabase.LoadAssetAtPath<FurnitureTable>(FurnitureTablePath);
            var store = AssetDatabase.LoadAssetAtPath<StoreTable>(FurnitureIdDrawer.StoreTablePath);
            foreach (var family in table.entries)
            {
                if (family == null || string.IsNullOrEmpty(family.familyId)) continue;
                ids.Add(family.familyId);
                var name = string.IsNullOrEmpty(family.displayName) ? family.familyId
                    : $"{family.displayName}（{family.familyId}）";
                labels.Add(Escape(name + MemberSummary(furniture, store, family.familyId)));
            }

            var index = Mathf.Max(0, ids.IndexOf(current));
            EditorGUI.BeginChangeCheck();
            var picked = EditorGUI.Popup(first, label.text, index, labels.ToArray());
            if (EditorGUI.EndChangeCheck() && picked >= 0 && picked < ids.Count)
                property.stringValue = ids[picked];

            if (missing) Hint(second, $"「{current}」不在家具族表中（可能已改名或删行），请重选");

            EditorGUI.EndProperty();
        }

        /// <summary>
        /// 下拉项后缀「×8 · 最低 ◈ 300 · 声望 40」：配色数量 + **全族最低**售价与解禁门槛。
        /// 取最低是因为按族匹配只要求任意一个配色，玩家自然会挑最容易到手的那个。
        /// </summary>
        private static string MemberSummary(FurnitureTable furniture, StoreTable store, string familyId)
        {
            if (furniture == null) return string.Empty;
            var count = 0;
            var minPrice = int.MaxValue;
            var minUnlock = int.MaxValue;
            foreach (var entry in furniture.entries)
            {
                if (entry == null || entry.familyId != familyId) continue;
                count++;
                if (store == null) continue;
                var sale = store.Find(entry.id);
                var price = sale != null ? sale.price : 0;          // 不在商店表 = 非卖品，等价 0 / 0
                var unlock = sale != null ? sale.unlockReputation : 0;
                if (price < minPrice) minPrice = price;
                if (unlock < minUnlock) minUnlock = unlock;
            }
            if (count == 0) return "  · ⚠ 该族没有任何家具";
            var summary = $"  ×{count}";
            if (store == null || minPrice == int.MaxValue) return summary;
            summary += minPrice > 0 ? $" · 最低 ◈ {minPrice}" : " · 初始拥有";
            if (minUnlock > 0) summary += $" · 声望 {minUnlock}";
            return summary;
        }

        private static bool NeedsHint(SerializedProperty property)
        {
            var table = AssetDatabase.LoadAssetAtPath<FurnitureFamilyTable>(FamilyTablePath);
            if (table == null) return true;
            var current = property.stringValue;
            return !string.IsNullOrEmpty(current) && table.Find(current) == null;
        }

        private static void Hint(Rect rect, string message)
        {
            var style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(1f, .6f, .45f) } };
            EditorGUI.LabelField(rect, GUIContent.none, new GUIContent(message), style);
        }

        /// <summary>EditorGUI.Popup 会把 '/' 解释成子菜单，显示名里若带斜杠会被拆开——换成全角避开。</summary>
        private static string Escape(string text) => text.Replace('/', '／');
    }
}

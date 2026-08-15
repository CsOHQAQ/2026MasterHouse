using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// `[FurnitureId]` 的属性抽屉：把 string 字段画成「从家具表读出全部行」的下拉。
    ///
    /// 做成 PropertyDrawer 而不是写在某个编辑器窗口里（需求重做说明 §4.5）——这样
    /// **窗口内编辑**与**在 Project 视图里直接选中资产编辑**两条路径都生效，不会出现
    /// 「换个入口就变成手打字符串」的裂口。
    ///
    /// 本抽屉的下拉逻辑源自旧 `NodeSim/Editor/LevelDefEditorWindow.DrawFurnitureBinding`
    /// （关卡关联家具）。那是全工程唯一一处同款实现，**该实现已于小游戏框架落地第 1 步删除**
    /// （NodeSim → Minigame/Circuit 搬家），此后全工程只剩本抽屉一处
    /// ——顺带解掉需求重做说明 §4.5 的时序陷阱。
    /// </summary>
    [CustomPropertyDrawer(typeof(FurnitureIdAttribute))]
    public sealed class FurnitureIdDrawer : PropertyDrawer
    {
        /// <summary>家具表资产路径（家具是表里的一行，不是独立资产，故只能按 id 关联）。</summary>
        private const string FurnitureTablePath = "Assets/Resources/OutGameUI/FurnitureTable.asset";
        /// <summary>商店表：下拉里带出售价与解禁声望，策划一眼能看出「这条需求得玩家有 40 声望才做得了」。</summary>
        internal const string StoreTablePath = "Assets/Resources/OutGameUI/StoreTable.asset";

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var line = EditorGUIUtility.singleLineHeight;
            if (property.propertyType != SerializedPropertyType.String) return line * 2 + 2;
            // 家具表缺失、或当前 id 不在表里时，下方补一行提示
            return NeedsHint(property) ? line * 2 + 2 : line;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.HelpBox(position, "[FurnitureId] 只能标在 string 字段上", MessageType.Error);
                return;
            }

            EditorGUI.BeginProperty(position, label, property);

            var line = EditorGUIUtility.singleLineHeight;
            var first = new Rect(position.x, position.y, position.width, line);
            var second = new Rect(position.x, position.y + line + 2, position.width, line);

            var table = AssetDatabase.LoadAssetAtPath<FurnitureTable>(FurnitureTablePath);
            if (table == null)
            {
                // 家具表缺失不阻塞编辑：退回手填 + 警告（§4.5）
                EditorGUI.BeginChangeCheck();
                var manual = EditorGUI.TextField(first, label, property.stringValue);
                if (EditorGUI.EndChangeCheck()) property.stringValue = manual;
                Hint(second, $"未找到家具表（{FurnitureTablePath}），暂时手填 id");
                EditorGUI.EndProperty();
                return;
            }

            var current = property.stringValue ?? string.Empty;
            var ids = new List<string>();
            var labels = new List<string>();

            // 当前值不在表里时，把它原样放在第 0 项：下拉显示真实值而不是假装「未选择」，
            // 且不选就不改——改名/删行导致的失联要让人看见，不能被静默吞掉
            var missing = !string.IsNullOrEmpty(current) && table.Find(current) == null;
            if (missing)
            {
                ids.Add(current);
                labels.Add(Escape($"⚠ {current}（不在家具表）"));
            }
            ids.Add(string.Empty);
            labels.Add("（未选择）");
            // 家具行数不多，每次绘制重建选项即可，新导表立刻生效（§4.5）
            var store = AssetDatabase.LoadAssetAtPath<StoreTable>(StoreTablePath);
            foreach (var entry in table.entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.id)) continue;
                ids.Add(entry.id);
                var name = string.IsNullOrEmpty(entry.displayName) ? entry.id : $"{entry.displayName}（{entry.id}）";
                labels.Add(Escape(name + SaleSuffix(store, entry.id)));
            }

            var index = Mathf.Max(0, ids.IndexOf(current));
            EditorGUI.BeginChangeCheck();
            var picked = EditorGUI.Popup(first, label.text, index, labels.ToArray());
            if (EditorGUI.EndChangeCheck() && picked >= 0 && picked < ids.Count)
                property.stringValue = ids[picked];

            if (missing) Hint(second, $"「{current}」不在家具表中（可能已改名或删行），请重选");

            EditorGUI.EndProperty();
        }

        private static bool NeedsHint(SerializedProperty property)
        {
            var table = AssetDatabase.LoadAssetAtPath<FurnitureTable>(FurnitureTablePath);
            if (table == null) return true;
            var current = property.stringValue;
            return !string.IsNullOrEmpty(current) && table.Find(current) == null;
        }

        private static void Hint(Rect rect, string message)
        {
            var style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(1f, .6f, .45f) } };
            EditorGUI.LabelField(rect, GUIContent.none, new GUIContent(message), style);
        }

        /// <summary>EditorGUI.Popup 会把 '/' 解释成子菜单，家具显示名里若带斜杠会被拆开——换成全角避开。</summary>
        private static string Escape(string text) => text.Replace('/', '／');

        /// <summary>
        /// 下拉项的售卖后缀「◈ 300 · 声望 40」（家具族体系说明 §4.2）：
        /// 需求点名一件高门槛家具时，策划得在**配需求的当下**就看见门槛，而不是等玩家卡住才发现。
        /// 不在商店表里 = 非卖品（初始就拥有），标成「初始拥有」。
        /// </summary>
        internal static string SaleSuffix(StoreTable store, string furnitureId)
        {
            if (store == null) return string.Empty;
            var sale = store.Find(furnitureId);
            if (sale == null) return "  · 初始拥有";
            var unlock = sale.unlockReputation > 0 ? $" · 声望 {sale.unlockReputation}" : string.Empty;
            return $"  · ◈ {sale.price}{unlock}";
        }
    }
}

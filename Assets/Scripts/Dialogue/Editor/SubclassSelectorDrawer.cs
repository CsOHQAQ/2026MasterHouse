using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// [SerializeReference] 字段的子类选择抽屉（设计说明 §11.1「必做前置」）。
    /// Unity 原生不给这类字段任何「选哪个子类」的 UI——没有本抽屉，对话的事件与条件
    /// 在 Inspector 里根本加不进去，整套多态设计等于不可用。
    ///
    /// 候选类型用 TypeCache 检索（编译期索引，比反射扫程序集快得多），
    /// 赋值走 property.managedReferenceValue = Activator.CreateInstance(type)。
    /// </summary>
    [CustomPropertyDrawer(typeof(SubclassSelectorAttribute))]
    public sealed class SubclassSelectorDrawer : PropertyDrawer
    {
        private const string NoneLabel = "（未选择）";

        /// <summary>候选类型缓存，键 = 基类型全名。静态字段在域重载时自动清空，与 TypeCache 的更新时机一致。</summary>
        private static readonly Dictionary<string, List<Type>> CandidateCache = new Dictionary<string, List<Type>>();

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.ManagedReference)
            {
                EditorGUI.LabelField(position, label,
                    new GUIContent("[SubclassSelector] 只能用在 [SerializeReference] 字段上"));
                return;
            }

            var baseType = ResolveType(property.managedReferenceFieldTypename);
            var currentType = ResolveType(property.managedReferenceFullTypename);

            var lineHeight = EditorGUIUtility.singleLineHeight;
            var labelWidth = EditorGUIUtility.labelWidth;
            var headerRect = new Rect(position.x, position.y, position.width, lineHeight);
            var titleRect = new Rect(headerRect.x, headerRect.y, labelWidth, lineHeight);
            var dropdownRect = new Rect(headerRect.x + labelWidth, headerRect.y,
                Mathf.Max(60f, headerRect.width - labelWidth), lineHeight);

            // 有实例才给折叠箭头——空引用没有子字段可展开
            if (currentType != null)
                property.isExpanded = EditorGUI.Foldout(titleRect, property.isExpanded, label, true);
            else
                EditorGUI.LabelField(titleRect, label);

            var buttonLabel = currentType != null ? DisplayNameOf(currentType) : NoneLabel;
            if (EditorGUI.DropdownButton(dropdownRect, new GUIContent(buttonLabel), FocusType.Keyboard))
                ShowTypeMenu(property, baseType, currentType);

            if (currentType == null || !property.isExpanded) return;

            EditorGUI.indentLevel++;
            var y = headerRect.yMax + EditorGUIUtility.standardVerticalSpacing;
            foreach (var child in Children(property))
            {
                var height = EditorGUI.GetPropertyHeight(child, true);
                EditorGUI.PropertyField(new Rect(position.x, y, position.width, height), child, true);
                y += height + EditorGUIUtility.standardVerticalSpacing;
            }
            EditorGUI.indentLevel--;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var height = EditorGUIUtility.singleLineHeight;
            if (property.propertyType != SerializedPropertyType.ManagedReference) return height;
            if (string.IsNullOrEmpty(property.managedReferenceFullTypename) || !property.isExpanded) return height;
            foreach (var child in Children(property))
                height += EditorGUI.GetPropertyHeight(child, true) + EditorGUIUtility.standardVerticalSpacing;
            return height;
        }

        // ── 类型菜单 ──

        private static void ShowTypeMenu(SerializedProperty property, Type baseType, Type currentType)
        {
            // 菜单回调是延迟执行的，那时手上这个 SerializedProperty 可能已失效——
            // 只捕获 serializedObject 与 propertyPath，回调里重新 FindProperty。
            var serialized = property.serializedObject;
            var path = property.propertyPath;
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent(NoneLabel), currentType == null, () => Assign(serialized, path, null));

            if (baseType == null)
            {
                menu.AddDisabledItem(new GUIContent("无法解析字段基类型（检查字段是否为 [SerializeReference]）"));
                menu.ShowAsContext();
                return;
            }

            var candidates = Candidates(baseType);
            if (candidates.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent($"没有可用的 {baseType.Name} 实现类"));
                menu.ShowAsContext();
                return;
            }

            menu.AddSeparator(string.Empty);
            foreach (var type in candidates)
            {
                var captured = type;
                menu.AddItem(new GUIContent(DisplayNameOf(captured)), captured == currentType,
                    () => Assign(serialized, path, captured));
            }
            menu.ShowAsContext();
        }

        private static void Assign(SerializedObject serialized, string path, Type type)
        {
            if (serialized == null || serialized.targetObject == null) return;
            serialized.Update();
            var property = serialized.FindProperty(path);
            if (property == null) return;
            property.managedReferenceValue = type != null ? Activator.CreateInstance(type) : null;
            property.isExpanded = true;
            serialized.ApplyModifiedProperties();
        }

        private static List<Type> Candidates(Type baseType)
        {
            var key = baseType.AssemblyQualifiedName ?? baseType.FullName;
            if (key != null && CandidateCache.TryGetValue(key, out var cached)) return cached;

            var list = new List<Type>();
            foreach (var type in TypeCache.GetTypesDerivedFrom(baseType))
            {
                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition) continue;
                // [SerializeReference] 只接受普通托管对象，UnityEngine.Object 派生类要用普通引用字段
                if (typeof(UnityEngine.Object).IsAssignableFrom(type)) continue;
                // Activator.CreateInstance 需要公开无参构造
                if (type.GetConstructor(Type.EmptyTypes) == null) continue;
                if (!Attribute.IsDefined(type, typeof(SerializableAttribute))) continue;
                list.Add(type);
            }
            // 稳定排序：按显示名字典序。禁止依赖 TypeCache 的返回顺序（§11.2 的精神对编辑器列表同样适用，
            // 否则不同机器/不同编译顺序下菜单项会跳来跳去）
            list.Sort((a, b) => string.CompareOrdinal(DisplayNameOf(a), DisplayNameOf(b)));
            if (key != null) CandidateCache[key] = list;
            return list;
        }

        // ── 工具 ──

        /// <summary>Unity 的 managedReference 类型名格式是「程序集名 空格 类型全名」。</summary>
        private static Type ResolveType(string typename)
        {
            if (string.IsNullOrEmpty(typename)) return null;
            var separator = typename.IndexOf(' ');
            if (separator <= 0) return null;
            var assembly = typename.Substring(0, separator);
            var fullName = typename.Substring(separator + 1);
            return Type.GetType($"{fullName}, {assembly}");
        }

        private static string DisplayNameOf(Type type)
        {
            var label = (SubclassLabelAttribute)Attribute.GetCustomAttribute(
                type, typeof(SubclassLabelAttribute), false);
            return label != null && !string.IsNullOrEmpty(label.Label)
                ? label.Label
                : ObjectNames.NicifyVariableName(type.Name);
        }

        private static IEnumerable<SerializedProperty> Children(SerializedProperty property)
        {
            var iterator = property.Copy();
            var end = property.GetEndProperty();
            if (!iterator.NextVisible(true)) yield break;
            do
            {
                if (SerializedProperty.EqualContents(iterator, end)) yield break;
                yield return iterator.Copy();
            } while (iterator.NextVisible(false));
        }
    }
}

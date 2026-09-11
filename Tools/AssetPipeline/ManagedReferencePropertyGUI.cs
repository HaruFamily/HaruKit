using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace HaruFamily.Tools.AssetPipeline.Editor
{
    internal static class ManagedReferencePropertyGUI
    {
        private static readonly Dictionary<Type, List<Type>> concreteTypes = new Dictionary<Type, List<Type>>();

        internal static void Draw(SerializedProperty property)
        {
            if (property.propertyType == SerializedPropertyType.ManagedReference)
            {
                DrawManagedReference(property);
                return;
            }

            if (property.isArray && property.propertyType != SerializedPropertyType.String)
            {
                property.isExpanded = EditorGUILayout.Foldout(property.isExpanded, property.displayName, true);
                if (!property.isExpanded) return;

                EditorGUI.indentLevel++;
                property.arraySize = Mathf.Max(0, EditorGUILayout.IntField("Size", property.arraySize));
                for (int i = 0; i < property.arraySize; i++)
                    Draw(property.GetArrayElementAtIndex(i));
                EditorGUI.indentLevel--;
                return;
            }

            if (property.hasVisibleChildren && property.propertyType == SerializedPropertyType.Generic)
            {
                property.isExpanded = EditorGUILayout.Foldout(property.isExpanded, property.displayName, true);
                if (!property.isExpanded) return;
                DrawChildren(property);
                return;
            }

            EditorGUILayout.PropertyField(property, false);
        }

        private static void DrawManagedReference(SerializedProperty property)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(property.displayName);
            string label = property.managedReferenceValue == null ? "None" : Nicify(property.managedReferenceValue.GetType().Name);
            if (EditorGUILayout.DropdownButton(new GUIContent(label), FocusType.Keyboard))
                ShowTypeMenu(property);
            EditorGUILayout.EndHorizontal();

            if (property.managedReferenceValue == null) return;
            DrawChildren(property);
        }

        private static void DrawChildren(SerializedProperty property)
        {
            SerializedProperty child = property.Copy();
            SerializedProperty end = child.GetEndProperty();
            bool enterChildren = true;
            EditorGUI.indentLevel++;
            while (child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end))
            {
                enterChildren = false;
                if (child.depth != property.depth + 1) continue;
                Draw(child.Copy());
            }
            EditorGUI.indentLevel--;
        }

        private static void ShowTypeMenu(SerializedProperty property)
        {
            Type baseType = ResolveFieldType(property.managedReferenceFieldTypename);
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("None"), property.managedReferenceValue == null, () => SetType(property, null));
            if (baseType == null)
            {
                menu.AddDisabledItem(new GUIContent("Unknown declared type"));
                menu.ShowAsContext();
                return;
            }

            foreach (Type type in GetConcreteTypes(baseType))
            {
                Type selectedType = type;
                bool selected = property.managedReferenceValue != null && property.managedReferenceValue.GetType() == type;
                menu.AddItem(new GUIContent(Nicify(type.Name)), selected, () => SetType(property, selectedType));
            }
            menu.ShowAsContext();
        }

        private static void SetType(SerializedProperty property, Type type)
        {
            SerializedObject owner = property.serializedObject;
            owner.Update();
            SerializedProperty current = owner.FindProperty(property.propertyPath);
            Undo.RecordObject(owner.targetObject, "Change Managed Reference Type");
            current.managedReferenceValue = type == null ? null : Activator.CreateInstance(type);
            owner.ApplyModifiedProperties();
            EditorUtility.SetDirty(owner.targetObject);
            AssetDatabase.SaveAssets();
        }

        internal static List<Type> GetConcreteTypes(Type baseType)
        {
            if (concreteTypes.TryGetValue(baseType, out List<Type> cached)) return cached;

            var result = new List<Type>();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }

                foreach (Type type in types)
                    if (type != null && !type.IsAbstract && !type.IsInterface && !type.ContainsGenericParameters && baseType.IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) != null)
                        result.Add(type);
            }
            result.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            concreteTypes.Add(baseType, result);
            return result;
        }

        private static Type ResolveFieldType(string fieldTypeName)
        {
            if (string.IsNullOrEmpty(fieldTypeName)) return null;
            int split = fieldTypeName.IndexOf(' ');
            if (split < 0) return null;
            string assemblyName = fieldTypeName.Substring(0, split);
            string typeName = fieldTypeName.Substring(split + 1);
            return Type.GetType($"{typeName}, {assemblyName}");
        }

        internal static string Nicify(string name)
        {
            return ObjectNames.NicifyVariableName(name.Replace("PipelineAsset_", string.Empty).Replace("Formula_", string.Empty));
        }
    }
}

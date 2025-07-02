using UnityEditor;
using UnityEngine;
using System;
using System.Linq;

[CustomPropertyDrawer(typeof(SerializeReferenceMenuAttribute))]
public class SerializeReferenceMenuDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.PropertyField(position, property, label, true);

        // Уменьшаем прямоугольник для кнопки, чтобы она появилась под списком
        Rect buttonRect = position;
        buttonRect.y += EditorGUI.GetPropertyHeight(property, true) + EditorGUIUtility.standardVerticalSpacing;
        buttonRect.height = EditorGUIUtility.singleLineHeight;
        buttonRect.x += EditorGUIUtility.labelWidth;
        buttonRect.width -= EditorGUIUtility.labelWidth;

        if (GUI.Button(buttonRect, "Add new modifier..."))
        {
            GenericMenu menu = new GenericMenu();
            Type baseType = GetBaseType(property);
            var types = TypeCache.GetTypesDerivedFrom(baseType);

            foreach (var type in types)
            {
                // Пропускаем абстрактные классы
                if (type.IsAbstract) continue;

                // Добавляем пункт меню
                menu.AddItem(new GUIContent(type.Name), false, () =>
                {
                    var instance = Activator.CreateInstance(type);
                    property.managedReferenceValue = instance;
                    property.serializedObject.ApplyModifiedProperties();
                });
            }
            menu.ShowAsContext();
        }
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        // Выделяем дополнительное место для нашей кнопки
        return EditorGUI.GetPropertyHeight(property, true) + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
    }

    private Type GetBaseType(SerializedProperty property)
    {
        // Получаем базовый тип из поля (например, BiomeModifier)
        string[] typeParts = property.managedReferenceFieldTypename.Split(' ');
        var type = Type.GetType($"{typeParts[1]}, {typeParts[0]}");
        return type;
    }
}
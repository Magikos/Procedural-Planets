using UnityEngine;
using UnityEditor;

[CustomPropertyDrawer(typeof(ColorSettings.BiomeColorSettings.Biome))]
public class BiomePropertyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        // Get properties
        SerializedProperty gradientProperty = property.FindPropertyRelative("gradient");
        SerializedProperty tintProperty = property.FindPropertyRelative("tint");
        SerializedProperty startHeightProperty = property.FindPropertyRelative("startHeight");
        SerializedProperty tintPercentProperty = property.FindPropertyRelative("tintPercent");

        // Calculate rects
        float singleLineHeight = EditorGUIUtility.singleLineHeight;
        float spacing = EditorGUIUtility.standardVerticalSpacing;

        // Create a custom label based on start height
        string customLabel = GetCustomLabel(startHeightProperty, tintProperty);

        // Create foldout with custom label
        Rect foldoutRect = new Rect(position.x, position.y, position.width, singleLineHeight);
        property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, customLabel, true);

        if (property.isExpanded)
        {
            EditorGUI.indentLevel++;

            Rect currentRect = new Rect(position.x, position.y + singleLineHeight + spacing, position.width, singleLineHeight);

            // Draw gradient (takes more height)
            float gradientHeight = EditorGUI.GetPropertyHeight(gradientProperty, true);
            Rect gradientRect = new Rect(position.x, currentRect.y, position.width, gradientHeight);
            EditorGUI.PropertyField(gradientRect, gradientProperty);
            currentRect.y += gradientHeight + spacing;

            // Draw tint color
            EditorGUI.PropertyField(currentRect, tintProperty);
            currentRect.y += singleLineHeight + spacing;

            // Draw start height
            EditorGUI.PropertyField(currentRect, startHeightProperty);
            currentRect.y += singleLineHeight + spacing;

            // Draw tint percent
            EditorGUI.PropertyField(currentRect, tintPercentProperty);

            EditorGUI.indentLevel--;
        }

        EditorGUI.EndProperty();
    }

    private string GetCustomLabel(SerializedProperty startHeightProperty, SerializedProperty tintProperty)
    {
        string baseLabel = "Biome";

        // Add start height info
        float startHeight = startHeightProperty.floatValue;
        baseLabel += $" (Height: {startHeight:F2})";

        // Add tint color info
        Color tint = tintProperty.colorValue;
        if (tint != Color.white)
        {
            baseLabel += $" - Tinted";
        }

        return baseLabel;
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float height = EditorGUIUtility.singleLineHeight; // Foldout

        if (property.isExpanded)
        {
            float spacing = EditorGUIUtility.standardVerticalSpacing;

            SerializedProperty gradientProperty = property.FindPropertyRelative("gradient");

            // Height for gradient + tint + startHeight + tintPercent
            height += EditorGUI.GetPropertyHeight(gradientProperty, true) + spacing; // gradient
            height += (EditorGUIUtility.singleLineHeight + spacing) * 3; // tint, startHeight, tintPercent
        }

        return height;
    }
}

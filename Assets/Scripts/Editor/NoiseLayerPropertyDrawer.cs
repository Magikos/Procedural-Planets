using UnityEngine;
using UnityEditor;

[CustomPropertyDrawer(typeof(ShapeSettings.NoiseLayer))]
public class NoiseLayerPropertyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        // Get properties
        SerializedProperty enabledProperty = property.FindPropertyRelative("enabled");
        SerializedProperty useFirstLayerAsMaskProperty = property.FindPropertyRelative("useFirstLayerAsMask");
        SerializedProperty noiseSettingsProperty = property.FindPropertyRelative("noiseSettings");

        // Calculate rects
        float singleLineHeight = EditorGUIUtility.singleLineHeight;
        float spacing = EditorGUIUtility.standardVerticalSpacing;

        // Create a custom label based on enabled state and filter type
        string customLabel = GetCustomLabel(property, enabledProperty, noiseSettingsProperty);

        // Create foldout with custom label
        Rect foldoutRect = new Rect(position.x, position.y, position.width, singleLineHeight);
        property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, customLabel, true);

        if (property.isExpanded)
        {
            EditorGUI.indentLevel++;

            Rect currentRect = new Rect(position.x, position.y + singleLineHeight + spacing, position.width, singleLineHeight);

            // Draw enabled toggle
            EditorGUI.PropertyField(currentRect, enabledProperty);
            currentRect.y += singleLineHeight + spacing;

            // Draw useFirstLayerAsMask
            EditorGUI.PropertyField(currentRect, useFirstLayerAsMaskProperty);
            currentRect.y += singleLineHeight + spacing;

            // Draw noise settings
            float noiseSettingsHeight = EditorGUI.GetPropertyHeight(noiseSettingsProperty, true);
            Rect noiseSettingsRect = new Rect(position.x, currentRect.y, position.width, noiseSettingsHeight);
            EditorGUI.PropertyField(noiseSettingsRect, noiseSettingsProperty, new GUIContent("Noise Settings"), true);

            EditorGUI.indentLevel--;
        }

        EditorGUI.EndProperty();
    }

    private string GetCustomLabel(SerializedProperty property, SerializedProperty enabledProperty, SerializedProperty noiseSettingsProperty)
    {
        string baseLabel = "Noise Layer";

        // Add enabled/disabled status
        if (!enabledProperty.boolValue)
        {
            baseLabel += " (Disabled)";
        }

        // Try to get filter type from noise settings
        // NoiseSettings is a serializable class, not a ScriptableObject, so we access it directly
        SerializedProperty filterTypeProperty = noiseSettingsProperty.FindPropertyRelative("filterType");
        if (filterTypeProperty != null)
        {
            NoiseSettings.FilterType filterType = (NoiseSettings.FilterType)filterTypeProperty.enumValueIndex;
            baseLabel += $" - {filterType}";
        }

        return baseLabel;
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float height = EditorGUIUtility.singleLineHeight;

        if (property.isExpanded)
        {
            float spacing = EditorGUIUtility.standardVerticalSpacing;

            // Height for enabled + useFirstLayerAsMask + noise settings
            height += (EditorGUIUtility.singleLineHeight + spacing) * 2; // enabled + useFirstLayerAsMask

            SerializedProperty noiseSettingsProperty = property.FindPropertyRelative("noiseSettings");
            height += EditorGUI.GetPropertyHeight(noiseSettingsProperty, true) + spacing;
        }

        return height;
    }
}

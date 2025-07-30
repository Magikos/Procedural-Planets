using UnityEngine;
using UnityEditor;

[CustomPropertyDrawer(typeof(NoiseSettings))]
public class NoiseSettingsPropertyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        // Get the filterType property
        SerializedProperty filterTypeProperty = property.FindPropertyRelative("filterType");
        SerializedProperty simpleNoiseProperty = property.FindPropertyRelative("simpleNoiseSettings");
        SerializedProperty ridgedNoiseProperty = property.FindPropertyRelative("ridgedNoiseSettings");

        // Calculate rects
        float singleLineHeight = EditorGUIUtility.singleLineHeight;
        float spacing = EditorGUIUtility.standardVerticalSpacing;

        Rect currentRect = new Rect(position.x, position.y, position.width, singleLineHeight);

        // Draw the enum dropdown with a better label
        EditorGUI.PropertyField(currentRect, filterTypeProperty, new GUIContent("Filter Type"));
        currentRect.y += singleLineHeight + spacing;

        // Draw the appropriate settings based on filter type - FLATTENED
        NoiseSettings.FilterType filterType = (NoiseSettings.FilterType)filterTypeProperty.enumValueIndex;

        // Add a subtle separator
        EditorGUI.DrawRect(new Rect(currentRect.x, currentRect.y - spacing / 2, currentRect.width, 1), new Color(0.5f, 0.5f, 0.5f, 0.3f));

        switch (filterType)
        {
            case NoiseSettings.FilterType.Simple:
                if (simpleNoiseProperty != null)
                {
                    DrawFlattenedNoiseSettings(currentRect, simpleNoiseProperty, position.width);
                }
                break;

            case NoiseSettings.FilterType.Ridged:
                if (ridgedNoiseProperty != null)
                {
                    DrawFlattenedNoiseSettings(currentRect, ridgedNoiseProperty, position.width);
                }
                break;
        }

        EditorGUI.EndProperty();
    }

    private void DrawFlattenedNoiseSettings(Rect startRect, SerializedProperty noiseProperty, float width)
    {
        float singleLineHeight = EditorGUIUtility.singleLineHeight;
        float spacing = EditorGUIUtility.standardVerticalSpacing;
        Rect currentRect = startRect;

        // Draw each property directly without the foldout
        SerializedProperty strengthProperty = noiseProperty.FindPropertyRelative("strength");
        SerializedProperty numLayersProperty = noiseProperty.FindPropertyRelative("numLayers");
        SerializedProperty baseRoughnessProperty = noiseProperty.FindPropertyRelative("baseRoughness");
        SerializedProperty roughnessProperty = noiseProperty.FindPropertyRelative("roughness");
        SerializedProperty persistenceProperty = noiseProperty.FindPropertyRelative("persistence");
        SerializedProperty centreProperty = noiseProperty.FindPropertyRelative("centre");
        SerializedProperty minValueProperty = noiseProperty.FindPropertyRelative("minValue");

        // Draw all the simple noise settings
        if (strengthProperty != null)
        {
            EditorGUI.PropertyField(currentRect, strengthProperty);
            currentRect.y += singleLineHeight + spacing;
        }

        if (numLayersProperty != null)
        {
            EditorGUI.PropertyField(currentRect, numLayersProperty);
            currentRect.y += singleLineHeight + spacing;
        }

        if (baseRoughnessProperty != null)
        {
            EditorGUI.PropertyField(currentRect, baseRoughnessProperty);
            currentRect.y += singleLineHeight + spacing;
        }

        if (roughnessProperty != null)
        {
            EditorGUI.PropertyField(currentRect, roughnessProperty);
            currentRect.y += singleLineHeight + spacing;
        }

        if (persistenceProperty != null)
        {
            EditorGUI.PropertyField(currentRect, persistenceProperty);
            currentRect.y += singleLineHeight + spacing;
        }

        if (centreProperty != null)
        {
            EditorGUI.PropertyField(currentRect, centreProperty);
            currentRect.y += singleLineHeight * 3 + spacing; // Vector3 takes more space
        }

        if (minValueProperty != null)
        {
            EditorGUI.PropertyField(currentRect, minValueProperty);
            currentRect.y += singleLineHeight + spacing;
        }

        // If it's ridged noise, also draw the weight multiplier
        SerializedProperty weightMultiplierProperty = noiseProperty.FindPropertyRelative("weightMultiplier");
        if (weightMultiplierProperty != null)
        {
            EditorGUI.PropertyField(currentRect, weightMultiplierProperty);
        }
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        SerializedProperty filterTypeProperty = property.FindPropertyRelative("filterType");

        float height = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing; // Filter type dropdown
        float singleLineHeight = EditorGUIUtility.singleLineHeight;
        float spacing = EditorGUIUtility.standardVerticalSpacing;

        // Add height for the selected settings (flattened)
        NoiseSettings.FilterType filterType = (NoiseSettings.FilterType)filterTypeProperty.enumValueIndex;

        switch (filterType)
        {
            case NoiseSettings.FilterType.Simple:
                // strength, numLayers, baseRoughness, roughness, persistence, centre (3 lines), minValue
                height += (singleLineHeight + spacing) * 7 + (singleLineHeight * 2); // +2 for Vector3
                break;

            case NoiseSettings.FilterType.Ridged:
                // All simple settings + weightMultiplier
                height += (singleLineHeight + spacing) * 8 + (singleLineHeight * 2); // +1 for weightMultiplier
                break;
        }

        return height;
    }
}

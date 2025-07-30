using UnityEngine;
using UnityEditor;

[CustomPropertyDrawer(typeof(ColorSettings.BiomeColorSettings))]
public class BiomeColorSettingsPropertyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        // Get properties
        SerializedProperty biomesProperty = property.FindPropertyRelative("biomes");
        SerializedProperty noiseProperty = property.FindPropertyRelative("noise");
        SerializedProperty noiseOffsetProperty = property.FindPropertyRelative("noiseOffset");
        SerializedProperty noiseStrengthProperty = property.FindPropertyRelative("noiseStrength");
        SerializedProperty blendAmountProperty = property.FindPropertyRelative("blendAmount");

        // Calculate rects
        float singleLineHeight = EditorGUIUtility.singleLineHeight;
        float spacing = EditorGUIUtility.standardVerticalSpacing;

        Rect currentRect = new Rect(position.x, position.y, position.width, singleLineHeight);

        // Draw noise settings first
        EditorGUI.LabelField(currentRect, "Biome Noise Settings", EditorStyles.boldLabel);
        currentRect.y += singleLineHeight + spacing;

        // Draw noise properties
        EditorGUI.PropertyField(currentRect, noiseProperty);
        currentRect.y += EditorGUI.GetPropertyHeight(noiseProperty, true) + spacing;

        EditorGUI.PropertyField(currentRect, noiseOffsetProperty);
        currentRect.y += singleLineHeight + spacing;

        EditorGUI.PropertyField(currentRect, noiseStrengthProperty);
        currentRect.y += singleLineHeight + spacing;

        EditorGUI.PropertyField(currentRect, blendAmountProperty);
        currentRect.y += singleLineHeight + spacing * 2;

        // Draw biomes section
        EditorGUI.LabelField(currentRect, "Biomes", EditorStyles.boldLabel);
        currentRect.y += singleLineHeight + spacing;

        // Draw biomes array with better styling
        DrawBiomesArray(currentRect, biomesProperty, position.width);

        EditorGUI.EndProperty();
    }

    private void DrawBiomesArray(Rect startRect, SerializedProperty biomesProperty, float width)
    {
        float singleLineHeight = EditorGUIUtility.singleLineHeight;
        float spacing = EditorGUIUtility.standardVerticalSpacing;
        Rect currentRect = startRect;

        // Add/Remove buttons
        Rect buttonRect = new Rect(currentRect.x, currentRect.y, width, singleLineHeight);
        float buttonWidth = (width - 10) / 3;

        Rect addButtonRect = new Rect(buttonRect.x, buttonRect.y, buttonWidth, singleLineHeight);
        Rect removeButtonRect = new Rect(buttonRect.x + buttonWidth + 5, buttonRect.y, buttonWidth, singleLineHeight);
        Rect countLabelRect = new Rect(buttonRect.x + (buttonWidth + 5) * 2, buttonRect.y, buttonWidth, singleLineHeight);

        if (GUI.Button(addButtonRect, "Add Biome"))
        {
            biomesProperty.arraySize++;
        }

        GUI.enabled = biomesProperty.arraySize > 0;
        if (GUI.Button(removeButtonRect, "Remove Biome"))
        {
            biomesProperty.arraySize--;
        }
        GUI.enabled = true;

        GUI.Label(countLabelRect, $"Count: {biomesProperty.arraySize}");
        currentRect.y += singleLineHeight + spacing;

        // Draw each biome
        for (int i = 0; i < biomesProperty.arraySize; i++)
        {
            SerializedProperty biomeProperty = biomesProperty.GetArrayElementAtIndex(i);
            float biomeHeight = EditorGUI.GetPropertyHeight(biomeProperty, true);

            Rect biomeRect = new Rect(currentRect.x, currentRect.y, width, biomeHeight);
            EditorGUI.PropertyField(biomeRect, biomeProperty, new GUIContent($"Biome {i + 1}"), true);

            currentRect.y += biomeHeight + spacing;
        }

        // Show message if no biomes
        if (biomesProperty.arraySize == 0)
        {
            Rect helpRect = new Rect(currentRect.x, currentRect.y, width, singleLineHeight * 2);
            EditorGUI.HelpBox(helpRect, "No biomes defined. Add a biome to begin coloring your planet.", MessageType.Info);
        }
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        SerializedProperty biomesProperty = property.FindPropertyRelative("biomes");
        SerializedProperty noiseProperty = property.FindPropertyRelative("noise");

        float height = 0;
        float singleLineHeight = EditorGUIUtility.singleLineHeight;
        float spacing = EditorGUIUtility.standardVerticalSpacing;

        // Height for noise settings section
        height += singleLineHeight + spacing; // "Biome Noise Settings" label
        height += EditorGUI.GetPropertyHeight(noiseProperty, true) + spacing; // noise
        height += (singleLineHeight + spacing) * 3; // noiseOffset, noiseStrength, blendAmount
        height += spacing; // extra space

        // Height for biomes section
        height += singleLineHeight + spacing; // "Biomes" label
        height += singleLineHeight + spacing; // Add/Remove buttons

        // Height for each biome
        for (int i = 0; i < biomesProperty.arraySize; i++)
        {
            SerializedProperty biomeProperty = biomesProperty.GetArrayElementAtIndex(i);
            height += EditorGUI.GetPropertyHeight(biomeProperty, true) + spacing;
        }

        // Height for help box if no biomes
        if (biomesProperty.arraySize == 0)
        {
            height += singleLineHeight * 2 + spacing;
        }

        return height;
    }
}

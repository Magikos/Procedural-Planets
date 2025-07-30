using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(ShapeSettings))]
public class ShapeSettingsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // Draw planet radius
        SerializedProperty planetRadiusProperty = serializedObject.FindProperty("planetRadius");
        EditorGUILayout.PropertyField(planetRadiusProperty);

        EditorGUILayout.Space();

        // Show calculated info
        var settings = (ShapeSettings)target;
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Planet Info", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"Radius: {settings.planetRadius:F0} units ({settings.planetRadius / 1000f:F1} km)");
        EditorGUILayout.LabelField("Heights are in meters above surface");
        EditorGUILayout.LabelField("Feature sizes are in kilometers");
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space();

        // Draw noise layers with better styling
        SerializedProperty noiseLayersProperty = serializedObject.FindProperty("noiseLayers");

        EditorGUILayout.LabelField("Noise Layers", EditorStyles.boldLabel);

        // Add/Remove buttons
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add Layer", GUILayout.Width(100)))
        {
            noiseLayersProperty.arraySize++;
            var newLayer = noiseLayersProperty.GetArrayElementAtIndex(noiseLayersProperty.arraySize - 1);

            // Initialize new layer with default values
            newLayer.FindPropertyRelative("enabled").boolValue = true;
            newLayer.FindPropertyRelative("useFirstLayerAsMask").boolValue = noiseLayersProperty.arraySize > 1;

            var noiseSettings = newLayer.FindPropertyRelative("noiseSettings");
            noiseSettings.FindPropertyRelative("filterType").enumValueIndex = 0; // Simple

            var simpleSettings = noiseSettings.FindPropertyRelative("simpleNoiseSettings");
            simpleSettings.FindPropertyRelative("maxHeightMeters").floatValue = 50f;
            simpleSettings.FindPropertyRelative("featureSize").floatValue = 10f;
            simpleSettings.FindPropertyRelative("numLayers").intValue = 4;
            simpleSettings.FindPropertyRelative("roughness").floatValue = 2f;
            simpleSettings.FindPropertyRelative("persistence").floatValue = 0.5f;
            simpleSettings.FindPropertyRelative("minValue").floatValue = 0f;
        }

        GUI.enabled = noiseLayersProperty.arraySize > 0;
        if (GUILayout.Button("Remove Layer", GUILayout.Width(100)))
        {
            noiseLayersProperty.arraySize--;
        }
        GUI.enabled = true;

        GUILayout.FlexibleSpace();

        // Quick actions
        if (GUILayout.Button("🎲", GUILayout.Width(25)))
        {
            RandomizeAllLayers();
        }

        EditorGUILayout.LabelField($"Count: {noiseLayersProperty.arraySize}", GUILayout.Width(60));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        // Draw each layer
        for (int i = 0; i < noiseLayersProperty.arraySize; i++)
        {
            SerializedProperty layerProperty = noiseLayersProperty.GetArrayElementAtIndex(i);

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();

            // Layer header with reorder buttons
            var headerStyle = new GUIStyle(EditorStyles.boldLabel);
            headerStyle.normal.textColor = layerProperty.FindPropertyRelative("enabled").boolValue ?
                Color.white : Color.gray;

            GUILayout.Label($"Layer {i + 1}", headerStyle, GUILayout.Width(60));

            GUILayout.FlexibleSpace();

            // Quick randomize button for this layer
            if (GUILayout.Button("🎲", GUILayout.Width(25)))
            {
                RandomizeLayer(layerProperty);
            }

            // Move up button
            GUI.enabled = i > 0;
            if (GUILayout.Button("↑", GUILayout.Width(25)))
            {
                noiseLayersProperty.MoveArrayElement(i, i - 1);
            }

            // Move down button
            GUI.enabled = i < noiseLayersProperty.arraySize - 1;
            if (GUILayout.Button("↓", GUILayout.Width(25)))
            {
                noiseLayersProperty.MoveArrayElement(i, i + 1);
            }

            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            // Layer properties
            EditorGUI.indentLevel++;

            SerializedProperty enabledProperty = layerProperty.FindPropertyRelative("enabled");
            EditorGUILayout.PropertyField(enabledProperty);

            if (enabledProperty.boolValue)
            {
                if (i > 0)
                {
                    SerializedProperty useMaskProperty = layerProperty.FindPropertyRelative("useFirstLayerAsMask");
                    EditorGUILayout.PropertyField(useMaskProperty, new GUIContent("Use First Layer as Mask"));
                }

                SerializedProperty noiseSettingsProperty = layerProperty.FindPropertyRelative("noiseSettings");
                DrawNoiseSettings(noiseSettingsProperty, settings.planetRadius);
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
        }

        if (serializedObject.ApplyModifiedProperties())
        {
            // Auto-update planet if it exists and has auto-update enabled
            var planet = FindFirstObjectByType<Planet>();
            if (planet != null && planet.autoUpdate && planet.shapeSettings == settings)
            {
                planet.GeneratePlanet();
            }
        }
    }

    void DrawNoiseSettings(SerializedProperty noiseSettingsProperty, float planetRadius)
    {
        SerializedProperty filterTypeProperty = noiseSettingsProperty.FindPropertyRelative("filterType");
        EditorGUILayout.PropertyField(filterTypeProperty);

        NoiseSettings.FilterType filterType = (NoiseSettings.FilterType)filterTypeProperty.enumValueIndex;

        SerializedProperty settingsProperty;
        string settingsName;

        if (filterType == NoiseSettings.FilterType.Simple)
        {
            settingsProperty = noiseSettingsProperty.FindPropertyRelative("simpleNoiseSettings");
            settingsName = "Simple Noise Settings";
        }
        else
        {
            settingsProperty = noiseSettingsProperty.FindPropertyRelative("ridgedNoiseSettings");
            settingsName = "Ridged Noise Settings";
        }

        EditorGUILayout.LabelField(settingsName, EditorStyles.boldLabel);

        // Draw user-friendly properties
        var maxHeightProperty = settingsProperty.FindPropertyRelative("maxHeightMeters");
        var featureSizeProperty = settingsProperty.FindPropertyRelative("featureSize");

        EditorGUILayout.PropertyField(maxHeightProperty, new GUIContent("Max Height (meters)"));
        EditorGUILayout.PropertyField(featureSizeProperty, new GUIContent("Feature Size (km)"));

        // Show calculated internal values as read-only
        float calculatedStrength = maxHeightProperty.floatValue / planetRadius;
        float calculatedBaseRoughness = 1f / (featureSizeProperty.floatValue * 1000f / planetRadius);

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Calculated Internal Values (Read-Only)", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"Strength: {calculatedStrength:F6}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"Base Roughness: {calculatedBaseRoughness:F6}", EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();

        // Draw other properties
        EditorGUILayout.PropertyField(settingsProperty.FindPropertyRelative("numLayers"));
        EditorGUILayout.PropertyField(settingsProperty.FindPropertyRelative("roughness"));
        EditorGUILayout.PropertyField(settingsProperty.FindPropertyRelative("persistence"));
        EditorGUILayout.PropertyField(settingsProperty.FindPropertyRelative("centre"));
        EditorGUILayout.PropertyField(settingsProperty.FindPropertyRelative("minValue"));

        if (filterType == NoiseSettings.FilterType.Ridged)
        {
            EditorGUILayout.PropertyField(settingsProperty.FindPropertyRelative("weightMultiplier"));
        }
    }

    void RandomizeAllLayers()
    {
        SerializedProperty noiseLayersProperty = serializedObject.FindProperty("noiseLayers");

        for (int i = 0; i < noiseLayersProperty.arraySize; i++)
        {
            RandomizeLayer(noiseLayersProperty.GetArrayElementAtIndex(i));
        }

        serializedObject.ApplyModifiedProperties();
        Debug.Log("Randomized all noise layers!");
    }

    void RandomizeLayer(SerializedProperty layerProperty)
    {
        var noiseSettings = layerProperty.FindPropertyRelative("noiseSettings");
        var filterType = (NoiseSettings.FilterType)noiseSettings.FindPropertyRelative("filterType").enumValueIndex;

        // Randomize filter type
        noiseSettings.FindPropertyRelative("filterType").enumValueIndex = Random.Range(0, 2);
        filterType = (NoiseSettings.FilterType)noiseSettings.FindPropertyRelative("filterType").enumValueIndex;

        SerializedProperty settingsProperty;
        if (filterType == NoiseSettings.FilterType.Simple)
        {
            settingsProperty = noiseSettings.FindPropertyRelative("simpleNoiseSettings");
        }
        else
        {
            settingsProperty = noiseSettings.FindPropertyRelative("ridgedNoiseSettings");
            settingsProperty.FindPropertyRelative("weightMultiplier").floatValue = Random.Range(0.5f, 1.2f);
        }

        // Randomize values
        settingsProperty.FindPropertyRelative("maxHeightMeters").floatValue = Random.Range(10f, 300f);
        settingsProperty.FindPropertyRelative("featureSize").floatValue = Random.Range(1f, 50f);
        settingsProperty.FindPropertyRelative("numLayers").intValue = Random.Range(2, 6);
        settingsProperty.FindPropertyRelative("roughness").floatValue = Random.Range(1.5f, 3f);
        settingsProperty.FindPropertyRelative("persistence").floatValue = Random.Range(0.3f, 0.7f);
        settingsProperty.FindPropertyRelative("minValue").floatValue = Random.Range(0f, 0.3f);

        // Randomize center
        var centre = settingsProperty.FindPropertyRelative("centre");
        centre.vector3Value = new Vector3(
            Random.Range(-1000f, 1000f),
            Random.Range(-1000f, 1000f),
            Random.Range(-1000f, 1000f)
        );
    }
}

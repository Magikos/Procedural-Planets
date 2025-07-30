using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(LODSettings))]
public class LODSettingsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        LODSettings lodSettings = (LODSettings)target;

        // Draw max LOD
        SerializedProperty maxLODProperty = serializedObject.FindProperty("maxLOD");
        EditorGUILayout.PropertyField(maxLODProperty);

        EditorGUILayout.Space();

        // Draw LOD distances with better visualization
        SerializedProperty lodDistancesProperty = serializedObject.FindProperty("lodDistances");

        EditorGUILayout.LabelField("LOD Distance Thresholds", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Distances are normalized by planet radius. Lower values = higher detail.", MessageType.Info);

        // Ensure the array size matches maxLOD + 1
        int expectedSize = maxLODProperty.intValue + 1;
        if (lodDistancesProperty.arraySize != expectedSize)
        {
            lodDistancesProperty.arraySize = expectedSize;
        }

        // Draw each LOD level
        for (int i = 0; i < lodDistancesProperty.arraySize; i++)
        {
            SerializedProperty distanceProperty = lodDistancesProperty.GetArrayElementAtIndex(i);

            EditorGUILayout.BeginHorizontal();

            // LOD level label
            string label = i == 0 ? "Highest Detail" : $"LOD Level {i}";
            EditorGUILayout.LabelField(label, GUILayout.Width(100));

            // Distance field
            EditorGUILayout.PropertyField(distanceProperty, GUIContent.none);

            // Visual indicator of detail level
            string detailIndicator = new string('●', Mathf.Max(1, lodDistancesProperty.arraySize - i));
            GUI.color = Color.Lerp(Color.green, Color.red, (float)i / lodDistancesProperty.arraySize);
            EditorGUILayout.LabelField(detailIndicator, GUILayout.Width(50));
            GUI.color = Color.white;

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space();

        // Add helpful buttons
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Reset to Defaults"))
        {
            ResetToDefaults(lodSettings);
        }
        if (GUILayout.Button("Generate Exponential"))
        {
            GenerateExponentialDistances(lodSettings);
        }
        EditorGUILayout.EndHorizontal();

        serializedObject.ApplyModifiedProperties();
    }

    void ResetToDefaults(LODSettings lodSettings)
    {
        lodSettings.maxLOD = 5;
        lodSettings.lodDistances = new float[] { 0f, 0.5f, 1f, 2f, 4f, 8f };
        EditorUtility.SetDirty(lodSettings);
    }

    void GenerateExponentialDistances(LODSettings lodSettings)
    {
        lodSettings.lodDistances = new float[lodSettings.maxLOD + 1];
        for (int i = 0; i < lodSettings.lodDistances.Length; i++)
        {
            lodSettings.lodDistances[i] = Mathf.Pow(2f, i - 1);
            if (i == 0) lodSettings.lodDistances[i] = 0f; // Highest detail always starts at 0
        }
        EditorUtility.SetDirty(lodSettings);
    }
}

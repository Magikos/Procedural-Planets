using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(BiomeRegistry))]
public class BiomeRegistryEditor : Editor
{
    static readonly string[] TempLabels = { "Cold", "Cool", "Warm", "Hot" };
    static readonly string[] MoistLabels = { "Dry", "Medium", "Wet" };

    public override void OnInspectorGUI()
    {
        var registry = (BiomeRegistry)target;
        serializedObject.Update();

        EditorGUILayout.PropertyField(serializedObject.FindProperty("TemperatureSteps"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("MoistureSteps"));

        int tempSteps = registry.TemperatureSteps;
        int moistSteps = registry.MoistureSteps;
        int expectedSize = tempSteps * moistSteps;

        var gridProp = serializedObject.FindProperty("GridEntries");

        // Auto-resize array if needed
        if (gridProp.arraySize != expectedSize)
        {
            gridProp.arraySize = expectedSize;
            serializedObject.ApplyModifiedProperties();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Biome Grid (rows = cold→hot, columns = dry→wet)", EditorStyles.boldLabel);

        // Column headers
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(54);
        for (int m = 0; m < moistSteps; m++)
        {
            string label = m < MoistLabels.Length ? MoistLabels[m] : $"M{m}";
            GUILayout.Label(label, EditorStyles.centeredGreyMiniLabel, GUILayout.MinWidth(40));
        }
        EditorGUILayout.EndHorizontal();

        // Grid rows
        for (int t = tempSteps - 1; t >= 0; t--)
        {
            EditorGUILayout.BeginHorizontal();
            string rowLabel = t < TempLabels.Length ? TempLabels[t] : $"T{t}";
            EditorGUILayout.LabelField(rowLabel, GUILayout.Width(50));

            for (int m = 0; m < moistSteps; m++)
            {
                int idx = t * moistSteps + m;
                if (idx < gridProp.arraySize)
                {
                    var element = gridProp.GetArrayElementAtIndex(idx);
                    EditorGUILayout.PropertyField(element, GUIContent.none);
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Elevation Overrides", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("OceanBiome"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("BeachBiome"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("MountainBiome"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("SnowyMountainBiome"));

        EditorGUILayout.Space();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("OceanThreshold"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("BeachWidth"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("MountainThreshold"));

        EditorGUILayout.Space();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("BlendWidth"));

        serializedObject.ApplyModifiedProperties();
    }
}

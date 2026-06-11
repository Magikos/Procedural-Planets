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

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space();
        DrawPhaseBSurfaceTextureSummary(registry);
    }

    // Phase B step 10: at-a-glance view of each biome's Albedo / Normal / ARM assignment +
    // a thumbnail preview. Lets the author spot missing textures immediately rather than
    // discovering magenta placeholders only after planet generation.
    static bool _showSurfaceTextureSummary = true;

    void DrawPhaseBSurfaceTextureSummary(BiomeRegistry registry)
    {
        _showSurfaceTextureSummary = EditorGUILayout.Foldout(_showSurfaceTextureSummary,
            "Phase B Surface Textures", true, EditorStyles.foldoutHeader);
        if (!_showSurfaceTextureSummary) return;

        int full = 0, partial = 0, missing = 0;
        for (int slot = 0; slot < registry.BiomeCount; slot++)
        {
            BiomeDefinition def = registry.GetDefinitionByIndex(slot);
            if (def == null) continue;
            int present = (def.SurfaceAlbedo != null ? 1 : 0)
                        + (def.SurfaceNormal != null ? 1 : 0)
                        + (def.SurfaceARM != null ? 1 : 0);
            if (present == 3) full++;
            else if (present > 0) partial++;
            else missing++;
        }

        EditorGUILayout.HelpBox(
            $"{full} fully textured, {partial} partial, {missing} missing.\n" +
            "Missing textures render as magenta in the terrain shader.",
            missing > 0 ? MessageType.Warning : MessageType.Info);

        for (int slot = 0; slot < registry.BiomeCount; slot++)
        {
            BiomeDefinition def = registry.GetDefinitionByIndex(slot);
            if (def == null) continue;

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                // Albedo thumbnail (64x64). AssetPreview is async; first paint may be empty.
                Texture preview = def.SurfaceAlbedo != null
                    ? AssetPreview.GetAssetPreview(def.SurfaceAlbedo)
                    : null;
                Rect r = GUILayoutUtility.GetRect(48, 48, GUILayout.Width(48), GUILayout.Height(48));
                EditorGUI.DrawRect(r, new Color(0.15f, 0.15f, 0.15f, 1f));
                if (preview != null) GUI.DrawTexture(r, preview, ScaleMode.ScaleToFit);
                else if (def.SurfaceAlbedo != null)
                    GUI.Label(r, "...", EditorStyles.centeredGreyMiniLabel);
                else
                    GUI.Label(r, "?", EditorStyles.centeredGreyMiniLabel);

                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField($"[{slot}] {def.Type}", EditorStyles.boldLabel);
                    string status = def.SurfaceAlbedo != null ? "Albedo" : "<no Albedo>";
                    if (def.SurfaceNormal != null) status += ", Normal"; else status += ", <no Normal>";
                    if (def.SurfaceARM != null) status += ", ARM"; else status += ", <no ARM>";
                    EditorGUILayout.LabelField(status, EditorStyles.miniLabel);
                    if (GUILayout.Button("Select asset", EditorStyles.miniButton, GUILayout.Width(100)))
                        Selection.activeObject = def;
                }
            }
        }
    }
}

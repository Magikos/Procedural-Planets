using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(Planet))]
public class PlanetEditor : Editor
{
    Planet planet;
    Editor shapeSettingsEditor;
    Editor colorSettingsEditor;
    Editor lodSettingsEditor;

    void OnEnable()
    {
        planet = (Planet)target;
    }

    public override void OnInspectorGUI()
    {
        using (var check = new EditorGUI.ChangeCheckScope())
        {
            // Draw the default Planet properties
            DrawDefaultInspector();

            // Check if properties changed
            if (check.changed && planet.autoUpdate)
            {
                planet.GeneratePlanet();
            }
        }

        // Add a manual generate button
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Generate Planet"))
        {
            planet.GeneratePlanet();
        }

        // Add performance warning for high resolution
        if (planet.resolution > 100)
        {
            GUI.color = Color.yellow;
            GUILayout.Label("⚠", GUILayout.Width(20));
            GUI.color = Color.white;
        }
        EditorGUILayout.EndHorizontal();

        // Quick setup buttons
        EditorGUILayout.Space();
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Quick Setup", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("🌍 Earth-like"))
        {
            planet.resolution = 50;
            if (planet.shapeSettings != null) planet.shapeSettings.planetRadius = 1000f;
            planet.GeneratePlanet();
            Debug.Log("Applied Earth-like setup");
        }
        if (GUILayout.Button("🌙 Moon-like"))
        {
            planet.resolution = 30;
            if (planet.shapeSettings != null) planet.shapeSettings.planetRadius = 800f;
            planet.GeneratePlanet();
            Debug.Log("Applied Moon-like setup");
        }
        if (GUILayout.Button("🎲 Random"))
        {
            planet.resolution = Random.Range(20, 60);
            if (planet.shapeSettings != null) planet.shapeSettings.planetRadius = Random.Range(500f, 1500f);
            planet.GeneratePlanet();
            Debug.Log("Applied random setup");
        }
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("Open Planet Settings Helper"))
        {
            PlanetSettingsHelper.ShowWindow();
        }

        EditorGUILayout.EndVertical();

        // Show performance info
        if (planet.resolution > 100)
        {
            EditorGUILayout.HelpBox($"High resolution ({planet.resolution}) may cause slow generation. Consider values under 100 for faster iteration.", MessageType.Warning);
        }

        // Draw ShapeSettings inline if assigned
        DrawShapeSettingsEditor();

        // Add quick asset creation if settings are null
        if (planet.shapeSettings == null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("No Shape Settings assigned. Create or assign one to begin shaping your planet.", MessageType.Info);
            if (GUILayout.Button("Create New Shape Settings"))
            {
                CreateShapeSettings();
            }
        }

        // Draw ColorSettings inline if assigned
        DrawColorSettingsEditor();

        if (planet.colorSettings == null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("No Color Settings assigned. Create or assign one to begin coloring your planet.", MessageType.Info);
            if (GUILayout.Button("Create New Color Settings"))
            {
                CreateColorSettings();
            }
        }

        // Draw LODSettings inline if assigned
        DrawLODSettingsEditor();

        if (planet.lodSettings == null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("No LOD Settings assigned. Create or assign one for level-of-detail optimization.", MessageType.Info);
            if (GUILayout.Button("Create New LOD Settings"))
            {
                CreateLODSettings();
            }
        }
    }

    void CreateShapeSettings()
    {
        Planet planet = (Planet)target;
        ShapeSettings newSettings = ScriptableObject.CreateInstance<ShapeSettings>();

        string path = EditorUtility.SaveFilePanelInProject(
            "Save Shape Settings",
            planet.name + "_ShapeSettings",
            "asset",
            "Save the new Shape Settings asset"
        );

        if (!string.IsNullOrEmpty(path))
        {
            AssetDatabase.CreateAsset(newSettings, path);
            AssetDatabase.SaveAssets();
            planet.shapeSettings = newSettings;
            EditorUtility.SetDirty(planet);
        }
    }

    void CreateColorSettings()
    {
        Planet planet = (Planet)target;
        ColorSettings newSettings = ScriptableObject.CreateInstance<ColorSettings>();

        string path = EditorUtility.SaveFilePanelInProject(
            "Save Color Settings",
            planet.name + "_ColorSettings",
            "asset",
            "Save the new Color Settings asset"
        );

        if (!string.IsNullOrEmpty(path))
        {
            AssetDatabase.CreateAsset(newSettings, path);
            AssetDatabase.SaveAssets();
            planet.colorSettings = newSettings;
            EditorUtility.SetDirty(planet);
        }
    }

    void CreateLODSettings()
    {
        Planet planet = (Planet)target;
        LODSettings newSettings = ScriptableObject.CreateInstance<LODSettings>();

        string path = EditorUtility.SaveFilePanelInProject(
            "Save LOD Settings",
            planet.name + "_LODSettings",
            "asset",
            "Save the new LOD Settings asset"
        );

        if (!string.IsNullOrEmpty(path))
        {
            AssetDatabase.CreateAsset(newSettings, path);
            AssetDatabase.SaveAssets();
            planet.lodSettings = newSettings;
            EditorUtility.SetDirty(planet);
        }
    }

    void DrawShapeSettingsEditor()
    {
        if (planet.shapeSettings != null)
        {
            // Add some space
            EditorGUILayout.Space();

            // Create a foldout for shape settings
            EditorGUILayout.LabelField("Shape Settings", EditorStyles.boldLabel);

            using (var check = new EditorGUI.ChangeCheckScope())
            {
                // Create or update the embedded editor
                CreateCachedEditor(planet.shapeSettings, null, ref shapeSettingsEditor);

                // Draw the shape settings inspector
                if (shapeSettingsEditor != null)
                {
                    EditorGUILayout.BeginVertical("box");
                    shapeSettingsEditor.OnInspectorGUI();
                    EditorGUILayout.EndVertical();
                }

                // Auto-regenerate if shape settings changed and autoUpdate is enabled
                if (check.changed && planet.autoUpdate)
                {
                    planet.GeneratePlanet();
                }
            }
        }
    }

    void DrawColorSettingsEditor()
    {
        if (planet.colorSettings != null)
        {
            // Add some space
            EditorGUILayout.Space();

            // Create a foldout for color settings
            EditorGUILayout.LabelField("Color Settings", EditorStyles.boldLabel);

            using (var check = new EditorGUI.ChangeCheckScope())
            {
                // Create or update the embedded editor
                CreateCachedEditor(planet.colorSettings, null, ref colorSettingsEditor);

                // Draw the color settings inspector
                if (colorSettingsEditor != null)
                {
                    EditorGUILayout.BeginVertical("box");
                    colorSettingsEditor.OnInspectorGUI();
                    EditorGUILayout.EndVertical();
                }

                // Auto-regenerate if color settings changed and autoUpdate is enabled
                if (check.changed && planet.autoUpdate)
                {
                    planet.GeneratePlanet();
                }
            }
        }
    }

    void DrawLODSettingsEditor()
    {
        if (planet.lodSettings != null)
        {
            // Add some space
            EditorGUILayout.Space();

            // Create a foldout for LOD settings
            EditorGUILayout.LabelField("LOD Settings", EditorStyles.boldLabel);

            using (var check = new EditorGUI.ChangeCheckScope())
            {
                // Create or update the embedded editor
                CreateCachedEditor(planet.lodSettings, null, ref lodSettingsEditor);

                // Draw the LOD settings inspector
                if (lodSettingsEditor != null)
                {
                    EditorGUILayout.BeginVertical("box");
                    lodSettingsEditor.OnInspectorGUI();
                    EditorGUILayout.EndVertical();
                }

                // Auto-regenerate if LOD settings changed and autoUpdate is enabled
                if (check.changed && planet.autoUpdate)
                {
                    planet.GeneratePlanet();
                }
            }
        }
    }

    void OnDisable()
    {
        // Clean up the cached editors
        if (shapeSettingsEditor != null)
        {
            DestroyImmediate(shapeSettingsEditor);
        }

        if (colorSettingsEditor != null)
        {
            DestroyImmediate(colorSettingsEditor);
        }

        if (lodSettingsEditor != null)
        {
            DestroyImmediate(lodSettingsEditor);
        }
    }

    void OnSceneGUI()
    {
        Planet planet = (Planet)target;

        if (planet.shapeSettings != null)
        {
            // Draw planet radius wireframe
            Handles.color = Color.cyan;
            Handles.DrawWireArc(planet.transform.position, planet.transform.up,
                planet.transform.right, 360, planet.shapeSettings.planetRadius);
            Handles.DrawWireArc(planet.transform.position, planet.transform.right,
                planet.transform.forward, 360, planet.shapeSettings.planetRadius);
            Handles.DrawWireArc(planet.transform.position, planet.transform.forward,
                planet.transform.up, 360, planet.shapeSettings.planetRadius);

            // Draw LOD distance rings if LOD settings exist
            if (planet.lodSettings != null && planet.lodSettings.lodDistances != null)
            {
                Handles.color = Color.yellow;
                for (int i = 1; i < planet.lodSettings.lodDistances.Length; i++)
                {
                    float distance = planet.lodSettings.lodDistances[i] * planet.shapeSettings.planetRadius;
                    Handles.DrawWireArc(planet.transform.position, planet.transform.up,
                        planet.transform.right, 360, distance);
                }
            }

            // Draw viewer connection if assigned
            if (planet.viewer != null)
            {
                Handles.color = Color.green;
                Handles.DrawLine(planet.transform.position, planet.viewer.position);

                // Show distance to viewer
                float viewerDistance = Vector3.Distance(planet.transform.position, planet.viewer.position);
                float normalizedDistance = viewerDistance / planet.shapeSettings.planetRadius;

                Vector3 midPoint = (planet.transform.position + planet.viewer.position) * 0.5f;
                Handles.Label(midPoint, $"Distance: {normalizedDistance:F2}x radius");
            }

            // Add a label showing planet info
            Handles.BeginGUI();
            Vector3 screenPos = HandleUtility.WorldToGUIPoint(planet.transform.position + Vector3.up * (planet.shapeSettings.planetRadius + 1));
            string info = $"Planet\nRadius: {planet.shapeSettings.planetRadius:F1}\nRes: {planet.resolution}";
            if (planet.lodSettings != null)
            {
                info += $"\nLOD Levels: {planet.lodSettings.maxLOD + 1}";
            }
            GUI.Box(new Rect(screenPos.x - 75, screenPos.y - 40, 150, 80), info);
            Handles.EndGUI();
        }
    }
}

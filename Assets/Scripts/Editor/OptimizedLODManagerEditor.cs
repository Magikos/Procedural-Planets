using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(OptimizedLODManager))]
public class OptimizedLODManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        OptimizedLODManager lodManager = (OptimizedLODManager)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("LOD System Controls", EditorStyles.boldLabel);

        if (GUILayout.Button("Reset LOD System"))
        {
            // This would reset all chunks
            Debug.Log("LOD System reset requested");
        }

        EditorGUILayout.Space();

        // Draw the default inspector
        DrawDefaultInspector();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Performance Stats", EditorStyles.boldLabel);

        if (Application.isPlaying)
        {
            // Show runtime stats here if needed
            EditorGUILayout.LabelField($"Active Chunks: {lodManager.GetActiveChunkCount()}");
            EditorGUILayout.LabelField($"Update Time: {lodManager.GetAverageUpdateTime():F2}ms");
        }
        else
        {
            EditorGUILayout.LabelField("Performance stats available in play mode");
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("Generate LOD Preset - Performance"))
        {
            GeneratePerformancePreset(lodManager);
        }

        if (GUILayout.Button("Generate LOD Preset - Quality"))
        {
            GenerateQualityPreset(lodManager);
        }

        if (GUILayout.Button("Generate LOD Preset - Balanced"))
        {
            GenerateBalancedPreset(lodManager);
        }
    }

    void GeneratePerformancePreset(OptimizedLODManager lodManager)
    {
        lodManager.lodSettings.maxActiveChunks = 100;
        lodManager.lodSettings.lodDistances = new float[] { 300f, 600f, 1200f, 2400f };
        lodManager.lodSettings.lodResolutions = new int[] { 64, 32, 16, 8 };
        lodManager.lodSettings.maxRenderDistance = 5000f;
        lodManager.lodSettings.updateInterval = 0.2f;
        lodManager.lodSettings.maxUpdatesPerFrame = 3;
        lodManager.lodSettings.useObjectPooling = true;

        EditorUtility.SetDirty(lodManager);
        Debug.Log("Applied Performance LOD preset - Optimized for lower-end hardware");
    }

    void GenerateQualityPreset(OptimizedLODManager lodManager)
    {
        lodManager.lodSettings.maxActiveChunks = 500;
        lodManager.lodSettings.lodDistances = new float[] { 800f, 1600f, 3200f, 6400f, 12800f };
        lodManager.lodSettings.lodResolutions = new int[] { 256, 128, 64, 32, 16 };
        lodManager.lodSettings.maxRenderDistance = 20000f;
        lodManager.lodSettings.updateInterval = 0.05f;
        lodManager.lodSettings.maxUpdatesPerFrame = 10;
        lodManager.lodSettings.useObjectPooling = true;

        EditorUtility.SetDirty(lodManager);
        Debug.Log("Applied Quality LOD preset - Optimized for high-end hardware");
    }

    void GenerateBalancedPreset(OptimizedLODManager lodManager)
    {
        lodManager.lodSettings.maxActiveChunks = 250;
        lodManager.lodSettings.lodDistances = new float[] { 500f, 1000f, 2000f, 4000f, 8000f };
        lodManager.lodSettings.lodResolutions = new int[] { 128, 64, 32, 16, 8 };
        lodManager.lodSettings.maxRenderDistance = 12000f;
        lodManager.lodSettings.updateInterval = 0.1f;
        lodManager.lodSettings.maxUpdatesPerFrame = 5;
        lodManager.lodSettings.useObjectPooling = true;

        EditorUtility.SetDirty(lodManager);
        Debug.Log("Applied Balanced LOD preset - Good for most hardware");
    }
}

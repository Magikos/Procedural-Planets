using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class AdvancedLODSettings
{
    [Header("LOD Configuration")]
    [Tooltip("Maximum number of chunks that can be active at once")]
    public int maxActiveChunks = 200;

    [Tooltip("Distance thresholds for each LOD level")]
    public float[] lodDistances = new float[] { 500f, 1000f, 2000f, 4000f, 8000f };

    [Tooltip("Resolution multipliers for each LOD level (higher = more detail)")]
    public int[] lodResolutions = new int[] { 128, 64, 32, 16, 8 };

    [Header("Culling")]
    [Tooltip("Maximum distance to render chunks")]
    public float maxRenderDistance = 12000f;

    [Tooltip("Use frustum culling to hide chunks outside camera view")]
    public bool useFrustumCulling = true;

    [Header("Update Settings")]
    [Tooltip("Time between LOD updates (seconds)")]
    public float updateInterval = 0.1f;

    [Tooltip("Maximum chunks to update per frame")]
    public int maxUpdatesPerFrame = 5;

    [Header("Performance")]
    [Tooltip("Use object pooling for chunk GameObjects")]
    public bool useObjectPooling = true;

    [Tooltip("Delay before destroying unused chunks (seconds)")]
    public float chunkDestroyDelay = 2f;
}

public class OptimizedLODManager : MonoBehaviour
{
    [Header("References")]
    public Transform viewer; // Usually the main camera
    public Planet targetPlanet;

    [Header("Settings")]
    public AdvancedLODSettings lodSettings = new AdvancedLODSettings();

    [Header("Debug")]
    public bool showDebugInfo = false;
    public bool logPerformanceStats = false;

    // Private fields
    private Dictionary<Vector3, TerrainChunk> activeChunks = new Dictionary<Vector3, TerrainChunk>();
    private Queue<TerrainChunk> chunkPool = new Queue<TerrainChunk>();
    private List<TerrainChunk> chunksToUpdate = new List<TerrainChunk>();
    private List<Vector3> chunksToRemove = new List<Vector3>();

    private float lastUpdateTime;
    private int frameUpdateCount;

    // Performance tracking
    private int totalChunksGenerated;
    private int chunksPooled;
    private float averageUpdateTime;

    void Start()
    {
        if (viewer == null)
        {
            Camera mainCam = Camera.main;
            if (mainCam != null)
                viewer = mainCam.transform;
            else
                Debug.LogWarning("OptimizedLODManager: No viewer assigned and no main camera found!");
        }

        if (targetPlanet == null)
            targetPlanet = GetComponent<Planet>();

        lastUpdateTime = Time.time;
    }

    void Update()
    {
        if (viewer == null || targetPlanet == null) return;

        // Check if it's time for an LOD update
        if (Time.time - lastUpdateTime >= lodSettings.updateInterval)
        {
            UpdateLOD();
            lastUpdateTime = Time.time;
        }

        // Process chunk updates with frame rate limiting
        ProcessChunkUpdates();

        if (showDebugInfo)
        {
            DisplayDebugInfo();
        }
    }

    void UpdateLOD()
    {
        float startTime = Time.realtimeSinceStartup;

        Vector3 viewerPosition = viewer.position;
        Vector3 planetCenter = targetPlanet.transform.position;
        float planetRadius = targetPlanet.shapeSettings?.planetRadius ?? 1000f;

        // Clear update lists
        chunksToUpdate.Clear();
        chunksToRemove.Clear();

        // Check existing chunks for updates or removal
        foreach (var kvp in activeChunks)
        {
            Vector3 chunkCenter = kvp.Key;
            TerrainChunk chunk = kvp.Value;

            float distanceToViewer = Vector3.Distance(viewerPosition, chunkCenter);

            // Check if chunk should be removed
            if (distanceToViewer > lodSettings.maxRenderDistance ||
                (lodSettings.useFrustumCulling && !IsChunkInFrustum(chunkCenter, chunk)))
            {
                chunksToRemove.Add(chunkCenter);
                continue;
            }

            // Check if chunk needs LOD update
            int newLOD = CalculateLODLevel(distanceToViewer);
            if (chunk.lodLevel != newLOD)
            {
                chunk.targetLODLevel = newLOD;
                chunksToUpdate.Add(chunk);
            }
        }

        // Remove chunks that are too far or outside frustum
        foreach (Vector3 chunkPos in chunksToRemove)
        {
            RemoveChunk(chunkPos);
        }

        // Generate new chunks if needed
        GenerateRequiredChunks(viewerPosition, planetCenter, planetRadius);

        // Update performance stats
        float updateTime = Time.realtimeSinceStartup - startTime;
        averageUpdateTime = Mathf.Lerp(averageUpdateTime, updateTime, 0.1f);

        if (logPerformanceStats && Time.frameCount % 60 == 0) // Log every 60 frames
        {
            Debug.Log($"LOD Performance: {activeChunks.Count} active chunks, {averageUpdateTime * 1000f:F2}ms update time");
        }
    }

    void ProcessChunkUpdates()
    {
        int updatesThisFrame = 0;

        for (int i = chunksToUpdate.Count - 1; i >= 0 && updatesThisFrame < lodSettings.maxUpdatesPerFrame; i--)
        {
            TerrainChunk chunk = chunksToUpdate[i];

            if (chunk != null && chunk.gameObject != null)
            {
                // Update chunk LOD
                chunk.UpdateLOD(chunk.targetLODLevel, lodSettings.lodResolutions);
                updatesThisFrame++;
            }

            chunksToUpdate.RemoveAt(i);
        }

        frameUpdateCount = updatesThisFrame;
    }

    int CalculateLODLevel(float distance)
    {
        for (int i = 0; i < lodSettings.lodDistances.Length; i++)
        {
            if (distance <= lodSettings.lodDistances[i])
            {
                return i;
            }
        }
        return lodSettings.lodDistances.Length - 1;
    }

    bool IsChunkInFrustum(Vector3 chunkCenter, TerrainChunk chunk)
    {
        if (!lodSettings.useFrustumCulling) return true;

        Camera cam = viewer.GetComponent<Camera>();
        if (cam == null) return true;

        // Simple sphere-frustum test
        float chunkRadius = chunk.GetBoundingRadius();
        Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(cam);

        return GeometryUtility.TestPlanesAABB(frustumPlanes, new Bounds(chunkCenter, Vector3.one * chunkRadius * 2));
    }

    void GenerateRequiredChunks(Vector3 viewerPos, Vector3 planetCenter, float planetRadius)
    {
        if (activeChunks.Count >= lodSettings.maxActiveChunks) return;

        // This is a simplified chunk generation system
        // In a real implementation, you'd have a more sophisticated spatial partitioning system

        float chunkSize = planetRadius * 0.2f; // Rough estimate
        int chunksPerAxis = Mathf.CeilToInt(lodSettings.maxRenderDistance / chunkSize);

        for (int x = -chunksPerAxis; x <= chunksPerAxis; x++)
        {
            for (int y = -chunksPerAxis; y <= chunksPerAxis; y++)
            {
                for (int z = -chunksPerAxis; z <= chunksPerAxis; z++)
                {
                    Vector3 chunkPos = planetCenter + new Vector3(x, y, z) * chunkSize;
                    float distance = Vector3.Distance(viewerPos, chunkPos);

                    if (distance <= lodSettings.maxRenderDistance && !activeChunks.ContainsKey(chunkPos))
                    {
                        CreateChunk(chunkPos, distance);

                        if (activeChunks.Count >= lodSettings.maxActiveChunks)
                            return;
                    }
                }
            }
        }
    }

    void CreateChunk(Vector3 position, float distanceToViewer)
    {
        TerrainChunk chunk;

        if (lodSettings.useObjectPooling && chunkPool.Count > 0)
        {
            chunk = chunkPool.Dequeue();
            // For existing TerrainChunk system, we can't easily reposition
            // so we'll create a new one instead for now
            chunksPooled--;
        }

        // Create chunk using your existing TerrainChunk constructor
        // We need to adapt this to work with your existing system
        int lodLevel = CalculateLODLevel(distanceToViewer);

        // Create bounds for the chunk
        float chunkSize = (targetPlanet.shapeSettings?.planetRadius ?? 1000f) * 0.2f;
        Bounds chunkBounds = new Bounds(position, Vector3.one * chunkSize);

        // Use one of the standard directions (we'll use up for now, but this should be improved)
        Vector3 localUp = (position - targetPlanet.transform.position).normalized;

        // Create the chunk using your existing constructor
        chunk = new TerrainChunk(
            targetPlanet.shapeGenerator,
            targetPlanet.colorGenerator,
            targetPlanet.lodSettings, // Use the original LODSettings from Planet
            targetPlanet.transform,
            localUp,
            chunkBounds,
            viewer,
            lodLevel
        );

        totalChunksGenerated++;
        activeChunks[position] = chunk;
    }

    void RemoveChunk(Vector3 position)
    {
        if (activeChunks.TryGetValue(position, out TerrainChunk chunk))
        {
            activeChunks.Remove(position);

            if (lodSettings.useObjectPooling)
            {
                // For pooling with existing TerrainChunk system, we'd need to modify TerrainChunk
                // For now, we'll just destroy it
                if (chunk.gameObject != null)
                {
                    if (Application.isPlaying)
                        Destroy(chunk.gameObject);
                    else
                        DestroyImmediate(chunk.gameObject);
                }
            }
            else
            {
                if (lodSettings.chunkDestroyDelay > 0 && Application.isPlaying)
                {
                    StartCoroutine(DestroyChunkDelayed(chunk, lodSettings.chunkDestroyDelay));
                }
                else
                {
                    if (chunk.gameObject != null)
                    {
                        if (Application.isPlaying)
                            Destroy(chunk.gameObject);
                        else
                            DestroyImmediate(chunk.gameObject);
                    }
                }
            }
        }
    }

    System.Collections.IEnumerator DestroyChunkDelayed(TerrainChunk chunk, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (chunk != null && chunk.gameObject != null)
        {
            if (Application.isPlaying)
                Destroy(chunk.gameObject);
            else
                DestroyImmediate(chunk.gameObject);
        }
    }

    void DisplayDebugInfo()
    {
        if (showDebugInfo)
        {
            // This could be enhanced with OnGUI or UI Text elements
            Debug.Log($"LOD Debug: {activeChunks.Count}/{lodSettings.maxActiveChunks} chunks, " +
                     $"{frameUpdateCount} updates this frame, " +
                     $"{chunksPooled} pooled chunks");
        }
    }

    void OnDrawGizmosSelected()
    {
        if (viewer == null) return;

        // Draw LOD distance rings
        Gizmos.color = Color.yellow;
        Vector3 viewerPos = viewer.position;

        for (int i = 0; i < lodSettings.lodDistances.Length; i++)
        {
            Gizmos.color = Color.Lerp(Color.green, Color.red, i / (float)lodSettings.lodDistances.Length);
            Gizmos.DrawWireSphere(viewerPos, lodSettings.lodDistances[i]);
        }

        // Draw max render distance
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(viewerPos, lodSettings.maxRenderDistance);

        // Draw active chunks
        Gizmos.color = Color.cyan;
        foreach (var chunk in activeChunks.Values)
        {
            if (chunk != null)
            {
                Gizmos.DrawWireCube(chunk.transform.position, Vector3.one * 50f);
            }
        }
    }

    // Public methods for editor and debugging
    public int GetActiveChunkCount()
    {
        return activeChunks.Count;
    }

    public float GetAverageUpdateTime()
    {
        return averageUpdateTime * 1000f; // Convert to milliseconds
    }

    public void ResetLODSystem()
    {
        // Clear all active chunks
        foreach (var chunk in activeChunks.Values)
        {
            if (chunk != null && chunk.gameObject != null)
            {
                if (Application.isPlaying)
                    Destroy(chunk.gameObject);
                else
                    DestroyImmediate(chunk.gameObject);
            }
        }

        activeChunks.Clear();
        chunksToUpdate.Clear();

        // Clear pool
        while (chunkPool.Count > 0)
        {
            var pooledChunk = chunkPool.Dequeue();
            if (pooledChunk != null && pooledChunk.gameObject != null)
            {
                if (Application.isPlaying)
                    Destroy(pooledChunk.gameObject);
                else
                    DestroyImmediate(pooledChunk.gameObject);
            }
        }

        Debug.Log("LOD System reset complete");
    }
}

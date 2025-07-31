using UnityEditor;
using UnityEngine;

public class Planet : MonoBehaviour
{
    [Range(2, 256)]
    public int resolution = 10;

    public bool autoUpdate = true;
    public ShapeSettings shapeSettings;
    public ColorSettings colorSettings;
    public LODSettings lodSettings;
    public Transform viewer; // Assign Main Camera here

    TerrainChunk[] rootChunks; // Now a field
    Coroutine lodUpdateCoroutine; // Track the coroutine for proper cleanup

    public ShapeGenerator shapeGenerator { get; private set; }
    public ColorGenerator colorGenerator { get; private set; }

    void Initialize()
    {
        ClearChildren();

        shapeGenerator = new ShapeGenerator(shapeSettings);
        colorGenerator = new ColorGenerator(colorSettings, shapeGenerator);

        // Initialize colors
        if (colorGenerator != null)
        {
            colorGenerator.UpdateColors();
        }

        rootChunks = new TerrainChunk[6];
        Vector3[] directions = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };

        for (int i = 0; i < 6; i++)
        {
            // Approximate bounds for root chunk
            float radius = shapeSettings.planetRadius;
            Bounds bounds = new Bounds(Vector3.zero, new Vector3(radius * 2, radius * 2, radius * 2));
            rootChunks[i] = new TerrainChunk(shapeGenerator, colorGenerator, lodSettings, transform, directions[i], bounds, viewer);
        }
    }

    public void GeneratePlanet()
    {
        // Stop any existing coroutine
        if (lodUpdateCoroutine != null)
        {
            StopCoroutine(lodUpdateCoroutine);
        }

        Initialize();
        lodUpdateCoroutine = StartCoroutine(UpdateLODCoroutine());
    }

    System.Collections.IEnumerator UpdateLODCoroutine()
    {
        while (rootChunks != null && viewer != null)
        {
            for (int i = 0; i < rootChunks.Length; i++)
            {
                if (rootChunks[i] != null)
                {
                    rootChunks[i].UpdateChunk();
                }
            }
            // Update LOD every 100ms instead of every frame for better performance
            yield return new WaitForSeconds(0.1f);
        }
    }

    void ClearChildren()
    {
        // Properly dispose of existing terrain chunks
        if (rootChunks != null)
        {
            for (int i = 0; i < rootChunks.Length; i++)
            {
                if (rootChunks[i] != null)
                {
                    rootChunks[i].Dispose();
                }
            }
            rootChunks = null;
        }

        // Destroy all existing child GameObjects (face objects)
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject childObj = transform.GetChild(i).gameObject;

            if (Application.isPlaying)
            {
                Destroy(childObj);
            }
#if UNITY_EDITOR
            else
            {
                // In editor mode, use delayCall to avoid OnValidate restrictions
                EditorApplication.delayCall += () =>
                {
                    if (childObj != null)
                        DestroyImmediate(childObj);
                };
            }
#endif
        }
    }

    void OnValidate()
    {
        if (autoUpdate)
        {
            GeneratePlanet();
        }
    }

    void OnDestroy()
    {
        if (lodUpdateCoroutine != null)
        {
            StopCoroutine(lodUpdateCoroutine);
        }
        ClearChildren();
    }
}
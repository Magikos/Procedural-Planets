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

    ShapeGenerator shapeGenerator;
    ColorGenerator colorGenerator;

    void Initialize()
    {
        ClearChildren();

        shapeGenerator = new ShapeGenerator(shapeSettings);
        colorGenerator = new ColorGenerator(colorSettings, shapeGenerator);

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
        Initialize();
    }

    void Update()
    {
        if (rootChunks != null)
        {
            for (int i = 0; i < rootChunks.Length; i++)
            {
                rootChunks[i].UpdateChunk();
            }
        }
    }

    void ClearChildren()
    {
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
}
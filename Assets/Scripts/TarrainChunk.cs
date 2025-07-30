using UnityEngine;

public class TerrainChunk
{
    const float sqrDstMultiplier = 5f;

    GameObject meshObject;
    Vector3 localUp;
    Vector3 axisA;
    Vector3 axisB;
    Bounds bounds;

    MeshRenderer meshRenderer;
    MeshFilter meshFilter;
    Mesh mesh;

    int lod;
    TerrainChunk[] children;
    bool hasChildren;

    ShapeGenerator shapeGenerator;
    ColorGenerator colorGenerator;
    LODSettings lodSettings;
    Transform viewer;

    // Enhanced LOD fields
    public int lodLevel { get; private set; }
    public int targetLODLevel { get; set; }
    private bool isInitialized = false;
    private Planet parentPlanet;

    public TerrainChunk(ShapeGenerator shapeGenerator, ColorGenerator colorGenerator, LODSettings lodSettings, Transform parentTransform, Vector3 localUp, Bounds bounds, Transform viewer, int lod = 0)
    {
        this.shapeGenerator = shapeGenerator;
        this.colorGenerator = colorGenerator;
        this.lodSettings = lodSettings;
        this.viewer = viewer;
        this.localUp = localUp;
        this.bounds = bounds;
        this.lod = lod;

        axisA = new Vector3(localUp.y, localUp.z, localUp.x);
        axisB = Vector3.Cross(localUp, axisA);

        meshObject = new GameObject("Terrain Chunk");
        meshRenderer = meshObject.AddComponent<MeshRenderer>();
        meshFilter = meshObject.AddComponent<MeshFilter>();
        mesh = new Mesh();
        meshFilter.sharedMesh = mesh;

        meshObject.transform.parent = parentTransform;
        meshObject.transform.localPosition = Vector3.zero;

        ConstructMesh();
        SetVisible(true); // Initial visibility for leaves
    }

    void ConstructMesh()
    {
        int resolution = GetResolutionForLOD(lod);
        Vector3[] vertices = new Vector3[resolution * resolution];
        Color[] colors = new Color[resolution * resolution];
        int[] triangles = new int[(resolution - 1) * (resolution - 1) * 6];
        int triIndex = 0;

        float minElevation = float.MaxValue;
        float maxElevation = float.MinValue;
        float[] elevations = new float[resolution * resolution];

        // First pass: Calculate all elevations to find min/max
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int i = x + y * resolution;
                Vector2 percent = new Vector2(x, y) / (resolution - 1);
                Vector3 pointOnUnitCube = localUp + (percent.x - 0.5f) * 2 * axisA + (percent.y - 0.5f) * 2 * axisB;
                Vector3 pointOnUnitSphere = pointOnUnitCube.normalized;

                float unscaledElevation = shapeGenerator.CalculateUnscaledElevation(pointOnUnitSphere);
                elevations[i] = unscaledElevation;

                if (unscaledElevation < minElevation) minElevation = unscaledElevation;
                if (unscaledElevation > maxElevation) maxElevation = unscaledElevation;
            }
        }

        // Second pass: Generate vertices and colors
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int i = x + y * resolution;
                Vector2 percent = new Vector2(x, y) / (resolution - 1);
                Vector3 pointOnUnitCube = localUp + (percent.x - 0.5f) * 2 * axisA + (percent.y - 0.5f) * 2 * axisB;
                Vector3 pointOnUnitSphere = pointOnUnitCube.normalized;

                float unscaledElevation = elevations[i];
                vertices[i] = pointOnUnitSphere * shapeGenerator.GetScaledElevation(unscaledElevation);

                // Calculate height percent for coloring
                float heightPercent = (maxElevation - minElevation) > 0.001f ?
                    (unscaledElevation - minElevation) / (maxElevation - minElevation) : 0.5f;

                // Get color based on height with null checks
                if (colorGenerator?.settings != null)
                {
                    colors[i] = colorGenerator.CalculateColorForHeight(heightPercent);
                }
                else
                {
                    // Fallback coloring if no color generator
                    if (heightPercent < 0.3f)
                        colors[i] = Color.blue;
                    else if (heightPercent < 0.7f)
                        colors[i] = Color.green;
                    else
                        colors[i] = Color.gray;
                }

                // Debug first vertex color
                if (i == 0)
                {
                    Debug.Log($"Chunk color debug - heightPercent: {heightPercent:F3}, color: {colors[i]}, minElev: {minElevation:F3}, maxElev: {maxElevation:F3}");
                }

                if (x != resolution - 1 && y != resolution - 1)
                {
                    triangles[triIndex] = i;
                    triangles[triIndex + 1] = i + resolution + 1;
                    triangles[triIndex + 2] = i + resolution;

                    triangles[triIndex + 3] = i;
                    triangles[triIndex + 4] = i + 1;
                    triangles[triIndex + 5] = i + resolution + 1;
                    triIndex += 6;
                }
            }
        }

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.colors = colors; // Apply per-vertex colors
        mesh.triangles = triangles;
        mesh.RecalculateNormals();

        // Update color generator with elevation range
        colorGenerator?.UpdateElevation(minElevation, maxElevation);

        // Set material and ensure it can use vertex colors
        if (colorGenerator?.settings?.planetMaterial != null)
        {
            // Use sharedMaterial in edit mode to avoid material leaks
            if (Application.isPlaying)
            {
                meshRenderer.material = colorGenerator.settings.planetMaterial;

                // Enable vertex color usage if the material supports it
                if (meshRenderer.material.HasProperty("_UseVertexColors"))
                {
                    meshRenderer.material.SetFloat("_UseVertexColors", 1f);
                }
            }
            else
            {
                meshRenderer.sharedMaterial = colorGenerator.settings.planetMaterial;
            }
        }
        else
        {
            Debug.LogWarning($"TerrainChunk: No material found. ColorGenerator null: {colorGenerator == null}, Settings null: {colorGenerator?.settings == null}");

            // Create a simple material that uses vertex colors as fallback
            Material fallbackMat = new Material(Shader.Find("Standard"));

            if (Application.isPlaying)
            {
                meshRenderer.material = fallbackMat;
            }
            else
            {
                meshRenderer.sharedMaterial = fallbackMat;
            }
        }
    }

    int GetResolutionForLOD(int lod)
    {
        return 2 + (int)Mathf.Pow(2, lod) * 15; // Adjust for your needs
    }

    public void UpdateChunk()
    {
        float viewerDstToBounds = Mathf.Sqrt(bounds.SqrDistance(viewer.position));
        float normalizedDst = viewerDstToBounds / shapeGenerator.planetRadius;

        bool shouldSubdivide = lod < lodSettings.maxLOD && normalizedDst < lodSettings.lodDistances[Mathf.Min(lod + 1, lodSettings.lodDistances.Length - 1)] * sqrDstMultiplier;

        if (shouldSubdivide != hasChildren)
        {
            if (shouldSubdivide)
            {
                SubdivideChunk();
            }
            else
            {
                CollapseChildren();
            }
        }

        SetVisible(!hasChildren); // Visible only if leaf (no children)

        if (hasChildren)
        {
            for (int i = 0; i < children.Length; i++)
            {
                children[i].UpdateChunk();
            }
        }
    }

    void SubdivideChunk()
    {
        children = new TerrainChunk[4];
        float halfSize = bounds.size.x / 2f;
        Vector3 quarterSize = bounds.size / 4f;

        // Improved subbounds for sphere projection
        children[0] = new TerrainChunk(shapeGenerator, colorGenerator, lodSettings, meshObject.transform, localUp, new Bounds(bounds.center - quarterSize, new Vector3(halfSize, halfSize, halfSize)), viewer, lod + 1);
        children[1] = new TerrainChunk(shapeGenerator, colorGenerator, lodSettings, meshObject.transform, localUp, new Bounds(bounds.center + new Vector3(quarterSize.x, quarterSize.y, -quarterSize.z), new Vector3(halfSize, halfSize, halfSize)), viewer, lod + 1);
        children[2] = new TerrainChunk(shapeGenerator, colorGenerator, lodSettings, meshObject.transform, localUp, new Bounds(bounds.center + new Vector3(quarterSize.x, -quarterSize.y, quarterSize.z), new Vector3(halfSize, halfSize, halfSize)), viewer, lod + 1);
        children[3] = new TerrainChunk(shapeGenerator, colorGenerator, lodSettings, meshObject.transform, localUp, new Bounds(bounds.center + new Vector3(-quarterSize.x, quarterSize.y, quarterSize.z), new Vector3(halfSize, halfSize, halfSize)), viewer, lod + 1);

        hasChildren = true;
    }

    void CollapseChildren()
    {
        if (children != null)
        {
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] != null)
                {
                    UnityEngine.Object.Destroy(children[i].meshObject);
                }
            }
            children = null;
        }
        hasChildren = false;
    }

    void SetVisible(bool visible)
    {
        meshObject.SetActive(visible);
    }

    // Enhanced LOD methods for OptimizedLODManager
    public void Initialize(Vector3 position, int lod, Planet planet)
    {
        this.lodLevel = lod;
        this.targetLODLevel = lod;
        this.parentPlanet = planet;
        this.isInitialized = true;

        if (planet != null)
        {
            this.shapeGenerator = planet.shapeGenerator;
            this.colorGenerator = planet.colorGenerator;
        }

        meshObject.transform.position = position;
        ConstructMesh();
    }

    public void UpdateLOD(int newLODLevel, int[] lodResolutions)
    {
        if (newLODLevel != lodLevel)
        {
            lodLevel = newLODLevel;
            ConstructMesh(); // Rebuild mesh with new resolution
        }
    }

    public float GetBoundingRadius()
    {
        return bounds.size.magnitude * 0.5f;
    }

    public GameObject gameObject
    {
        get { return meshObject; }
    }

    public Transform transform
    {
        get { return meshObject.transform; }
    }
}
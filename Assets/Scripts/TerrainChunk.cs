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

        meshObject = new GameObject("Terrain Chunk LOD" + lod);
        meshRenderer = meshObject.AddComponent<MeshRenderer>();
        meshFilter = meshObject.AddComponent<MeshFilter>();
        mesh = new Mesh();
        meshFilter.sharedMesh = mesh;

        meshObject.transform.parent = parentTransform;
        meshObject.transform.localPosition = Vector3.zero;

        ConstructMesh();
        SetVisible(true);
    }

    void ConstructMesh()
    {
        int resolution = GetResolutionForLOD(lod);
        Vector3[] vertices = new Vector3[resolution * resolution];
        int[] triangles = new int[(resolution - 1) * (resolution - 1) * 6];
        int triIndex = 0;
        Color[] colors = new Color[resolution * resolution]; // For shader if used

        float minElev = float.MaxValue;
        float maxElev = float.MinValue;

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int i = x + y * resolution;
                Vector2 percent = new Vector2(x, y) / (resolution - 1);
                Vector3 pointOnUnitCube = localUp + (percent.x - 0.5f) * 2 * axisA + (percent.y - 0.5f) * 2 * axisB;
                Vector3 pointOnUnitSphere = pointOnUnitCube.normalized;

                float unscaledElevation = shapeGenerator.CalculateUnscaledElevation(pointOnUnitSphere);
                vertices[i] = pointOnUnitSphere * shapeGenerator.GetScaledElevation(unscaledElevation);

                // Calculate proper color based on elevation
                float heightPercent = Mathf.InverseLerp(-1f, 1f, unscaledElevation); // Normalize elevation to 0-1
                colors[i] = colorGenerator.CalculateColorForHeight(heightPercent);

                // Track elevation for bounds
                if (unscaledElevation < minElev) minElev = unscaledElevation;
                if (unscaledElevation > maxElev) maxElev = unscaledElevation;

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
        mesh.triangles = triangles;
        mesh.colors = colors;

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        Debug.Log($"LOD {lod} elevation range: {minElev:F4} → {maxElev:F4}");
        Debug.Log($"Mesh.bounds extents: {mesh.bounds.extents}");

        meshRenderer.material = colorGenerator.settings.planetMaterial;
    }

    int GetResolutionForLOD(int lod)
    {
        // More reasonable resolution scaling: starts at 64, halves each LOD level
        int baseResolution = 64;
        return Mathf.Max(8, baseResolution >> lod); // Minimum resolution of 8
    }

    public void UpdateChunk()
    {
        float viewerDstToBounds = Mathf.Sqrt(bounds.SqrDistance(viewer.position));
        float normalizedDst = viewerDstToBounds / shapeGenerator.planetRadius;

        bool shouldSubdivide = lod < lodSettings.maxLOD && normalizedDst < lodSettings.lodDistances[Mathf.Min(lod + 1, lodSettings.lodDistances.Length - 1)];

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

        SetVisible(!hasChildren);

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
                    children[i].Dispose();
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

    public void Dispose()
    {
        CollapseChildren();
        if (mesh != null)
        {
            if (Application.isPlaying)
                Object.Destroy(mesh);
            else
                Object.DestroyImmediate(mesh);
            mesh = null;
        }
        if (meshObject != null)
        {
            if (Application.isPlaying)
                Object.Destroy(meshObject);
            else
                Object.DestroyImmediate(meshObject);
            meshObject = null;
        }
    }
}
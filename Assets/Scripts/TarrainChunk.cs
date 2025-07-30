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
        int[] triangles = new int[(resolution - 1) * (resolution - 1) * 6];
        int triIndex = 0;

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
        mesh.RecalculateNormals();

        meshRenderer.material = colorGenerator.settings.planetMaterial;
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
}
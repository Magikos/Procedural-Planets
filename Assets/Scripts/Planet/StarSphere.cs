using UnityEngine;

public class StarSphere : MonoBehaviour
{
    [Header("Generation")]
    [Range(500, 10000)] public int StarCount = 3000;
    public float SphereRadius = 5000f;
    [Range(0.1f, 5f)] public float StarSize = 1.5f;
    public int Seed = 42;

    [Header("References")]
    public Transform PlanetCenter;

    MeshFilter _meshFilter;
    MeshRenderer _meshRenderer;

    void OnEnable()
    {
        EventBus<PlanetGeneratedEvent>.Listen(OnPlanetGenerated);
    }

    void OnDisable()
    {
        EventBus<PlanetGeneratedEvent>.Unlisten(OnPlanetGenerated);
    }

    void OnPlanetGenerated(PlanetGeneratedEvent evt)
    {
        SphereRadius = evt.PlanetRadius * 80f;
        Generate();
    }

    void Start()
    {
        Generate();
    }

    public void Generate()
    {
        EnsureComponents();

        var rand = new System.Random(Seed);
        int vertCount = StarCount * 4;
        int triCount = StarCount * 6;

        var vertices = new Vector3[vertCount];
        var colors = new Color[vertCount];
        var triangles = new int[triCount];

        for (int i = 0; i < StarCount; i++)
        {
            Vector3 dir = RandomUnitVector(rand);
            Vector3 pos = dir * SphereRadius;

            // Brightness variation
            float brightness = 0.4f + (float)rand.NextDouble() * 0.6f;
            // Slight color variation: warm or cool white
            float temp = (float)rand.NextDouble();
            Color starColor = Color.Lerp(
                new Color(0.8f, 0.85f, 1f),
                new Color(1f, 0.95f, 0.8f),
                temp) * brightness;

            // Size variation
            float size = StarSize * (0.5f + (float)rand.NextDouble() * 0.5f);

            // Build a camera-facing quad (two triangles)
            Vector3 up = Vector3.Cross(dir, Vector3.right).normalized;
            if (up.sqrMagnitude < 0.01f)
                up = Vector3.Cross(dir, Vector3.forward).normalized;
            Vector3 right = Vector3.Cross(dir, up).normalized;

            int vi = i * 4;
            vertices[vi + 0] = pos + (-right - up) * size;
            vertices[vi + 1] = pos + (right - up) * size;
            vertices[vi + 2] = pos + (right + up) * size;
            vertices[vi + 3] = pos + (-right + up) * size;

            colors[vi + 0] = starColor;
            colors[vi + 1] = starColor;
            colors[vi + 2] = starColor;
            colors[vi + 3] = starColor;

            int ti = i * 6;
            triangles[ti + 0] = vi;
            triangles[ti + 1] = vi + 2;
            triangles[ti + 2] = vi + 1;
            triangles[ti + 3] = vi;
            triangles[ti + 4] = vi + 3;
            triangles[ti + 5] = vi + 2;
        }

        var mesh = _meshFilter.sharedMesh;
        if (mesh == null)
        {
            mesh = new Mesh { name = "StarSphere" };
            _meshFilter.sharedMesh = mesh;
        }

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.colors = colors;
        mesh.triangles = triangles;

        // No normals needed, no lighting
    }

    void EnsureComponents()
    {
        if (_meshFilter == null) _meshFilter = GetComponent<MeshFilter>();
        if (_meshFilter == null) _meshFilter = gameObject.AddComponent<MeshFilter>();

        if (_meshRenderer == null) _meshRenderer = GetComponent<MeshRenderer>();
        if (_meshRenderer == null) _meshRenderer = gameObject.AddComponent<MeshRenderer>();

        if (_meshRenderer.sharedMaterial == null || _meshRenderer.sharedMaterial.name == "Default-Material")
        {
            var shader = Shader.Find("Planet/Stars");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            _meshRenderer.sharedMaterial = new Material(shader) { name = "Stars" };
        }

        _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _meshRenderer.receiveShadows = false;
    }

    /// <summary>Returns the star direction (unit vector) for a given star index. Useful for future constellation queries.</summary>
    public Vector3 GetStarDirection(int index)
    {
        var rand = new System.Random(Seed);
        for (int i = 0; i < index; i++) RandomUnitVector(rand);
        return RandomUnitVector(rand);
    }

    static Vector3 RandomUnitVector(System.Random rand)
    {
        float z = 2f * (float)rand.NextDouble() - 1f;
        float t = 2f * Mathf.PI * (float)rand.NextDouble();
        float r = Mathf.Sqrt(1f - z * z);
        return new Vector3(r * Mathf.Cos(t), r * Mathf.Sin(t), z);
    }
}

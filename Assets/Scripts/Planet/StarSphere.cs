using UnityEngine;

public class StarSphere : MonoBehaviour
{
    [Header("Generation")]
    [Range(500, 10000)] public int StarCount = 3000;
    public int Seed = 42;

    [Header("References")]
    public Transform PlanetCenter;

    float _sphereRadius = 5000f;
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
        _sphereRadius = evt.PlanetRadius * 80f;
        if (PlanetCenter == null)
        {
            var planet = FindAnyObjectByType<Planet>();
            if (planet != null) PlanetCenter = planet.transform;
        }
        Generate();
    }

    void Start()
    {
        if (_sphereRadius <= 0f) _sphereRadius = 5000f;
        Generate();
    }

    void LateUpdate()
    {
        // Keep star sphere centered on planet so it's always surrounding the camera
        if (PlanetCenter != null)
            transform.position = PlanetCenter.position;
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

        // Star size scales with sphere radius
        float baseStarSize = _sphereRadius * 0.002f;

        for (int i = 0; i < StarCount; i++)
        {
            Vector3 dir = RandomUnitVector(rand);
            Vector3 pos = dir * _sphereRadius;

            float brightness = 0.4f + (float)rand.NextDouble() * 0.6f;
            float temp = (float)rand.NextDouble();
            Color starColor = Color.Lerp(
                new Color(0.8f, 0.85f, 1f),
                new Color(1f, 0.95f, 0.8f),
                temp) * brightness;

            float size = baseStarSize * (0.5f + (float)rand.NextDouble() * 1.5f);

            // Build quad perpendicular to direction from center
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

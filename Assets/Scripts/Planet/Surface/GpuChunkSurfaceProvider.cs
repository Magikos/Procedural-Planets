#if PLANET_GPU_EXPERIMENT
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

// Parked experiment: move planet surface generation to GPU compute. First proof landed
// six root cube-face patches from compute-generated vertex/normal buffers; runtime Tick
// is a no-op until quadtree chunk descriptors and indirect rendering land. Not wired into
// PlanetResolution; gated behind PLANET_GPU_EXPERIMENT so it stops shipping until the
// arc resumes.
public sealed class GpuChunkSurfaceProvider : IPlanetSurfaceProvider
{
    const int PatchResolution = 33;
    static readonly bool UseFullNoiseKernel = false;
    static readonly bool UseGpuReadback = false;
    const int MaxReadbackWaitFrames = 180;
    const string TerrainComputeResource = "GpuPlanetTerrain";
    const string PatchShaderName = "Planet/GpuChunkPatch";

    static readonly int ResolutionId = Shader.PropertyToID("_Resolution");
    static readonly int FaceIndexId = Shader.PropertyToID("_FaceIndex");
    static readonly int PlanetRadiusId = Shader.PropertyToID("_PlanetRadius");
    static readonly int OceanLevelId = Shader.PropertyToID("_OceanLevel");
    static readonly int UseNoiseId = Shader.PropertyToID("_UseNoise");
    static readonly int NoiseLayerCountId = Shader.PropertyToID("_NoiseLayerCount");
    static readonly int NoiseLayersId = Shader.PropertyToID("_NoiseLayers");
    static readonly int VerticesId = Shader.PropertyToID("_Vertices");
    static readonly int NormalsAndElevationId = Shader.PropertyToID("_NormalsAndElevation");
    static readonly int GpuTerrainVerticesId = Shader.PropertyToID("_GpuTerrainVertices");
    static readonly int GpuTerrainNormalsId = Shader.PropertyToID("_GpuTerrainNormals");

    readonly Transform _planetTransform;
    readonly ShapeGenerator _shapeGenerator;
    readonly Material _fallbackMaterial;
    readonly Planet.FaceRenderMask _renderMask;
    readonly int _seed;
    readonly float _oceanLevel;

    MeshFilter[] _meshFilters;
    MeshRenderer[] _renderers;
    Mesh[] _meshes;
    Material[] _materials;
    ComputeBuffer[] _vertexBuffers;
    ComputeBuffer[] _normalBuffers;
    GpuFaceSampler[] _samplers;
    readonly List<IFaceMeshSampler> _samplerView = new(6);

    ComputeShader _terrainCompute;
    ComputeBuffer _noiseLayerBuffer;
    int _kernel;

    public GpuChunkSurfaceProvider(
        Transform planetTransform,
        ShapeGenerator shapeGenerator,
        Material fallbackMaterial,
        Planet.FaceRenderMask renderMask,
        int seed,
        float oceanLevel)
    {
        _planetTransform = planetTransform;
        _shapeGenerator = shapeGenerator;
        _fallbackMaterial = fallbackMaterial;
        _renderMask = renderMask;
        _seed = seed;
        _oceanLevel = oceanLevel;
    }

    public async Awaitable GenerateAsync(IProgressHandle progress, CancellationToken ct)
    {
        progress?.Report(0f, "Loading GPU terrain pipeline...");
        EnsureResources();
        EnsureFaceObjects();
        UploadNoiseLayers();

        progress?.Report(0.15f, "Dispatching GPU terrain...");
        for (int face = 0; face < 6; face++)
        {
            DispatchFace(face);
            progress?.Report(0.15f + 0.45f * ((face + 1f) / 6f), $"Dispatched GPU face {face + 1}/6");
            await Awaitable.NextFrameAsync(ct);
        }

        if (UseGpuReadback)
        {
            progress?.Report(0.65f, "Reading GPU terrain query cache...");
            await ReadBackFaceSamplers(ct, progress);
        }
        else
        {
            progress?.Report(0.65f, "Building GPU terrain query cache...");
            BuildProofSamplers(progress);
        }

        _samplerView.Clear();
        for (int i = 0; i < _samplers.Length; i++)
            _samplerView.Add(_samplers[i]);

        progress?.Report(1f, "GPU terrain ready.");
    }

    public bool TryGetLocalSurfaceRadius(Vector3 localUnitDirection, out float localRadius)
    {
        localRadius = 0f;
        if (_samplers == null || localUnitDirection.sqrMagnitude < 0.0001f) return false;

        var (face, uv) = CoordinateConverter.UnitSphereToCubeFace(localUnitDirection);
        if (face < 0 || face >= _samplers.Length || _samplers[face] == null) return false;
        return _samplers[face].TrySampleRadius(uv, out localRadius);
    }

    public Awaitable GenerateColorsAsync(IBiomeProvider biomeProvider, IProgressHandle progress, CancellationToken ct)
    {
        progress?.Report(1f, "GPU terrain colors skipped.");
        return Awaitable.NextFrameAsync(ct);
    }

    public void Tick(Vector3 observerWorldPosition, Camera observerCamera)
    {
        // Proof mode has no runtime LOD yet. The next step is CPU-owned quadtree descriptors
        // feeding GPU patch buffers without rebuilding Unity meshes.
    }

    public IReadOnlyList<IFaceMeshSampler> GetFaceMeshSamplers() => _samplerView;

    public int RebakeBiomeMapsAt(Vector3 localUnitDirection) => 0;

    public void Dispose()
    {
        ReleaseComputeBuffers();

        if (_materials != null)
        {
            for (int i = 0; i < _materials.Length; i++)
                DestroyUnityObject(_materials[i]);
        }

        if (_meshes != null)
        {
            for (int i = 0; i < _meshes.Length; i++)
                DestroyUnityObject(_meshes[i]);
        }
    }

    void EnsureResources()
    {
        if (_terrainCompute == null)
        {
            if (!SystemInfo.supportsComputeShaders)
                throw new System.NotSupportedException("GPU terrain requires compute shader support.");
            if (!SystemInfo.supportsAsyncGPUReadback)
                throw new System.NotSupportedException("GPU terrain proof requires AsyncGPUReadback support.");

            _terrainCompute = Resources.Load<ComputeShader>(TerrainComputeResource);
            if (_terrainCompute == null)
                throw new System.InvalidOperationException($"Missing Resources/{TerrainComputeResource}.compute.");
            _kernel = _terrainCompute.FindKernel("CSGenerateTerrain");
        }
    }

    void EnsureFaceObjects()
    {
        if (_meshFilters != null) return;

        _meshFilters = new MeshFilter[6];
        _renderers = new MeshRenderer[6];
        _meshes = new Mesh[6];
        _materials = new Material[6];
        _vertexBuffers = new ComputeBuffer[6];
        _normalBuffers = new ComputeBuffer[6];
        _samplers = new GpuFaceSampler[6];

        Shader patchShader = Shader.Find(PatchShaderName);
        if (patchShader == null)
            throw new System.InvalidOperationException($"Missing shader '{PatchShaderName}'.");

        for (int face = 0; face < 6; face++)
        {
            var go = new GameObject($"gpu-chunk-face-{face}");
            go.transform.parent = _planetTransform;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var renderer = go.AddComponent<MeshRenderer>();
            var filter = go.AddComponent<MeshFilter>();
            var mesh = CreatePatchMesh(face);
            var material = new Material(patchShader)
            {
                name = $"GPU Planet Face {face}"
            };
            if (_fallbackMaterial != null)
                material.CopyPropertiesFromMaterial(_fallbackMaterial);

            filter.sharedMesh = mesh;
            renderer.sharedMaterial = material;

            _meshFilters[face] = filter;
            _renderers[face] = renderer;
            _meshes[face] = mesh;
            _materials[face] = material;

            bool renderFace = _renderMask == Planet.FaceRenderMask.All || (int)_renderMask - 1 == face;
            go.SetActive(renderFace);
        }
    }

    Mesh CreatePatchMesh(int face)
    {
        int vertexCount = PatchResolution * PatchResolution;
        int cells = PatchResolution - 1;
        int triangleIndexCount = cells * cells * 6;

        var vertices = new Vector3[vertexCount];
        var uvs = new Vector2[vertexCount];
        var triangles = new int[triangleIndexCount];

        for (int y = 0; y < PatchResolution; y++)
        {
            for (int x = 0; x < PatchResolution; x++)
            {
                int index = x + y * PatchResolution;
                Vector2 uv = new Vector2((float)x / cells, (float)y / cells);
                Vector3 dir = CoordinateConverter.CubeFaceToUnitSphere(face, uv);
                vertices[index] = dir * _shapeGenerator.Settings.PlanetRadius;
                uvs[index] = uv;
            }
        }

        for (int y = 0; y < cells; y++)
        {
            for (int x = 0; x < cells; x++)
            {
                int tri = (x + y * cells) * 6;
                int topLeft = x + y * PatchResolution;
                int topRight = topLeft + 1;
                int bottomLeft = topLeft + PatchResolution;
                int bottomRight = bottomLeft + 1;

                triangles[tri] = topLeft;
                triangles[tri + 1] = bottomRight;
                triangles[tri + 2] = bottomLeft;
                triangles[tri + 3] = topLeft;
                triangles[tri + 4] = topRight;
                triangles[tri + 5] = bottomRight;
            }
        }

        float radius = _shapeGenerator.Settings.PlanetRadius;
        var mesh = new Mesh
        {
            name = $"gpu-chunk-face-{face}",
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
            vertices = vertices,
            triangles = triangles,
            uv = uvs,
            bounds = new Bounds(Vector3.zero, Vector3.one * radius * 2.5f)
        };
        mesh.RecalculateNormals();
        return mesh;
    }

    void UploadNoiseLayers()
    {
        if (_noiseLayerBuffer != null)
        {
            _noiseLayerBuffer.Release();
            _noiseLayerBuffer = null;
        }

        var sourceLayers = _shapeGenerator.Settings.NoiseLayers;
        int count = sourceLayers?.Length ?? 0;
        var layers = new GpuNoiseLayer[Mathf.Max(count, 1)];
        for (int i = 0; i < count; i++)
        {
            var source = sourceLayers[i];
            var settings = source.NoiseSettings;
            layers[i] = new GpuNoiseLayer
            {
                Enabled = source.Enabled ? 1 : 0,
                FilterType = (int)settings.Filter,
                UseFirstLayerAsMask = source.UseFirstLayerAsMask ? 1 : 0,
                Layers = Mathf.Clamp(settings.Layers, 0, 8),
                Seed = _seed + i,
                Strength = settings.Strength,
                BaseRoughness = settings.BaseRoughness,
                Roughness = settings.Roughness,
                Persistence = settings.Persistence,
                Center = settings.Center,
                MinValue = settings.MinValue
            };
        }

        _noiseLayerBuffer = new ComputeBuffer(layers.Length, Marshal.SizeOf<GpuNoiseLayer>());
        _noiseLayerBuffer.SetData(layers);
    }

    void DispatchFace(int face)
    {
        int vertexCount = PatchResolution * PatchResolution;

        ReleaseFaceBuffers(face);
        _vertexBuffers[face] = new ComputeBuffer(vertexCount, sizeof(float) * 4);
        _normalBuffers[face] = new ComputeBuffer(vertexCount, sizeof(float) * 4);

        _terrainCompute.SetInt(ResolutionId, PatchResolution);
        _terrainCompute.SetInt(FaceIndexId, face);
        _terrainCompute.SetFloat(PlanetRadiusId, _shapeGenerator.Settings.PlanetRadius);
        _terrainCompute.SetInt(UseNoiseId, UseFullNoiseKernel ? 1 : 0);
        _terrainCompute.SetInt(NoiseLayerCountId, UseFullNoiseKernel ? _shapeGenerator.LayerCount : 0);
        _terrainCompute.SetBuffer(_kernel, NoiseLayersId, _noiseLayerBuffer);
        _terrainCompute.SetBuffer(_kernel, VerticesId, _vertexBuffers[face]);
        _terrainCompute.SetBuffer(_kernel, NormalsAndElevationId, _normalBuffers[face]);

        int groups = Mathf.CeilToInt(PatchResolution / 8f);
        _terrainCompute.Dispatch(_kernel, groups, groups, 1);

        var mat = _materials[face];
        mat.SetBuffer(GpuTerrainVerticesId, _vertexBuffers[face]);
        mat.SetBuffer(GpuTerrainNormalsId, _normalBuffers[face]);
        mat.SetFloat(PlanetRadiusId, _shapeGenerator.Settings.PlanetRadius);
        mat.SetFloat(OceanLevelId, _oceanLevel);
    }

    async Awaitable ReadBackFaceSamplers(CancellationToken ct, IProgressHandle progress)
    {
        var vertexRequests = new AsyncGPUReadbackRequest[6];
        var normalRequests = new AsyncGPUReadbackRequest[6];
        for (int face = 0; face < 6; face++)
        {
            vertexRequests[face] = AsyncGPUReadback.Request(_vertexBuffers[face]);
            normalRequests[face] = AsyncGPUReadback.Request(_normalBuffers[face]);
        }

        bool allDone;
        int waitFrames = 0;
        do
        {
            ct.ThrowIfCancellationRequested();
            allDone = true;
            for (int face = 0; face < 6; face++)
                allDone &= vertexRequests[face].done && normalRequests[face].done;
            if (!allDone)
            {
                waitFrames++;
                if (waitFrames > MaxReadbackWaitFrames)
                    throw new System.TimeoutException($"GPU terrain readback timed out after {MaxReadbackWaitFrames} frames.");
                if (waitFrames % 30 == 0)
                    progress?.Report(0.65f, $"Waiting for GPU terrain readback... {waitFrames}/{MaxReadbackWaitFrames}");
                await Awaitable.NextFrameAsync(ct);
            }
        }
        while (!allDone);

        for (int face = 0; face < 6; face++)
        {
            if (vertexRequests[face].hasError || normalRequests[face].hasError)
                throw new System.InvalidOperationException($"GPU terrain readback failed for face {face}.");

            var vertices = vertexRequests[face].GetData<Vector4>();
            var normals = normalRequests[face].GetData<Vector4>();
            _samplers[face] = GpuFaceSampler.FromReadback(PatchResolution, vertices, normals);

            var elevations = _samplers[face].Elevations;
            for (int i = 0; i < elevations.Length; i++)
                _shapeGenerator.RecordElevationSample(elevations[i]);

            progress?.Report(0.65f + 0.30f * ((face + 1f) / 6f), $"Read GPU face {face + 1}/6");
        }
    }

    void BuildProofSamplers(IProgressHandle progress)
    {
        for (int face = 0; face < 6; face++)
        {
            _samplers[face] = GpuFaceSampler.FromAnalyticProof(
                face,
                PatchResolution,
                _shapeGenerator.Settings.PlanetRadius);

            var elevations = _samplers[face].Elevations;
            for (int i = 0; i < elevations.Length; i++)
                _shapeGenerator.RecordElevationSample(elevations[i]);

            progress?.Report(0.65f + 0.30f * ((face + 1f) / 6f), $"Prepared GPU face cache {face + 1}/6");
        }
    }

    static float EvaluateProofElevation(Vector3 samplePoint)
    {
        float continental = Mathf.Sin(samplePoint.x * 7.0f + samplePoint.z * 3.5f) * Mathf.Cos(samplePoint.y * 5.0f);
        float detail = Mathf.Sin(Vector3.Dot(samplePoint, new Vector3(17.0f, 11.0f, 13.0f)));
        return continental * 0.018f + detail * 0.004f - 0.004f;
    }

    void ReleaseComputeBuffers()
    {
        if (_noiseLayerBuffer != null)
        {
            _noiseLayerBuffer.Release();
            _noiseLayerBuffer = null;
        }

        if (_vertexBuffers == null) return;
        for (int i = 0; i < _vertexBuffers.Length; i++)
            ReleaseFaceBuffers(i);
    }

    void ReleaseFaceBuffers(int face)
    {
        if (_vertexBuffers != null && _vertexBuffers[face] != null)
        {
            _vertexBuffers[face].Release();
            _vertexBuffers[face] = null;
        }

        if (_normalBuffers != null && _normalBuffers[face] != null)
        {
            _normalBuffers[face].Release();
            _normalBuffers[face] = null;
        }
    }

    static void DestroyUnityObject(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Object.Destroy(obj);
        else Object.DestroyImmediate(obj);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct GpuNoiseLayer
    {
        public int Enabled;
        public int FilterType;
        public int UseFirstLayerAsMask;
        public int Layers;
        public int Seed;
        public float Strength;
        public float BaseRoughness;
        public float Roughness;
        public float Persistence;
        public Vector3 Center;
        public float MinValue;
    }

    sealed class GpuFaceSampler : IFaceMeshSampler
    {
        float[] _radii;

        public Vector3[] UnitSpherePoints { get; private set; }
        public float[] Elevations { get; private set; }
        public int Resolution { get; private set; }

        public static GpuFaceSampler FromReadback(
            int resolution,
            Unity.Collections.NativeArray<Vector4> vertices,
            Unity.Collections.NativeArray<Vector4> normalsAndElevation)
        {
            int count = vertices.Length;
            var sampler = new GpuFaceSampler
            {
                Resolution = resolution,
                UnitSpherePoints = new Vector3[count],
                Elevations = new float[count],
                _radii = new float[count]
            };

            for (int i = 0; i < count; i++)
            {
                Vector3 v = new Vector3(vertices[i].x, vertices[i].y, vertices[i].z);
                sampler._radii[i] = v.magnitude;
                sampler.UnitSpherePoints[i] = sampler._radii[i] > 0.0001f ? v / sampler._radii[i] : Vector3.up;
                sampler.Elevations[i] = normalsAndElevation[i].w;
            }

            return sampler;
        }

        public static GpuFaceSampler FromAnalyticProof(int face, int resolution, float planetRadius)
        {
            int count = resolution * resolution;
            int cells = Mathf.Max(resolution - 1, 1);
            var sampler = new GpuFaceSampler
            {
                Resolution = resolution,
                UnitSpherePoints = new Vector3[count],
                Elevations = new float[count],
                _radii = new float[count]
            };

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int index = x + y * resolution;
                    Vector2 uv = new Vector2((float)x / cells, (float)y / cells);
                    Vector3 direction = CoordinateConverter.CubeFaceToUnitSphere(face, uv);
                    float elevation = EvaluateProofElevation(direction);
                    sampler.UnitSpherePoints[index] = direction;
                    sampler.Elevations[index] = elevation;
                    sampler._radii[index] = planetRadius * (1f + elevation);
                }
            }

            return sampler;
        }

        public bool TrySampleRadius(Vector2 uv, out float radius)
        {
            radius = 0f;
            if (_radii == null || _radii.Length == 0) return false;

            float gx = Mathf.Clamp01(uv.x) * (Resolution - 1);
            float gy = Mathf.Clamp01(uv.y) * (Resolution - 1);
            int x0 = Mathf.FloorToInt(gx);
            int y0 = Mathf.FloorToInt(gy);
            int x1 = Mathf.Min(x0 + 1, Resolution - 1);
            int y1 = Mathf.Min(y0 + 1, Resolution - 1);
            float fx = gx - x0;
            float fy = gy - y0;

            float r00 = _radii[x0 + y0 * Resolution];
            float r10 = _radii[x1 + y0 * Resolution];
            float r01 = _radii[x0 + y1 * Resolution];
            float r11 = _radii[x1 + y1 * Resolution];
            radius = Mathf.Lerp(Mathf.Lerp(r00, r10, fx), Mathf.Lerp(r01, r11, fx), fy);
            return radius > 0f;
        }
    }
}
#endif

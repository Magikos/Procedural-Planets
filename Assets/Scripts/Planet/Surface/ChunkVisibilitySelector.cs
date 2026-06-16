using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

public interface IChunkVisibilitySelector
{
    void AttachModel(TerrainQuadtree[] quadtrees, bool[] faceVisible);
    bool UpdateForCamera(Vector3 observerWorldPosition, Camera observerCamera);
    IReadOnlyList<PlanetChunk> GetVisibleLeaves(int face);
    void GetGrassResidencyChunks(Camera camera, float frustumPaddingDegrees, List<PlanetChunk> output);
    void ResetVisibleLeaves();
    void LogInitialDiagnostics();
    void Dispose();
}

// Per-frame LOD/frustum/horizon selection over the cube-sphere quadtree. Each Tick it walks
// every face, decides "render this OR recurse into children" from the chunk's projected screen
// size, and toggles the mesh cache for chunks that entered/left the visible set. Holds the
// per-face visible-leaf snapshot so the provider's queries (visible-chunk snapshot, raycast)
// can read it.
//
// Split out of ChunkedSurfaceProvider (restructure step 4). Reads the shared quadtree model and
// drives IChunkMeshCache.SetVisible one-way; it never reads back from the render layer.
public sealed class ChunkVisibilitySelector : IChunkVisibilitySelector
{
    // LOD target: a chunk refines when its conservative projected diameter exceeds this.
    // This is a screen-space contract, not a terrain-value tuning knob.
    const float TargetChunkScreenPixels = 220f;
    const float LodSplitHysteresis = 1.10f;
    const float LodMergeHysteresis = 0.85f;
    const float FrustumBoundsPadding = 1.08f;
    const float HorizonRadiusScale = 0.98f;
    const float HorizonMarginScale = 2f;
    const float DiagnosticsLogIntervalSeconds = 1f;
    // Flip to true (or define the symbol below) to log per-tick chunk visibility breakdowns.
    // Default off â€” was spamming the console once visibility changes were happening every frame
    // during fly-through. Useful when diagnosing LOD/culling behavior.
#if PLANET_CHUNK_DIAGNOSTICS
    const bool EnableChunkDiagnosticsLog = true;
#else
    static readonly bool EnableChunkDiagnosticsLog = false;
#endif

    readonly Transform _planetTransform;
    readonly ShapeGenerator _shapeGenerator;
    readonly IChunkMeshCache _meshCache;
    readonly int _maxChunkDepth;
    readonly int _chunkResolution;

    TerrainQuadtree[] _quadtrees;
    bool[] _faceVisible;

    // Per-face visible-leaf snapshot â€” compared each Tick to detect changes.
    readonly List<PlanetChunk>[] _visibleLeavesPerFace = new List<PlanetChunk>[6];
    readonly List<PlanetChunk> _tmpVisibleLeaves = new();
    readonly HashSet<PlanetChunk> _tmpVisibleSet = new();

    readonly Plane[] _lodFrustumPlanes = new Plane[6];
    readonly Plane[] _grassResidencyFrustumPlanes = new Plane[6];
    Camera _lodCamera;
    bool _hasLodCamera;
    float _lodFocalLengthPixels = 935f;
    float _nextDiagnosticsLogTime;

    // Cached per-tick to avoid repeated Transform property reads across hundreds of EstimateWorldBounds calls.
    Vector3 _cachedPlanetPosition;
    Matrix4x4 _cachedLocalToWorld;
    float _cachedPlanetScale;

    static readonly string ProfTick = "ChunkedSurfaceProvider.Tick";
    static readonly string ProfVisibility = "ChunkedSurfaceProvider.UpdateVisibility";

    public ChunkVisibilitySelector(
        Transform planetTransform,
        ShapeGenerator shapeGenerator,
        IChunkMeshCache meshCache,
        int maxChunkDepth,
        int chunkResolution)
    {
        _planetTransform = planetTransform;
        _shapeGenerator = shapeGenerator;
        _meshCache = meshCache;
        _maxChunkDepth = maxChunkDepth;
        _chunkResolution = chunkResolution;
    }

    // Quadtrees + per-face visibility are created by the orchestrator (EnsureFaceObjects) after
    // construction, so they are wired in once they exist rather than through the constructor.
    public void AttachModel(TerrainQuadtree[] quadtrees, bool[] faceVisible)
    {
        _quadtrees = quadtrees;
        _faceVisible = faceVisible;
    }

    public IReadOnlyList<PlanetChunk> GetVisibleLeaves(int face)
    {
        if (_visibleLeavesPerFace == null || face < 0 || face >= _visibleLeavesPerFace.Length)
            return null;
        return _visibleLeavesPerFace[face];
    }

    public bool UpdateForCamera(Vector3 observerWorldPosition, Camera observerCamera)
    {
        Profiler.BeginSample(ProfTick);

        PrepareLodContext(observerCamera);
        bool anyVisibilityChanged = false;

        // Per-face visibility filter. Changes only toggle cached chunk renderers.
        Profiler.BeginSample(ProfVisibility);
        for (int f = 0; f < 6; f++)
        {
            if (UpdateVisibleLeavesForFace(f, observerWorldPosition))
            {
                anyVisibilityChanged = true;
            }
        }
        Profiler.EndSample();

        if (anyVisibilityChanged)
            LogChunkDiagnostics("tick");

        Profiler.EndSample();
        return anyVisibilityChanged;
    }

    public void GetGrassResidencyChunks(
        Camera camera,
        float frustumPaddingDegrees,
        List<PlanetChunk> output)
    {
        if (camera == null || !camera.isActiveAndEnabled || _quadtrees == null)
            return;

        RefreshTransformCache();
        CalculateExpandedFrustumPlanes(camera, frustumPaddingDegrees, _grassResidencyFrustumPlanes);
        Vector3 observerPosition = camera.transform.position;
        for (int face = 0; face < _quadtrees.Length; face++)
        {
            if (_faceVisible != null && !_faceVisible[face])
                continue;
            GatherGrassResidencyLeaves(
                _quadtrees[face].Root,
                observerPosition,
                _grassResidencyFrustumPlanes,
                output);
        }
    }

    public void ResetVisibleLeaves()
    {
        for (int f = 0; f < 6; f++)
            _visibleLeavesPerFace[f]?.Clear();
    }

    public void LogInitialDiagnostics() => LogChunkDiagnostics("initial");

    public void Dispose()
    {
        for (int f = 0; f < 6; f++)
            _visibleLeavesPerFace[f]?.Clear();
    }

    void PrepareLodContext(Camera camera)
    {
        _lodCamera = camera != null && camera.isActiveAndEnabled ? camera : null;
        _hasLodCamera = _lodCamera != null;
        RefreshTransformCache();
        if (!_hasLodCamera) return;

        GeometryUtility.CalculateFrustumPlanes(_lodCamera, _lodFrustumPlanes);

        float pixelHeight = _lodCamera.pixelHeight > 0 ? _lodCamera.pixelHeight : Mathf.Max(Screen.height, 1);
        float halfFovRad = Mathf.Max(_lodCamera.fieldOfView, 1f) * Mathf.Deg2Rad * 0.5f;
        _lodFocalLengthPixels = pixelHeight / (2f * Mathf.Tan(halfFovRad));
    }

    void RefreshTransformCache()
    {
        _cachedPlanetPosition = _planetTransform.position;
        _cachedLocalToWorld = _planetTransform.localToWorldMatrix;
        Vector3 lossyScale = _planetTransform.lossyScale;
        _cachedPlanetScale = Mathf.Max(Mathf.Max(Mathf.Abs(lossyScale.x), Mathf.Abs(lossyScale.y)), Mathf.Abs(lossyScale.z));
    }

    bool UpdateVisibleLeavesForFace(int faceIdx, Vector3 observerPos)
    {
        _tmpVisibleLeaves.Clear();
        GatherVisibleLeaves(_quadtrees[faceIdx].Root, observerPos, _tmpVisibleLeaves);

        var current = _visibleLeavesPerFace[faceIdx];
        if (current == null) current = _visibleLeavesPerFace[faceIdx] = new List<PlanetChunk>(64);

        bool changed = current.Count != _tmpVisibleLeaves.Count;
        if (!changed)
            for (int i = 0; i < current.Count; i++)
                if (!ReferenceEquals(current[i], _tmpVisibleLeaves[i])) { changed = true; break; }

        if (changed)
        {
            _tmpVisibleSet.Clear();
            for (int i = 0; i < _tmpVisibleLeaves.Count; i++)
                _tmpVisibleSet.Add(_tmpVisibleLeaves[i]);

            for (int i = 0; i < current.Count; i++)
            {
                var oldLeaf = current[i];
                if (!_tmpVisibleSet.Contains(oldLeaf))
                    _meshCache.SetVisible(oldLeaf, false);
            }

            for (int i = 0; i < _tmpVisibleLeaves.Count; i++)
            {
                var newLeaf = _tmpVisibleLeaves[i];
                if (!ContainsReference(current, newLeaf))
                    _meshCache.SetVisible(newLeaf, true);
            }

            current.Clear();
            current.AddRange(_tmpVisibleLeaves);
        }
        return changed;
    }

    static bool ContainsReference(List<PlanetChunk> list, PlanetChunk chunk)
    {
        if (list == null) return false;
        for (int i = 0; i < list.Count; i++)
            if (ReferenceEquals(list[i], chunk))
                return true;
        return false;
    }

    void GatherVisibleLeaves(PlanetChunk chunk, Vector3 observerPos, List<PlanetChunk> output)
    {
        if (chunk == null || chunk.CpuVertices == null) return;

        Bounds worldBounds = EstimateWorldBounds(chunk);
        if (!IsChunkVisibleCandidate(chunk, observerPos, worldBounds)) return;

        bool hasChildren = chunk.Children != null && chunk.Children.Length > 0;
        if (hasChildren && ShouldSubdivide(chunk, observerPos, worldBounds))
        {
            for (int i = 0; i < chunk.Children.Length; i++)
                GatherVisibleLeaves(chunk.Children[i], observerPos, output);
        }
        else
        {
            output.Add(chunk);
        }
    }

    void GatherGrassResidencyLeaves(
        PlanetChunk chunk,
        Vector3 observerPos,
        Plane[] residencyFrustumPlanes,
        List<PlanetChunk> output)
    {
        if (chunk == null || chunk.CpuVertices == null)
            return;

        Bounds worldBounds = EstimateWorldBounds(chunk);
        if (!GeometryUtility.TestPlanesAABB(residencyFrustumPlanes, worldBounds)
            || !IsChunkAboveHorizon(chunk, observerPos, worldBounds))
        {
            return;
        }

        bool hasChildren = chunk.Children != null && chunk.Children.Length > 0;
        if (hasChildren && ShouldSubdivide(chunk, observerPos, worldBounds))
        {
            for (int i = 0; i < chunk.Children.Length; i++)
                GatherGrassResidencyLeaves(
                    chunk.Children[i],
                    observerPos,
                    residencyFrustumPlanes,
                    output);
            return;
        }

        output.Add(chunk);
    }

    static void CalculateExpandedFrustumPlanes(
        Camera camera,
        float paddingDegrees,
        Plane[] outputPlanes)
    {
        float near = Mathf.Max(camera.nearClipPlane, 0.01f);
        float far = Mathf.Max(camera.farClipPlane, near + 1f);
        float aspect = Mathf.Max(camera.aspect, 0.01f);
        float padding = Mathf.Clamp(paddingDegrees, 0f, 60f);
        Matrix4x4 projection;

        if (camera.orthographic)
        {
            float scale = 1f + padding / 45f;
            float halfHeight = Mathf.Max(camera.orthographicSize * scale, 0.01f);
            float halfWidth = halfHeight * aspect;
            projection = Matrix4x4.Ortho(
                -halfWidth,
                halfWidth,
                -halfHeight,
                halfHeight,
                near,
                far);
        }
        else
        {
            float verticalFov = Mathf.Clamp(camera.fieldOfView + padding * 2f, 1f, 175f);
            projection = Matrix4x4.Perspective(verticalFov, aspect, near, far);
        }

        GeometryUtility.CalculateFrustumPlanes(
            projection * camera.worldToCameraMatrix,
            outputPlanes);
    }

    bool ShouldSubdivide(PlanetChunk chunk, Vector3 observerPos, Bounds worldBounds)
    {
        if (chunk.DetailLevel >= _maxChunkDepth) return false;
        if (!_hasLodCamera) return false;

        float projectedPixels = ProjectedChunkDiameterPixels(chunk, observerPos, worldBounds);
        float threshold = WasRefinedInPreviousVisibility(chunk)
            ? TargetChunkScreenPixels * LodMergeHysteresis
            : TargetChunkScreenPixels * LodSplitHysteresis;
        return projectedPixels > threshold;
    }

    bool WasRefinedInPreviousVisibility(PlanetChunk chunk)
    {
        var leaves = _visibleLeavesPerFace[chunk.FaceIndex];
        if (leaves == null || leaves.Count == 0) return false;

        for (int i = 0; i < leaves.Count; i++)
            if (!ReferenceEquals(leaves[i], chunk) && IsDescendantOf(leaves[i], chunk))
                return true;
        return false;
    }

    static bool IsDescendantOf(PlanetChunk node, PlanetChunk ancestor)
    {
        while (node != null)
        {
            if (ReferenceEquals(node, ancestor)) return true;
            node = node.Parent;
        }
        return false;
    }

    float DistanceToChunkCenterSquared(PlanetChunk chunk, Vector3 observerPos)
    {
        Vector3 sphereCenter = CoordinateConverter.CubeFaceToUnitSphere(chunk.FaceIndex, chunk.UvCenter);
        Vector3 worldCenter = _cachedPlanetPosition
            + _cachedLocalToWorld.MultiplyVector(sphereCenter) * _shapeGenerator.Settings.PlanetRadius;
        return (observerPos - worldCenter).sqrMagnitude;
    }

    float ProjectedChunkDiameterPixels(PlanetChunk chunk, Vector3 observerPos, Bounds worldBounds)
    {
        float diameter = worldBounds.extents.magnitude * 2f;
        float distance = Mathf.Sqrt(Mathf.Max(DistanceToChunkCenterSquared(chunk, observerPos), 0.0001f));
        float safeDistance = Mathf.Max(Mathf.Max(distance, diameter * 0.25f), 1f);
        return diameter * _lodFocalLengthPixels / safeDistance;
    }

    bool IsChunkVisibleCandidate(PlanetChunk chunk, Vector3 observerPos, Bounds worldBounds)
    {
        if (!_hasLodCamera) return true;
        if (!GeometryUtility.TestPlanesAABB(_lodFrustumPlanes, worldBounds))
            return false;
        return IsChunkAboveHorizon(chunk, observerPos, worldBounds);
    }

    Bounds EstimateWorldBounds(PlanetChunk chunk)
    {
        Bounds local = chunk.CpuLocalBounds;
        Vector3 center = _cachedLocalToWorld.MultiplyPoint3x4(local.center);
        float radius = Mathf.Max(local.extents.magnitude * _cachedPlanetScale * FrustumBoundsPadding, 1f);
        return new Bounds(center, Vector3.one * (radius * 2f));
    }

    bool IsChunkAboveHorizon(PlanetChunk chunk, Vector3 observerPos, Bounds worldBounds)
    {
        Vector3 planetCenter = _cachedPlanetPosition;
        Vector3 observer = observerPos - planetCenter;
        float observerDistance = observer.magnitude;
        float planetRadius = _shapeGenerator.Settings.PlanetRadius * Mathf.Max(HorizonRadiusScale, 0.01f);
        if (observerDistance <= planetRadius) return true;

        Vector3 chunkCenter = worldBounds.center - planetCenter;
        if (chunkCenter.sqrMagnitude < 0.0001f) return true;

        float dot = Vector3.Dot(chunkCenter.normalized, observer / observerDistance);
        float horizonThreshold = Mathf.Clamp(planetRadius / observerDistance, -1f, 1f);
        float angularMargin = Mathf.Clamp01(worldBounds.extents.magnitude / observerDistance) * HorizonMarginScale;
        return dot >= horizonThreshold - angularMargin;
    }

    void LogChunkDiagnostics(string reason)
    {
        if (!EnableChunkDiagnosticsLog) return;

        float now = Time.realtimeSinceStartup;
        if (reason == "tick" && now < _nextDiagnosticsLogTime) return;
        _nextDiagnosticsLogTime = now + DiagnosticsLogIntervalSeconds;

        int totalLeaves = 0;
        int totalVertices = 0;
        int totalTriangles = 0;
        int[] depthCounts = new int[Mathf.Max(_maxChunkDepth + 1, 1)];

        for (int f = 0; f < 6; f++)
        {
            var leaves = _visibleLeavesPerFace[f];
            if (leaves == null) continue;
            for (int i = 0; i < leaves.Count; i++)
            {
                var leaf = leaves[i];
                if (leaf == null || leaf.CpuVertices == null) continue;
                totalLeaves++;
                totalVertices += leaf.CpuVertices.Length;
                totalTriangles += ChunkTriangleTemplate.TriangleCount(_chunkResolution) / 3;
                if (leaf.DetailLevel >= 0 && leaf.DetailLevel < depthCounts.Length)
                    depthCounts[leaf.DetailLevel]++;
            }
        }

        LoggerProvider.Log(LogLevel.Debug, "ChunkLOD",
            $"{reason}: visibleLeaves={totalLeaves} verts={totalVertices:n0} tris={totalTriangles:n0} depthCounts={FormatDepthCounts(depthCounts)} camera={(_hasLodCamera ? _lodCamera.name : "none")}");
    }

    static string FormatDepthCounts(int[] depthCounts)
    {
        if (depthCounts == null || depthCounts.Length == 0) return "[]";

        string result = "[";
        bool first = true;
        for (int i = 0; i < depthCounts.Length; i++)
        {
            if (depthCounts[i] == 0) continue;
            if (!first) result += ",";
            result += i.ToString() + ":" + depthCounts[i].ToString();
            first = false;
        }
        return result + "]";
    }
}

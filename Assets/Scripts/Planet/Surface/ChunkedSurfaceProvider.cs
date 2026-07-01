using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Unity.Collections;
using UnityEngine;

public interface IChunkVisibilitySource
{
    void GetVisibleChunksSnapshot(List<PlanetChunk> output);
    event System.Action<PlanetChunk> ChunkShown;
    event System.Action<PlanetChunk> ChunkHidden;
}

// Phase A "High" resolution provider â€” pre-cache + visibility filter.
//
// At Planet.GenerateAsync time, the provider builds 6 quadtrees to a fixed MaxChunkDepth and
// schedules Burst mesh jobs for EVERY chunk (internal + leaves) in batches. All chunk CPU
// vertex data is populated by the time GenerateAsync returns. At runtime, Tick is a cheap
// visibility filter: for each non-leaf, decide "render this OR recurse into children" based
// on camera distance. Rebuilds happen only when a face's visible leaf set actually changes,
// capped at 1 face per Tick.
//
// Pivoted to this design 2026-05-30 after step-7 perf testing showed dynamic subdivision
// hitched too hard during fly-through. See docs/design/2026-05-30-chunk-skeleton.md.
public sealed class ChunkedSurfaceProvider : IPlanetSurfaceProvider, IChunkVisibilitySource, IMemoryReporter
{
    // Per-chunk vertex grid resolution. 97 = 9,409 vertices and 18,432 triangles per chunk
    // (vs 65 = 4,225 verts / 8,192 tris). The bump improves biome-color sharpness and terrain
    // detail at the cost of ~2.2Ã— memory and mesh-gen time. True per-pixel biome boundaries
    // need shader-based biome sampling, which is Phase B work â€” this is the best we can do at
    // the vertex-color level.
    const int ChunkResolution = 97;
    // Depth at which we aggregate chunk vertex data for the WaterMeshBuilder face sampler.
    // 2 â†’ 385Â² per face (with R=97), 16Ã— finer shorelines than the root chunk. Bounded by
    // MaxChunkDepth at construction time. Memory: ~14 MB total across 6 faces at depth 2.
    const int WaterAggregateDepth = 2;
    const int TextureAllocationBatchSize = 64;
    const float HardDiscEdgePixels = 1.25f;
    static readonly Color32[] EmptySurfaceStatePixels =
        new Color32[PlanetChunkTextures.BiomeMapResolution * PlanetChunkTextures.BiomeMapResolution];
    static readonly byte[] EmptyPathWearPixels =
        new byte[PlanetChunkTextures.PathWearResolution * PlanetChunkTextures.PathWearResolution];

    readonly Transform _planetTransform;
    readonly ShapeGenerator _shapeGenerator;
    readonly Material _faceMaterial;
    readonly Planet.FaceRenderMask _renderMask;
    readonly int _maxChunkDepth;
    readonly bool _usesFaceBiomeAtlases;

    Transform[] _faceRoots;
    TerrainQuadtree[] _quadtrees;
    bool[] _faceVisible;
    IFaceMeshSampler[] _rootSamplers;
    readonly BiomeAtlasService _biomeAtlas;
    readonly ChunkSurfaceGenerator _generator;
    readonly IChunkMeshCache _meshCache;
    readonly IChunkVisibilitySelector _selector;
    GrassSurfaceAtlasGpuData _grassSurfaceAtlases;

    readonly List<PlanetChunk> _allChunks = new();

    // Working pixel arrays reused across one paint stroke so a drag doesn't allocate a fresh
    // GetPixels32() copy per stamp per chunk. Flushed by EndSurfaceStateBatch() on stroke end.
    readonly Dictionary<PlanetChunk, Color32[]> _surfaceStateBatch = new();

    // Per-texel max coverage laid down by the current stroke. Overlapping stamps in one drag
    // composite by max (a smooth soft-edged line) instead of summing into a hard core; the
    // accumulated coverage is added to the persistent mask, so repeated drags still build up.
    readonly Dictionary<PlanetChunk, byte[]> _strokeContribution = new();
    readonly Dictionary<PlanetChunk, byte[]> _strokeBaseline = new();

    bool _initialized;

    // The mesh cache owns the visible-set toggling, so it raises these; forward subscriptions
    // straight through so external observers (grass) bind once to the provider.
    public event System.Action<PlanetChunk> ChunkShown
    {
        add => _meshCache.ChunkShown += value;
        remove => _meshCache.ChunkShown -= value;
    }
    public event System.Action<PlanetChunk> ChunkHidden
    {
        add => _meshCache.ChunkHidden += value;
        remove => _meshCache.ChunkHidden -= value;
    }
    public GrassSurfaceAtlasGpuData GrassSurfaceAtlases => _grassSurfaceAtlases;
    public int MaxChunkDepth => _maxChunkDepth;

    public ChunkedSurfaceProvider(
        Transform planetTransform,
        ShapeGenerator shapeGenerator,
        Material faceMaterial,
        Planet.FaceRenderMask renderMask,
        int maxChunkDepth)
    {
        _planetTransform = planetTransform;
        _shapeGenerator = shapeGenerator;
        _faceMaterial = faceMaterial;
        _renderMask = renderMask;
        _maxChunkDepth = Mathf.Clamp(maxChunkDepth, 0, PlanetChunk.MaxDetailLevel);
        _usesFaceBiomeAtlases = BiomeAtlasService.CanBuildFaceAtlases(
            _maxChunkDepth,
            out _);
        _biomeAtlas = new BiomeAtlasService(_maxChunkDepth);
        _generator = new ChunkSurfaceGenerator(_shapeGenerator, ChunkResolution);
        _meshCache = new ChunkMeshCache(_faceMaterial, _biomeAtlas, ChunkResolution);
        _selector = new ChunkVisibilitySelector(
            _planetTransform, _shapeGenerator, _meshCache, _maxChunkDepth, ChunkResolution);
        MemoryDebugModule.Register(this);
    }

    public void AppendMemoryReport(System.Text.StringBuilder sb)
    {
        long bytesPerTexture = (long)(PlanetChunkTextures.BiomeMapResolution * PlanetChunkTextures.BiomeMapResolution) * 4L;
        long bytesPerPathWearTexture = PlanetChunkTextures.PathWearResolution * PlanetChunkTextures.PathWearResolution;
        sb.AppendLine($"Chunk texture sets (any): {PlanetChunkTextures.LiveTextureSets}");
        sb.AppendLine($"Chunk biome texture sets: {PlanetChunkTextures.LiveBiomeTextureSets} " +
            $"(raw pixels/copy={MemoryDebugModule.FormatBytes(PlanetChunkTextures.LiveBiomeTextureSets * 3 * bytesPerTexture)})");
        sb.AppendLine($"Chunk surface-state textures: {PlanetChunkTextures.LiveSurfaceStateTextures} " +
            $"(raw pixels/copy={MemoryDebugModule.FormatBytes(PlanetChunkTextures.LiveSurfaceStateTextures * bytesPerTexture)})");
        sb.AppendLine($"Chunk path-wear textures: {PlanetChunkTextures.LivePathWearTextures} " +
            $"(raw pixels/copy={MemoryDebugModule.FormatBytes(PlanetChunkTextures.LivePathWearTextures * bytesPerPathWearTexture)})");

        long retainedBytes = 0;
        for (int i = 0; i < _allChunks.Count; i++)
        {
            if (_allChunks[i] != null)
                retainedBytes += _allChunks[i].GetRetainedCpuDataBytes();
        }
        sb.AppendLine($"Chunk CPU arrays retained: {MemoryDebugModule.FormatBytes(retainedBytes)}");
    }

    public async Awaitable GenerateAsync(IProgressHandle progress, CancellationToken ct)
    {
        EnsureFaceObjects();

        progress?.Report(0f, "Building chunk quadtrees...");
        LoggerProvider.Log(
            LogLevel.Debug,
            "ChunkLOD",
            _usesFaceBiomeAtlases
                ? "Using direct face-atlas biome bake; temporary per-chunk biome textures are disabled."
                : "Using per-chunk biome texture fallback because face atlases are unavailable.");

        // Keep texture-mode terrain disabled until GenerateColorsAsync has baked and bound
        // real biome maps. Otherwise newly visible chunks sample blank startup textures.
        if (_faceMaterial != null) _faceMaterial.DisableKeyword(BiomeTextureModeKeyword);

        // 1) Build the full quadtree to max depth on every face.
        for (int f = 0; f < 6; f++)
        {
            _quadtrees[f].BuildToFixedDepth(_maxChunkDepth);
            progress?.Report(
                0.025f * ((f + 1f) / 6f),
                $"Built terrain tree {f + 1}/6...");
            await Awaitable.NextFrameAsync(ct);
        }

        // 2) Gather every chunk (internal + leaf) â€” all are rendering candidates depending
        //    on camera distance. Sort leaves-first / coarse-to-fine isn't necessary; the
        //    Burst scheduler handles arbitrary order well.
        var allChunks = new List<PlanetChunk>(1024);
        for (int f = 0; f < 6; f++) CollectAllChunks(_quadtrees[f].Root, allChunks);
        _allChunks.Clear();
        _allChunks.AddRange(allChunks);

        // Surface-state textures persist per chunk. Per-chunk biome textures are only needed
        // on the fallback path; the normal high-resolution path bakes directly into face atlases.
        for (int batchStart = 0; batchStart < _allChunks.Count; batchStart += TextureAllocationBatchSize)
        {
            int batchEnd = Mathf.Min(
                batchStart + TextureAllocationBatchSize,
                _allChunks.Count);
            for (int i = batchStart; i < batchEnd; i++)
            {
                PlanetChunkTextures.Allocate(
                    _allChunks[i],
                    allocateBiomeTextures: !_usesFaceBiomeAtlases);
            }

            float pct = _allChunks.Count > 0
                ? (float)batchEnd / _allChunks.Count
                : 1f;
            progress?.Report(
                0.025f + 0.095f * pct,
                $"Prepared chunk textures {batchEnd}/{_allChunks.Count}...");
            await Awaitable.NextFrameAsync(ct);
        }

        int total = allChunks.Count;
        progress?.Report(0.12f, $"Generating {total} chunks...");

        // 3) Schedule + drain mesh jobs in bounded batches (owned by the generator).
        await _generator.GenerateMeshesAsync(allChunks, progress, 0.12f, 0.66f, ct);

        // 4) Meshes are no longer pre-built for every node. Render handles are pooled by the
        // mesh cache and built on demand as chunks become visible; the retained compact CPU
        // source rebuilds them on page-in. Visibility (step 7) drives the initial uploads.

        // 5) Build face-sampler views for the water builder. Aggregating depth-N chunks into
        //    a single per-face grid raises water-mesh resolution from ChunkResolution (root)
        //    to 2^N Ã— (ChunkResolution-1) + 1 â€” at depth 2 with R=97 that's 385Â² per face,
        //    16Ã— finer shorelines than the root sampler.
        int waterAggregateDepth = Mathf.Clamp(WaterAggregateDepth, 0, _maxChunkDepth);
        _rootSamplers = new IFaceMeshSampler[6];
        for (int f = 0; f < 6; f++)
        {
            if (waterAggregateDepth == 0)
            {
                _rootSamplers[f] = new ChunkedFaceMeshSampler(_quadtrees[f].Root, ChunkResolution);
            }
            else
            {
                var chunksAtDepth = new List<PlanetChunk>(1 << (waterAggregateDepth * 2));
                CollectChunksAtDepth(_quadtrees[f].Root, waterAggregateDepth, chunksAtDepth);
                _rootSamplers[f] = new ChunkedFaceMeshSampler(chunksAtDepth, ChunkResolution, waterAggregateDepth);
            }

            progress?.Report(
                0.78f + 0.08f * ((f + 1f) / 6f),
                $"Preparing water terrain data {f + 1}/6...");
            await Awaitable.NextFrameAsync(ct);
        }

        // 6) Build face-space radius + normal atlases for Phase C grass placement. The
        // renderer is added later; this data must exist before grass compute can place roots.
        progress?.Report(0.86f, "Building grass surface atlases...");
        await BuildGrassSurfaceAtlasesAsync(
            new ProgressRangeHandle(progress, 0.86f, 0.12f),
            ct);

        // 7) Defer initial visibility until Planet finishes color/water generation.
        // Showing chunks before biome generation can render placeholder terrain, so visibility
        // stays deferred until the complete planet generation sequence finishes.
        progress?.Report(0.99f, "Preparing visibility...");
        _selector.ResetVisibleLeaves();

        _initialized = true;
        _selector.LogInitialDiagnostics();
        progress?.Report(1f, "Chunked planet ready.");
    }

    public void Tick(Vector3 observerWorldPosition, Camera observerCamera)
    {
        if (!_initialized) return;
        using (FrameTimingCounters.Measure(FrameTimingSection.SurfaceVisibility))
            _selector.UpdateForCamera(observerWorldPosition, observerCamera);
    }

    public void GetVisibleChunksSnapshot(List<PlanetChunk> output)
    {
        output.Clear();
        for (int f = 0; f < 6; f++)
        {
            var leaves = _selector.GetVisibleLeaves(f);
            if (leaves == null) continue;

            for (int i = 0; i < leaves.Count; i++)
            {
                PlanetChunk chunk = leaves[i];
                if (chunk != null && _meshCache.IsActuallyVisible(chunk))
                    output.Add(chunk);
            }
        }
    }

    public bool IsChunkTerrainVisible(PlanetChunk chunk)
    {
        return _meshCache.IsActuallyVisible(chunk);
    }

    public void GetGrassResidencyChunks(
        Camera camera,
        float frustumPaddingDegrees,
        List<PlanetChunk> output)
    {
        if (output == null)
            return;

        output.Clear();
        if (!_initialized)
            return;

        _selector.GetGrassResidencyChunks(camera, frustumPaddingDegrees, output);
    }

    public bool TryGetLocalSurfaceRadius(Vector3 localUnitDirection, out float localRadius)
    {
        localRadius = 0f;
        if (_quadtrees == null) return false;

        var (face, faceUv) = CoordinateConverter.UnitSphereToCubeFace(localUnitDirection);
        if (face < 0 || face >= 6 || _quadtrees[face] == null) return false;

        var leaf = _quadtrees[face].FindLeafContaining(faceUv);
        if (leaf == null) return false;

        float chunkSize = leaf.UvHalfExtent * 2f;
        Vector2 chunkLocalUv = new(
            (faceUv.x - (leaf.UvCenter.x - leaf.UvHalfExtent)) / chunkSize,
            (faceUv.y - (leaf.UvCenter.y - leaf.UvHalfExtent)) / chunkSize);

        return leaf.TrySampleRadius(chunkLocalUv, out localRadius) && localRadius > 0f;
    }

    public bool TryPaintSurfaceStateDisc(
        Vector3 localUnitDirection,
        float radiusMeters,
        float strength,
        out string summary)
        => TryPaintSurfaceStateBrush(localUnitDirection, radiusMeters, strength,
            SurfacePathShape.SoftDisc, SurfacePathOperation.Paint, batched: false, strokeCoverage: false, out summary);

    public bool TryPaintSurfaceStateBrush(
        Vector3 localUnitDirection,
        float radiusMeters,
        float strength,
        SurfacePathShape shape,
        SurfacePathOperation operation,
        bool batched,
        bool strokeCoverage,
        out string summary)
    {
        summary = "path wear paint unavailable";
        if (!_initialized || _quadtrees == null || _allChunks.Count == 0)
            return false;
        if (!TryResolveBrushFootprint(localUnitDirection, out int face, out Vector2 faceUv, out float metersPerUv))
            return false;

        float radiusFaceUv = Mathf.Max(0.00001f, Mathf.Max(radiusMeters, 0.1f) / metersPerUv);
        byte pavedAlpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(strength) * 255f);

        int overlappedChunks = 0;
        int touchedChunks = 0;
        int changedPixels = 0;
        Vector2 minUv = faceUv - Vector2.one * radiusFaceUv;
        Vector2 maxUv = faceUv + Vector2.one * radiusFaceUv;
        for (int i = 0; i < _allChunks.Count; i++)
        {
            PlanetChunk chunk = _allChunks[i];
            if (chunk == null || !chunk.IsLeaf || chunk.FaceIndex != face || chunk.PathWearTexture == null)
                continue;
            if (!ChunkUvRectOverlaps(chunk, minUv, maxUv))
                continue;

            overlappedChunks++;
            byte[] pixels = GetPathWearPixels(chunk);
            byte[] contribution = GetStrokeContribution(chunk, pixels.Length, strokeCoverage);
            byte[] baseline = GetStrokeBaseline(chunk, pixels, strokeCoverage);
            int changed = PaintChunkPathWear(chunk, pixels, contribution, baseline, faceUv, radiusFaceUv, pavedAlpha, shape, operation);
            if (changed <= 0)
                continue;

            ApplyPathWear(chunk);
            touchedChunks++;
            changedPixels += changed;
        }

        summary = $"{operation.ToString().ToLowerInvariant()} path {shape.ToString().ToLowerInvariant()} face={face} uv=({faceUv.x:F3},{faceUv.y:F3}) radius={radiusMeters:F1}m chunks={touchedChunks} pixels={changedPixels}";
        return overlappedChunks > 0;
    }

    public bool TryPaintSurfaceStateTestPattern(
        Vector3 localUnitDirection,
        float sizeMeters,
        float strength,
        out string summary)
    {
        summary = "path wear pattern unavailable";
        if (!_initialized || _quadtrees == null || _allChunks.Count == 0)
            return false;
        if (!TryResolveBrushFootprint(localUnitDirection, out int face, out Vector2 faceUv, out float metersPerUv))
            return false;

        float halfSizeFaceUv = Mathf.Max(0.00001f, Mathf.Max(sizeMeters, 1f) * 0.5f / metersPerUv);
        byte pavedAlpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(strength) * 255f);

        int touchedChunks = 0;
        int changedPixels = 0;
        Vector2 minUv = faceUv - Vector2.one * halfSizeFaceUv;
        Vector2 maxUv = faceUv + Vector2.one * halfSizeFaceUv;
        for (int i = 0; i < _allChunks.Count; i++)
        {
            PlanetChunk chunk = _allChunks[i];
            if (chunk == null || !chunk.IsLeaf || chunk.FaceIndex != face || chunk.PathWearTexture == null)
                continue;
            if (!ChunkUvRectOverlaps(chunk, minUv, maxUv))
                continue;

            byte[] pixels = GetPathWearPixels(chunk);
            int changed = PaintChunkPathWearTestPattern(chunk, pixels, faceUv, halfSizeFaceUv, pavedAlpha);
            if (changed <= 0)
                continue;

            ApplyPathWear(chunk);
            touchedChunks++;
            changedPixels += changed;
        }

        summary = $"painted path test pattern face={face} uv=({faceUv.x:F3},{faceUv.y:F3}) size={sizeMeters:F1}m chunks={touchedChunks} pixels={changedPixels}";
        return changedPixels > 0;
    }

    public bool TryPaintSurfaceStateChannel(
        Vector3 localUnitDirection,
        float radiusMeters,
        float strength,
        int channel,
        SurfacePathShape shape,
        SurfacePathOperation operation,
        bool batched,
        out string summary)
    {
        summary = "surface-state paint unavailable";
        if (!_initialized || _quadtrees == null || _allChunks.Count == 0)
            return false;
        if (!TryResolveBrushFootprint(localUnitDirection, out int face, out Vector2 faceUv, out float metersPerUv))
            return false;

        channel = Mathf.Clamp(channel, 0, 3);
        float radiusFaceUv = Mathf.Max(0.00001f, Mathf.Max(radiusMeters, 0.1f) / metersPerUv);
        byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(strength) * 255f);

        int overlappedChunks = 0;
        int touchedChunks = 0;
        int changedPixels = 0;
        Vector2 minUv = faceUv - Vector2.one * radiusFaceUv;
        Vector2 maxUv = faceUv + Vector2.one * radiusFaceUv;
        for (int i = 0; i < _allChunks.Count; i++)
        {
            PlanetChunk chunk = _allChunks[i];
            if (chunk == null || !chunk.IsLeaf || chunk.FaceIndex != face || chunk.SurfaceStateTexture == null)
                continue;
            if (!ChunkUvRectOverlaps(chunk, minUv, maxUv))
                continue;

            overlappedChunks++;
            Color32[] pixels = GetSurfaceStatePixels(chunk, batched);
            int changed = PaintChunkSurfaceStateChannel(chunk, pixels, faceUv, radiusFaceUv, alpha, channel, shape, operation);
            if (changed <= 0)
                continue;

            ApplySurfaceState(chunk, pixels);
            touchedChunks++;
            changedPixels += changed;
        }

        summary = $"{operation.ToString().ToLowerInvariant()} surface-state channel={channel} {shape.ToString().ToLowerInvariant()} face={face} uv=({faceUv.x:F3},{faceUv.y:F3}) radius={radiusMeters:F1}m chunks={touchedChunks} pixels={changedPixels}";
        return overlappedChunks > 0;
    }

    public bool TryPaintScorchTestPattern(
        Vector3 localUnitDirection,
        float sizeMeters,
        float strength,
        out string summary)
    {
        summary = "scorch pattern unavailable";
        if (!_initialized || _quadtrees == null || _allChunks.Count == 0)
            return false;
        if (!TryResolveBrushFootprint(localUnitDirection, out int face, out Vector2 faceUv, out float metersPerUv))
            return false;

        float halfSizeFaceUv = Mathf.Max(0.00001f, Mathf.Max(sizeMeters, 1f) * 0.5f / metersPerUv);
        byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(strength) * 255f);

        int touchedChunks = 0;
        int changedPixels = 0;
        Vector2 minUv = faceUv - Vector2.one * halfSizeFaceUv;
        Vector2 maxUv = faceUv + Vector2.one * halfSizeFaceUv;
        for (int i = 0; i < _allChunks.Count; i++)
        {
            PlanetChunk chunk = _allChunks[i];
            if (chunk == null || !chunk.IsLeaf || chunk.FaceIndex != face || chunk.SurfaceStateTexture == null)
                continue;
            if (!ChunkUvRectOverlaps(chunk, minUv, maxUv))
                continue;

            Color32[] pixels = GetSurfaceStatePixels(chunk, batched: false);
            int changed = PaintChunkScorchTestPattern(chunk, pixels, faceUv, halfSizeFaceUv, alpha);
            if (changed <= 0)
                continue;

            ApplySurfaceState(chunk, pixels);
            touchedChunks++;
            changedPixels += changed;
        }

        summary = $"painted scorch test pattern face={face} uv=({faceUv.x:F3},{faceUv.y:F3}) size={sizeMeters:F1}m chunks={touchedChunks} pixels={changedPixels}";
        return changedPixels > 0;
    }

    public int RebuildPathWearFromStamps(IReadOnlyList<SurfacePathEditStamp> stamps, long now)
    {
        EndSurfaceStateBatch();
        ClearPathWearMasks();
        if (!_initialized || stamps == null || stamps.Count == 0)
            return 0;

        var contribution = new Dictionary<PlanetChunk, byte[]>();
        int currentStroke = int.MinValue;
        SurfacePathOperation currentOperation = SurfacePathOperation.Paint;
        bool hasGroup = false;
        PathWearBakeStamp previous = default;
        bool hasPrevious = false;
        int baked = 0;

        for (int i = 0; i < stamps.Count; i++)
        {
            SurfacePathEditStamp stamp = stamps[i];
            if (stamp.kind != "path")
                continue;
            if (!TryCreateBakeStamp(stamp, now, out PathWearBakeStamp bake))
                continue;

            bool sameGroup = hasGroup
                && bake.StrokeId == currentStroke
                && bake.Operation == currentOperation;
            if (!sameGroup)
            {
                ApplyPathWearContribution(contribution, currentOperation);
                contribution.Clear();
                currentStroke = bake.StrokeId;
                currentOperation = bake.Operation;
                hasGroup = true;
                hasPrevious = false;
            }

            AccumulatePathWearElement(contribution, bake.Face, bake.FaceUv, bake.FaceUv,
                bake.RadiusFaceUv, bake.Alpha, bake.Shape, segment: false);
            if (hasPrevious
                && previous.Face == bake.Face
                && previous.Shape != SurfacePathShape.HardSquare
                && bake.Shape != SurfacePathShape.HardSquare)
            {
                AccumulatePathWearElement(contribution, bake.Face, previous.FaceUv, bake.FaceUv,
                    Mathf.Max(previous.RadiusFaceUv, bake.RadiusFaceUv),
                    (byte)Mathf.Max(previous.Alpha, bake.Alpha),
                    bake.Shape,
                    segment: true);
            }

            previous = bake;
            hasPrevious = true;
            baked++;
        }

        ApplyPathWearContribution(contribution, currentOperation);
        return baked;
    }

    public int RebuildSurfaceStateFromStamps(IReadOnlyList<SurfacePathEditStamp> stamps, long now)
    {
        EndSurfaceStateBatch();
        ClearSurfaceStateOnly();
        if (!_initialized || stamps == null || stamps.Count == 0)
            return 0;

        int replayed = 0;
        for (int i = 0; i < stamps.Count; i++)
        {
            SurfacePathEditStamp stamp = stamps[i];
            if (stamp.kind != "scorch")
                continue;

            float strength = EffectiveStampStrength(stamp, now);
            if (strength <= 0f)
                continue;

            if (TryPaintSurfaceStateChannel(
                    stamp.direction,
                    Mathf.Max(stamp.radiusMeters, 0.1f),
                    strength,
                    channel: 1,
                    ParseStampShape(stamp.shape),
                    stamp.operation == "erase" ? SurfacePathOperation.Erase : SurfacePathOperation.Paint,
                    batched: false,
                    out _))
            {
                replayed++;
            }
        }

        return replayed;
    }

    // Resolves the cube face, face-space UV, and meters-per-UV scale at a local surface
    // direction. Shared by every brush so the disc/pattern paths agree on footprint math.
    bool TryResolveBrushFootprint(Vector3 localUnitDirection, out int face, out Vector2 faceUv, out float metersPerUv)
    {
        face = -1;
        faceUv = default;
        metersPerUv = 1f;
        if (localUnitDirection.sqrMagnitude < 0.0001f)
            return false;

        Vector3 dir = localUnitDirection.normalized;
        FaceSpaceCellRangeBuilder.DirectionToFaceUv(dir, out face, out faceUv);
        if (face < 0 || face >= _quadtrees.Length || _quadtrees[face] == null)
            return false;

        PlanetChunk centerChunk = _quadtrees[face].FindLeafContaining(faceUv);
        if (centerChunk == null)
            return false;

        float chunkSize = centerChunk.UvHalfExtent * 2f;
        Vector2 centerLocalUv = new(
            (faceUv.x - (centerChunk.UvCenter.x - centerChunk.UvHalfExtent)) / chunkSize,
            (faceUv.y - (centerChunk.UvCenter.y - centerChunk.UvHalfExtent)) / chunkSize);
        float localRadius = _shapeGenerator.Settings != null ? _shapeGenerator.Settings.PlanetRadius : 1f;
        if (centerChunk.TrySampleRadius(centerLocalUv, out float sampledRadius) && sampledRadius > 0f)
            localRadius = sampledRadius;

        float worldScale = FaceSpaceCellRangeBuilder.GetUniformWorldScale(_planetTransform);
        metersPerUv = FaceSpaceCellRangeBuilder.ComputeMetersPerUV(
            face, faceUv, Mathf.Max(localRadius * worldScale, 0.001f));
        return true;
    }

    bool TryCreateBakeStamp(SurfacePathEditStamp stamp, long now, out PathWearBakeStamp bake)
    {
        bake = default;
        float strength = EffectiveStampStrength(stamp, now);
        if (strength <= 0f)
            return false;
        if (!TryResolveBrushFootprint(stamp.direction, out int face, out Vector2 faceUv, out float metersPerUv))
            return false;

        bake = new PathWearBakeStamp
        {
            Face = face,
            FaceUv = faceUv,
            RadiusFaceUv = Mathf.Max(0.00001f, Mathf.Max(stamp.radiusMeters, 0.1f) / metersPerUv),
            Alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(strength) * 255f),
            Shape = ParseStampShape(stamp.shape),
            Operation = stamp.operation == "erase" ? SurfacePathOperation.Erase : SurfacePathOperation.Paint,
            StrokeId = stamp.strokeId,
        };
        return bake.Alpha > 0;
    }

    Color32[] GetSurfaceStatePixels(PlanetChunk chunk, bool batched)
    {
        if (batched && _surfaceStateBatch.TryGetValue(chunk, out Color32[] cached))
            return cached;

        Color32[] pixels = chunk.SurfaceStateTexture.GetPixels32();
        if (batched)
            _surfaceStateBatch[chunk] = pixels;
        return pixels;
    }

    byte[] GetStrokeContribution(PlanetChunk chunk, int pixelCount, bool strokeCoverage)
    {
        if (!strokeCoverage)
            return null;
        if (_strokeContribution.TryGetValue(chunk, out byte[] arr) && arr.Length == pixelCount)
            return arr;

        arr = new byte[pixelCount];
        _strokeContribution[chunk] = arr;
        return arr;
    }

    void ApplySurfaceState(PlanetChunk chunk, Color32[] pixels)
    {
        chunk.SurfaceStateTexture.SetPixels32(pixels);
        chunk.SurfaceStateTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        _biomeAtlas.UpdateSurfaceStateAtlasRegion(chunk);
    }

    static byte[] GetPathWearPixels(PlanetChunk chunk)
    {
        int pixelCount = PlanetChunkTextures.PathWearResolution * PlanetChunkTextures.PathWearResolution;
        if (chunk.PathWearPixels == null || chunk.PathWearPixels.Length != pixelCount)
            chunk.PathWearPixels = new byte[pixelCount];
        return chunk.PathWearPixels;
    }

    byte[] GetStrokeBaseline(PlanetChunk chunk, byte[] pixels, bool strokeCoverage)
    {
        if (!strokeCoverage)
            return null;
        if (_strokeBaseline.TryGetValue(chunk, out byte[] arr) && arr.Length == pixels.Length)
            return arr;

        arr = new byte[pixels.Length];
        System.Array.Copy(pixels, arr, pixels.Length);
        _strokeBaseline[chunk] = arr;
        return arr;
    }

    void ApplyPathWear(PlanetChunk chunk)
    {
        if (chunk.PathWearTexture == null || chunk.PathWearPixels == null)
            return;
        chunk.PathWearTexture.SetPixelData(chunk.PathWearPixels, 0);
        chunk.PathWearTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        _biomeAtlas.UpdatePathWearAtlasRegion(chunk);
    }

    int ClearPathWearMasks()
    {
        int cleared = 0;
        for (int i = 0; i < _allChunks.Count; i++)
        {
            PlanetChunk chunk = _allChunks[i];
            if (chunk?.PathWearTexture == null)
                continue;

            byte[] pathWear = GetPathWearPixels(chunk);
            System.Array.Clear(pathWear, 0, pathWear.Length);
            chunk.PathWearTexture.SetPixelData(EmptyPathWearPixels, 0);
            chunk.PathWearTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            if (chunk.IsLeaf)
                _biomeAtlas.UpdatePathWearAtlasRegion(chunk);
            cleared++;
        }
        return cleared;
    }

    int ClearSurfaceStateOnly()
    {
        int cleared = 0;
        for (int i = 0; i < _allChunks.Count; i++)
        {
            PlanetChunk chunk = _allChunks[i];
            Texture2D texture = chunk?.SurfaceStateTexture;
            if (texture == null)
                continue;

            texture.SetPixels32(EmptySurfaceStatePixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            if (chunk.IsLeaf)
                _biomeAtlas.UpdateSurfaceStateAtlasRegion(chunk);
            cleared++;
        }
        return cleared;
    }

    void ApplyPathWearContribution(Dictionary<PlanetChunk, byte[]> contribution, SurfacePathOperation operation)
    {
        if (contribution == null || contribution.Count == 0)
            return;

        foreach (KeyValuePair<PlanetChunk, byte[]> pair in contribution)
        {
            PlanetChunk chunk = pair.Key;
            byte[] values = pair.Value;
            byte[] pixels = GetPathWearPixels(chunk);
            bool changed = false;

            for (int i = 0; i < values.Length; i++)
            {
                byte value = values[i];
                if (value == 0)
                    continue;

                byte next = operation == SurfacePathOperation.Erase
                    ? (byte)Mathf.Max(0, pixels[i] - value)
                    : SaturatingWear(pixels[i], value);
                if (next == pixels[i])
                    continue;

                pixels[i] = next;
                changed = true;
            }

            if (changed)
                ApplyPathWear(chunk);
        }
    }

    public void EndSurfaceStateBatch()
    {
        _surfaceStateBatch.Clear();
        _strokeContribution.Clear();
        _strokeBaseline.Clear();
    }

    // Drops the current stroke's max-coverage tracking without ending the batch, so replay can
    // composite each saved stroke independently while reusing the cached pixel arrays.
    public void ResetStrokeContribution()
    {
        _strokeContribution.Clear();
        _strokeBaseline.Clear();
    }

    public int ClearSurfaceStateMasks()
    {
        EndSurfaceStateBatch();
        int cleared = 0;
        for (int i = 0; i < _allChunks.Count; i++)
        {
            PlanetChunk chunk = _allChunks[i];
            Texture2D texture = chunk?.SurfaceStateTexture;
            if (texture == null && chunk?.PathWearTexture == null)
                continue;

            if (texture != null)
            {
                texture.SetPixels32(EmptySurfaceStatePixels);
                texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            }
            if (chunk.PathWearTexture != null)
            {
                byte[] pathWear = GetPathWearPixels(chunk);
                System.Array.Clear(pathWear, 0, pathWear.Length);
                chunk.PathWearTexture.SetPixelData(EmptyPathWearPixels, 0);
                chunk.PathWearTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            }
            if (chunk.IsLeaf)
            {
                _biomeAtlas.UpdateSurfaceStateAtlasRegion(chunk);
                _biomeAtlas.UpdatePathWearAtlasRegion(chunk);
            }
            cleared++;
        }
        return cleared;
    }

    public bool TryGetFaceSurfaceStateAtlas(int face, out Texture2D surfaceState)
        => _biomeAtlas.TryGetFaceSurfaceStateAtlas(face, out surfaceState);

    public bool TryGetFacePathWearAtlas(int face, out Texture2D pathWear)
        => _biomeAtlas.TryGetFacePathWearAtlas(face, out pathWear);

    public bool TryRaycastVisibleSurface(Ray localRay, float maxDistance,
        out Vector3 localPoint, out Vector3 localNormal, out float localDistance)
    {
        localPoint = default;
        localNormal = default;
        localDistance = 0f;

        if (localRay.direction.sqrMagnitude < 0.0001f)
            return false;

        localRay.direction = localRay.direction.normalized;
        float bestDistance = maxDistance > 0f ? maxDistance : float.PositiveInfinity;
        bool hit = false;
        int[] triangles = ChunkTriangleTemplate.Get(ChunkResolution);

        for (int face = 0; face < 6; face++)
        {
            IReadOnlyList<PlanetChunk> leaves = _selector.GetVisibleLeaves(face);
            if (leaves == null) continue;

            for (int i = 0; i < leaves.Count; i++)
            {
                PlanetChunk chunk = leaves[i];
                if (!_meshCache.IsActuallyVisible(chunk) || chunk.CpuVertices == null)
                    continue;
                if (!chunk.CpuLocalBounds.IntersectRay(localRay, out float boundsDistance))
                    continue;
                if (boundsDistance > bestDistance)
                    continue;

                if (ChunkSurfaceQueries.RaycastChunkTriangles(localRay, chunk, triangles, bestDistance,
                        out float chunkDistance, out Vector3 chunkPoint, out Vector3 chunkNormal)
                    && chunkDistance < bestDistance)
                {
                    bestDistance = chunkDistance;
                    localPoint = chunkPoint;
                    localNormal = chunkNormal;
                    hit = true;
                }
            }
        }

        if (!hit)
            return false;

        localDistance = bestDistance;
        if (localNormal.sqrMagnitude < 0.0001f)
            localNormal = localPoint.sqrMagnitude > 0.0001f ? localPoint.normalized : Vector3.up;
        localNormal.Normalize();
        if (Vector3.Dot(localNormal, localPoint) < 0f)
            localNormal = -localNormal;
        return true;
    }

    static bool ChunkUvRectOverlaps(PlanetChunk chunk, Vector2 minUv, Vector2 maxUv)
    {
        float cMinU = chunk.UvCenter.x - chunk.UvHalfExtent;
        float cMaxU = chunk.UvCenter.x + chunk.UvHalfExtent;
        float cMinV = chunk.UvCenter.y - chunk.UvHalfExtent;
        float cMaxV = chunk.UvCenter.y + chunk.UvHalfExtent;
        return maxUv.x >= cMinU && minUv.x <= cMaxU
            && maxUv.y >= cMinV && minUv.y <= cMaxV;
    }

    void AccumulatePathWearElement(
        Dictionary<PlanetChunk, byte[]> contribution,
        int face,
        Vector2 a,
        Vector2 b,
        float radiusFaceUv,
        byte alpha,
        SurfacePathShape shape,
        bool segment)
    {
        if (alpha == 0 || radiusFaceUv <= 0f)
            return;

        Vector2 minUv = Vector2.Min(a, b) - Vector2.one * radiusFaceUv;
        Vector2 maxUv = Vector2.Max(a, b) + Vector2.one * radiusFaceUv;
        for (int i = 0; i < _allChunks.Count; i++)
        {
            PlanetChunk chunk = _allChunks[i];
            if (chunk == null || !chunk.IsLeaf || chunk.FaceIndex != face || chunk.PathWearTexture == null)
                continue;
            if (!ChunkUvRectOverlaps(chunk, minUv, maxUv))
                continue;

            byte[] values = GetContribution(contribution, chunk);
            AccumulatePathWearElement(chunk, values, a, b, radiusFaceUv, alpha, shape, segment);
        }
    }

    static byte[] GetContribution(Dictionary<PlanetChunk, byte[]> contribution, PlanetChunk chunk)
    {
        int pixelCount = PlanetChunkTextures.PathWearResolution * PlanetChunkTextures.PathWearResolution;
        if (contribution.TryGetValue(chunk, out byte[] values) && values.Length == pixelCount)
            return values;

        values = new byte[pixelCount];
        contribution[chunk] = values;
        return values;
    }

    static void AccumulatePathWearElement(
        PlanetChunk chunk,
        byte[] values,
        Vector2 a,
        Vector2 b,
        float radiusFaceUv,
        byte alpha,
        SurfacePathShape shape,
        bool segment)
    {
        int resolution = PlanetChunkTextures.PathWearResolution;
        float minU = chunk.UvCenter.x - chunk.UvHalfExtent;
        float minV = chunk.UvCenter.y - chunk.UvHalfExtent;
        float chunkSize = Mathf.Max(chunk.UvHalfExtent * 2f, 0.00001f);
        Vector2 localA = new((a.x - minU) / chunkSize, (a.y - minV) / chunkSize);
        Vector2 localB = new((b.x - minU) / chunkSize, (b.y - minV) / chunkSize);
        float radiusLocal = radiusFaceUv / chunkSize;

        float edgeLocal = shape == SurfacePathShape.HardDisc ? HardDiscEdgePixels / resolution : 0f;
        Vector2 localMin = Vector2.Min(localA, localB) - Vector2.one * (radiusLocal + edgeLocal);
        Vector2 localMax = Vector2.Max(localA, localB) + Vector2.one * (radiusLocal + edgeLocal);
        int startX = Mathf.Clamp(Mathf.FloorToInt(localMin.x * resolution), 0, resolution - 1);
        int endX = Mathf.Clamp(Mathf.CeilToInt(localMax.x * resolution), 0, resolution - 1);
        int startY = Mathf.Clamp(Mathf.FloorToInt(localMin.y * resolution), 0, resolution - 1);
        int endY = Mathf.Clamp(Mathf.CeilToInt(localMax.y * resolution), 0, resolution - 1);

        for (int y = startY; y <= endY; y++)
        {
            float py = (y + 0.5f) / resolution;
            for (int x = startX; x <= endX; x++)
            {
                float px = (x + 0.5f) / resolution;
                float distance01 = shape == SurfacePathShape.HardSquare
                    ? SquareDistance01(new Vector2(px, py), localA, radiusLocal)
                    : CapsuleDistance01(new Vector2(px, py), localA, localB, radiusLocal, segment);
                float mask = PathWearMask(shape, distance01, radiusLocal, resolution);
                if (mask <= 0f)
                    continue;

                byte value = (byte)Mathf.RoundToInt(alpha * mask);
                int index = y * resolution + x;
                if (value > values[index])
                    values[index] = value;
            }
        }
    }

    static float CapsuleDistance01(Vector2 p, Vector2 a, Vector2 b, float radius, bool segment)
    {
        Vector2 closest = a;
        if (segment)
        {
            Vector2 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            if (lenSq > 0.0000001f)
                closest = a + ab * Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
        }
        return Vector2.Distance(p, closest) / Mathf.Max(radius, 0.00001f);
    }

    static float SquareDistance01(Vector2 p, Vector2 center, float radius)
    {
        Vector2 d = new(Mathf.Abs(p.x - center.x), Mathf.Abs(p.y - center.y));
        return Mathf.Max(d.x, d.y) / Mathf.Max(radius, 0.00001f);
    }

    static float PathWearMask(SurfacePathShape shape, float distance01, float radiusLocal, int resolution)
    {
        if (shape == SurfacePathShape.SoftDisc)
            return distance01 < 1f ? 1f - Mathf.SmoothStep(0.35f, 1f, distance01) : 0f;
        if (shape == SurfacePathShape.HardDisc)
        {
            float edge01 = HardDiscEdgePixels / Mathf.Max(radiusLocal * resolution, 1f);
            return 1f - SmoothStep01(1f - edge01, 1f + edge01, distance01);
        }

        return distance01 < 1f ? 1f : 0f;
    }

    static int PaintChunkSurfaceStateChannel(
        PlanetChunk chunk,
        Color32[] pixels,
        Vector2 centerFaceUv,
        float radiusFaceUv,
        byte alpha,
        int channel,
        SurfacePathShape shape,
        SurfacePathOperation operation)
    {
        int resolution = PlanetChunkTextures.BiomeMapResolution;
        if (resolution <= 0 || radiusFaceUv <= 0f || alpha == 0)
            return 0;

        float minU = chunk.UvCenter.x - chunk.UvHalfExtent;
        float minV = chunk.UvCenter.y - chunk.UvHalfExtent;
        float size = Mathf.Max(chunk.UvHalfExtent * 2f, 0.00001f);
        Vector2 centerLocal = new(
            (centerFaceUv.x - minU) / size,
            (centerFaceUv.y - minV) / size);
        float radiusLocal = radiusFaceUv / size;
        if (radiusLocal <= 0f)
            return 0;

        float edgeLocal = shape == SurfacePathShape.HardDisc ? HardDiscEdgePixels / resolution : 0f;
        int startX = Mathf.Clamp(Mathf.FloorToInt((centerLocal.x - radiusLocal - edgeLocal) * resolution), 0, resolution - 1);
        int endX = Mathf.Clamp(Mathf.CeilToInt((centerLocal.x + radiusLocal + edgeLocal) * resolution), 0, resolution - 1);
        int startY = Mathf.Clamp(Mathf.FloorToInt((centerLocal.y - radiusLocal - edgeLocal) * resolution), 0, resolution - 1);
        int endY = Mathf.Clamp(Mathf.CeilToInt((centerLocal.y + radiusLocal + edgeLocal) * resolution), 0, resolution - 1);

        int changed = 0;
        for (int y = startY; y <= endY; y++)
        {
            float py = (y + 0.5f) / resolution;
            for (int x = startX; x <= endX; x++)
            {
                float px = (x + 0.5f) / resolution;
                float distance01 = shape == SurfacePathShape.HardSquare
                    ? SquareDistance01(new Vector2(px, py), centerLocal, radiusLocal)
                    : Vector2.Distance(new Vector2(px, py), centerLocal) / radiusLocal;
                float mask = PathWearMask(shape, distance01, radiusLocal, resolution);
                if (mask <= 0f)
                    continue;

                byte value = (byte)Mathf.RoundToInt(alpha * mask);
                if (value == 0)
                    continue;

                int index = y * resolution + x;
                Color32 pixel = pixels[index];
                byte current = GetChannel(pixel, channel);
                byte next = operation == SurfacePathOperation.Erase
                    ? (byte)Mathf.Max(0, current - value)
                    : SaturatingWear(current, value);
                if (next == current)
                    continue;

                SetChannel(ref pixel, channel, next);
                pixels[index] = pixel;
                changed++;
            }
        }

        return changed;
    }

    static byte GetChannel(Color32 pixel, int channel)
    {
        switch (channel)
        {
            case 0: return pixel.r;
            case 1: return pixel.g;
            case 2: return pixel.b;
            default: return pixel.a;
        }
    }

    static void SetChannel(ref Color32 pixel, int channel, byte value)
    {
        switch (channel)
        {
            case 0:
                pixel.r = value;
                break;
            case 1:
                pixel.g = value;
                break;
            case 2:
                pixel.b = value;
                break;
            default:
                pixel.a = value;
                break;
        }
    }

    static int PaintChunkScorchTestPattern(
        PlanetChunk chunk,
        Color32[] pixels,
        Vector2 centerFaceUv,
        float halfSizeFaceUv,
        byte alpha)
    {
        int resolution = PlanetChunkTextures.BiomeMapResolution;
        if (resolution <= 0 || halfSizeFaceUv <= 0f || alpha == 0)
            return 0;

        float minU = chunk.UvCenter.x - chunk.UvHalfExtent;
        float minV = chunk.UvCenter.y - chunk.UvHalfExtent;
        float chunkSize = Mathf.Max(chunk.UvHalfExtent * 2f, 0.00001f);
        Vector2 patternMin = centerFaceUv - Vector2.one * halfSizeFaceUv;
        float patternSize = Mathf.Max(halfSizeFaceUv * 2f, 0.00001f);

        int startX = Mathf.Clamp(Mathf.FloorToInt(((patternMin.x - minU) / chunkSize) * resolution), 0, resolution - 1);
        int endX = Mathf.Clamp(Mathf.CeilToInt(((patternMin.x + patternSize - minU) / chunkSize) * resolution), 0, resolution - 1);
        int startY = Mathf.Clamp(Mathf.FloorToInt(((patternMin.y - minV) / chunkSize) * resolution), 0, resolution - 1);
        int endY = Mathf.Clamp(Mathf.CeilToInt(((patternMin.y + patternSize - minV) / chunkSize) * resolution), 0, resolution - 1);

        int changed = 0;
        for (int y = startY; y <= endY; y++)
        {
            float faceY = minV + ((y + 0.5f) / resolution) * chunkSize;
            for (int x = startX; x <= endX; x++)
            {
                float faceX = minU + ((x + 0.5f) / resolution) * chunkSize;
                Vector2 p = new(
                    (faceX - patternMin.x) / patternSize,
                    (faceY - patternMin.y) / patternSize);
                float mask = EvaluateScorchTestPattern(p);
                if (mask <= 0f)
                    continue;

                byte value = (byte)Mathf.RoundToInt(alpha * mask);
                int index = y * resolution + x;
                Color32 pixel = pixels[index];
                byte next = SaturatingWear(pixel.g, value);
                if (next == pixel.g)
                    continue;

                pixel.g = next;
                pixels[index] = pixel;
                changed++;
            }
        }

        return changed;
    }

    static int PaintChunkPathWear(
        PlanetChunk chunk,
        byte[] pixels,
        byte[] contribution,
        byte[] baseline,
        Vector2 centerFaceUv,
        float radiusFaceUv,
        byte pavedAlpha,
        SurfacePathShape shape,
        SurfacePathOperation operation)
    {
        int resolution = PlanetChunkTextures.PathWearResolution;
        if (resolution <= 0 || radiusFaceUv <= 0f || pavedAlpha == 0)
            return 0;

        float minU = chunk.UvCenter.x - chunk.UvHalfExtent;
        float minV = chunk.UvCenter.y - chunk.UvHalfExtent;
        float size = Mathf.Max(chunk.UvHalfExtent * 2f, 0.00001f);
        Vector2 centerLocal = new(
            (centerFaceUv.x - minU) / size,
            (centerFaceUv.y - minV) / size);
        float radiusLocal = radiusFaceUv / size;
        if (radiusLocal <= 0f)
            return 0;

        float edgeLocal = shape == SurfacePathShape.HardDisc ? HardDiscEdgePixels / resolution : 0f;
        int startX = Mathf.Clamp(Mathf.FloorToInt((centerLocal.x - radiusLocal - edgeLocal) * resolution), 0, resolution - 1);
        int endX = Mathf.Clamp(Mathf.CeilToInt((centerLocal.x + radiusLocal + edgeLocal) * resolution), 0, resolution - 1);
        int startY = Mathf.Clamp(Mathf.FloorToInt((centerLocal.y - radiusLocal - edgeLocal) * resolution), 0, resolution - 1);
        int endY = Mathf.Clamp(Mathf.CeilToInt((centerLocal.y + radiusLocal + edgeLocal) * resolution), 0, resolution - 1);

        int changed = 0;
        for (int y = startY; y <= endY; y++)
        {
            float py = (y + 0.5f) / resolution;
            for (int x = startX; x <= endX; x++)
            {
                float px = (x + 0.5f) / resolution;
                float distance01 = shape == SurfacePathShape.HardSquare
                    ? SquareDistance01(new Vector2(px, py), centerLocal, radiusLocal)
                    : Vector2.Distance(new Vector2(px, py), centerLocal) / radiusLocal;
                float mask = PathWearMask(shape, distance01, radiusLocal, resolution);
                if (mask <= 0f)
                    continue;

                byte value = (byte)Mathf.RoundToInt(pavedAlpha * mask);
                if (value == 0)
                    continue;

                int index = y * resolution + x;

                // Within a stroke, only the incremental increase over what this stroke already
                // laid down here is applied, so overlapping stamps in one drag compose by max
                // (a smooth line) instead of stacking into a hard core. Across strokes (or for
                // one-shot paints, contribution == null) it's plain additive buildup.
                byte applied = value;
                if (contribution != null)
                {
                    byte prev = contribution[index];
                    if (value <= prev)
                        continue;
                    applied = (byte)(value - prev);
                    contribution[index] = value;
                }

                byte current = pixels[index];
                byte baseValue = baseline != null ? baseline[index] : current;
                byte next = operation == SurfacePathOperation.Erase
                    ? (byte)Mathf.Max(0, baseValue - applied)
                    : SaturatingWear(baseValue, applied);
                if (next == current)
                    continue;

                pixels[index] = next;
                changed++;
            }
        }

        return changed;
    }

    static int PaintChunkPathWearTestPattern(
        PlanetChunk chunk,
        byte[] pixels,
        Vector2 centerFaceUv,
        float halfSizeFaceUv,
        byte pavedAlpha)
    {
        int resolution = PlanetChunkTextures.PathWearResolution;
        if (resolution <= 0 || halfSizeFaceUv <= 0f || pavedAlpha == 0)
            return 0;

        float minU = chunk.UvCenter.x - chunk.UvHalfExtent;
        float minV = chunk.UvCenter.y - chunk.UvHalfExtent;
        float chunkSize = Mathf.Max(chunk.UvHalfExtent * 2f, 0.00001f);
        Vector2 patternMin = centerFaceUv - Vector2.one * halfSizeFaceUv;
        float patternSize = Mathf.Max(halfSizeFaceUv * 2f, 0.00001f);

        int startX = Mathf.Clamp(Mathf.FloorToInt(((patternMin.x - minU) / chunkSize) * resolution), 0, resolution - 1);
        int endX = Mathf.Clamp(Mathf.CeilToInt(((patternMin.x + patternSize - minU) / chunkSize) * resolution), 0, resolution - 1);
        int startY = Mathf.Clamp(Mathf.FloorToInt(((patternMin.y - minV) / chunkSize) * resolution), 0, resolution - 1);
        int endY = Mathf.Clamp(Mathf.CeilToInt(((patternMin.y + patternSize - minV) / chunkSize) * resolution), 0, resolution - 1);

        int changed = 0;
        for (int y = startY; y <= endY; y++)
        {
            float faceY = minV + ((y + 0.5f) / resolution) * chunkSize;
            for (int x = startX; x <= endX; x++)
            {
                float faceX = minU + ((x + 0.5f) / resolution) * chunkSize;
                Vector2 p = new(
                    (faceX - patternMin.x) / patternSize,
                    (faceY - patternMin.y) / patternSize);
                float mask = EvaluateSurfaceStateTestPattern(p);
                if (mask <= 0f)
                    continue;

                byte value = (byte)Mathf.RoundToInt(pavedAlpha * mask);
                int index = y * resolution + x;
                byte next = SaturatingWear(pixels[index], value);
                if (next == pixels[index])
                    continue;

                pixels[index] = next;
                changed++;
            }
        }

        return changed;
    }

    static byte SaturatingWear(byte current, byte deposit)
    {
        int remaining = (255 - current) * (255 - deposit);
        return (byte)(255 - ((remaining + 127) / 255));
    }

    static float EffectiveStampStrength(SurfacePathEditStamp stamp, long now)
    {
        float strength = Mathf.Clamp01(stamp.strength);
        if (stamp.regrowSeconds <= 0f)
            return strength;

        float elapsed = Mathf.Max(0f, now - stamp.createdUnixSeconds);
        return strength * (1f - Mathf.Clamp01(elapsed / stamp.regrowSeconds));
    }

    static SurfacePathShape ParseStampShape(string shape)
    {
        switch (shape)
        {
            case "hard-disc":
                return SurfacePathShape.HardDisc;
            case "hard-square":
            case "square":
                return SurfacePathShape.HardSquare;
            default:
                return SurfacePathShape.SoftDisc;
        }
    }

    struct PathWearBakeStamp
    {
        public int Face;
        public Vector2 FaceUv;
        public float RadiusFaceUv;
        public byte Alpha;
        public SurfacePathShape Shape;
        public SurfacePathOperation Operation;
        public int StrokeId;
    }

    static float EvaluateSurfaceStateTestPattern(Vector2 p)
    {
        if (p.x < 0f || p.x > 1f || p.y < 0f || p.y > 1f)
            return 0f;

        float mask = 0f;

        float roadCenter = 0.77f + Mathf.Sin((p.x * 2.35f + 0.08f) * Mathf.PI * 2f) * 0.07f;
        float road = 1f - SmoothStep01(0.035f, 0.075f, Mathf.Abs(p.y - roadCenter));
        float roadGate = SmoothStep01(0.04f, 0.09f, p.x) * (1f - SmoothStep01(0.91f, 0.96f, p.x));
        mask = Mathf.Max(mask, road * roadGate);

        float circleDistance = Vector2.Distance(p, new Vector2(0.22f, 0.27f)) / 0.19f;
        mask = Mathf.Max(mask, 1f - SmoothStep01(0.12f, 1f, circleDistance));

        if (p.x >= 0.43f && p.x <= 0.62f && p.y >= 0.14f && p.y <= 0.38f)
            mask = 1f;

        if (p.x >= 0.69f && p.x <= 0.95f && p.y >= 0.12f && p.y <= 0.40f)
        {
            int cx = Mathf.FloorToInt((p.x - 0.69f) / (0.26f / 6f));
            int cy = Mathf.FloorToInt((p.y - 0.12f) / (0.28f / 6f));
            if (((cx + cy) & 1) == 0)
                mask = 1f;
        }

        if (p.x >= 0.08f && p.x <= 0.92f && p.y >= 0.48f && p.y <= 0.58f)
        {
            float t = Mathf.InverseLerp(0.08f, 0.92f, p.x);
            mask = Mathf.Max(mask, Mathf.SmoothStep(0f, 1f, t));
        }

        return Mathf.Clamp01(mask);
    }

    static float EvaluateScorchTestPattern(Vector2 p)
    {
        if (p.x < 0f || p.x > 1f || p.y < 0f || p.y > 1f)
            return 0f;

        float lightBurn = 0.28f * SoftEllipse(p, new Vector2(0.24f, 0.72f), new Vector2(0.17f, 0.12f), 0.20f);
        float heavyBurn = SoftEllipse(p, new Vector2(0.76f, 0.72f), new Vector2(0.17f, 0.14f), 0.15f);
        float mixedShape = SoftEllipse(p, new Vector2(0.50f, 0.30f), new Vector2(0.34f, 0.20f), 0.25f);
        float breakup = Mathf.PerlinNoise(p.x * 13.7f + 4.1f, p.y * 13.7f + 8.3f);
        float detail = Mathf.PerlinNoise(p.x * 37.0f + 11.0f, p.y * 37.0f + 19.0f);
        float mixedBurn = mixedShape * Mathf.Clamp01(breakup * 0.85f + detail * 0.35f - 0.10f);

        return Mathf.Clamp01(Mathf.Max(lightBurn, Mathf.Max(heavyBurn, mixedBurn)));
    }

    static float SoftEllipse(Vector2 p, Vector2 center, Vector2 radius, float edge)
    {
        Vector2 d = new(
            (p.x - center.x) / Mathf.Max(radius.x, 0.0001f),
            (p.y - center.y) / Mathf.Max(radius.y, 0.0001f));
        return 1f - SmoothStep01(1f - edge, 1f, d.magnitude);
    }

    static float SmoothStep01(float edge0, float edge1, float value)
        => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge0, edge1, value));

    // Phase B step 9: cached biome provider so RebakeBiomeMapsAt (Phase E entry point) can
    // re-run the bake without the caller plumbing the provider through again. Set on each
    // GenerateColorsAsync call.
    IBiomeProvider _cachedBiomeProvider;

    public async Awaitable GenerateColorsAsync(IBiomeProvider biomeProvider, IProgressHandle progress, CancellationToken ct)
    {
        if (biomeProvider == null || _allChunks.Count == 0) return;
        _cachedBiomeProvider = biomeProvider;

        progress?.Report(0f, "Applying biome colors...");

        // Build the Burst-compatible biome lookup snapshot + flat color LUT once for the
        // whole bake pass. ColorGenerator is the only IBiomeProvider that owns a registry +
        // color table; other providers fall through with bakeLookupBuilt=false so the chunk
        // biome textures stay at their step-3 zero-init state.
        BiomeLookupData lookup = default;
        Color[] lutColors = null;
        IBiomeAssignmentField assignmentField = null;
        bool bakeLookupBuilt = false;
        bool releaseChunkBiomeTextures = false;
        if (biomeProvider is ColorGenerator cg && cg.Registry != null && cg.BiomeColors != null)
        {
            lookup = cg.Registry.BuildLookupData(Allocator.Persistent);
            lutColors = cg.BiomeColors;
            assignmentField = cg.BiomeAssignmentField;
            bakeLookupBuilt = true;
        }

        try
        {
            var totalTimer = Stopwatch.StartNew();
            long vertexTicks = 0;
            long mapBakeTicks = 0;
            long retainUploadTicks = 0;
            long atlasTicks = 0;
            int bakedMapChunks = 0;
            const int colorBatchSize = 96;
            int total = _allChunks.Count;
            int mapBakeTarget = bakeLookupBuilt
                ? CountBiomeMapBakeTargets(_allChunks, _usesFaceBiomeAtlases)
                : 0;
            bool vertexColorsRequired = !bakeLookupBuilt || !_usesFaceBiomeAtlases;
            for (int batchStart = 0; batchStart < total; batchStart += colorBatchSize)
            {
                int batchEnd = Mathf.Min(batchStart + colorBatchSize, total);

                await Awaitable.BackgroundThreadAsync();
                ct.ThrowIfCancellationRequested();
                BiomeLookupData lookupCopy = lookup; // captured by closure
                Color[] lutCopy = lutColors;
                IBiomeAssignmentField assignmentCopy = assignmentField;
                bool bakeEnabled = bakeLookupBuilt;
                bool faceAtlasMode = _usesFaceBiomeAtlases;
                bool calculateVertexColors = vertexColorsRequired;
                var vertexTimer = Stopwatch.StartNew();
                System.Threading.Tasks.Parallel.For(batchStart, batchEnd, i =>
                {
                    var chunk = _allChunks[i];
                    if (calculateVertexColors)
                        CalculateChunkColors(chunk, biomeProvider);
                    else
                        CalculateChunkBiomeData(chunk, biomeProvider);
                });
                vertexTimer.Stop();
                vertexTicks += vertexTimer.ElapsedTicks;

                if (bakeEnabled)
                {
                    var mapBakeTimer = Stopwatch.StartNew();
                    System.Threading.Tasks.Parallel.For(batchStart, batchEnd, i =>
                    {
                        var chunk = _allChunks[i];
                        if (!ShouldBakeChunkMap(chunk, faceAtlasMode))
                            return;

                        BiomeAtlasService.BakeChunkMap(chunk, lookupCopy, assignmentCopy, lutCopy);
                        System.Threading.Interlocked.Increment(ref bakedMapChunks);
                    });
                    mapBakeTimer.Stop();
                    mapBakeTicks += mapBakeTimer.ElapsedTicks;
                }
                ct.ThrowIfCancellationRequested();

                await Awaitable.MainThreadAsync();
                var retainUploadTimer = Stopwatch.StartNew();
                for (int i = batchStart; i < batchEnd; i++)
                {
                    var chunk = _allChunks[i];
                    bool retainedColorSource = vertexColorsRequired
                        ? _meshCache.RetainColorSource(chunk)
                        : true;
                    bool retainedBiomeSource = _meshCache.RetainBiomeSource(chunk);
                    if (retainedColorSource && retainedBiomeSource)
                    {
                        chunk.ReleaseCpuDataAfterBake(
                            retainSurfaceSamplingData: chunk.IsLeaf,
                            retainUnitSphereForWaterSampler: _maxChunkDepth == 0);
                    }
                    if (bakeEnabled && !faceAtlasMode)
                        BiomeAtlasService.UploadChunkMap(chunk, releasePendingPixels: !chunk.IsLeaf);
                }
                retainUploadTimer.Stop();
                retainUploadTicks += retainUploadTimer.ElapsedTicks;

                float pct = (float)batchEnd / total;
                progress?.Report(0.75f * pct, $"Applied biome colors {batchEnd}/{total}");
                await Awaitable.NextFrameAsync(ct);
            }

            if (bakeLookupBuilt)
            {
                var atlasTimer = Stopwatch.StartNew();
                await _biomeAtlas.BuildFaceAtlasesAsync(
                    _allChunks,
                    new ProgressRangeHandle(progress, 0.75f, 0.23f),
                    ct);
                if (_usesFaceBiomeAtlases && !_biomeAtlas.HasCompleteAtlases())
                {
                    throw new System.InvalidOperationException(
                        "Required biome face atlases were not built completely.");
                }
                _meshCache.RebindAll();
                if (_faceMaterial != null) _faceMaterial.EnableKeyword(BiomeTextureModeKeyword);
                releaseChunkBiomeTextures = _biomeAtlas.HasCompleteAtlases();
                progress?.Report(1f, "Biome colors ready.");
                atlasTimer.Stop();
                atlasTicks += atlasTimer.ElapsedTicks;
            }

            totalTimer.Stop();
            LoggerProvider.Log(
                LogLevel.Debug,
                "PhaseB",
                $"Biome color timings: total={totalTimer.ElapsedMilliseconds}ms, " +
                $"vertex={TicksToMilliseconds(vertexTicks):F1}ms, " +
                $"mapBake={TicksToMilliseconds(mapBakeTicks):F1}ms, " +
                $"retainUpload={TicksToMilliseconds(retainUploadTicks):F1}ms, " +
                $"atlas={TicksToMilliseconds(atlasTicks):F1}ms, " +
                $"chunks={total}, mapBakeChunks={bakedMapChunks}/{mapBakeTarget}, " +
                $"faceAtlas={_usesFaceBiomeAtlases}");
        }
        finally
        {
            if (bakeLookupBuilt)
            {
                BiomeAtlasService.LogBakeSummary(_allChunks, biomeProvider);
                BiomeAtlasService.ReleasePendingPixels(_allChunks);
                if (releaseChunkBiomeTextures)
                {
                    _biomeAtlas.ReleasePerChunkBiomeTextures(_allChunks);
                    ReleaseRetainedVertexColors();
                }
                lookup.Dispose();
            }
        }
    }

    async Awaitable BuildGrassSurfaceAtlasesAsync(
        IProgressHandle progress,
        CancellationToken ct)
    {
        DisposeGrassSurfaceAtlases();
        _grassSurfaceAtlases = await GrassSurfaceAtlasBuilder.BuildAsync(
            _allChunks,
            _maxChunkDepth,
            progress,
            ct);
    }

    public IReadOnlyList<IFaceMeshSampler> GetFaceMeshSamplers()
        => _rootSamplers ?? (IReadOnlyList<IFaceMeshSampler>)System.Array.Empty<IFaceMeshSampler>();

    // Phase B step 9: Phase E entry point. Walks the chunk tree on the face containing the
    // direction, finds the leaf, re-runs the bake + GPU upload for that single chunk.
    // Returns the count rebaked (0 if pre-init, no registry available, etc.). Phase E callers
    // will typically expand this to a radius â€” for now single-chunk is sufficient plumbing.
    public int RebakeBiomeMapsAt(Vector3 localUnitDirection)
    {
        if (_cachedBiomeProvider is not ColorGenerator cg || cg.Registry == null || cg.BiomeColors == null)
            return 0;
        if (_quadtrees == null || _quadtrees.Length != 6) return 0;
        if (localUnitDirection.sqrMagnitude < 0.0001f) return 0;

        var (face, faceUv) = CoordinateConverter.UnitSphereToCubeFace(localUnitDirection.normalized);
        if (face < 0 || face >= _quadtrees.Length || _quadtrees[face] == null) return 0;

        PlanetChunk leaf = _quadtrees[face].FindLeafContaining(faceUv);
        if (leaf == null) return 0;

        BiomeLookupData lookup = cg.Registry.BuildLookupData(Allocator.Persistent);
        bool updated = false;
        try
        {
            BiomeAtlasService.BakeChunkMap(leaf, lookup, cg.BiomeAssignmentField, cg.BiomeColors);
            updated = _biomeAtlas.UpdateFaceAtlasRegion(leaf);
            if (!updated && leaf.BiomeBlendedColorTexture != null)
            {
                BiomeAtlasService.UploadChunkMap(leaf, releasePendingPixels: true);
                updated = true;
            }
            _meshCache.Rebind(leaf);
        }
        finally
        {
            leaf.PendingBiomeBlendedColorPixels = null;
            leaf.PendingBiomeIdsPixels = null;
            leaf.PendingBiomeWeightsPixels = null;
            lookup.Dispose();
        }
        return updated ? 1 : 0;
    }

    const string BiomeTextureModeKeyword = "_BIOME_COLOR_MODE_TEXTURE";

    public void Dispose()
    {
        // Reverse construction order: selector, mesh cache, generator, biome atlas.
        _selector.Dispose();
        _meshCache.Dispose();
        _generator.Dispose();
        _biomeAtlas.Dispose();
        DisposeGrassSurfaceAtlases();
        if (_faceMaterial != null) _faceMaterial.DisableKeyword(BiomeTextureModeKeyword);

        // Phase B step 3: dispose per-chunk biome + surface-state textures so we don't leak
        // GPU memory across planet regen.
        int chunkTexturesBefore = PlanetChunkTextures.LiveTextureSets;
        int chunkBiomeTexturesBefore = PlanetChunkTextures.LiveBiomeTextureSets;
        int surfaceStateTexturesBefore = PlanetChunkTextures.LiveSurfaceStateTextures;
        int pathWearTexturesBefore = PlanetChunkTextures.LivePathWearTextures;
        int chunkCount = _allChunks.Count;
        for (int i = 0; i < _allChunks.Count; i++)
            PlanetChunkTextures.Dispose(_allChunks[i]);
        _allChunks.Clear();
        MemoryDebugModule.Unregister(this);
        LoggerProvider.Log(LogLevel.Debug, "ChunkLOD",
            $"Dispose: disposed {chunkCount} chunk texture sets. " +
            $"Any {chunkTexturesBefore} -> {PlanetChunkTextures.LiveTextureSets}, " +
            $"biome {chunkBiomeTexturesBefore} -> {PlanetChunkTextures.LiveBiomeTextureSets}, " +
            $"surface-state {surfaceStateTexturesBefore} -> {PlanetChunkTextures.LiveSurfaceStateTextures}, " +
            $"path-wear {pathWearTexturesBefore} -> {PlanetChunkTextures.LivePathWearTextures}");
    }

    // ---- Initial gen helpers ----------------------------------------------------------------

    static void CollectAllChunks(PlanetChunk chunk, List<PlanetChunk> output)
    {
        if (chunk == null) return;
        output.Add(chunk);
        if (chunk.Children != null)
            for (int i = 0; i < chunk.Children.Length; i++)
                CollectAllChunks(chunk.Children[i], output);
    }

    static void CollectChunksAtDepth(PlanetChunk chunk, int targetDepth, List<PlanetChunk> output)
    {
        if (chunk == null) return;
        if (chunk.DetailLevel == targetDepth) { output.Add(chunk); return; }
        if (chunk.DetailLevel > targetDepth) return;
        if (chunk.Children != null)
            for (int i = 0; i < chunk.Children.Length; i++)
                CollectChunksAtDepth(chunk.Children[i], targetDepth, output);
    }

    void EnsureFaceObjects()
    {
        if (_faceRoots != null) return;

        _faceRoots = new Transform[6];
        _quadtrees = new TerrainQuadtree[6];
        _faceVisible = new bool[6];

        for (int f = 0; f < 6; f++)
        {
            var go = new GameObject($"chunk-face-{f}");
            go.transform.parent = _planetTransform;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            _faceRoots[f] = go.transform;
            _quadtrees[f] = new TerrainQuadtree(f);

            _faceVisible[f] = _renderMask == Planet.FaceRenderMask.All || (int)_renderMask - 1 == f;
            go.SetActive(_faceVisible[f]);
        }

        _meshCache.AttachFaceObjects(_faceRoots, _faceVisible);
        _selector.AttachModel(_quadtrees, _faceVisible);
    }

    // Public surface contract (grass placement reads the face atlases). Delegates to the service.
    public bool TryGetFaceBiomeAtlases(int face, out Texture2D blended, out Texture2D ids, out Texture2D weights)
        => _biomeAtlas.TryGetFaceAtlases(face, out blended, out ids, out weights);

    void DisposeGrassSurfaceAtlases()
    {
        _grassSurfaceAtlases?.Dispose();
        _grassSurfaceAtlases = null;
    }

    static void CalculateChunkColors(PlanetChunk chunk, IBiomeProvider biomeProvider)
    {
        if (chunk == null || biomeProvider == null || chunk.CpuUnitSpherePoints == null || chunk.CpuElevations == null)
            return;

        int count = chunk.CpuUnitSpherePoints.Length;
        if (chunk.CpuElevations.Length != count) return;

        var colors = chunk.CpuColors;
        if (colors == null || colors.Length != count) colors = new Color[count];

        var biomeData = chunk.CpuBiomeData;
        if (biomeData == null || biomeData.Length != count) biomeData = new Vector4[count];

        for (int i = 0; i < count; i++)
            colors[i] = biomeProvider.GetBiomeColorAndData(chunk.CpuUnitSpherePoints[i], chunk.CpuElevations[i], out biomeData[i]);

        chunk.CpuColors = colors;
        chunk.CpuBiomeData = biomeData;
    }

    static void CalculateChunkBiomeData(PlanetChunk chunk, IBiomeProvider biomeProvider)
    {
        if (chunk == null || biomeProvider == null || chunk.CpuUnitSpherePoints == null || chunk.CpuElevations == null)
            return;

        int count = chunk.CpuUnitSpherePoints.Length;
        if (chunk.CpuElevations.Length != count) return;

        var biomeData = chunk.CpuBiomeData;
        if (biomeData == null || biomeData.Length != count) biomeData = new Vector4[count];

        for (int i = 0; i < count; i++)
            biomeProvider.GetBiomeData(chunk.CpuUnitSpherePoints[i], chunk.CpuElevations[i], out biomeData[i]);

        chunk.CpuBiomeData = biomeData;
    }

    static bool ShouldBakeChunkMap(PlanetChunk chunk, bool usesFaceBiomeAtlases)
        => chunk != null && (!usesFaceBiomeAtlases || chunk.IsLeaf);

    static int CountBiomeMapBakeTargets(IReadOnlyList<PlanetChunk> chunks, bool usesFaceBiomeAtlases)
    {
        if (chunks == null)
            return 0;

        if (!usesFaceBiomeAtlases)
            return chunks.Count;

        int count = 0;
        for (int i = 0; i < chunks.Count; i++)
        {
            if (chunks[i]?.IsLeaf == true)
                count++;
        }
        return count;
    }

    static double TicksToMilliseconds(long ticks)
        => ticks * 1000.0 / Stopwatch.Frequency;

    void ReleaseRetainedVertexColors()
    {
        for (int i = 0; i < _allChunks.Count; i++)
        {
            PlanetChunk chunk = _allChunks[i];
            if (chunk != null)
                chunk.CpuColors32 = null;
        }
    }

}

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

// Incremental, cell-persistent scatter gather (Valheim ZoneSystem model). Instead of re-scanning the
// whole camera disc every move, the surface is partitioned into fixed cube-face TILES at level Lt. Each
// (tile, prototype) payload is gathered ONCE via ScatterField.GatherTilePrototype (a pure function of the
// tile + seed), cached, and reused; a camera move only gathers newly-entered (tile, prototype) pairs and
// evicts tiles that left range. Per-move cost is the frontier ring, not the disc.
//
// Readiness is tracked per (tile, prototype) (a bitset per tile): a tile that entered range far away holds
// only the far prototypes (trees); as the camera closes, its short-range prototypes (bushes, grass) are
// enqueued and filled in — the payload is never a camera-clipped subset, so it is path-independent.
//
// Threading mirrors the old renderer: one sequential background worker gathers into worker-local lists
// from an immutable context + transform snapshot; the cache (tiles + draw buckets) is only ever mutated
// on the main thread. An epoch guards commits so a result produced for a previous world is dropped.
[CommandPrefix("scatter")]
public sealed class ScatterTileCache
{
    // A (tile, prototype) unit of work / readiness.
    readonly struct WorkKey : IEquatable<WorkKey>
    {
        public readonly long Tile;
        public readonly int Proto;
        public WorkKey(long tile, int proto) { Tile = tile; Proto = proto; }
        public bool Equals(WorkKey o) => Tile == o.Tile && Proto == o.Proto;
        public override bool Equals(object o) => o is WorkKey k && Equals(k);
        public override int GetHashCode() => unchecked((int)(Tile * 397) ^ Proto);
    }

    sealed class TileEntry
    {
        public readonly int Face, Tx, Ty;

        // Which prototypes have been gathered+committed for this tile. A single ulong silently aliased
        // prototype 64+ onto 0+ (C# masks the shift count), which went live as soon as tree variants pushed
        // the library past 64 entries — tiles then read as ready for prototypes never gathered.
        readonly ulong[] _ready;

        public TileEntry(int face, int tx, int ty, int protoCount)
        {
            Face = face; Tx = tx; Ty = ty;
            _ready = new ulong[(protoCount + 63) >> 6];
        }

        public bool IsReady(int p) => (_ready[p >> 6] & (1UL << (p & 63))) != 0;
        public void MarkReady(int p) => _ready[p >> 6] |= 1UL << (p & 63);
    }

    const float ReevalMoveMeters = 40f;    // re-plan the required tile set only after this much camera travel
    const int MaxPairsPerTick = 256;       // serial-fallback: (tile, prototype) gathers per background hop
    const int MaxPairsPerBatch = 1024;     // Burst path: pairs per parallel dispatch (larger amortises schedule/await)
    const int JobInnerBatch = 4;           // IJobParallelFor inner batch size
    const int CommitInstancesPerFrame = 20000; // Burst path: cap main-thread commit per frame; spread the rest
    const long DrainBudgetMs = 200;        // one worker invocation keeps draining batches up to this wall time

    readonly ScatterField _field;
    readonly Transform _planetTransform;
    readonly ILogger _log = LoggerProvider.Get();
    readonly FaceSpaceCell[] _ranges = new FaceSpaceCell[FaceSpaceCellRangeBuilder.MaxRanges];

    readonly Dictionary<long, TileEntry> _tiles = new();
    readonly HashSet<WorkKey> _inFlight = new();                 // pairs the worker is actively gathering
    readonly List<(WorkKey key, float dist)> _work = new();      // queue, rebuilt in full each reeval

    // Per-prototype packed draw buckets keyed by tile: appended on commit, swap-removed per tile on evict
    // (O(the tile's instances), never an O(all live instances) rebuild).
    ScatterDrawBuckets _buckets;

    // Optional harvested-instance filter: instances whose ScatterId is recorded here are dropped at Commit,
    // so a harvested (chopped/collected) instance never re-enters the draw on re-gather or reload.
    ScatterHarvestStore _harvest;

    int _protoCount;
    int _tileLevel;
    float[] _protoRadius = Array.Empty<float>(); // far draw end + prefetch lead; <0 = never gathered
    int[] _sortedProtoIndex = Array.Empty<int>();    // renderable prototypes, descending radius
    float[] _sortedProtoRadius = Array.Empty<float>(); // _protoRadius in that same order
    int _sortedProtoCount;
    float _globalMaxRadius;
    float _tileWorld;                            // one tile's world size; eviction hysteresis
    bool _configured;
    int _epoch;
    bool _working;
    Vector3 _lastReevalPos = FarAway;

    // Burst gather inputs (only when the ground sampler is an IBurstElevationSource). Per-world constants
    // built once at Configure into Persistent memory and reused every batch; disposed on reconfigure/teardown
    // after the outstanding job handle completes so a job never references freed memory.
    NativeArray<NoiseFilterData> _noiseLayers;
    NativeArray<byte> _diagCells;
    DiagnosticTerrainSettingsData _diagData;
    NativeArray<ScatterProtoParams> _protoParams;
    bool _nativeAllocated;
    bool _burstReady;
    float _planetRadius;
    JobHandle _pending;
    bool _hasPending;

    static readonly Vector3 FarAway = new Vector3(1e9f, 1e9f, 1e9f);




    public ScatterTileCache(ScatterField field, Transform planetTransform)
    {
        _field = field;
        _planetTransform = planetTransform;
        ConsoleRegistry.RegisterInstance(this);
    }

    [ConsoleCommand("tiles", "Report the scatter tile cache: live tiles, instances, queue depth, tile level.", MonoTargetType.Registry)]
    string TilesCmd() => _configured
        ? $"scatter tiles: Lt={_tileLevel}, {LiveTileCount} live tiles, {LiveInstanceCount} instances, {_work.Count} queued, {_inFlight.Count} in-flight, far radius {_globalMaxRadius:F0} m"
        : "scatter tiles: not configured (generate a planet first)";

    public int TileLevel => _tileLevel;
    public List<Matrix4x4> Matrices(int proto) => _buckets.Matrices(proto);
    public IReadOnlyList<Vector3> Positions(int proto) => _buckets.Positions(proto);
    public IReadOnlyList<ulong> Ids(int proto) => _buckets.Ids(proto);

    // Remove a single instance this frame (harvested). The bucket drops it and marks the prototype's draw
    // dirty, so the GPU re-uploads without it; persistence (so it stays gone after re-gather/reload) is the
    // caller's job via the harvest store consulted in Commit.
    public bool RemoveInstance(int proto, ulong id) => _buckets != null && _buckets.RemoveInstanceById(proto, id);

    // The harvested-instance filter consulted in Commit. Set once after construction (before the first gather).
    public void SetHarvestStore(ScatterHarvestStore store) => _harvest = store;
    // Whether this prototype's matrix list changed since the last call (for the GPU draw's dirty upload).
    public bool ConsumeDrawDirty(int proto) => _buckets.ConsumeDirty(proto);

    // Live diagnostics for the scatter.* counters (I2/I4): resident state + outstanding work.
    public int LiveTileCount => _tiles.Count;
    public int PendingPairCount => _inFlight.Count + _work.Count;
    public int LiveInstanceCount => _buckets?.InstanceCount ?? 0;

    public void Configure()
    {
        _epoch++;
        DisposeNativeBuffers();
        if (!_field.TryCaptureGatherContext(out ScatterField.GatherContext ctx) || !ctx.IsValid)
        {
            _configured = false;
            return;
        }
        _protoCount = ctx.Library.Prototypes.Length;
        _buckets = new ScatterDrawBuckets(_protoCount);

        // Lt <= every prototype level so each prototype cell has exactly one parent tile (I4). 7 bits per
        // axis packs the tile id, so Lt is capped at 7 (128 tiles/face axis, ~82 m tiles on this planet).
        int minLevel = int.MaxValue;
        for (int p = 0; p < _protoCount; p++) minLevel = Mathf.Min(minLevel, ctx.Levels[p]);
        _tileLevel = Mathf.Clamp(Mathf.Min(7, minLevel), 0, 7);

        _protoRadius = new float[_protoCount];
        _globalMaxRadius = 0f;
        float worldScale = FaceSpaceCellRangeBuilder.GetUniformWorldScale(_planetTransform);
        _tileWorld = 2f * ctx.BaseRadiusLocal * worldScale * ScatterQuadtree.CellUvWidth(_tileLevel);
        float prefetch = Mathf.Max(_tileWorld, 40f);
        for (int p = 0; p < _protoCount; p++)
        {
            var proto = ctx.Library.Prototypes[p];
            // Only renderable prototypes are ever gathered — placement-only prototypes draw nothing, so
            // the cache never spends work on them (this also drops them from the frontier entirely).
            _protoRadius[p] = proto.CanRender ? proto.FarGatherRadius + prefetch : -1f;
            if (_protoRadius[p] > _globalMaxRadius) _globalMaxRadius = _protoRadius[p];
        }

        // Renderable prototypes ordered by descending radius. Reeval walks tiles nearest-first and retreats a
        // cursor through this, so the ordering is what lets it skip prototypes that cannot reach a tile.
        _sortedProtoIndex = new int[_protoCount];
        _sortedProtoRadius = new float[_protoCount];
        _sortedProtoCount = 0;
        for (int p = 0; p < _protoCount; p++)
            if (_protoRadius[p] > 0f) _sortedProtoIndex[_sortedProtoCount++] = p;
        System.Array.Sort(_sortedProtoIndex, 0, _sortedProtoCount,
            Comparer<int>.Create((a, b) => _protoRadius[b].CompareTo(_protoRadius[a])));
        for (int k = 0; k < _sortedProtoCount; k++) _sortedProtoRadius[k] = _protoRadius[_sortedProtoIndex[k]];

        _tiles.Clear();
        _inFlight.Clear();
        _work.Clear();
        _lastReevalPos = FarAway;

        // Parallel Burst gather requires an analytic-noise ground sampler. If the active sampler can't
        // provide blittable elevation data, _burstReady stays false and the worker uses the serial path.
        _burstReady = false;
        if (_field.Ground is IBurstElevationSource src)
        {
            _noiseLayers = src.BuildNoiseFilterData(Allocator.Persistent);
            _diagCells = src.BuildDiagnosticCells(Allocator.Persistent);
            _diagData = src.DiagnosticData;
            _planetRadius = src.PlanetRadius;
            _protoParams = new NativeArray<ScatterProtoParams>(_protoCount, Allocator.Persistent);
            for (int p = 0; p < _protoCount; p++) _protoParams[p] = ScatterProtoParams.From(ctx.Library.Prototypes[p]);
            _nativeAllocated = true;
            _burstReady = true;
        }

        _configured = true;
    }

    public void Reset()
    {
        _epoch++;
        DisposeNativeBuffers();
        _configured = false;
        _lastReevalPos = FarAway;
    }

    // Completes any in-flight job (its inputs reference these buffers) before freeing the per-world native
    // buffers. Safe to call when nothing is allocated.
    void DisposeNativeBuffers()
    {
        if (_hasPending) { _pending.Complete(); _hasPending = false; }
        if (!_nativeAllocated) return;
        if (_noiseLayers.IsCreated) _noiseLayers.Dispose();
        if (_diagCells.IsCreated) _diagCells.Dispose();
        if (_protoParams.IsCreated) _protoParams.Dispose();
        _nativeAllocated = false;
        _burstReady = false;
    }

    // Called every frame from ScatterRenderer.Render. Re-plans the required tile set only after enough
    // travel, then launches one background batch if any work is outstanding.
    public void Update(Vector3 cameraPos)
    {
        if (!_configured) return;

        // The re-plan costs ~30 ms at 176 prototypes and 16.5k tiles, spread evenly across five stages with
        // no single hotspot left to optimise. It only fires every ReevalMoveMeters — about once a second at
        // flying speed — so there are ~60 idle frames to spread it over. Running one stage per frame turns a
        // 30 ms stall into a worst case of the largest single stage.
        if (_replanStage == ReplanStage.Idle
            && (cameraPos - _lastReevalPos).sqrMagnitude > ReevalMoveMeters * ReevalMoveMeters)
        {
            if (BeginReplan(cameraPos)) { _lastReevalPos = cameraPos; }
        }
        else if (_replanStage != ReplanStage.Idle)
        {
            StepReplan();
        }

        if (!_working && _work.Count > 0) _ = RunWorkerAsync();
    }

    enum ReplanStage { Idle, Evict, Candidates, SortTiles, Filter, Publish }

    ReplanStage _replanStage = ReplanStage.Idle;
    ScatterField.GatherContext _replanCtx;
    PlanetTransformSnapshot _replanSnap;
    Vector3 _replanAnchor;
    Vector3 _replanCameraPos;
    int _replanEpoch;
    // The new plan is built here and swapped in at Publish, so the worker keeps draining the previous plan
    // instead of seeing a half-built one.
    readonly List<(WorkKey key, float dist)> _workNext = new();

    bool BeginReplan(Vector3 cameraPos)
    {
        if (!_field.TryCaptureGatherContext(out ScatterField.GatherContext ctx) || !ctx.IsValid) return false;
        var snap = PlanetTransformSnapshot.Capture(_planetTransform);
        if (!TryAnchor(snap, cameraPos, ctx.BaseRadiusLocal, out Vector3 anchorWS)) return false;

        _replanCtx = ctx;
        _replanSnap = snap;
        _replanAnchor = anchorWS;
        _replanCameraPos = cameraPos;
        _replanEpoch = _epoch;
        _replanStage = ReplanStage.Evict;
        StepReplan();   // do the first stage immediately so nothing waits a frame to start
        return true;
    }

    void StepReplan()
    {
        // A world change invalidates everything the in-flight plan captured.
        if (_replanEpoch != _epoch || !_configured) { _replanStage = ReplanStage.Idle; return; }

        switch (_replanStage)
        {
            case ReplanStage.Evict:      ReplanEvict();      _replanStage = ReplanStage.Candidates; break;
            case ReplanStage.Candidates: ReplanCandidates(); _replanStage = ReplanStage.SortTiles;  break;
            case ReplanStage.SortTiles:  ReplanSortTiles();  _replanStage = ReplanStage.Filter;     break;
            case ReplanStage.Filter:     ReplanFilter();     _replanStage = ReplanStage.Publish;    break;
            case ReplanStage.Publish:    ReplanPublish();    _replanStage = ReplanStage.Idle;       break;
        }
    }

    void ReplanEvict()
    {
        ScatterField.GatherContext ctx = _replanCtx;
        PlanetTransformSnapshot snap = _replanSnap;
        Vector3 anchorWS = _replanAnchor;

        // Evict tiles that left range (distance-based, with hysteresis). Each departed tile's instances are
        // swap-removed from the buckets in O(its own instances) — never an O(all live instances) rebuild.
        float evictBeyond = _globalMaxRadius + _tileWorld; // keep one tile of hysteresis past the farthest draw
        _scratchEvict.Clear();
        foreach (var kv in _tiles)
            if (TileDistance(kv.Value, snap, ctx.BaseRadiusLocal, anchorWS) > evictBeyond)
                _scratchEvict.Add(kv.Key);
        for (int i = 0; i < _scratchEvict.Count; i++)
        {
            _buckets.RemoveTile(_scratchEvict[i]);
            _tiles.Remove(_scratchEvict[i]);
        }
    }

    void ReplanCandidates()
    {
        ScatterField.GatherContext ctx = _replanCtx;
        PlanetTransformSnapshot snap = _replanSnap;
        Vector3 anchorWS = _replanAnchor;
        Vector3 cameraPos = _replanCameraPos;

        // Plan the required (tile, prototype) set: for each renderable prototype, the tiles at Lt within
        // its (draw end + prefetch) radius that are not already ready or pending.
        //
        // Tile geometry does not depend on the prototype, so the range build and the per-tile distance are
        // computed ONCE at the global max radius and then filtered per prototype. Doing it inside the
        // prototype loop repeated the same distance math _protoCount times — at 109 prototypes that was the
        // whole cost of the re-plan, and it is what made this stall grow when tree variants raised the count.
        _workNext.Clear();
        float cellUv = ScatterQuadtree.CellUvWidth(_tileLevel);

        _scratchTiles.Clear();
        var maxRange = FaceSpaceCellRangeBuilder.BuildRangesLocal(
            cameraPos, snap, ctx.BaseRadiusLocal, _globalMaxRadius, cellUv, 1, _ranges);
        int tilesPerAxis = 1 << _tileLevel;
        for (int rk = 0; rk < maxRange.Count; rk++)
        {
            FaceSpaceCell cell = _ranges[rk];
            for (int dy = 0; dy < cell.GridSize.y; dy++)
            for (int dx = 0; dx < cell.GridSize.x; dx++)
            {
                int tx = cell.PageOriginCellUV.x + dx, ty = cell.PageOriginCellUV.y + dy;
                if ((uint)tx >= (uint)tilesPerAxis || (uint)ty >= (uint)tilesPerAxis) continue;
                float dist = TileCenterDistance(cell.FaceIndex, tx, ty, snap, ctx.BaseRadiusLocal, anchorWS);
                if (dist > _globalMaxRadius) continue; // clip the conservative square range to the widest disc
                long tileId = PackTile(cell.FaceIndex, tx, ty);
                // Resolve the entry once per tile rather than once per (tile, prototype).
                _tiles.TryGetValue(tileId, out TileEntry entry);
                _scratchTiles.Add((tileId, dist, entry));
            }
        }

        // Tile-major, both sides sorted: tiles by ascending distance, prototypes by descending radius. A
        // prototype-major walk re-read the whole candidate list once per prototype, which at 109 prototypes
        // and ~17k tiles moved tens of megabytes per re-plan. Walking tiles once keeps each tile's entry in
        // cache while every prototype that reaches it is tested.
        //
        // Because both are sorted, the set of prototypes reaching the current tile only ever shrinks, so a
        // single retreating cursor replaces any per-prototype search.
    }

    void ReplanSortTiles()
    {
        _scratchTiles.Sort(static (a, b) => a.dist.CompareTo(b.dist));
    }

    void ReplanFilter()
    {
        int protoLimit = _sortedProtoCount;
        for (int i = 0; i < _scratchTiles.Count; i++)
        {
            (long tileId, float dist, TileEntry entry) = _scratchTiles[i];
            while (protoLimit > 0 && _sortedProtoRadius[protoLimit - 1] < dist) protoLimit--;
            if (protoLimit == 0) break; // nothing reaches this far, and every later tile is further still
            for (int k = 0; k < protoLimit; k++)
            {
                int p = _sortedProtoIndex[k];
                if (entry != null && entry.IsReady(p)) continue;
                var key = new WorkKey(tileId, p);
                if (_inFlight.Contains(key)) continue; // the worker is already gathering this pair
                _workNext.Add((key, dist));
            }
        }
    }

    void ReplanPublish()
    {
        // Nearest first: fill the visible frontier before prefetch tiles.
        _workNext.Sort(static (a, b) => a.dist.CompareTo(b.dist));
        _work.Clear();
        _work.AddRange(_workNext);
        _workNext.Clear();
    }

    readonly List<long> _scratchEvict = new();
    readonly List<(long tile, float dist, TileEntry entry)> _scratchTiles = new();
    readonly List<WorkKey> _batch = new();
    readonly List<List<ScatterInstance>> _batchResults = new();

    async Awaitable RunWorkerAsync()
    {
        _working = true;
        int epoch = _epoch;
        try
        {
            if (!_field.TryCaptureGatherContext(out ScatterField.GatherContext ctx) || !ctx.IsValid) return;
            var snap = PlanetTransformSnapshot.Capture(_planetTransform);
            int tileLevel = _tileLevel;
            var wall = System.Diagnostics.Stopwatch.StartNew();
            int totalPairs = 0;

            // Keep draining batches within one invocation instead of one batch per frame: the gather runs
            // off the main thread (parallel Burst when available, else a serial background pass), so a cold
            // cache of thousands of pairs fills in fast regardless of editor frame rate, killing the load
            // pop-in. Bounded by wall time so a moving camera's reeval/eviction — which runs from Update each
            // frame regardless of _working — isn't starved for long.
            do
            {
                _batch.Clear();
                int maxTake = _burstReady ? MaxPairsPerBatch : MaxPairsPerTick;
                int take = Mathf.Min(maxTake, _work.Count);
                for (int i = 0; i < take; i++) { _batch.Add(_work[i].key); _inFlight.Add(_work[i].key); }
                _work.RemoveRange(0, take);
                if (_batch.Count == 0) break;

                while (_batchResults.Count < _batch.Count) _batchResults.Add(new List<ScatterInstance>(256));
                for (int i = 0; i < _batch.Count; i++) _batchResults[i].Clear();

                if (_burstReady)
                {
                    if (!await GatherBatchBurst(ctx, snap, tileLevel, epoch)) return; // stale world; finally releases in-flight
                }
                else
                {
                    await Awaitable.BackgroundThreadAsync();
                    for (int i = 0; i < _batch.Count; i++)
                    {
                        UnpackTile(_batch[i].Tile, out int face, out int tx, out int ty);
                        _field.GatherTilePrototype(ctx, snap, face, tx, ty, tileLevel, _batch[i].Proto, _batchResults[i], out _);
                    }
                    await Awaitable.MainThreadAsync();
                    if (epoch != _epoch || !_configured) return; // world changed under us; finally releases in-flight
                    for (int i = 0; i < _batch.Count; i++) Commit(_batch[i], _batchResults[i]);
                }

                for (int i = 0; i < _batch.Count; i++) _inFlight.Remove(_batch[i]);
                totalPairs += _batch.Count;
                _batch.Clear();
            }
            while (_work.Count > 0 && wall.ElapsedMilliseconds < DrainBudgetMs);

            if (totalPairs > 0)
                _log.Log(LogLevel.Debug, "Scatter",
                    $"tiles +{totalPairs} pairs {wall.ElapsedMilliseconds} ms | live {LiveTileCount} tiles {LiveInstanceCount} inst, {_work.Count} queued {_inFlight.Count} inflight");
        }
        catch (OperationCanceledException) { /* teardown mid-await */ }
        catch (Exception e)
        {
            _log.Log(LogLevel.Warning, "Scatter", $"tile gather failed: {e}");
        }
        finally
        {
            // Release any batch still in-flight (early return / exception) so those pairs are retried on the
            // next reeval; committed batches already cleared themselves out of _inFlight and _batch above.
            for (int i = 0; i < _batch.Count; i++) _inFlight.Remove(_batch[i]);
            _working = false;
        }
    }

    // One parallel Burst batch: build native pair inputs, precompute biome (managed, off main), schedule
    // the IJobParallelFor, await completion by yielding frames (no Task.Run, main stays responsive), then
    // read the per-pair NativeStream and Commit on the main thread. Returns false if the world changed
    // under the batch (caller returns; finally releases in-flight). Per-world buffers (_noiseLayers etc.)
    // are read [ReadOnly] and outlive the job because DisposeNativeBuffers completes _pending first.
    async Awaitable<bool> GatherBatchBurst(ScatterField.GatherContext ctx, PlanetTransformSnapshot snap,
        int tileLevel, int epoch)
    {
        int take = _batch.Count;
        var pairs = default(NativeArray<ScatterPairInput>);
        var biomeMap = default(NativeParallelHashMap<long, ScatterBiomeSample>);
        var stream = default(NativeStream);
        try
        {
            pairs = new NativeArray<ScatterPairInput>(take, Allocator.Persistent);
            for (int i = 0; i < take; i++)
            {
                UnpackTile(_batch[i].Tile, out int face, out int tx, out int ty);
                pairs[i] = new ScatterPairInput
                {
                    Face = face, TileX = tx, TileY = ty,
                    Level = ctx.Levels[_batch[i].Proto], ProtoIndex = _batch[i].Proto,
                };
            }

            int cellCap = Mathf.Max(1, ScatterBiomePrecompute.CountCells(pairs, take, tileLevel));
            biomeMap = new NativeParallelHashMap<long, ScatterBiomeSample>(cellCap, Allocator.Persistent);

            // Managed biome (Voronoi + climate) can't run in Burst — evaluate the coarse cells this batch
            // needs off the main thread, exactly the memo the serial gather already pays.
            await Awaitable.BackgroundThreadAsync();
            ScatterBiomePrecompute.Build(pairs, take, tileLevel, _field.Ground, _field.Biome, ctx.BaseRadiusLocal, biomeMap);
            await Awaitable.MainThreadAsync();
            if (epoch != _epoch || !_configured) return false;

            stream = new NativeStream(take, Allocator.Persistent);
            var job = new ScatterGatherJob
            {
                Pairs = pairs,
                NoiseLayers = _noiseLayers,
                DiagCells = _diagCells,
                DiagData = _diagData,
                Protos = _protoParams,
                Biome = biomeMap,
                Snap = snap,
                WorldSeed = ctx.WorldSeed,
                TileLevel = tileLevel,
                BaseRadiusLocal = ctx.BaseRadiusLocal,
                SeaRadiusLocal = ctx.SeaRadiusLocal,
                PlanetRadius = _planetRadius,
                Scale = snap.UniformScale,
                HasOcean = ctx.HasOcean ? (byte)1 : (byte)0,
                Out = stream.AsWriter(),
            };
            // Complete on the main thread (Unity forbids completing a job from a worker thread). Doing it
            // inline instead of yielding a full frame per batch drains at job speed (~7x faster), which keeps
            // the stream from being outrun; the short job wait is the main-thread cost, and the commit below
            // spreads across frames.
            _pending = job.Schedule(take, JobInnerBatch);
            _hasPending = true;
            JobHandle.ScheduleBatchedJobs();
            _pending.Complete();
            _hasPending = false;

            if (epoch != _epoch || !_configured) return false; // Configure/Reset ran while completing

            // Committing baked matrices for a whole batch (up to ~100k instances in dense biomes) in one
            // frame is the remaining spike. Spread it: yield a frame each time the committed instance count
            // crosses the cap, so the main thread never commits more than ~CommitInstancesPerFrame at once.
            var reader = stream.AsReader();
            int sinceYield = 0;
            for (int i = 0; i < take; i++)
            {
                var list = _batchResults[i];
                list.Clear();
                int nItems = reader.BeginForEachIndex(i);
                for (int k = 0; k < nItems; k++) list.Add(reader.Read<ScatterInstance>());
                reader.EndForEachIndex();
                Commit(_batch[i], list);
                sinceYield += list.Count;
                if (sinceYield >= CommitInstancesPerFrame && i + 1 < take)
                {
                    sinceYield = 0;
                    await Awaitable.NextFrameAsync();
                    if (epoch != _epoch || !_configured) return false;
                }
            }
            return true;
        }
        finally
        {
            if (_hasPending) { _pending.Complete(); _hasPending = false; }
            if (pairs.IsCreated) pairs.Dispose();
            if (biomeMap.IsCreated) biomeMap.Dispose();
            if (stream.IsCreated) stream.Dispose();
        }
    }

    void Commit(WorkKey key, List<ScatterInstance> instances)
    {
        UnpackTile(key.Tile, out int face, out int tx, out int ty);
        if (!_tiles.TryGetValue(key.Tile, out var entry))
        {
            entry = new TileEntry(face, tx, ty, _protoCount);
            _tiles[key.Tile] = entry;
        }
        int p = key.Proto;
        entry.MarkReady(p);
        // `instances` is the worker's scratch list, consumed synchronously here; the buckets keep the
        // baked matrix + world position per instance, keyed by tile so eviction can remove just this slice.
        for (int i = 0; i < instances.Count; i++)
        {
            var inst = instances[i];
            if (_harvest != null && _harvest.Contains(inst.Id)) continue; // harvested: keep it out of the draw
            _buckets.Add(key.Tile, p, Matrix4x4.TRS(inst.PositionWS, inst.Rotation, Vector3.one * inst.Scale), inst.PositionWS, inst.Id);
        }
    }

    public void Dispose()
    {
        _epoch++;
        DisposeNativeBuffers();
        _configured = false;
        _tiles.Clear();
        _inFlight.Clear();
        _work.Clear();
        _buckets?.Clear();
    }

    // --- tile geometry / id packing (Lt <= 7 -> 7 bits per axis) ---

    static long PackTile(int face, int tx, int ty) => ((long)face << 14) | ((long)tx << 7) | (uint)ty;
    static void UnpackTile(long id, out int face, out int tx, out int ty)
    {
        face = (int)(id >> 14) & 0x7;
        tx = (int)(id >> 7) & 0x7F;
        ty = (int)id & 0x7F;
    }

    bool TryAnchor(in PlanetTransformSnapshot snap, Vector3 cameraPos, float baseRadiusLocal, out Vector3 anchorWS)
    {
        anchorWS = default;
        Vector3 toCam = cameraPos - snap.Center;
        if (toCam.sqrMagnitude < 1e-6f) return false;
        Vector3 localDir = snap.InverseTransformDirection(toCam.normalized).normalized;
        anchorWS = snap.TransformPoint(localDir * baseRadiusLocal);
        return true;
    }

    float TileCenterDistance(int face, int tx, int ty, in PlanetTransformSnapshot snap, float baseRadiusLocal, Vector3 anchorWS)
    {
        int n = 1 << _tileLevel;
        Vector2 uv = new Vector2((tx + 0.5f) / n, (ty + 0.5f) / n);
        Vector3 dir = FaceSpaceCellRangeBuilder.CubeFaceToUnitSphere(face, uv);
        Vector3 centerWS = snap.TransformPoint(dir * baseRadiusLocal);
        return Vector3.Distance(centerWS, anchorWS);
    }

    float TileDistance(TileEntry e, in PlanetTransformSnapshot snap, float baseRadiusLocal, Vector3 anchorWS)
        => TileCenterDistance(e.Face, e.Tx, e.Ty, snap, baseRadiusLocal, anchorWS);
}

using System;
using System.Collections.Generic;
using UnityEngine;

// Incremental, cell-persistent scatter gather (Valheim ZoneSystem model). Instead of re-scanning the
// whole camera disc every move, the surface is partitioned into fixed cube-face TILES at level Lt. Each
// (tile, prototype) payload is gathered ONCE via ScatterField.GatherTilePrototype (a pure function of the
// tile + seed), cached, and reused; a camera move only gathers newly-entered (tile, prototype) pairs and
// evicts tiles that left range. Per-move cost is the frontier ring, not the disc.
//
// Readiness is tracked per (tile, prototype) (ulong ReadyMask): a tile that entered range far away holds
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
        public readonly List<ScatterInstance>[] ByProto; // null until that prototype is gathered
        public ulong ReadyMask;
        public TileEntry(int face, int tx, int ty, int protoCount)
        {
            Face = face; Tx = tx; Ty = ty;
            ByProto = new List<ScatterInstance>[protoCount];
        }
    }

    const float ReevalMoveMeters = 40f;    // re-plan the required tile set only after this much camera travel
    const int MaxPairsPerTick = 48;        // (tile, prototype) gathers committed per background excursion

    readonly ScatterField _field;
    readonly Transform _planetTransform;
    readonly ILogger _log = LoggerProvider.Get();
    readonly FaceSpaceCell[] _ranges = new FaceSpaceCell[FaceSpaceCellRangeBuilder.MaxRanges];

    readonly Dictionary<long, TileEntry> _tiles = new();
    readonly HashSet<WorkKey> _inFlight = new();                 // pairs the worker is actively gathering
    readonly List<(WorkKey key, float dist)> _work = new();      // queue, rebuilt in full each reeval

    // Draw buckets, derived from live tiles: appended on commit, rebuilt (affected prototypes only) on evict.
    List<Matrix4x4>[] _matrices = Array.Empty<List<Matrix4x4>>();
    List<Vector3>[] _positions = Array.Empty<List<Vector3>>();

    int _protoCount;
    int _tileLevel;
    float[] _protoRadius = Array.Empty<float>(); // far draw end + prefetch lead; <0 = never gathered
    float _globalMaxRadius;
    float _tileWorld;                            // one tile's world size; eviction hysteresis
    bool _configured;
    int _epoch;
    bool _working;
    Vector3 _lastReevalPos = FarAway;

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
    public IReadOnlyList<Matrix4x4> Matrices(int proto) => _matrices[proto];
    public IReadOnlyList<Vector3> Positions(int proto) => _positions[proto];

    // Live diagnostics for the scatter.* counters (I2/I4): resident state + outstanding work.
    public int LiveTileCount => _tiles.Count;
    public int PendingPairCount => _inFlight.Count + _work.Count;
    public int LiveInstanceCount { get { int n = 0; for (int p = 0; p < _matrices.Length; p++) n += _matrices[p].Count; return n; } }

    public void Configure()
    {
        _epoch++;
        if (!_field.TryCaptureGatherContext(out ScatterField.GatherContext ctx) || !ctx.IsValid)
        {
            _configured = false;
            return;
        }
        _protoCount = ctx.Library.Prototypes.Length;
        _matrices = new List<Matrix4x4>[_protoCount];
        _positions = new List<Vector3>[_protoCount];

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
            _matrices[p] = new List<Matrix4x4>();
            _positions[p] = new List<Vector3>();
            var proto = ctx.Library.Prototypes[p];
            // Only renderable prototypes are ever gathered — placement-only prototypes draw nothing, so
            // the cache never spends work on them (this also drops them from the frontier entirely).
            _protoRadius[p] = proto.CanRender ? proto.FarGatherRadius + prefetch : -1f;
            if (_protoRadius[p] > _globalMaxRadius) _globalMaxRadius = _protoRadius[p];
        }

        _tiles.Clear();
        _inFlight.Clear();
        _work.Clear();
        _lastReevalPos = FarAway;
        _configured = true;
    }

    public void Reset()
    {
        _epoch++;
        _configured = false;
        _lastReevalPos = FarAway;
    }

    // Called every frame from ScatterRenderer.Render. Re-plans the required tile set only after enough
    // travel, then launches one background batch if any work is outstanding.
    public void Update(Vector3 cameraPos)
    {
        if (!_configured) return;
        if ((cameraPos - _lastReevalPos).sqrMagnitude > ReevalMoveMeters * ReevalMoveMeters)
        {
            Reeval(cameraPos);
            _lastReevalPos = cameraPos;
        }
        if (!_working && _work.Count > 0) _ = RunWorkerAsync();
    }

    void Reeval(Vector3 cameraPos)
    {
        if (!_field.TryCaptureGatherContext(out ScatterField.GatherContext ctx) || !ctx.IsValid) return;
        var snap = PlanetTransformSnapshot.Capture(_planetTransform);
        if (!TryAnchor(snap, cameraPos, ctx.BaseRadiusLocal, out Vector3 anchorWS)) return;

        // Evict tiles that left range (distance-based, with hysteresis). Batch the affected prototypes and
        // rebuild only those draw buckets once — never a full O(all instances) rebuild (I5).
        float evictBeyond = _globalMaxRadius + _tileWorld; // keep one tile of hysteresis past the farthest draw
        ulong affected = 0;
        _scratchEvict.Clear();
        foreach (var kv in _tiles)
        {
            if (TileDistance(kv.Value, snap, ctx.BaseRadiusLocal, anchorWS) > evictBeyond)
            {
                _scratchEvict.Add(kv.Key);
                affected |= kv.Value.ReadyMask;
            }
        }
        for (int i = 0; i < _scratchEvict.Count; i++) _tiles.Remove(_scratchEvict[i]);
        if (affected != 0) RebuildBuckets(affected);

        // Plan the required (tile, prototype) set: for each renderable prototype, the tiles at Lt within
        // its (draw end + prefetch) radius that are not already ready or pending.
        _work.Clear();
        float cellUv = ScatterQuadtree.CellUvWidth(_tileLevel);
        for (int p = 0; p < _protoCount; p++)
        {
            float radius = _protoRadius[p];
            if (radius <= 0f) continue;
            float r2 = radius * radius;
            var result = FaceSpaceCellRangeBuilder.BuildRangesLocal(cameraPos, snap, ctx.BaseRadiusLocal, radius, cellUv, 1, _ranges);
            for (int rk = 0; rk < result.Count; rk++)
            {
                FaceSpaceCell cell = _ranges[rk];
                int n = 1 << _tileLevel;
                for (int dy = 0; dy < cell.GridSize.y; dy++)
                for (int dx = 0; dx < cell.GridSize.x; dx++)
                {
                    int tx = cell.PageOriginCellUV.x + dx, ty = cell.PageOriginCellUV.y + dy;
                    if ((uint)tx >= (uint)n || (uint)ty >= (uint)n) continue;
                    float dist = TileCenterDistance(cell.FaceIndex, tx, ty, snap, ctx.BaseRadiusLocal, anchorWS);
                    if (dist * dist > r2) continue; // clip the conservative square range to the prototype disc
                    long tileId = PackTile(cell.FaceIndex, tx, ty);
                    if (_tiles.TryGetValue(tileId, out var e) && (e.ReadyMask & (1UL << p)) != 0) continue;
                    var key = new WorkKey(tileId, p);
                    if (_inFlight.Contains(key)) continue; // the worker is already gathering this pair
                    _work.Add((key, dist));
                }
            }
        }
        // Nearest first: fill the visible frontier before prefetch tiles.
        _work.Sort(static (a, b) => a.dist.CompareTo(b.dist));
    }

    readonly List<long> _scratchEvict = new();
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

            _batch.Clear();
            int take = Mathf.Min(MaxPairsPerTick, _work.Count);
            for (int i = 0; i < take; i++) { _batch.Add(_work[i].key); _inFlight.Add(_work[i].key); }
            _work.RemoveRange(0, take);
            if (_batch.Count == 0) return;

            while (_batchResults.Count < _batch.Count) _batchResults.Add(new List<ScatterInstance>(256));
            for (int i = 0; i < _batch.Count; i++) _batchResults[i].Clear();

            int tileLevel = _tileLevel;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await Awaitable.BackgroundThreadAsync();
            for (int i = 0; i < _batch.Count; i++)
            {
                UnpackTile(_batch[i].Tile, out int face, out int tx, out int ty);
                _field.GatherTilePrototype(ctx, snap, face, tx, ty, tileLevel, _batch[i].Proto, _batchResults[i], out _);
            }
            sw.Stop();
            await Awaitable.MainThreadAsync();

            if (epoch == _epoch && _configured)
            {
                for (int i = 0; i < _batch.Count; i++) Commit(_batch[i], _batchResults[i]);
                _log.Log(LogLevel.Debug, "Scatter",
                    $"tiles +{_batch.Count} pairs {sw.ElapsedMilliseconds} ms | live {LiveTileCount} tiles {LiveInstanceCount} inst, {_work.Count} queued {_inFlight.Count} inflight");
            }
        }
        catch (OperationCanceledException) { /* teardown mid-await */ }
        catch (Exception e)
        {
            _log.Log(LogLevel.Warning, "Scatter", $"tile gather failed: {e}");
        }
        finally
        {
            // Always release the in-flight keys so a failed/stale/cancelled pair is retried on the next
            // reeval (a still-required pair reappears in _work; a done pair is skipped via ReadyMask).
            for (int i = 0; i < _batch.Count; i++) _inFlight.Remove(_batch[i]);
            _working = false;
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
        // Copy: the worker reuses its scratch lists across batches.
        var owned = new List<ScatterInstance>(instances);
        entry.ByProto[p] = owned;
        entry.ReadyMask |= 1UL << p;
        for (int i = 0; i < owned.Count; i++)
        {
            var inst = owned[i];
            _matrices[p].Add(Matrix4x4.TRS(inst.PositionWS, inst.Rotation, Vector3.one * inst.Scale));
            _positions[p].Add(inst.PositionWS);
        }
    }

    void RebuildBuckets(ulong protos)
    {
        for (int p = 0; p < _protoCount; p++)
        {
            if ((protos & (1UL << p)) == 0) continue;
            _matrices[p].Clear();
            _positions[p].Clear();
            foreach (var kv in _tiles)
            {
                var e = kv.Value;
                if ((e.ReadyMask & (1UL << p)) == 0) continue;
                var list = e.ByProto[p];
                for (int i = 0; i < list.Count; i++)
                {
                    var inst = list[i];
                    _matrices[p].Add(Matrix4x4.TRS(inst.PositionWS, inst.Rotation, Vector3.one * inst.Scale));
                    _positions[p].Add(inst.PositionWS);
                }
            }
        }
    }

    public void Dispose()
    {
        _epoch++;
        _configured = false;
        _tiles.Clear();
        _inFlight.Clear();
        _work.Clear();
        for (int p = 0; p < _matrices.Length; p++) { _matrices[p].Clear(); _positions[p].Clear(); }
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

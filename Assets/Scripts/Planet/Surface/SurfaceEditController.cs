using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Saved edit stamps are the durable world state. Path wear, scorch, and future
// surface textures are derived caches rebuilt from this stamp list.
//
// The stamps live in the world delta log, one record each, so this is a view over that log rather than a store
// of its own. Every mutation appends; nothing rewrites a whole file per brush sample the way the old
// surface-edits-{seed}.json did.
public sealed class SurfaceEditController : ISurfacePathBrushService
{
    const int LegacySaveVersion = 1;
    const float DefaultRegrowRefreshSeconds = 5f;
    static readonly int SurfacePathDebugId = Shader.PropertyToID("_SurfacePathDebug");

    readonly Transform _planetTransform;
    readonly ILogger _logger;
    readonly System.Action _invalidateGrass;
    readonly List<SurfaceEditStamp> _stamps = new();

    ChunkedSurfaceProvider _provider;
    Material _terrainMaterial;
    IWorldDeltaLog _delta;
    IWorldDeltaLog _loadedDelta;
    int _seed;
    int _loadedSeed = int.MinValue;
    ulong _nextStampId = 1;
    float _nextRegrowRefreshTime;
    int _strokeCounter;
    int _activeStrokeId;
    bool _strokeOpen;
    bool _warnedNoDeltaLog;
    float _regrowRefreshSeconds = DefaultRegrowRefreshSeconds;

    public SurfaceEditController(Transform planetTransform, ILogger logger, System.Action invalidateGrass)
    {
        _planetTransform = planetTransform;
        _logger = logger;
        _invalidateGrass = invalidateGrass;
    }

    public void Configure(ChunkedSurfaceProvider provider, Material terrainMaterial, int seed, IWorldDeltaLog deltaLog)
    {
        _provider = provider;
        _terrainMaterial = terrainMaterial;
        _seed = seed;
        _delta = deltaLog;
    }

    /// <summary>The durable stamp set, in creation order. Rebuilt from the log; a caller must not mutate it.</summary>
    public IReadOnlyList<SurfaceEditStamp> SavedStamps
    {
        get
        {
            EnsureLoaded();
            return _stamps;
        }
    }

    public bool TryPaintDisc(Vector3 localUnitDirection, float radiusMeters, float strength,
        float regrowSeconds, bool saveStamp, out string summary,
        bool invalidateGrass = true, bool saveImmediately = true)
        => TryPaintBrush(localUnitDirection, radiusMeters, strength, regrowSeconds,
            SurfacePathShape.SoftDisc, SurfacePathOperation.Paint, saveStamp, out summary,
            invalidateGrass, saveImmediately);

    public bool TryPaintBrush(Vector3 localUnitDirection, float radiusMeters, float strength,
        float regrowSeconds, SurfacePathShape shape, SurfacePathOperation operation, bool saveStamp,
        out string summary, bool invalidateGrass = true, bool saveImmediately = true)
    {
        summary = "path paint requires a generated chunked planet";
        if (_provider == null)
            return false;

        float radius = Mathf.Max(radiusMeters, 0.1f);
        float alpha = Mathf.Clamp01(strength);
        bool painted = _provider.TryPaintSurfaceStateBrush(
            localUnitDirection.normalized,
            radius,
            alpha,
            shape,
            operation,
            batched: !saveImmediately,
            strokeCoverage: !saveImmediately,
            out summary);
        if (!painted)
            return false;

        if (saveStamp)
        {
            SurfaceEditStamp stamp = new()
            {
                kind = SurfaceEditStampCodec.PathKind,
                shape = SurfaceEditStampCodec.ShapeId(shape),
                operation = SurfaceEditStampCodec.OperationId(operation),
                strokeId = NextStrokeId(saveImmediately),
                direction = localUnitDirection.normalized,
                radiusMeters = radius,
                strength = alpha,
                createdUnixSeconds = NowUnixSeconds(),
                regrowSeconds = Mathf.Max(0f, regrowSeconds),
            };
            AddStamp(stamp);
            if (saveImmediately)
                _provider.RebuildPathWearFromStamps(_stamps, NowUnixSeconds());

            summary += stamp.regrowSeconds > 0f
                ? $"; saved {stamp.operation} stamp regrow={stamp.regrowSeconds:F0}s"
                : $"; saved permanent {stamp.operation} stamp";
        }

        if (invalidateGrass)
            _invalidateGrass?.Invoke();
        return true;
    }

    public bool TryPaintPattern(Vector3 localUnitDirection, float sizeMeters, float strength, out string summary)
    {
        summary = "path pattern requires a generated chunked planet";
        if (_provider == null)
            return false;

        bool painted = _provider.TryPaintSurfaceStateTestPattern(
            localUnitDirection.normalized,
            Mathf.Max(sizeMeters, 1f),
            Mathf.Clamp01(strength),
            out summary);
        if (painted)
            _invalidateGrass?.Invoke();
        return painted;
    }

    public bool TryPaintScorch(Vector3 localUnitDirection, float radiusMeters, float strength,
        float regrowSeconds, SurfacePathShape shape, SurfacePathOperation operation, bool saveStamp,
        out string summary, bool invalidateGrass = true, bool saveImmediately = true)
    {
        summary = "scorch paint requires a generated chunked planet";
        if (_provider == null)
            return false;

        float radius = Mathf.Max(radiusMeters, 0.1f);
        float alpha = Mathf.Clamp01(strength);
        bool painted = _provider.TryPaintSurfaceStateChannel(
            localUnitDirection.normalized,
            radius,
            alpha,
            channel: 1,
            shape,
            operation,
            batched: !saveImmediately,
            out summary);
        if (!painted)
            return false;

        if (saveStamp)
        {
            SurfaceEditStamp stamp = new()
            {
                kind = SurfaceEditStampCodec.ScorchKind,
                shape = SurfaceEditStampCodec.ShapeId(shape),
                operation = SurfaceEditStampCodec.OperationId(operation),
                strokeId = NextStrokeId(saveImmediately),
                direction = localUnitDirection.normalized,
                radiusMeters = radius,
                strength = alpha,
                createdUnixSeconds = NowUnixSeconds(),
                regrowSeconds = Mathf.Max(0f, regrowSeconds),
            };
            AddStamp(stamp);
            summary += stamp.regrowSeconds > 0f
                ? $"; saved {stamp.operation} scorch regrow={stamp.regrowSeconds:F0}s"
                : $"; saved permanent {stamp.operation} scorch";
        }

        if (invalidateGrass)
            _invalidateGrass?.Invoke();
        return true;
    }

    public bool TryPaintScorchPattern(Vector3 localUnitDirection, float sizeMeters, float strength, out string summary)
    {
        summary = "scorch pattern requires a generated chunked planet";
        if (_provider == null)
            return false;

        bool painted = _provider.TryPaintScorchTestPattern(
            localUnitDirection.normalized,
            Mathf.Max(sizeMeters, 1f),
            Mathf.Clamp01(strength),
            out summary);
        if (painted)
            _invalidateGrass?.Invoke();
        return painted;
    }

    public int ClearRuntimeMasks()
    {
        int cleared = _provider != null ? _provider.ClearSurfaceStateMasks() : 0;
        if (cleared > 0)
            _invalidateGrass?.Invoke();
        return cleared;
    }

    public string ReplaySavedStamps()
    {
        int replayed = ReplayStamps(clearFirst: true);
        return $"replayed {replayed} saved surface edit(s)";
    }

    public string ClearSavedStamps()
    {
        EnsureLoaded();
        int stamps = RemoveStamps(kind: null);
        int chunks = ClearRuntimeMasks();
        return $"cleared {stamps} saved path stamp(s), {chunks} runtime chunk mask(s)";
    }

    public string ClearSavedScorchStamps()
    {
        EnsureLoaded();
        int removed = RemoveStamps(SurfaceEditStampCodec.ScorchKind);
        int replayed = ReplayStamps(clearFirst: true);
        return $"cleared {removed} saved scorch stamp(s), replayed {replayed} remaining surface edit(s)";
    }

    public string Status()
    {
        if (_provider == null)
            return "path mask unavailable: active provider is not chunked";

        EnsureLoaded();
        int active = CountActiveStamps(NowUnixSeconds());
        int pathCount = CountKind(SurfaceEditStampCodec.PathKind);
        int scorchCount = CountKind(SurfaceEditStampCodec.ScorchKind);
        int regrowing = CountRegrowingStamps();
        float debug = _terrainMaterial != null && _terrainMaterial.HasProperty(SurfacePathDebugId)
            ? _terrainMaterial.GetFloat(SurfacePathDebugId)
            : 0f;
        return $"path mask ready: wear={PlanetChunkTextures.PathWearResolution} R8 vector-baked, surface-state fallback=64 RGBA; debug={DebugName(debug)}, saved={_stamps.Count} (path={pathCount}, scorch={scorchCount}, regrowing={regrowing}), active={active}, regrow-refresh={_regrowRefreshSeconds:F1}s, store={(_delta != null ? "world delta log" : "memory only (no delta log)")}";
    }

    public string SetDebug(bool? enabled)
    {
        if (_terrainMaterial == null || !_terrainMaterial.HasProperty(SurfacePathDebugId))
            return "path debug unavailable: terrain material has no _SurfacePathDebug";

        if (enabled.HasValue)
            _terrainMaterial.SetFloat(SurfacePathDebugId, enabled.Value ? 1f : 0f);

        bool active = _terrainMaterial.GetFloat(SurfacePathDebugId) > 0.5f;
        return $"path debug: {(active ? "hot-pink" : "off")}";
    }

    public string SetDebugWear(bool? enabled)
    {
        if (_terrainMaterial == null || !_terrainMaterial.HasProperty(SurfacePathDebugId))
            return "path debug unavailable: terrain material has no _SurfacePathDebug";

        if (enabled.HasValue)
            _terrainMaterial.SetFloat(SurfacePathDebugId, enabled.Value ? 2f : 0f);

        return $"path debug: {DebugName(_terrainMaterial.GetFloat(SurfacePathDebugId))}";
    }

    public string SetRegrowRefresh(float? seconds)
    {
        if (seconds.HasValue)
        {
            _regrowRefreshSeconds = Mathf.Clamp(seconds.Value, 0.1f, 3600f);
            _nextRegrowRefreshTime = Time.unscaledTime;
        }

        return $"path regrow-refresh: {_regrowRefreshSeconds:F1}s";
    }

    public string RefreshRegrowthNow()
    {
        int replayed = ReplayStamps(clearFirst: true);
        _nextRegrowRefreshTime = Time.unscaledTime + _regrowRefreshSeconds;
        return $"regrowth refreshed; replayed {replayed} saved surface edit(s)";
    }

    public void FlushPendingSave()
    {
        bool hadOpenStroke = _strokeOpen;
        _provider?.EndSurfaceStateBatch();
        _strokeOpen = false;
        if (hadOpenStroke && _provider != null)
        {
            EnsureLoaded();
            _provider.RebuildPathWearFromStamps(_stamps, NowUnixSeconds());
            _invalidateGrass?.Invoke();
        }
    }

    public void FlushSurfacePathEdits() => FlushPendingSave();

    public bool TryGetSurfacePathLocalDirection(Vector3 worldPoint, out Vector3 localUnitDirection)
    {
        localUnitDirection = default;
        if (_planetTransform == null)
            return false;

        Vector3 localPoint = _planetTransform.InverseTransformPoint(worldPoint);
        if (localPoint.sqrMagnitude < 0.0001f)
            return false;

        localUnitDirection = localPoint.normalized;
        return true;
    }

    public bool TryPaintSurfacePathBrushAtLocalDirection(Vector3 localUnitDirection, float radiusMeters, float strength,
        float regrowSeconds, SurfacePathShape shape, SurfacePathOperation operation, out string summary,
        bool saveStamp = true, bool invalidateGrass = true, bool saveImmediately = true)
    {
        if (localUnitDirection.sqrMagnitude < 0.0001f)
        {
            summary = "path paint requires a non-zero local direction";
            return false;
        }

        return TryPaintBrush(localUnitDirection.normalized, radiusMeters, strength, regrowSeconds,
            shape, operation, saveStamp, out summary, invalidateGrass, saveImmediately);
    }

    public bool TryBuildSurfacePathBrushPreview(Vector3 localUnitDirection, float surfaceRadius,
        float radiusMeters, SurfacePathShape shape, Vector3[] points, int segments, float liftMeters,
        out int count)
    {
        count = 0;
        if (_planetTransform == null || points == null || points.Length == 0 || localUnitDirection.sqrMagnitude < 0.0001f)
            return false;

        bool square = shape == SurfacePathShape.HardSquare;
        int required = square ? 5 : Mathf.Clamp(segments, 8, points.Length - 1) + 1;
        if (points.Length < required)
            return false;

        Vector3 direction = localUnitDirection.normalized;
        FaceSpaceCellRangeBuilder.DirectionToFaceUv(direction, out int face, out Vector2 faceUv);
        float metersPerUv = FaceSpaceCellRangeBuilder.ComputeMetersPerUV(face, faceUv, Mathf.Max(surfaceRadius, 0.001f));
        float radiusUv = Mathf.Max(0.00001f, radiusMeters / metersPerUv);
        float worldScale = FaceSpaceCellRangeBuilder.GetUniformWorldScale(_planetTransform);
        float localRadius = surfaceRadius / Mathf.Max(worldScale, 0.0001f);

        if (square)
        {
            for (int i = 0; i < 5; i++)
            {
                int corner = i & 3;
                float sx = (corner == 0 || corner == 3) ? -1f : 1f;
                float sy = corner < 2 ? -1f : 1f;
                points[i] = PreviewPoint(face, faceUv + new Vector2(sx, sy) * radiusUv, localRadius, liftMeters);
            }

            count = 5;
            return true;
        }

        int discSegments = required - 1;
        for (int i = 0; i <= discSegments; i++)
        {
            float angle = i / (float)discSegments * Mathf.PI * 2f;
            Vector2 uv = faceUv + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radiusUv;
            points[i] = PreviewPoint(face, uv, localRadius, liftMeters);
        }

        count = required;
        return true;
    }

    Vector3 PreviewPoint(int face, Vector2 uv, float localRadius, float liftMeters)
    {
        Vector3 local = FaceSpaceCellRangeBuilder.CubeFaceToUnitSphere(face, uv) * localRadius;
        Vector3 world = _planetTransform.TransformPoint(local);
        Vector3 normal = (world - _planetTransform.position).sqrMagnitude > 0.0001f
            ? (world - _planetTransform.position).normalized
            : _planetTransform.up;
        return world + normal * Mathf.Max(0f, liftMeters);
    }

    // One-shot paints are their own stroke; a drag (saveImmediately == false) keeps the same id
    // until FlushPendingSave closes it, so its stamps composite by max instead of stacking.
    int NextStrokeId(bool immediate)
    {
        // Before the counter is seeded from the saved stamps, not after: a fresh id that collides with a
        // saved stroke composites the two as one drag on the next replay.
        EnsureLoaded();
        if (immediate)
            return ++_strokeCounter;

        if (!_strokeOpen)
        {
            _activeStrokeId = ++_strokeCounter;
            _strokeOpen = true;
        }
        return _activeStrokeId;
    }

    public int ReplayStamps(bool clearFirst)
    {
        if (_provider == null)
            return 0;

        EnsureLoaded();
        long now = NowUnixSeconds();
        PruneExpired(now);
        if (clearFirst)
            _provider.ClearSurfaceStateMasks();

        int replayed = _provider.RebuildPathWearFromStamps(_stamps, now)
            + _provider.RebuildSurfaceStateFromStamps(_stamps, now);
        if (clearFirst || replayed > 0)
            _invalidateGrass?.Invoke();
        return replayed;
    }

    public void TickRegrowth()
    {
        if (Time.unscaledTime < _nextRegrowRefreshTime)
            return;

        EnsureLoaded();
        if (!HasRegrowingStamps())
        {
            _nextRegrowRefreshTime = Time.unscaledTime + _regrowRefreshSeconds;
            return;
        }

        // ponytail: coarse replay is enough for debug regrowth; shader-time fade if this needs to look smooth.
        ReplayStamps(clearFirst: true);
        _nextRegrowRefreshTime = Time.unscaledTime + _regrowRefreshSeconds;
    }

    void AddStamp(SurfaceEditStamp stamp)
    {
        EnsureLoaded();
        PruneExpired(NowUnixSeconds());
        Apply(SurfaceEditStampCodec.Encode(_nextStampId, stamp));
    }

    // --- delta log view ---

    // One append, then the same fold the replay uses. Going through Ingest rather than adding to _stamps
    // directly is what keeps a live world and a reloaded one from drifting apart: every stamp the session
    // holds has been through the encoder and back.
    void Apply(in WorldDelta delta)
    {
        if (_delta == null)
        {
            if (!_warnedNoDeltaLog)
            {
                _warnedNoDeltaLog = true;
                _logger?.Log(LogLevel.Warning, "SurfaceEdit", "No delta log configured; surface edits will not persist.");
            }
            Ingest(delta);
            return;
        }
        Ingest(_delta.Append(delta));
    }

    void Ingest(in WorldDelta delta)
    {
        if (delta.Kind != DeltaKind.SurfaceStamp)
            return;
        if (delta.Key >= _nextStampId)
            _nextStampId = delta.Key + 1;

        if (delta.State == SurfaceEditStampCodec.StateRemoved)
        {
            RemoveStampRecord(delta.Key);
            return;
        }
        if (!SurfaceEditStampCodec.TryDecode(delta, out SurfaceEditStamp stamp))
            return;

        _stamps.Add(stamp);
        if (stamp.strokeId > _strokeCounter)
            _strokeCounter = stamp.strokeId;
    }

    void RemoveStampRecord(ulong recordId)
    {
        for (int i = 0; i < _stamps.Count; i++)
        {
            if (_stamps[i].recordId != recordId)
                continue;

            _stamps.RemoveAt(i);
            return;
        }
    }

    // Tombstones every stamp of `kind` (all of them when null). Removal must be a record of its own: an
    // erased stamp is still in the log, and a client that only replayed the additions would paint it back.
    int RemoveStamps(string kind)
    {
        int removed = 0;
        for (int i = _stamps.Count - 1; i >= 0; i--)
        {
            if (kind != null && _stamps[i].kind != kind)
                continue;

            Apply(SurfaceEditStampCodec.Tombstone(_stamps[i].recordId));
            removed++;
        }
        return removed;
    }

    void EnsureLoaded()
    {
        if (_loadedSeed == _seed && ReferenceEquals(_loadedDelta, _delta))
            return;

        _loadedSeed = _seed;
        _loadedDelta = _delta;
        _stamps.Clear();
        _strokeCounter = 0;
        _activeStrokeId = 0;
        _strokeOpen = false;
        _nextStampId = 1;

        if (_delta != null)
            foreach (WorldDelta d in _delta.Snapshot())
                Ingest(d);

        ImportLegacySave();
    }

    int PruneExpired(long now)
    {
        int removed = 0;
        for (int i = _stamps.Count - 1; i >= 0; i--)
        {
            if (EffectiveStrength(_stamps[i], now) > 0f)
                continue;

            Apply(SurfaceEditStampCodec.Tombstone(_stamps[i].recordId));
            removed++;
        }
        return removed;
    }

    // --- migration off the old per-seed JSON ---

    // Reads the v1 JSON once and appends it to the delta log. The guard is the log itself: once it holds any
    // surface-stamp record the import has already run, and re-running it would paint back stamps the player
    // has since cleared. The file is left exactly where it is - it is the player's save data, and nothing here
    // has standing to delete it.
    void ImportLegacySave()
    {
        if (_delta == null || HasSurfaceStampRecords())
            return;

        string path = LegacyPath();
        if (!File.Exists(path))
            return;

        try
        {
            SurfaceEditSaveData data = JsonUtility.FromJson<SurfaceEditSaveData>(File.ReadAllText(path));
            if (data?.stamps == null || data.version != LegacySaveVersion || data.planetSeed != _seed)
            {
                _logger?.Log(LogLevel.Info, "SurfaceEdit",
                    $"Ignoring {Path.GetFileName(path)}: version {data?.version} seed {data?.planetSeed}.");
                return;
            }

            int maxStrokeId = 0;
            for (int i = 0; i < data.stamps.Count; i++)
                maxStrokeId = Mathf.Max(maxStrokeId, data.stamps[i].strokeId);

            for (int i = 0; i < data.stamps.Count; i++)
            {
                SurfaceEditStamp stamp = data.stamps[i];
                if (stamp == null)
                    continue;
                // Saves older than the stroke-id field group every stamp into stroke 0, which composites them
                // as one drag rather than as separate paints.
                if (stamp.strokeId <= 0)
                    stamp.strokeId = ++maxStrokeId;
                Apply(SurfaceEditStampCodec.Encode(_nextStampId, stamp));
            }

            if (data.stamps.Count > 0)
                _logger?.Log(LogLevel.Info, "SurfaceEdit",
                    $"Imported {data.stamps.Count} surface edit(s) from the pre-delta-log save.");
        }
        catch (System.Exception ex)
        {
            _logger.LogException("SurfaceEdit", ex);
        }
    }

    bool HasSurfaceStampRecords()
    {
        System.Collections.Generic.IReadOnlyList<WorldDelta> snapshot = _delta.Snapshot();
        for (int i = 0; i < snapshot.Count; i++)
            if (snapshot[i].Kind == DeltaKind.SurfaceStamp)
                return true;
        return false;
    }

    int CountActiveStamps(long now)
    {
        int active = 0;
        for (int i = 0; i < _stamps.Count; i++)
        {
            if (EffectiveStrength(_stamps[i], now) > 0f)
                active++;
        }
        return active;
    }

    int CountKind(string kind)
    {
        int count = 0;
        for (int i = 0; i < _stamps.Count; i++)
        {
            if (_stamps[i].kind == kind)
                count++;
        }
        return count;
    }

    int CountRegrowingStamps()
    {
        int count = 0;
        for (int i = 0; i < _stamps.Count; i++)
        {
            if (_stamps[i].regrowSeconds > 0f)
                count++;
        }
        return count;
    }

    bool HasRegrowingStamps()
    {
        for (int i = 0; i < _stamps.Count; i++)
        {
            if (_stamps[i].regrowSeconds > 0f)
                return true;
        }
        return false;
    }

    static float EffectiveStrength(SurfaceEditStamp stamp, long now)
    {
        float strength = Mathf.Clamp01(stamp.strength);
        if (stamp.regrowSeconds <= 0f)
            return strength;

        float elapsed = Mathf.Max(0f, now - stamp.createdUnixSeconds);
        return strength * (1f - Mathf.Clamp01(elapsed / stamp.regrowSeconds));
    }

    static long NowUnixSeconds() => System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    static string DebugName(float value)
    {
        if (value > 1.5f)
            return "wear-mask";
        return value > 0.5f ? "hot-pink" : "off";
    }

    string LegacyPath() =>
        Path.Combine(Application.persistentDataPath, "ProceduralPlanets", $"surface-edits-{_seed}.json");
}

[System.Serializable]
public sealed class SurfaceEditSaveData
{
    public int version;
    public int planetSeed;
    public List<SurfaceEditStamp> stamps;
}

[System.Serializable]
public sealed class SurfaceEditStamp
{
    public string kind;
    public string shape;
    public string operation;
    public int strokeId;
    public Vector3 direction;
    public float radiusMeters;
    public float strength;
    public long createdUnixSeconds;
    public float regrowSeconds;
    public Vector3 surfaceNormal;
    public double createdGameSeconds;

    /// <summary>Key of the delta record this stamp came from. Not part of the stamp; it addresses the record.</summary>
    [System.NonSerialized] public ulong recordId;
}

// A stamp has nine fields and no transform, so it does not fit the delta record's hoisted fields - which is
// exactly what the record's opaque payload is for. The one field that IS hoisted is the direction: it goes in
// the record's Position so spatial code (a replication interest bucket, a log inspector) can place a stamp
// without knowing this encoding.
//
// The payload carries its own format byte. A later field is added by writing a new format and keeping a read
// path for the old one, the same way the log itself versions.
public static class SurfaceEditStampCodec
{
    public const string PathKind = "path";
    public const string ScorchKind = "scorch";
    public const string BloodKind = "blood";

    public const byte StatePresent = 0;
    public const byte StateRemoved = 1;

    const byte PayloadFormat1 = 1;
    const int PayloadBytes1 = 28;

    public static WorldDelta Encode(ulong recordId, SurfaceEditStamp stamp)
    {
        if (stamp == null || (stamp.kind != PathKind && stamp.kind != ScorchKind && stamp.kind != BloodKind))
            throw new System.ArgumentException("Unsupported surface stamp kind.", nameof(stamp));
        bool blood = stamp.kind == BloodKind;
        if (blood && !ValidBlood(stamp)) throw new System.ArgumentException("Invalid blood stamp geometry or game time.", nameof(stamp));
        var payload = new byte[blood ? 48 : PayloadBytes1];
        payload[0] = blood ? (byte)2 : PayloadFormat1;
        payload[1] = (byte)(blood ? 2 : stamp.kind == ScorchKind ? 1 : 0);
        payload[2] = (byte)ParseShape(stamp.shape);
        payload[3] = (byte)(stamp.operation == "erase" ? SurfacePathOperation.Erase : SurfacePathOperation.Paint);
        System.BitConverter.GetBytes(stamp.strokeId).CopyTo(payload, 4);
        System.BitConverter.GetBytes(stamp.radiusMeters).CopyTo(payload, 8);
        System.BitConverter.GetBytes(stamp.strength).CopyTo(payload, 12);
        System.BitConverter.GetBytes(stamp.createdUnixSeconds).CopyTo(payload, 16);
        System.BitConverter.GetBytes(stamp.regrowSeconds).CopyTo(payload, 24);
        if (blood)
        {
            System.BitConverter.GetBytes(stamp.surfaceNormal.x).CopyTo(payload, 28);
            System.BitConverter.GetBytes(stamp.surfaceNormal.y).CopyTo(payload, 32);
            System.BitConverter.GetBytes(stamp.surfaceNormal.z).CopyTo(payload, 36);
            System.BitConverter.GetBytes(stamp.createdGameSeconds).CopyTo(payload, 40);
        }
        return new WorldDelta(0, DeltaKind.SurfaceStamp, recordId, stamp.direction,
            state: StatePresent, payload: payload);
    }

    /// <summary>A cleared or fully regrown stamp. Last write per key wins, so this replaces the record rather than adding one.</summary>
    public static WorldDelta Tombstone(ulong recordId) =>
        new(0, DeltaKind.SurfaceStamp, recordId, state: StateRemoved);

    public static bool TryDecode(in WorldDelta delta, out SurfaceEditStamp stamp)
    {
        stamp = null;
        byte[] payload = delta.Payload;
        if (payload == null || payload.Length < PayloadBytes1 ||
            !(payload[0] == PayloadFormat1 && payload[1] <= 1 || payload[0] == 2 && payload[1] == 2 && payload.Length >= 48))
            return false;

        stamp = new SurfaceEditStamp
        {
            recordId = delta.Key,
            kind = payload[1] == 2 ? BloodKind : payload[1] == 1 ? ScorchKind : PathKind,
            shape = ShapeId((SurfacePathShape)payload[2]),
            operation = OperationId((SurfacePathOperation)payload[3]),
            strokeId = System.BitConverter.ToInt32(payload, 4),
            direction = delta.Position,
            radiusMeters = System.BitConverter.ToSingle(payload, 8),
            strength = System.BitConverter.ToSingle(payload, 12),
            createdUnixSeconds = System.BitConverter.ToInt64(payload, 16),
            regrowSeconds = System.BitConverter.ToSingle(payload, 24),
        };
        if (payload[0] == 2)
        {
            stamp.surfaceNormal = new Vector3(System.BitConverter.ToSingle(payload, 28),
                System.BitConverter.ToSingle(payload, 32), System.BitConverter.ToSingle(payload, 36));
            stamp.createdGameSeconds = System.BitConverter.ToDouble(payload, 40);
            if (!ValidBlood(stamp)) { stamp = null; return false; }
        }
        return true;
    }

    // The provider reads these as strings, so the wire form is a byte and the in-memory form stays what
    // ChunkedSurfaceProvider already compares against.
    static bool ValidBlood(SurfaceEditStamp stamp) =>
        float.IsFinite(stamp.direction.x) && float.IsFinite(stamp.direction.y) && float.IsFinite(stamp.direction.z) &&
        float.IsFinite(stamp.surfaceNormal.sqrMagnitude) && stamp.surfaceNormal.sqrMagnitude > .0001f &&
        float.IsFinite(stamp.radiusMeters) && stamp.radiusMeters > 0f &&
        float.IsFinite(stamp.strength) && stamp.strength >= 0f && stamp.strength <= 1f &&
        float.IsFinite(stamp.regrowSeconds) && stamp.regrowSeconds > 0f &&
        !double.IsNaN(stamp.createdGameSeconds) && !double.IsInfinity(stamp.createdGameSeconds) && stamp.createdGameSeconds >= 0d;

    public static string ShapeId(SurfacePathShape shape) => shape switch
    {
        SurfacePathShape.HardDisc => "hard-disc",
        SurfacePathShape.HardSquare => "hard-square",
        _ => "disc",
    };

    public static SurfacePathShape ParseShape(string shape) => shape switch
    {
        "hard-disc" => SurfacePathShape.HardDisc,
        "hard-square" => SurfacePathShape.HardSquare,
        _ => SurfacePathShape.SoftDisc,
    };

    public static string OperationId(SurfacePathOperation operation) =>
        operation == SurfacePathOperation.Erase ? "erase" : "paint";
}

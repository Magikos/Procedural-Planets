using System.Collections.Generic;
using System.IO;
using UnityEngine;

public sealed class SurfacePathEditController : ISurfacePathBrushService
{
    const int SaveVersion = 1;
    const float RegrowRefreshSeconds = 5f;
    static readonly int SurfacePathDebugId = Shader.PropertyToID("_SurfacePathDebug");

    readonly Transform _planetTransform;
    readonly ILogger _logger;
    readonly System.Action _invalidateGrass;
    readonly List<SurfacePathEditStamp> _stamps = new();

    ChunkedSurfaceProvider _provider;
    Material _terrainMaterial;
    int _seed;
    int _loadedSeed = int.MinValue;
    float _nextRegrowRefreshTime;
    bool _saveDirty;
    int _strokeCounter;
    int _activeStrokeId;
    bool _strokeOpen;

    public SurfacePathEditController(Transform planetTransform, ILogger logger, System.Action invalidateGrass)
    {
        _planetTransform = planetTransform;
        _logger = logger;
        _invalidateGrass = invalidateGrass;
    }

    public void Configure(ChunkedSurfaceProvider provider, Material terrainMaterial, int seed)
    {
        _provider = provider;
        _terrainMaterial = terrainMaterial;
        _seed = seed;
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
            SurfacePathEditStamp stamp = new()
            {
                kind = "path",
                shape = ShapeId(shape),
                operation = OperationId(operation),
                strokeId = NextStrokeId(saveImmediately),
                direction = localUnitDirection.normalized,
                radiusMeters = radius,
                strength = alpha,
                createdUnixSeconds = NowUnixSeconds(),
                regrowSeconds = Mathf.Max(0f, regrowSeconds),
            };
            AddStamp(stamp, saveImmediately);
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
            SurfacePathEditStamp stamp = new()
            {
                kind = "scorch",
                shape = ShapeId(shape),
                operation = OperationId(operation),
                strokeId = NextStrokeId(saveImmediately),
                direction = localUnitDirection.normalized,
                radiusMeters = radius,
                strength = alpha,
                createdUnixSeconds = NowUnixSeconds(),
                regrowSeconds = Mathf.Max(0f, regrowSeconds),
            };
            AddStamp(stamp, saveImmediately);
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
        int stamps = _stamps.Count;
        _stamps.Clear();

        string path = FilePath();
        if (File.Exists(path))
            File.Delete(path);

        int chunks = ClearRuntimeMasks();
        return $"cleared {stamps} saved path stamp(s), {chunks} runtime chunk mask(s)";
    }

    public string ClearSavedScorchStamps()
    {
        EnsureLoaded();
        int removed = 0;
        for (int i = _stamps.Count - 1; i >= 0; i--)
        {
            if (_stamps[i].kind != "scorch")
                continue;

            _stamps.RemoveAt(i);
            removed++;
        }

        if (removed > 0)
            Save();
        int replayed = ReplayStamps(clearFirst: true);
        return $"cleared {removed} saved scorch stamp(s), replayed {replayed} remaining surface edit(s)";
    }

    public string Status()
    {
        if (_provider == null)
            return "path mask unavailable: active provider is not chunked";

        EnsureLoaded();
        int active = CountActiveStamps(NowUnixSeconds());
        int pathCount = CountKind("path");
        int scorchCount = CountKind("scorch");
        float debug = _terrainMaterial != null && _terrainMaterial.HasProperty(SurfacePathDebugId)
            ? _terrainMaterial.GetFloat(SurfacePathDebugId)
            : 0f;
        return $"path mask ready: wear={PlanetChunkTextures.PathWearResolution} R8 vector-baked, surface-state fallback=64 RGBA; debug={DebugName(debug)}, saved={_stamps.Count} (path={pathCount}, scorch={scorchCount}), active={active}, file={Path.GetFileName(FilePath())}";
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
        if (_saveDirty)
            Save();
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
        int removed = PruneExpired(now);
        if (clearFirst)
            _provider.ClearSurfaceStateMasks();

        int replayed = _provider.RebuildPathWearFromStamps(_stamps, now)
            + _provider.RebuildSurfaceStateFromStamps(_stamps, now);
        if (removed > 0)
            Save();
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
            return;

        // ponytail: coarse replay is enough for debug regrowth; shader-time fade if this needs to look smooth.
        ReplayStamps(clearFirst: true);
        _nextRegrowRefreshTime = Time.unscaledTime + RegrowRefreshSeconds;
    }

    void AddStamp(SurfacePathEditStamp stamp, bool saveImmediately)
    {
        EnsureLoaded();
        _saveDirty |= PruneExpired(NowUnixSeconds()) > 0;
        _stamps.Add(stamp);
        if (saveImmediately)
            Save();
        else
            _saveDirty = true;
    }

    void EnsureLoaded()
    {
        if (_loadedSeed == _seed)
            return;

        _loadedSeed = _seed;
        _stamps.Clear();
        _strokeCounter = 0;
        _activeStrokeId = 0;
        _strokeOpen = false;

        string path = FilePath();
        if (!File.Exists(path))
            return;

        try
        {
            string json = File.ReadAllText(path);
            SurfacePathEditSaveData data = JsonUtility.FromJson<SurfacePathEditSaveData>(json);
            if (data?.stamps == null || data.version != SaveVersion || data.planetSeed != _seed)
                return;

            _stamps.AddRange(data.stamps);
            int maxStrokeId = 0;
            for (int i = 0; i < _stamps.Count; i++)
                maxStrokeId = Mathf.Max(maxStrokeId, _stamps[i].strokeId);

            bool migrated = false;
            for (int i = 0; i < _stamps.Count; i++)
            {
                if (_stamps[i].strokeId > 0)
                    continue;

                _stamps[i].strokeId = ++maxStrokeId;
                migrated = true;
            }

            _strokeCounter = maxStrokeId;
            _saveDirty = migrated;
        }
        catch (System.Exception ex)
        {
            _logger.LogException("SurfacePath", ex);
        }
    }

    void Save()
    {
        string directory = Path.GetDirectoryName(FilePath());
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        SurfacePathEditSaveData data = new()
        {
            version = SaveVersion,
            planetSeed = _seed,
            stamps = _stamps,
        };
        File.WriteAllText(FilePath(), JsonUtility.ToJson(data, prettyPrint: true));
        _saveDirty = false;
    }

    int PruneExpired(long now)
    {
        int removed = 0;
        for (int i = _stamps.Count - 1; i >= 0; i--)
        {
            if (EffectiveStrength(_stamps[i], now) > 0f)
                continue;

            _stamps.RemoveAt(i);
            removed++;
        }
        return removed;
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

    bool HasRegrowingStamps()
    {
        for (int i = 0; i < _stamps.Count; i++)
        {
            if (_stamps[i].regrowSeconds > 0f)
                return true;
        }
        return false;
    }

    static float EffectiveStrength(SurfacePathEditStamp stamp, long now)
    {
        float strength = Mathf.Clamp01(stamp.strength);
        if (stamp.regrowSeconds <= 0f)
            return strength;

        float elapsed = Mathf.Max(0f, now - stamp.createdUnixSeconds);
        return strength * (1f - Mathf.Clamp01(elapsed / stamp.regrowSeconds));
    }

    static long NowUnixSeconds() => System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    static string ShapeId(SurfacePathShape shape)
    {
        switch (shape)
        {
            case SurfacePathShape.HardDisc:
                return "hard-disc";
            case SurfacePathShape.HardSquare:
                return "hard-square";
            default:
                return "disc";
        }
    }

    static string DebugName(float value)
    {
        if (value > 1.5f)
            return "wear-mask";
        return value > 0.5f ? "hot-pink" : "off";
    }

    static SurfacePathShape ParseShape(string shape)
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

    static string OperationId(SurfacePathOperation operation) =>
        operation == SurfacePathOperation.Erase ? "erase" : "paint";

    static SurfacePathOperation ParseOperation(string operation) =>
        operation == "erase" ? SurfacePathOperation.Erase : SurfacePathOperation.Paint;

    string FilePath()
    {
        string directory = Path.Combine(Application.persistentDataPath, "ProceduralPlanets");
        return Path.Combine(directory, $"surface-edits-{_seed}.json");
    }
}

[System.Serializable]
public sealed class SurfacePathEditSaveData
{
    public int version;
    public int planetSeed;
    public List<SurfacePathEditStamp> stamps;
}

[System.Serializable]
public sealed class SurfacePathEditStamp
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
}

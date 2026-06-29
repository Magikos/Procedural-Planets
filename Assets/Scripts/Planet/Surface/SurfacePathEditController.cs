using System.Collections.Generic;
using System.IO;
using UnityEngine;

public enum SurfacePathShape
{
    SoftDisc,
    HardDisc,
    HardSquare,
}

public enum SurfacePathOperation
{
    Paint,
    Erase,
}

public sealed class SurfacePathEditController
{
    const int SaveVersion = 1;
    const float RegrowRefreshSeconds = 5f;
    static readonly int SurfacePathDebugId = Shader.PropertyToID("_SurfacePathDebug");

    readonly ILogger _logger;
    readonly System.Action _invalidateGrass;
    readonly List<SurfacePathEditStamp> _stamps = new();

    ChunkedSurfaceProvider _provider;
    Material _terrainMaterial;
    int _seed;
    int _loadedSeed = int.MinValue;
    float _nextRegrowRefreshTime;
    bool _saveDirty;

    public SurfacePathEditController(ILogger logger, System.Action invalidateGrass)
    {
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
                direction = localUnitDirection.normalized,
                radiusMeters = radius,
                strength = alpha,
                createdUnixSeconds = NowUnixSeconds(),
                regrowSeconds = Mathf.Max(0f, regrowSeconds),
            };
            AddStamp(stamp, saveImmediately);
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

    public string Status()
    {
        if (_provider == null)
            return "path mask unavailable: active provider is not chunked";

        EnsureLoaded();
        int active = CountActiveStamps(NowUnixSeconds());
        float debug = _terrainMaterial != null && _terrainMaterial.HasProperty(SurfacePathDebugId)
            ? _terrainMaterial.GetFloat(SurfacePathDebugId)
            : 0f;
        return $"path mask ready: R=paved, G=scorched; debug={(debug > 0.5f ? "hot-pink" : "off")}, saved={_stamps.Count}, active={active}, file={Path.GetFileName(FilePath())}";
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

    public void FlushPendingSave()
    {
        if (_saveDirty)
            Save();
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

        int replayed = 0;
        // ponytail: stamp replay is O(stamps * chunks); batch by chunk if saves get large.
        for (int i = 0; i < _stamps.Count; i++)
        {
            SurfacePathEditStamp stamp = _stamps[i];
            if (stamp.kind != "path")
                continue;

            float alpha = EffectiveStrength(stamp, now);
            if (alpha <= 0f)
                continue;

            if (_provider.TryPaintSurfaceStateBrush(
                    stamp.direction,
                    Mathf.Max(stamp.radiusMeters, 0.1f),
                    alpha,
                    ParseShape(stamp.shape),
                    ParseOperation(stamp.operation),
                    out _))
            {
                replayed++;
            }
        }

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
            _saveDirty = false;
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
    public Vector3 direction;
    public float radiusMeters;
    public float strength;
    public long createdUnixSeconds;
    public float regrowSeconds;
}

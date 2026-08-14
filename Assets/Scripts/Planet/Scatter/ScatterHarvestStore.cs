using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Persistent set of harvested scatter instance ids (ScatterId), keyed by planet seed. Mirrors
// SurfaceEditController's save/load: a seed-scoped JSON file under persistentDataPath. ScatterTileCache
// consults Contains(id) in Commit, so a harvested instance never re-enters the draw on re-gather or reload.
// The whole scatter world is rebuilt on every load/regen, so this Commit-time filter makes removal persist
// with no replay pass. The id is a stable ScatterId, so a saved harvest rebinds to exactly one instance.
public sealed class ScatterHarvestStore
{
    const int SaveVersion = 1;

    readonly ILogger _log;
    readonly HashSet<ulong> _harvested = new();
    int _seed;
    bool _loaded;

    public ScatterHarvestStore(ILogger log = null) => _log = log;

    // Bind to a world seed and load its harvested set. Idempotent for the same seed.
    public void Configure(int seed)
    {
        if (_loaded && seed == _seed) return;
        _seed = seed;
        _loaded = false;
        _harvested.Clear();
        EnsureLoaded();
    }

    public bool Contains(ulong id) => _harvested.Contains(id);
    public int Count => _harvested.Count;

    // Record a harvested instance and persist. Returns false if it was already recorded.
    public bool Add(ulong id)
    {
        EnsureLoaded();
        if (!_harvested.Add(id)) return false;
        Save();
        return true;
    }

    [System.Serializable]
    struct SaveData { public int version; public int planetSeed; public string[] ids; }

    static string FilePath(int seed) =>
        Path.Combine(Application.persistentDataPath, "ProceduralPlanets", $"scatter-harvest-{seed}.json");

    void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        _harvested.Clear();
        string path = FilePath(_seed);
        if (!File.Exists(path)) return;
        try
        {
            var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
            if (data.version != SaveVersion || data.planetSeed != _seed || data.ids == null) return;
            // JsonUtility cannot serialize ulong; ids are stored as decimal strings.
            foreach (string s in data.ids)
                if (ulong.TryParse(s, out ulong id)) _harvested.Add(id);
        }
        catch (System.Exception e)
        {
            _log?.Log(LogLevel.Warning, "ScatterHarvest", $"Failed to load {path}: {e.Message}");
        }
    }

    void Save()
    {
        string path = FilePath(_seed);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var ids = new string[_harvested.Count];
            int i = 0;
            foreach (ulong id in _harvested) ids[i++] = id.ToString();
            File.WriteAllText(path, JsonUtility.ToJson(new SaveData { version = SaveVersion, planetSeed = _seed, ids = ids }));
        }
        catch (System.Exception e)
        {
            _log?.Log(LogLevel.Warning, "ScatterHarvest", $"Failed to save {path}: {e.Message}");
        }
    }
}

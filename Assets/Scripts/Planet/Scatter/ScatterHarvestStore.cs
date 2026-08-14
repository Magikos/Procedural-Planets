using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Persistent OVERRIDE layer for harvested scatter, keyed by planet seed. Scatter itself is not a stored
// object list — it is derived from the seed — so a chop is persisted as an override record here, layered on
// the deterministic base. ScatterTileCache consults Contains(id) in Commit, so a harvested instance never
// re-enters the draw or a reload.
//
// A record is a small state machine: a felled tree becomes a Stump (a placed marker at that spot, rendered
// by StumpRenderer, later dug up with a shovel), then Dug (fully gone). A Standing tree is simply absent from
// the store. Both Stump and Dug drop the standing instance from the draw (Contains == true); only Stump is
// rendered. The record carries position + prototype so the stump can be drawn without re-deriving them.
public sealed class ScatterHarvestStore
{
    const int SaveVersion = 2; // v1 stored bare harvested ids; v2 adds state + position + proto (v1 files reset)

    public enum HarvestState { Stump = 0, Dug = 1 }

    public struct HarvestNode
    {
        public ulong Id;
        public Vector3 Position;
        public int ProtoIndex;
        public HarvestState State;
    }

    readonly ILogger _log;
    readonly Dictionary<ulong, HarvestNode> _nodes = new();
    int _seed;
    bool _loaded;

    public ScatterHarvestStore(ILogger log = null) => _log = log;

    // Bind to a world seed and load its records. Idempotent for the same seed.
    public void Configure(int seed)
    {
        if (_loaded && seed == _seed) return;
        _seed = seed;
        _loaded = false;
        _nodes.Clear();
        EnsureLoaded();
    }

    // Any recorded id (Stump or Dug) means the standing tree is gone — the Commit filter drops it.
    public bool Contains(ulong id) => _nodes.ContainsKey(id);
    public int Count => _nodes.Count;

    // A tree was felled: it becomes a stump. Returns false if this id was already recorded.
    public bool RecordStump(ulong id, Vector3 position, int protoIndex)
    {
        EnsureLoaded();
        if (_nodes.ContainsKey(id)) return false;
        _nodes[id] = new HarvestNode { Id = id, Position = position, ProtoIndex = protoIndex, State = HarvestState.Stump };
        Save();
        return true;
    }

    // A stump was dug up: it becomes fully gone (no longer rendered). Returns false if there was no stump.
    public bool RecordDug(ulong id)
    {
        EnsureLoaded();
        if (!_nodes.TryGetValue(id, out HarvestNode n) || n.State == HarvestState.Dug) return false;
        n.State = HarvestState.Dug;
        _nodes[id] = n;
        Save();
        return true;
    }

    // Fill `into` with every Stump-state record (for the stump renderer / stump picking).
    public void CollectStumps(List<HarvestNode> into)
    {
        into.Clear();
        foreach (KeyValuePair<ulong, HarvestNode> kv in _nodes)
            if (kv.Value.State == HarvestState.Stump) into.Add(kv.Value);
    }

    [Serializable] struct NodeSave { public string id; public Vector3 position; public int proto; public int state; }
    [Serializable] struct SaveData { public int version; public int planetSeed; public NodeSave[] nodes; }

    static string FilePath(int seed) =>
        Path.Combine(Application.persistentDataPath, "ProceduralPlanets", $"scatter-harvest-{seed}.json");

    void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        _nodes.Clear();
        string path = FilePath(_seed);
        if (!File.Exists(path)) return;
        try
        {
            var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
            if (data.version != SaveVersion || data.planetSeed != _seed || data.nodes == null) return;
            // JsonUtility cannot serialize ulong; ids are stored as decimal strings.
            foreach (NodeSave ns in data.nodes)
                if (ulong.TryParse(ns.id, out ulong id))
                    _nodes[id] = new HarvestNode
                    {
                        Id = id, Position = ns.position, ProtoIndex = ns.proto, State = (HarvestState)ns.state,
                    };
        }
        catch (Exception e)
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
            var nodes = new NodeSave[_nodes.Count];
            int i = 0;
            foreach (KeyValuePair<ulong, HarvestNode> kv in _nodes)
                nodes[i++] = new NodeSave
                {
                    id = kv.Key.ToString(), position = kv.Value.Position,
                    proto = kv.Value.ProtoIndex, state = (int)kv.Value.State,
                };
            File.WriteAllText(path, JsonUtility.ToJson(new SaveData { version = SaveVersion, planetSeed = _seed, nodes = nodes }));
        }
        catch (Exception e)
        {
            _log?.Log(LogLevel.Warning, "ScatterHarvest", $"Failed to save {path}: {e.Message}");
        }
    }
}

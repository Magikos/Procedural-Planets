using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Persistent OVERRIDE + placed-object layer for harvested scatter, keyed by planet seed. Scatter itself is not
// a stored object list — it is derived from the seed — so a chop is persisted here, layered on the base.
//
// Two record kinds:
//  - STUMP nodes, keyed by the felled tree's ScatterId. ScatterTileCache consults Contains(id) in Commit, so a
//    harvested tree never re-enters the draw or a reload. A node is a small state machine: Stump -> Dug.
//  - LOG records, keyed by a running id (the fallen top, a genuinely placed object with a settled rotation).
//    These do NOT gate Commit; a LogRenderer draws them and they are removed when chopped for wood.
public sealed class ScatterHarvestStore
{
    const int SaveVersion = 3; // v1 bare ids; v2 stump state machine; v3 adds fallen-log records (older reset)

    public enum HarvestState { Stump = 0, Dug = 1 }

    public struct HarvestNode
    {
        public ulong Id;
        public Vector3 Position;
        public int ProtoIndex;
        public HarvestState State;
    }

    public struct LogRecord
    {
        public ulong Id;
        public Vector3 Position;
        public Quaternion Rotation;
        public int ProtoIndex;
    }

    readonly ILogger _log;
    readonly Dictionary<ulong, HarvestNode> _nodes = new();
    readonly Dictionary<ulong, LogRecord> _logs = new();
    ulong _nextLogId = 1;
    int _seed;
    bool _loaded;

    public ScatterHarvestStore(ILogger log = null) => _log = log;

    public void Configure(int seed)
    {
        if (_loaded && seed == _seed) return;
        _seed = seed;
        _loaded = false;
        _nodes.Clear();
        _logs.Clear();
        _nextLogId = 1;
        EnsureLoaded();
    }

    // --- stumps (gate the standing-tree draw) ---

    public bool Contains(ulong id) => _nodes.ContainsKey(id);
    public int Count => _nodes.Count;

    public bool RecordStump(ulong id, Vector3 position, int protoIndex)
    {
        EnsureLoaded();
        if (_nodes.ContainsKey(id)) return false;
        _nodes[id] = new HarvestNode { Id = id, Position = position, ProtoIndex = protoIndex, State = HarvestState.Stump };
        Save();
        return true;
    }

    public bool RecordDug(ulong id)
    {
        EnsureLoaded();
        if (!_nodes.TryGetValue(id, out HarvestNode n) || n.State == HarvestState.Dug) return false;
        n.State = HarvestState.Dug;
        _nodes[id] = n;
        Save();
        return true;
    }

    public void CollectStumps(List<HarvestNode> into)
    {
        into.Clear();
        foreach (KeyValuePair<ulong, HarvestNode> kv in _nodes)
            if (kv.Value.State == HarvestState.Stump) into.Add(kv.Value);
    }

    // --- fallen logs (placed objects, harvestable for wood) ---

    // Record a fallen log at its settled transform. Returns the new log id.
    public ulong RecordLog(Vector3 position, Quaternion rotation, int protoIndex)
    {
        EnsureLoaded();
        ulong id = _nextLogId++;
        _logs[id] = new LogRecord { Id = id, Position = position, Rotation = rotation, ProtoIndex = protoIndex };
        Save();
        return id;
    }

    public bool RemoveLog(ulong logId)
    {
        EnsureLoaded();
        if (!_logs.Remove(logId)) return false;
        Save();
        return true;
    }

    public void CollectLogs(List<LogRecord> into)
    {
        into.Clear();
        foreach (KeyValuePair<ulong, LogRecord> kv in _logs) into.Add(kv.Value);
    }

    // --- persistence ---

    [Serializable] struct NodeSave { public string id; public Vector3 position; public int proto; public int state; }
    [Serializable] struct LogSave { public string id; public Vector3 position; public Quaternion rotation; public int proto; }
    [Serializable] struct SaveData { public int version; public int planetSeed; public ulong nextLogId; public NodeSave[] nodes; public LogSave[] logs; }

    static string FilePath(int seed) =>
        Path.Combine(Application.persistentDataPath, "ProceduralPlanets", $"scatter-harvest-{seed}.json");

    void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        _nodes.Clear();
        _logs.Clear();
        _nextLogId = 1;
        string path = FilePath(_seed);
        if (!File.Exists(path)) return;
        try
        {
            var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
            if (data.version != SaveVersion || data.planetSeed != _seed) return;
            _nextLogId = data.nextLogId < 1 ? 1 : data.nextLogId;
            if (data.nodes != null)
                foreach (NodeSave ns in data.nodes)
                    if (ulong.TryParse(ns.id, out ulong id))
                        _nodes[id] = new HarvestNode { Id = id, Position = ns.position, ProtoIndex = ns.proto, State = (HarvestState)ns.state };
            if (data.logs != null)
                foreach (LogSave ls in data.logs)
                    if (ulong.TryParse(ls.id, out ulong id))
                        _logs[id] = new LogRecord { Id = id, Position = ls.position, Rotation = ls.rotation, ProtoIndex = ls.proto };
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
                nodes[i++] = new NodeSave { id = kv.Key.ToString(), position = kv.Value.Position, proto = kv.Value.ProtoIndex, state = (int)kv.Value.State };
            var logs = new LogSave[_logs.Count];
            i = 0;
            foreach (KeyValuePair<ulong, LogRecord> kv in _logs)
                logs[i++] = new LogSave { id = kv.Key.ToString(), position = kv.Value.Position, rotation = kv.Value.Rotation, proto = kv.Value.ProtoIndex };
            File.WriteAllText(path, JsonUtility.ToJson(new SaveData { version = SaveVersion, planetSeed = _seed, nextLogId = _nextLogId, nodes = nodes, logs = logs }));
        }
        catch (Exception e)
        {
            _log?.Log(LogLevel.Warning, "ScatterHarvest", $"Failed to save {path}: {e.Message}");
        }
    }
}

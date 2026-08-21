using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Persistent OVERRIDE + placed-object layer for harvested scatter. Scatter itself is not a stored object list
// - it is derived from the seed - so a chop is persisted as an exception layered on the base.
//
// Two record kinds:
//  - STUMP nodes, keyed by the felled tree's ScatterId. ScatterTileCache consults Contains(id) in Commit, so a
//    harvested tree never re-enters the draw or a reload. A node is a small state machine: Stump -> Dug.
//  - LOG records, keyed by an EntityId (the fallen top, a genuinely placed object with a settled rotation).
//    These do NOT gate Commit; a LogRenderer draws them and they are removed when chopped for wood.
//
// This is a view over the world delta log, not a store in its own right. Mutations append one record; they no
// longer rewrite a whole JSON file per chop, and the same records are what a joining client will replay.
public sealed class ScatterHarvestStore
{
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
    readonly EntityIdAllocator _entityIds = new();

    IWorldDeltaLog _delta;
    int _seed;
    bool _loaded;

    public ScatterHarvestStore(ILogger log = null) => _log = log;

    /// <summary>Points the store at a world's delta log and rebuilds its view from the records already there.</summary>
    public void Configure(int seed, IWorldDeltaLog deltaLog)
    {
        if (_loaded && seed == _seed && ReferenceEquals(deltaLog, _delta)) return;
        _seed = seed;
        _delta = deltaLog;
        _loaded = true;
        Rebuild();
        ImportLegacySave();
    }

    // --- stumps (gate the standing-tree draw) ---

    public bool Contains(ulong id) => _nodes.ContainsKey(id);
    public int Count => _nodes.Count;

    public bool RecordStump(ulong id, Vector3 position, int protoIndex)
    {
        if (_nodes.ContainsKey(id)) return false;
        Apply(new WorldDelta(0, DeltaKind.ScatterState, id, position, Quaternion.identity, protoIndex,
            (byte)HarvestState.Stump));
        return true;
    }

    public bool RecordDug(ulong id)
    {
        if (!_nodes.TryGetValue(id, out HarvestNode n) || n.State == HarvestState.Dug) return false;
        Apply(new WorldDelta(0, DeltaKind.ScatterState, id, n.Position, Quaternion.identity, n.ProtoIndex,
            (byte)HarvestState.Dug));
        return true;
    }

    public void CollectStumps(List<HarvestNode> into)
    {
        into.Clear();
        foreach (KeyValuePair<ulong, HarvestNode> kv in _nodes)
            if (kv.Value.State == HarvestState.Stump) into.Add(kv.Value);
    }

    // --- fallen logs (placed objects, harvestable for wood) ---

    /// <summary>Records a fallen log at its settled transform. Returns the new entity id.</summary>
    public ulong RecordLog(Vector3 position, Quaternion rotation, int protoIndex)
    {
        EntityId id = _entityIds.Next();
        Apply(new WorldDelta(0, DeltaKind.EntitySpawned, id.Value, position, rotation, protoIndex));
        return id.Value;
    }

    public bool RemoveLog(ulong logId)
    {
        if (!_logs.ContainsKey(logId)) return false;
        Apply(new WorldDelta(0, DeltaKind.EntityRemoved, logId));
        return true;
    }

    public void CollectLogs(List<LogRecord> into)
    {
        into.Clear();
        foreach (KeyValuePair<ulong, LogRecord> kv in _logs) into.Add(kv.Value);
    }

    // --- delta log view ---

    // One append, then the same fold the replay uses. Going through Ingest rather than mutating the
    // dictionaries directly is what keeps a live world and a reloaded one from drifting apart.
    void Apply(in WorldDelta delta)
    {
        if (_delta == null)
        {
            _log?.Log(LogLevel.Warning, "ScatterHarvest", "No delta log configured; the change will not persist.");
            Ingest(delta);
            return;
        }
        Ingest(_delta.Append(delta));
    }

    void Rebuild()
    {
        _nodes.Clear();
        _logs.Clear();
        _entityIds.Reset();
        if (_delta == null) return;
        foreach (WorldDelta d in _delta.Snapshot()) Ingest(d);
    }

    void Ingest(in WorldDelta d)
    {
        switch (d.Kind)
        {
            case DeltaKind.ScatterState:
                _nodes[d.Key] = new HarvestNode
                {
                    Id = d.Key,
                    Position = d.Position,
                    ProtoIndex = d.TypeIndex,
                    State = (HarvestState)d.State,
                };
                break;

            case DeltaKind.EntitySpawned:
                _entityIds.Observe(new EntityId(d.Key));
                _logs[d.Key] = new LogRecord
                {
                    Id = d.Key,
                    Position = d.Position,
                    Rotation = d.Rotation,
                    ProtoIndex = d.TypeIndex,
                };
                break;

            case DeltaKind.EntityRemoved:
                // The record stays in the log as a tombstone so a client replaying it also removes the log;
                // only this view drops it.
                _entityIds.Observe(new EntityId(d.Key));
                _logs.Remove(d.Key);
                break;
        }
    }

    // --- migration off the old per-seed JSON ---

    static string LegacyPath(int seed) =>
        Path.Combine(Application.persistentDataPath, "ProceduralPlanets", $"scatter-harvest-{seed}.json");

    // Reads a v3 JSON save once and appends it to the delta log, then renames the file aside so the import
    // cannot repeat. Renaming rather than deleting keeps the old save recoverable, and the guard matters:
    // re-importing after a compaction would resurrect logs the player has already chopped.
    void ImportLegacySave()
    {
        if (_delta == null) return;
        string path = LegacyPath(_seed);
        if (!File.Exists(path)) return;

        try
        {
            var data = JsonUtility.FromJson<LegacySaveData>(File.ReadAllText(path));
            if (data.version == LegacySaveVersion && data.planetSeed == _seed)
            {
                int imported = 0;
                if (data.nodes != null)
                    foreach (LegacyNode ns in data.nodes)
                        if (ulong.TryParse(ns.id, out ulong id) && !_nodes.ContainsKey(id))
                        {
                            Apply(new WorldDelta(0, DeltaKind.ScatterState, id, ns.position, Quaternion.identity,
                                ns.proto, (byte)ns.state));
                            imported++;
                        }
                if (data.logs != null)
                    foreach (LegacyLog ls in data.logs)
                    {
                        Apply(new WorldDelta(0, DeltaKind.EntitySpawned, _entityIds.Next().Value,
                            ls.position, ls.rotation, ls.proto));
                        imported++;
                    }
                if (imported > 0)
                    _log?.Log(LogLevel.Info, "ScatterHarvest",
                        $"Imported {imported} record(s) from the pre-delta-log save.");
            }
            else
            {
                _log?.Log(LogLevel.Info, "ScatterHarvest",
                    $"Ignoring {Path.GetFileName(path)}: version {data.version} seed {data.planetSeed}.");
            }

            string aside = path + ".migrated";
            if (File.Exists(aside)) File.Delete(aside);
            File.Move(path, aside);
        }
        catch (Exception e)
        {
            _log?.Log(LogLevel.Warning, "ScatterHarvest", $"Failed to import {path}: {e.Message}");
        }
    }

    const int LegacySaveVersion = 3;   // v1 bare ids; v2 stump state machine; v3 fallen-log records

    [Serializable] struct LegacyNode { public string id; public Vector3 position; public int proto; public int state; }
    [Serializable] struct LegacyLog { public string id; public Vector3 position; public Quaternion rotation; public int proto; }
    [Serializable] struct LegacySaveData { public int version; public int planetSeed; public ulong nextLogId; public LegacyNode[] nodes; public LegacyLog[] logs; }
}

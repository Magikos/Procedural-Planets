using System.Collections.Generic;
using UnityEngine;

// Per-prototype packed draw buckets (matrices + parallel world positions) that the renderer iterates by
// index. Instances are keyed by their owning tile so a tile that leaves range is removed in O(its own
// instances) via swap-remove, instead of the whole bucket being rebuilt O(all live instances) on every
// eviction — that rebuild dominated frame time once the parallel gather kept the world full.
//
// The renderer treats each bucket as an unordered set (it distance-bands every instance every frame and
// reads matrices[i]/positions[i] at the same index), so swap-remove — which reorders — is safe: the only
// invariant is that matrices[i] and positions[i] stay the same instance, which the swap preserves.
public sealed class ScatterDrawBuckets
{
    readonly int _protoCount;
    readonly List<Matrix4x4>[] _matrices;
    readonly List<Vector3>[] _positions;
    readonly List<ulong>[] _ids;        // parallel to _matrices[p]: ScatterId of each packed slot (harvest key)
    readonly List<long>[] _ownerTile;   // parallel to _matrices[p]: tile owning each packed slot
    readonly List<int>[] _ownerSlot;    // parallel: index of this slot within its tile's per-proto index list
    // parallel: Time.timeSinceLevelLoad when the slot became resident, so the shader can ramp a newly
    // gathered instance in instead of it appearing at full opacity. Flying at 60 m/s the tile plan runs a
    // permanent backlog and thousands of instances land inside view every second; without this they pop.
    readonly List<float>[] _born;
    // tileId -> per-prototype list of packed indices this tile occupies (its slots in _matrices[proto]).
    readonly Dictionary<long, List<int>[]> _tileIdx = new();
    // Per-prototype: this frame the packed matrix list changed (add/remove). The GPU draw re-uploads +
    // re-inverts only dirty prototypes; a static camera (no gather churn) uploads nothing.
    readonly bool[] _dirty;

    public ScatterDrawBuckets(int protoCount)
    {
        _protoCount = protoCount;
        _matrices = new List<Matrix4x4>[protoCount];
        _positions = new List<Vector3>[protoCount];
        _ids = new List<ulong>[protoCount];
        _ownerTile = new List<long>[protoCount];
        _ownerSlot = new List<int>[protoCount];
        _born = new List<float>[protoCount];
        _dirty = new bool[protoCount];
        for (int p = 0; p < protoCount; p++)
        {
            _matrices[p] = new List<Matrix4x4>();
            _positions[p] = new List<Vector3>();
            _ids[p] = new List<ulong>();
            _ownerTile[p] = new List<long>();
            _ownerSlot[p] = new List<int>();
            _born[p] = new List<float>();
        }
    }

    // Returns whether prototype `proto`'s matrix list changed since the last call, clearing the flag.
    public bool ConsumeDirty(int proto)
    {
        bool d = _dirty[proto];
        _dirty[proto] = false;
        return d;
    }

    // Concrete List, not IReadOnlyList: GraphicsBuffer.SetData takes a List<T> directly, so the GPU upload
    // path avoids copying every instance through an interface indexer into a staging array.
    public List<Matrix4x4> Matrices(int proto) => _matrices[proto];
    public IReadOnlyList<Vector3> Positions(int proto) => _positions[proto];
    public IReadOnlyList<ulong> Ids(int proto) => _ids[proto];
    public List<float> Born(int proto) => _born[proto];

    public int InstanceCount
    {
        get { int n = 0; for (int p = 0; p < _protoCount; p++) n += _matrices[p].Count; return n; }
    }

    public void Add(long tileId, int proto, Matrix4x4 matrix, Vector3 position, ulong id)
        => Add(tileId, proto, matrix, position, id, Time.timeSinceLevelLoad);

    // bornOverride preserves an existing instance's arrival time. RemoveInstanceById rebuilds a whole tile
    // to drop one harvested prop; without it every surviving prop in that tile would re-fade as if new.
    public void Add(long tileId, int proto, Matrix4x4 matrix, Vector3 position, ulong id, float bornOverride)
    {
        if (!_tileIdx.TryGetValue(tileId, out var perProto))
        {
            perProto = new List<int>[_protoCount];
            _tileIdx[tileId] = perProto;
        }
        var idx = perProto[proto] ??= new List<int>();
        int packed = _matrices[proto].Count;
        _matrices[proto].Add(matrix);
        _positions[proto].Add(position);
        _ids[proto].Add(id);
        _ownerTile[proto].Add(tileId);
        _ownerSlot[proto].Add(idx.Count);
        _born[proto].Add(bornOverride);
        idx.Add(packed);
        _dirty[proto] = true;
    }

    // Remove a single instance by its ScatterId (harvested this frame). Rare user action, so it runs in
    // O(the owning tile's instances): snapshot that tile, drop it via the proven RemoveTile, then re-add
    // every instance except the harvested one — reusing the Add/RemoveTile invariants rather than
    // hand-rolled single-slot surgery on the tile-index bookkeeping. Returns false if the id is not resident.
    public bool RemoveInstanceById(int proto, ulong id)
    {
        if ((uint)proto >= (uint)_protoCount) return false;
        int packed = _ids[proto].IndexOf(id);
        if (packed < 0) return false;
        long tile = _ownerTile[proto][packed];
        if (!_tileIdx.TryGetValue(tile, out var perProto)) return false;

        var survivors = new List<(int proto, Matrix4x4 matrix, Vector3 position, ulong id, float born)>();
        for (int p = 0; p < _protoCount; p++)
        {
            var slots = perProto[p];
            if (slots == null) continue;
            for (int k = 0; k < slots.Count; k++)
            {
                int packedIdx = slots[k];
                ulong sid = _ids[p][packedIdx];
                if (p == proto && sid == id) continue; // the harvested instance
                survivors.Add((p, _matrices[p][packedIdx], _positions[p][packedIdx], sid, _born[p][packedIdx]));
            }
        }

        RemoveTile(tile);
        foreach (var s in survivors) Add(tile, s.proto, s.matrix, s.position, s.id, s.born);
        return true;
    }

    public void RemoveTile(long tileId)
    {
        if (!_tileIdx.TryGetValue(tileId, out var perProto)) return;
        for (int p = 0; p < _protoCount; p++)
        {
            var idx = perProto[p];
            if (idx != null && idx.Count > 0) { RemoveBlock(p, idx); _dirty[p] = true; }
        }
        _tileIdx.Remove(tileId);
    }

    // Swap-remove every slot in `idx` from prototype p's packed lists. Each removal moves the current last
    // slot into the hole and repoints the moved slot's owning tile at its new index; when the moved slot
    // belongs to the tile being removed, it stays in `idx` (updated) and is drained on a later iteration.
    void RemoveBlock(int p, List<int> idx)
    {
        var mats = _matrices[p]; var poss = _positions[p]; var ids = _ids[p];
        var owner = _ownerTile[p]; var slot = _ownerSlot[p]; var born = _born[p];
        while (idx.Count > 0)
        {
            int packed = idx[idx.Count - 1];
            idx.RemoveAt(idx.Count - 1);
            int last = mats.Count - 1;
            if (packed != last)
            {
                mats[packed] = mats[last];
                poss[packed] = poss[last];
                ids[packed] = ids[last];
                born[packed] = born[last];
                long moverTile = owner[last];
                int moverSlot = slot[last];
                owner[packed] = moverTile;
                slot[packed] = moverSlot;
                _tileIdx[moverTile][p][moverSlot] = packed;
            }
            mats.RemoveAt(last);
            poss.RemoveAt(last);
            ids.RemoveAt(last);
            owner.RemoveAt(last);
            slot.RemoveAt(last);
            born.RemoveAt(last);
        }
    }

    public void Clear()
    {
        for (int p = 0; p < _protoCount; p++)
        {
            _matrices[p].Clear();
            _positions[p].Clear();
            _ids[p].Clear();
            _ownerTile[p].Clear();
            _ownerSlot[p].Clear();
            _born[p].Clear();
        }
        _tileIdx.Clear();
    }
}

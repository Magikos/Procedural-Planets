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
    readonly List<long>[] _ownerTile;   // parallel to _matrices[p]: tile owning each packed slot
    readonly List<int>[] _ownerSlot;    // parallel: index of this slot within its tile's per-proto index list
    // tileId -> per-prototype list of packed indices this tile occupies (its slots in _matrices[proto]).
    readonly Dictionary<long, List<int>[]> _tileIdx = new();

    public ScatterDrawBuckets(int protoCount)
    {
        _protoCount = protoCount;
        _matrices = new List<Matrix4x4>[protoCount];
        _positions = new List<Vector3>[protoCount];
        _ownerTile = new List<long>[protoCount];
        _ownerSlot = new List<int>[protoCount];
        for (int p = 0; p < protoCount; p++)
        {
            _matrices[p] = new List<Matrix4x4>();
            _positions[p] = new List<Vector3>();
            _ownerTile[p] = new List<long>();
            _ownerSlot[p] = new List<int>();
        }
    }

    public IReadOnlyList<Matrix4x4> Matrices(int proto) => _matrices[proto];
    public IReadOnlyList<Vector3> Positions(int proto) => _positions[proto];

    public int InstanceCount
    {
        get { int n = 0; for (int p = 0; p < _protoCount; p++) n += _matrices[p].Count; return n; }
    }

    public void Add(long tileId, int proto, Matrix4x4 matrix, Vector3 position)
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
        _ownerTile[proto].Add(tileId);
        _ownerSlot[proto].Add(idx.Count);
        idx.Add(packed);
    }

    public void RemoveTile(long tileId)
    {
        if (!_tileIdx.TryGetValue(tileId, out var perProto)) return;
        for (int p = 0; p < _protoCount; p++)
        {
            var idx = perProto[p];
            if (idx != null) RemoveBlock(p, idx);
        }
        _tileIdx.Remove(tileId);
    }

    // Swap-remove every slot in `idx` from prototype p's packed lists. Each removal moves the current last
    // slot into the hole and repoints the moved slot's owning tile at its new index; when the moved slot
    // belongs to the tile being removed, it stays in `idx` (updated) and is drained on a later iteration.
    void RemoveBlock(int p, List<int> idx)
    {
        var mats = _matrices[p]; var poss = _positions[p];
        var owner = _ownerTile[p]; var slot = _ownerSlot[p];
        while (idx.Count > 0)
        {
            int packed = idx[idx.Count - 1];
            idx.RemoveAt(idx.Count - 1);
            int last = mats.Count - 1;
            if (packed != last)
            {
                mats[packed] = mats[last];
                poss[packed] = poss[last];
                long moverTile = owner[last];
                int moverSlot = slot[last];
                owner[packed] = moverTile;
                slot[packed] = moverSlot;
                _tileIdx[moverTile][p][moverSlot] = packed;
            }
            mats.RemoveAt(last);
            poss.RemoveAt(last);
            owner.RemoveAt(last);
            slot.RemoveAt(last);
        }
    }

    public void Clear()
    {
        for (int p = 0; p < _protoCount; p++)
        {
            _matrices[p].Clear();
            _positions[p].Clear();
            _ownerTile[p].Clear();
            _ownerSlot[p].Clear();
        }
        _tileIdx.Clear();
    }
}

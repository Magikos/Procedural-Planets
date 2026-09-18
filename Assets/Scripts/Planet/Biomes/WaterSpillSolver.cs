// Priority-flood depression fill (Barnes et al.). For every cell it answers "if water accumulated here
// until it could escape, what height would the surface sit at?" - which is the level a lake sits at, since
// a lake with an outflow is by definition brim-full to its lowest rim saddle.
//
// The algorithm: seed a min-heap with the cells whose level is already known (the ocean), then repeatedly
// pop the LOWEST unresolved cell and relax its neighbours with filled = max(ownElevation, poppedFilled).
// Popping lowest-first is what makes it correct: the first path that reaches a basin is necessarily the one
// crossing its lowest rim, so the value that lands inside is the spill height.
//
// Nested basins and merged spillways need no special handling - a basin inside a basin is simply reached
// after its parent fills, and two basins that overflow into each other resolve to the same level because
// whichever saddle is lower is popped first.
//
// Cells that drain to the ocean end with filled == their own elevation, so `filled > elevation` is exactly
// the submerged set and the difference is the water depth.
using System.Threading;

public static class WaterSpillSolver
{
    // elevation, neighbors (4 per cell, -1 for none) and seeds are all indexed by global cell id.
    // Returns the filled surface height per cell, in the same elevation units as the input.
    public static float[] Solve(float[] elevation, int[] neighbors, bool[] seeds, float seedLevel, CancellationToken ct = default)
        => SolveDrainage(elevation, neighbors, seeds, seedLevel, ct).Filled;

    public sealed class Drainage
    {
        public float[] Filled;
        public float[] Elevation;
        public int[] Receiver;
        public int[] Order;
    }

    public static Drainage SolveDrainage(float[] elevation, int[] neighbors, bool[] seeds, float seedLevel, CancellationToken ct = default)
    {
        if (elevation == null || neighbors == null || seeds == null || neighbors.Length != elevation.Length * 4 || seeds.Length != elevation.Length)
            throw new System.ArgumentException("Drainage arrays must describe the same four-neighbor grid.");
        if (!float.IsFinite(seedLevel)) throw new System.ArgumentOutOfRangeException(nameof(seedLevel));
        ct.ThrowIfCancellationRequested();
        int count = elevation.Length;
        var filled = new float[count];
        var receiver = new int[count];
        var settled = new bool[count];
        var order = new System.Collections.Generic.List<int>(count);
        for (int i = 0; i < count; i++)
        {
            if (!float.IsFinite(elevation[i])) throw new System.ArgumentException("Terrain elevations must be finite.");
            filled[i] = float.MaxValue;
            receiver[i] = -1;
        }

        var heap = new MinHeap(count / 4 + 16);
        for (int i = 0; i < count; i++)
        {
            if ((i & 255) == 0) ct.ThrowIfCancellationRequested();
            if (!seeds[i]) continue;
            filled[i] = seedLevel;
            heap.Push(i, seedLevel);
        }

        while (heap.TryPop(out int cell, out float cellLevel))
        {
            ct.ThrowIfCancellationRequested();
            // A cell can be pushed more than once; the stale entries have a higher key than the value that
            // finally stuck, so skipping them here is what keeps the heap from re-expanding settled regions.
            if (cellLevel > filled[cell] || settled[cell]) continue;
            settled[cell] = true;
            order.Add(cell);

            int baseIndex = cell * 4;
            for (int n = 0; n < 4; n++)
            {
                int next = neighbors[baseIndex + n];
                if (next < 0) continue;
                if (next >= count) throw new System.ArgumentException("Drainage neighbor is outside the grid.");
                if (settled[next] || seeds[next]) continue;

                float candidate = elevation[next] > cellLevel ? elevation[next] : cellLevel;
                if (candidate >= filled[next]) continue;

                filled[next] = candidate;
                receiver[next] = cell;
                heap.Push(next, candidate);
            }
        }

        return new Drainage { Filled = filled, Elevation = (float[])elevation.Clone(), Receiver = receiver, Order = order.ToArray() };
    }

    // Binary min-heap keyed by float. Unity's runtime profile predates System.Collections.Generic
    // PriorityQueue, and this needs no removal or reprioritise - stale entries are filtered on pop.
    sealed class MinHeap
    {
        int[] _items;
        float[] _keys;
        int _count;

        public MinHeap(int capacity)
        {
            capacity = capacity < 16 ? 16 : capacity;
            _items = new int[capacity];
            _keys = new float[capacity];
        }

        public void Push(int item, float key)
        {
            if (_count == _items.Length) Grow();
            int i = _count++;
            _items[i] = item;
            _keys[i] = key;
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (!Less(i, parent)) break;
                Swap(parent, i);
                i = parent;
            }
        }

        public bool TryPop(out int item, out float key)
        {
            if (_count == 0) { item = -1; key = 0f; return false; }

            item = _items[0];
            key = _keys[0];
            _count--;
            if (_count > 0)
            {
                _items[0] = _items[_count];
                _keys[0] = _keys[_count];
                int i = 0;
                while (true)
                {
                    int left = i * 2 + 1;
                    if (left >= _count) break;
                    int smallest = left;
                    int right = left + 1;
                    if (right < _count && Less(right, left)) smallest = right;
                    if (!Less(smallest, i)) break;
                    Swap(i, smallest);
                    i = smallest;
                }
            }
            return true;
        }

        void Grow()
        {
            var newItems = new int[_items.Length * 2];
            var newKeys = new float[_keys.Length * 2];
            System.Array.Copy(_items, newItems, _count);
            System.Array.Copy(_keys, newKeys, _count);
            _items = newItems;
            _keys = newKeys;
        }

        bool Less(int a, int b) => _keys[a] < _keys[b] || (_keys[a] == _keys[b] && _items[a] < _items[b]);

        void Swap(int a, int b)
        {
            (_items[a], _items[b]) = (_items[b], _items[a]);
            (_keys[a], _keys[b]) = (_keys[b], _keys[a]);
        }
    }
}

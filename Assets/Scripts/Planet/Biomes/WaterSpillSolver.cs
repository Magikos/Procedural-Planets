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
public static class WaterSpillSolver
{
    // elevation, neighbors (4 per cell, -1 for none) and seeds are all indexed by global cell id.
    // Returns the filled surface height per cell, in the same elevation units as the input.
    public static float[] Solve(float[] elevation, int[] neighbors, bool[] seeds, float seedLevel)
    {
        int count = elevation.Length;
        var filled = new float[count];
        for (int i = 0; i < count; i++) filled[i] = float.MaxValue;

        var heap = new MinHeap(count / 4 + 16);
        for (int i = 0; i < count; i++)
        {
            if (!seeds[i]) continue;
            filled[i] = seedLevel;
            heap.Push(i, seedLevel);
        }

        while (heap.TryPop(out int cell, out float cellLevel))
        {
            // A cell can be pushed more than once; the stale entries have a higher key than the value that
            // finally stuck, so skipping them here is what keeps the heap from re-expanding settled regions.
            if (cellLevel > filled[cell]) continue;

            int baseIndex = cell * 4;
            for (int n = 0; n < 4; n++)
            {
                int next = neighbors[baseIndex + n];
                if (next < 0) continue;

                float candidate = elevation[next] > cellLevel ? elevation[next] : cellLevel;
                if (candidate >= filled[next]) continue;

                filled[next] = candidate;
                heap.Push(next, candidate);
            }
        }

        return filled;
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
                if (_keys[parent] <= _keys[i]) break;
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
                    if (right < _count && _keys[right] < _keys[left]) smallest = right;
                    if (_keys[i] <= _keys[smallest]) break;
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

        void Swap(int a, int b)
        {
            (_items[a], _items[b]) = (_items[b], _items[a]);
            (_keys[a], _keys[b]) = (_keys[b], _keys[a]);
        }
    }
}

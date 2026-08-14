using System.Collections.Generic;

// Minimal item-count inventory for the harvesting POC: no stacking rules, no UI, no persistence — just counts
// keyed by item id. Registered app-scope so it survives world regeneration. A real inventory (stacks, weight,
// slots, save/load) replaces this later; the harvest verb only needs Add.
public sealed class InventoryService
{
    readonly Dictionary<string, int> _items = new();

    public void Add(string itemId, int count = 1)
    {
        if (string.IsNullOrEmpty(itemId) || count <= 0) return;
        _items.TryGetValue(itemId, out int c);
        _items[itemId] = c + count;
    }

    public int Count(string itemId) => _items.TryGetValue(itemId, out int c) ? c : 0;
    public int DistinctItems => _items.Count;
}

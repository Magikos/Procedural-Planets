using System.Collections.Generic;

// Minimal item-count inventory for the harvesting POC: no stacking rules, no UI, no persistence — just counts
// keyed by item id. A real inventory (stacks, weight, slots, save/load) replaces this later; the harvest verb
// only needs Add.
//
// planned: read side (Count, DistinctItems) is for the inventory UI and crafting costs,
// docs/design/2026-08-20-magikos-game-architecture.md section 11.4.
//
// Owned by a private field on Planet, NOT registered in the service locator. An earlier comment here claimed
// app scope, which was never true; the claim is removed rather than made true because item counts belong to
// a player, and a listen server holds up to eight of them.
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

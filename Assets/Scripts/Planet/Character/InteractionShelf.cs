using UnityEngine;

/// <summary>Ordered item destinations. A slot becomes occupied only after placement completes.</summary>
public sealed class InteractionShelf : MonoBehaviour
{
    public Transform[] Slots = System.Array.Empty<Transform>();
    Transform[] _items = System.Array.Empty<Transform>();

    public int NextSlot()
    {
        if (_items.Length != Slots.Length) _items = new Transform[Slots.Length];
        for (int i = 0; i < Slots.Length; i++)
            if (Slots[i] != null && _items[i] == null) return i;
        return -1;
    }

    public bool Available(int slot) => slot >= 0 && slot < Slots.Length && Slots[slot] != null &&
        (_items.Length != Slots.Length || _items[slot] == null);

    public bool Place(int slot, Transform item)
    {
        if (item == null || !Available(slot)) return false;
        if (_items.Length != Slots.Length) _items = new Transform[Slots.Length];
        _items[slot] = item;
        return true;
    }

    public void ResetSlots() => _items = new Transform[Slots.Length];
}

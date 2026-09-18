using System;
using UnityEngine;

[Serializable]
public struct ItemQuantity
{
    public string Item;
    [Min(1)] public int Count;
    public ItemQuantity(string item, int count) { Item = item; Count = count; }
}

/// <summary>Editable inventory exchange. Animation and station state remain with their existing owners.</summary>
[CreateAssetMenu(menuName = "Magikos/Crafting Recipe")]
public sealed class CraftingRecipe : ScriptableObject
{
    public ItemQuantity[] Costs = Array.Empty<ItemQuantity>();
    public ItemQuantity[] Results = Array.Empty<ItemQuantity>();
    public bool CanMake(InventoryService inventory) => inventory != null && inventory.TryExchange(Costs, Results, false);
    public bool TryMake(InventoryService inventory) => inventory != null && inventory.TryExchange(Costs, Results);
}

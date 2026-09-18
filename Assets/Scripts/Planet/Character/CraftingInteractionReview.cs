using UnityEngine;

/// <summary>Supplies a local inventory for recipe review. Production callers inject the player's inventory.</summary>
public sealed class CraftingInteractionReview : MonoBehaviour
{
    public HumanInteractionReview Review;
    public ItemQuantity[] StartingItems;
    public CraftingRecipe[] Recipes;

    public void ResetStock()
    {
        Review.Cancel();
        Review.Inventory = new InventoryService();
        foreach (var item in StartingItems) Review.Inventory.Add(item.Item, item.Count);
    }

    void Start() => ResetStock();

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(Screen.width - 310, 10, 300, 230), GUI.skin.box);
        GUILayout.Label("Recipe inventory (review only)");
        foreach (var item in StartingItems) GUILayout.Label(item.Item + ": " + Review.Inventory.Count(item.Item));
        foreach (var recipe in Recipes)
            foreach (var item in recipe.Results) GUILayout.Label(item.Item + ": " + Review.Inventory.Count(item.Item));
        if (GUILayout.Button("Reset ingredients")) ResetStock();
        GUILayout.EndArea();
    }
}

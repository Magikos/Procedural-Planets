using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Connects real rendered foliage to the shared food stock and regrowth authority.</summary>
public static class CreatureForageAuthor
{
    const string Folder = "Assets/Resources/Settings/Scatter/";
    const string LibraryPath = "Assets/Resources/Settings/ScatterLibrary.asset";

    [MenuItem("Tools/Creatures/Enable Habitat Forage")]
    static void BuildMenu() => Build();

    public static string Build()
    {
        var library = AssetDatabase.LoadAssetAtPath<ScatterLibrary>(LibraryPath)
            ?? throw new InvalidOperationException("Missing scatter library.");
        var prototypes = library.Prototypes.ToList();
        foreach (var entry in new[]
        {
            ("Grassland Grass Prototype", .35f), ("Steppe Grass Prototype", .35f), ("Savanna Grass Prototype", .35f),
            ("TEM Forest Grass", .35f), ("TEM Swamp Grass", .35f), ("IceBog Reeds Prototype", .5f),
            ("Swamp Reeds Prototype", .5f), ("Lake Cattails", .5f), ("Lake Reeds", .5f), ("Lake Wildflowers", .25f),
            ("Forest Wildflowers Prototype", .25f), ("Grassland Wildflowers Prototype", .25f),
            ("LMHPOLY Forest Flower White", .25f), ("LMHPOLY Taiga Flower Blue", .25f),
            ("LMHPOLY Steppe Flower White", .25f), ("LMHPOLY Steppe Flower Purple", .25f),
            ("LMHPOLY Savanna Flower Yellow", .25f), ("LMHPOLY Tropical Flower Pink", .25f),
        })
        {
            var prototype = AssetDatabase.LoadAssetAtPath<ScatterPrototype>(Folder + entry.Item1 + ".asset")
                ?? throw new InvalidOperationException("Missing forage prototype: " + entry.Item1);
            if (!prototypes.Contains(prototype)) throw new InvalidOperationException("Forage prototype is not in the active library: " + entry.Item1);
            if (prototype.FoodUnits > 0f) continue;
            prototype.FoodUnits = entry.Item2; prototype.FoodRegrowSeconds = 1800f;
            EditorUtility.SetDirty(prototype);
        }

        var source = AssetDatabase.LoadAssetAtPath<ScatterPrototype>(Folder + "Steppe Grass Prototype.asset");
        var used = new HashSet<int>();
        foreach (string guid in AssetDatabase.FindAssets("t:ScatterPrototype"))
        {
            var item = AssetDatabase.LoadAssetAtPath<ScatterPrototype>(AssetDatabase.GUIDToAssetPath(guid));
            if (item != null) used.Add(item.SlotId);
        }
        AddGrass("Mountain Forage Grass", BiomeType.Mountain, 5f, .65f, 55f, .35f);
        AddGrass("Tundra Forage Grass", BiomeType.Tundra, 6f, .5f, 38f, .3f);
        AddGrass("Desert Forage Grass", BiomeType.Desert, 8f, .3f, 28f, .2f);
        library.Prototypes = prototypes.ToArray();
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();
        return Audit();

        void AddGrass(string name, BiomeType biome, float spacing, float weight, float slope, float food)
        {
            string path = Folder + name + ".asset";
            var item = AssetDatabase.LoadAssetAtPath<ScatterPrototype>(path);
            if (item == null)
            {
                // Append beyond every authored slot, including inactive prototypes that may have saved identities.
                int slot = used.Count == 0 ? 0 : used.Max() + 1;
                if (slot > ScatterId.MaxSlot) throw new InvalidOperationException("No unused scatter slot remains for " + name);
                item = UnityEngine.Object.Instantiate(source);
                item.name = item.DisplayName = name; item.SlotId = slot;
                item.Biome = biome; item.SpacingMeters = spacing; item.Weight = weight;
                item.MaxSlopeDegrees = slope; item.FoodUnits = food; item.FoodRegrowSeconds = 1800f;
                item.ScaleRange = biome == BiomeType.Desert ? new Vector2(.55f, .9f) : new Vector2(.65f, 1.1f);
                item.HasMinAltitude = false; item.HasMaxAltitude = false;
                AssetDatabase.CreateAsset(item, path);
                used.Add(slot);
            }
            if (!prototypes.Contains(item)) prototypes.Add(item);
        }
    }

    public static string Audit()
    {
        var library = AssetDatabase.LoadAssetAtPath<ScatterLibrary>(LibraryPath);
        var text = new StringBuilder("Forage authoring coverage (terrain placement and reachability still require world sampling):");
        foreach (BiomeType biome in Enum.GetValues(typeof(BiomeType)))
        {
            var food = library.Prototypes.Where(p => p != null && p.Biome == biome && p.FoodUnits > 0f && p.Weight > 0f).ToArray();
            if (food.Length == 0) continue;
            text.Append("\n").Append(biome).Append(": ").Append(food.Length).Append(" real foliage prototypes");
            foreach (var item in food)
                text.Append($"\n  {item.DisplayName} slot={item.SlotId}, food={item.FoodUnits:F2}, regrow={item.FoodRegrowSeconds:F0}s, spacing={item.SpacingMeters:F1}m, weight={item.Weight:F2}, slope<={item.MaxSlopeDegrees:F0}");
        }
        return text.ToString();
    }
}

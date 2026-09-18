using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class CreatureHabitatAuthor
{
    [MenuItem("Tools/Creatures/Configure Planet Habitats")]
    public static void Configure()
    {
        var library = AssetDatabase.LoadAssetAtPath<CreatureLibrary>("Assets/Resources/Settings/CreatureLibrary.asset");
        if (library == null) throw new InvalidOperationException("Planet creature library is missing.");
        var list = library.Species.ToList();
        Add("Bear", new[] { 24 }, 1.65f, 12, 1.5f, ResourceKind.Meat | ResourceKind.Plants,
            BiomeType.Forest, BiomeType.Taiga, BiomeType.Mountain, BiomeType.LakeShore);
        Add("PolarBear", new[] { 25 }, 1.7f, 14, 1.5f, ResourceKind.Meat,
            BiomeType.Tundra);
        Add("Goat", new[] { 26, 27, 28 }, 1.05f, 4, 2f, ResourceKind.Plants,
            BiomeType.Mountain, BiomeType.Tundra, BiomeType.Steppe, BiomeType.Scrub);
        Add("Snake", new[] { 29, 30, 31 }, .18f, 1, .6f, ResourceKind.Meat,
            BiomeType.Desert, BiomeType.Scrub, BiomeType.Savanna);
        foreach (var species in list)
        {
            if (species == null) continue;
            if (species.DisplayName == "Placeholder Bird") species.DisplayName = "Eagle";
            bool known = true;
            float density = 10f, slope = 40f, water = 1.5f;
            switch (species.DisplayName)
            {
                case "Deer": density = 25; slope = 35; water = 1.8f; break;
                case "Rabbit": density = 60; slope = 40; water = 1.5f; break;
                case "Wolf": density = 8; slope = 45; break;
                case "Boar": density = 10; slope = 35; water = 2; break;
                case "Fox": density = 8; slope = 40; break;
                case "Bear": density = 1.5f; slope = 45; water = 2; break;
                case "PolarBear": density = 1; slope = 40; water = 1; break;
                case "Goat": density = 15; slope = 60; water = 1.1f; break;
                case "Snake": density = 12; slope = 35; water = 1; break;
                case "Eagle": density = 12; slope = 45; water = 1.2f; break;
                case "Vulture": density = 3; slope = 45; water = 1; break;
                case "Seagull": density = 18; slope = 35; water = 1.5f; break;
                default: known = false; break;
            }
            if (!known) continue;
            string audioName = species.DisplayName == "PolarBear" ? "Bear" : species.DisplayName;
            var audio = AssetDatabase.LoadAssetAtPath<CreatureAudioSettings>("Assets/Art/Creatures/" + audioName + "/" + audioName + "Audio.asset");
            if (audio != null) species.Audio = audio;
            species.Habitat = new CreatureHabitat { DensityPerSquareKm = density, MaximumSlope = slope,
                FreshWaterMultiplier = water, PreferFreshWater = water > 1f };
            species.SprintSeconds = species.DisplayName switch
            {
                "Rabbit" => 8f, "Deer" => 15f, "Wolf" => 30f, "Snake" => 8f,
                "Goat" => 16f, _ => 18f
            };
            species.AwakeSeconds = 1800f;
            species.SleepRecoverySeconds = 180f;
            species.BodyMassKg = species.DisplayName switch
            {
                "Deer" => 80, "Rabbit" => 2, "Wolf" => 40, "Boar" => 70, "Fox" => 7,
                "Bear" => 200, "PolarBear" => 250, "Goat" => 35, "Snake" => 3,
                "Vulture" => 7, "Seagull" => 1, _ => 4
            };
            species.MaximumPreyMassRatio = species.DisplayName switch
            { "Wolf" => 3, "Fox" => .7f, "Bear" or "PolarBear" => 2, "Snake" => 1, _ => 0 };
            if (species.CruiseAltitudeMeters > 0f)
            {
                species.Diet = ResourceKind.Meat | ResourceKind.FreshWater;
                species.SprintSeconds = 60;
                species.Scavenger = true;
            }
            if (species.DisplayName == "Snake") species.ThirstSeconds = 7200;
            if (species.DisplayName == "Seagull") species.Diet |= ResourceKind.SaltWater;
            // Home selection needs a local food chain. Snow and IceBog have no configured cold prey yet.
            if (species.DisplayName == "PolarBear") species.Biomes = new[] { BiomeType.Tundra };
            if (species.DisplayName == "Goat") species.Biomes = species.Biomes.Where(b => b != BiomeType.Snow).ToArray();
            if (species.DisplayName is "Deer" or "Rabbit" or "Wolf" or "Boar" or "Fox")
                species.Biomes = species.Biomes.Concat(new[] { BiomeType.LakeShore }).Distinct().ToArray();
            if (species.DisplayName == "Rabbit")
            {
                species.Biomes = species.Biomes.Concat(new[] { BiomeType.Desert, BiomeType.Tundra, BiomeType.Forest }).Distinct().ToArray();
                species.Habitat.BiomeWeights = new[]
                {
                    new CreatureHabitat.BiomeWeight { Biome = BiomeType.Desert, Weight = .15f },
                    new CreatureHabitat.BiomeWeight { Biome = BiomeType.Tundra, Weight = .35f }
                };
            }
        }
        library.Species = list.ToArray();
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();

        void Add(string name, int[] slots, float height, int health, float walk, ResourceKind diet, params BiomeType[] biomes)
        {
            if (list.Any(s => s?.DisplayName == name)) return;
            var visual = AssetDatabase.LoadAssetAtPath<CreatureVisualSettings>("Assets/Art/Creatures/" + name + "/" + name + "Visuals.asset");
            if (visual == null) throw new InvalidOperationException("Build biome animal visuals before habitat configuration: " + name);
            bool predator = (diet & ResourceKind.Meat) != 0;
            list.Add(new CreatureSpecies { DisplayName = name, PerTerritory = 0, AdditionalSlots = slots,
                Visuals = visual, BodyHeightMeters = height, MaxHealth = health, WalkSpeedMps = walk,
                HomeRangeMeters = predator ? 160f : 90f, AwarenessMeters = predator ? 40f : 35f,
                Diet = diet | ResourceKind.FreshWater, Biomes = biomes, MinAltitudeMeters = 2,
                MaxAltitudeMeters = name == "Goat" ? 4500 : 3000, Faction = predator ? CreatureFaction.Predator : CreatureFaction.Wildlife,
                VigilanceSeconds = predator ? 1 : 3, RespawnSeconds = predator ? 1800 : 600,
                Perception = AssetDatabase.LoadAssetAtPath<CreaturePerceptionSettings>("Assets/Resources/Settings/CreaturePerception/" + (predator ? "Wolf" : "Deer") + ".asset"),
                Audio = AssetDatabase.LoadAssetAtPath<CreatureAudioSettings>("Assets/Art/Creatures/" + (name == "PolarBear" ? "Bear" : name) + "/" + (name == "PolarBear" ? "Bear" : name) + "Audio.asset") });
        }
    }
}

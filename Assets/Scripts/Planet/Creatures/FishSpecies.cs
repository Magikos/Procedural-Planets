using UnityEngine;

public enum FishHabitat { Freshwater, Ocean }

/// <summary>Immutable species rules shared by spawning, swimming, and presentation.</summary>
public sealed record FishSpecies(string Name, string Model, FishHabitat Habitat, float Length,
    float Clearance, float MinimumWaterDepth, float SwimSpeed, float Awareness, CreatureFaction Faction)
{
    public static readonly FishSpecies Freshwater = new("Freshwater fish", "SmallFish", FishHabitat.Freshwater,
        0.4f, 0.25f, 1.2f, 1.2f, 8f, CreatureFaction.Wildlife);
    public static readonly FishSpecies Coastal = new("Coastal fish", "SmallFish", FishHabitat.Ocean,
        0.5f, 0.3f, 1.5f, 1.5f, 10f, CreatureFaction.Wildlife);
    public static readonly FishSpecies Shark = new("Shark", "Shark", FishHabitat.Ocean,
        3.5f, 1.8f, 8f, 2.5f, 15f, CreatureFaction.Predator);

    public bool Accepts(WaterSample water) => water.IsOcean == (Habitat == FishHabitat.Ocean) &&
        water.BodyDepth >= MinimumWaterDepth;
}

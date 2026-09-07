using UnityEngine;

/// <summary>Editor authoring surface for the creature species the world spawns. Runtime reads
/// <see cref="CreatureLibraryDto"/>, never this asset.</summary>
[CreateAssetMenu(menuName = "Planet/Creature Library", fileName = "CreatureLibrary")]
public sealed class CreatureLibrary : ScriptableObject
{
    public CreatureSpecies[] Species = System.Array.Empty<CreatureSpecies>();

    [Tooltip("Metres around an observer inside which a creature is fully simulated. Outside it a creature " +
             "still EXISTS - it is demoted to a record and fast-forwarded when seen again.")]
    [Min(1f)] public float ObserverBubbleMeters = 300f;
}

[System.Serializable]
public sealed class CreatureSpecies
{
    public CreatureVisualSettings Visuals;
    public string DisplayName = "Placeholder";

    [Tooltip("How many of this species one territory supports. This is the whole of birth: a territory's " +
             "carrying capacity, derived from the seed.")]
    [Range(0, 63)] public int PerTerritory = 3;

    [Tooltip("Metres from its home point a creature wanders freely. Beyond it, it steers back.")]
    [Min(1f)] public float HomeRangeMeters = 120f;

    [Min(0f)] public float WalkSpeedMps = 2.5f;

    [Tooltip("Metres per second a creature closes on its home while nobody is watching. Also the rate a " +
             "re-observed creature is fast-forwarded home by.")]
    [Min(0f)] public float DriftHomeSpeedMps = 0.8f;

    [Tooltip("Seconds a death suppresses its slot. ZERO OR LESS MEANS NEVER: the slot stays empty for the " +
             "life of the save, which is the unique-boss case.")]
    public float RespawnSeconds = 300f;

    [Tooltip("Signed metres above sea level. Negative is underwater, so an aquatic species is an ordinary " +
             "altitude band rather than a separate concept.")]
    public float MinAltitudeMeters = 2f;
    public float MaxAltitudeMeters = 3000f;

    [Tooltip("Biomes this species will settle in. EMPTY MEANS ANY — a species with no list is limited only " +
             "by its altitude band.")]
    public BiomeType[] Biomes = System.Array.Empty<BiomeType>();

    [Tooltip("Whose side it is on. Decides what it fears through the faction table, so adding a species " +
             "means picking a faction rather than listing who it runs from.")]
    public CreatureFaction Faction = CreatureFaction.Wildlife;

    [Tooltip("Metres at which it notices a threat. A deer looks further than a rabbit.")]
    [Min(1f)] public float AwarenessMeters = 35f;

    [Tooltip("Maximum health. Damage persists across unloading and save reloads.")]
    [Min(1)] public int MaxHealth = 3;

    [Tooltip("Simulated seconds from satisfied to maximum hunger. Zero disables hunger growth.")]
    [Min(0f)] public float HungerSeconds = 1800f;
    [Tooltip("Simulated seconds from satisfied to maximum thirst. Zero disables thirst growth.")]
    [Min(0f)] public float ThirstSeconds = 900f;

    [Tooltip("What killing it credits to the inventory - the same one a felled tree feeds.")]
    public string YieldItemId = "Hide";
    [Min(0)] public int YieldCount = 1;

    [Tooltip("Metres it holds ABOVE the ground. Zero walks. Anything else flies - the motor already keeps a " +
             "body at a height, so a bird is this one number rather than a separate driver.")]
    [Min(0f)] public float CruiseAltitudeMeters = 0f;

    [Min(0.1f)] public float BodyHeightMeters = 1.7f;
    public Color BodyColor = new(0.45f, 0.33f, 0.22f);
}

public sealed record CreatureSpeciesDto(
    string DisplayName,
    int PerTerritory,
    float HomeRangeMeters,
    float WalkSpeedMps,
    float DriftHomeSpeedMps,
    float RespawnSeconds,
    float MinAltitudeMeters,
    float MaxAltitudeMeters,
    float BodyHeightMeters,
    Color BodyColor,
    BiomeType[] Biomes,
    CreatureFaction Faction,
    float AwarenessMeters,
    int MaxHealth,
    string YieldItemId,
    int YieldCount,
    float CruiseAltitudeMeters)
{
    public CreatureVisualDto Visuals { get; init; }
    public float HungerSeconds { get; init; } = 1800f;
    public float ThirstSeconds { get; init; } = 900f;
    /// <summary>True when a death of this species never lapses - the boss case, same code path as a deer.</summary>
    public bool NeverRespawns => RespawnSeconds <= 0f;

    /// <summary>What killing one credits to the inventory.</summary>
    public HarvestYield Yield => new(YieldItemId, YieldCount);

    /// <summary>
    /// Whether the species settles in a biome. An EMPTY list means any: a species is limited by its altitude
    /// band alone until someone says otherwise, which is what keeps a new species a one-line addition.
    /// </summary>
    public bool LivesIn(BiomeType biome)
    {
        if (Biomes == null || Biomes.Length == 0) return true;
        for (int i = 0; i < Biomes.Length; i++)
            if (Biomes[i] == biome) return true;
        return false;
    }

    public bool Suits(float altitudeMeters, BiomeType biome) =>
        altitudeMeters >= MinAltitudeMeters && altitudeMeters <= MaxAltitudeMeters && LivesIn(biome);

    public static CreatureSpeciesDto From(CreatureSpecies src) =>
        src == null
            ? null
            : new CreatureSpeciesDto(
                string.IsNullOrWhiteSpace(src.DisplayName) ? "Unnamed" : src.DisplayName,
                Mathf.Clamp(src.PerTerritory, 0, CreatureKey.MaxSlot + 1),
                Mathf.Max(1f, src.HomeRangeMeters),
                Mathf.Max(0f, src.WalkSpeedMps),
                Mathf.Max(0f, src.DriftHomeSpeedMps),
                src.RespawnSeconds,
                src.MinAltitudeMeters,
                src.MaxAltitudeMeters,
                Mathf.Max(0.1f, src.BodyHeightMeters),
                src.BodyColor,
                // Copied, not aliased: a DTO is a snapshot, and sharing the asset's array would let an
                // inspector edit reach code that already read the settings.
                src.Biomes == null ? System.Array.Empty<BiomeType>() : (BiomeType[])src.Biomes.Clone(),
                src.Faction,
                Mathf.Max(1f, src.AwarenessMeters),
                Mathf.Max(1, src.MaxHealth),
                string.IsNullOrWhiteSpace(src.YieldItemId) ? "Hide" : src.YieldItemId,
                Mathf.Max(0, src.YieldCount),
                Mathf.Max(0f, src.CruiseAltitudeMeters))
            {
                Visuals = src.Visuals != null ? src.Visuals.Snapshot() : null,
                HungerSeconds = FiniteDuration(src.HungerSeconds),
                ThirstSeconds = FiniteDuration(src.ThirstSeconds),
            };

    static float FiniteDuration(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Max(0f, value);
}

public sealed record CreatureLibraryDto(CreatureSpeciesDto[] Species, float ObserverBubbleMeters)
{
    public int Count => Species?.Length ?? 0;

    public CreatureSpeciesDto At(int index) =>
        Species != null && (uint)index < (uint)Species.Length ? Species[index] : null;

    /// <summary>
    /// A copy with one species' respawn expiry changed. The array is rebuilt rather than written through:
    /// a DTO is a snapshot, and mutating the array in place would change a value every holder already read.
    /// </summary>
    public CreatureLibraryDto WithRespawnSeconds(int speciesIndex, float seconds)
    {
        if (Species == null || (uint)speciesIndex >= (uint)Species.Length) return this;
        var next = (CreatureSpeciesDto[])Species.Clone();
        next[speciesIndex] = next[speciesIndex] with { RespawnSeconds = seconds };
        return this with { Species = next };
    }

    public static CreatureLibraryDto From(CreatureLibrary src)
    {
        if (src?.Species == null || src.Species.Length == 0)
            return Placeholder;

        var species = new CreatureSpeciesDto[src.Species.Length];
        for (int i = 0; i < species.Length; i++)
            species[i] = CreatureSpeciesDto.From(src.Species[i]);
        return new CreatureLibraryDto(species, Mathf.Max(1f, src.ObserverBubbleMeters));
    }

    /// <summary>
    /// The one placeholder animal the residency spine is exercised with. It exists in code rather than only in
    /// an asset because a world with no library is still a valid world, and a residency system with nothing to
    /// place proves nothing.
    /// </summary>
    public static CreatureLibraryDto Placeholder { get; } = new(
        new[]
        {
            // Woodland browser: tall, sparse, wooded biomes. Notices a threat at 45 m.
            new CreatureSpeciesDto("Placeholder Deer", 3, 120f, 2.5f, 0.8f, 300f, 2f, 3000f, 1.7f,
                new Color(0.45f, 0.33f, 0.22f),
                new[] { BiomeType.Forest, BiomeType.Taiga, BiomeType.Tropical, BiomeType.Swamp },
                CreatureFaction.Wildlife, 45f, 3, "Hide", 2, 0f),

            // Open-country grazer: small, numerous, and deliberately in biomes the deer refuses, so crossing
            // a biome line visibly swaps which animal is around you. Skittish - bolts at 25 m.
            new CreatureSpeciesDto("Placeholder Rabbit", 6, 45f, 3.2f, 1.2f, 120f, 2f, 2200f, 0.45f,
                new Color(0.62f, 0.58f, 0.52f),
                new[] { BiomeType.Grassland, BiomeType.Scrub, BiomeType.Steppe, BiomeType.Savanna },
                CreatureFaction.Wildlife, 25f, 1, "Hide", 1, 0f),

            // The flier, and the whole of what makes it one: a cruise altitude. Everything else about it is an
            // ordinary resident - a slot, a home range, a death record, a carcass. It notices further than
            // anything on the ground, which is what being up there is for.
            new CreatureSpeciesDto("Placeholder Bird", 4, 200f, 6f, 2.5f, 180f, 2f, 4000f, 0.35f,
                new Color(0.22f, 0.20f, 0.24f),
                System.Array.Empty<BiomeType>(),
                CreatureFaction.Wildlife, 70f, 1, "Feathers", 2, CruiseAltitude),
        },
        300f);

    /// <summary>
    /// Metres a placeholder bird holds above the ground. Low ON PURPOSE: high enough to read as flying, low
    /// enough that it is still a thing in the world rather than a dot, and low enough to be worth aiming at
    /// once there is anything to aim with.
    /// </summary>
    const float CruiseAltitude = 9f;
}

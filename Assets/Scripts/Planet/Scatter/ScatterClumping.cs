using Unity.Mathematics;

// Shared habitat suitability and independent species colonies. Pure math keeps both gather routes identical.
public static class ScatterClumping
{
    public static float Keep(float3 dir, float planetWorldRadius, float clumpiness, float patchScaleMeters,
        uint groupSeed, uint biomeSeed, float slopeCos, float shadePreference = 0f,
        uint worldSeed = 0, float woodlandPotential = -1f, float treeAge = -1f, bool aquatic = false)
    {
        if (clumpiness <= 0f) return 1f;
        float potential = woodlandPotential < 0f
            ? VegetationHabitat.WoodlandPotential((BiomeType)biomeSeed) : woodlandPotential;
        float cover = VegetationHabitat.Cover(dir, planetWorldRadius, worldSeed, slopeCos, potential);
        float colony = VegetationHabitat.ValueNoise(dir * (planetWorldRadius / math.max(patchScaleMeters, 1f)),
            groupSeed ^ worldSeed ^ 0x9e3779b9u);
        float colonyKeep = math.smoothstep(0.3f, 0.7f, colony);
        float pref = math.clamp(shadePreference, -1f, 1f);
        float habitat = pref < 0f ? math.lerp(cover, 1f - cover, -pref) : math.lerp(cover, cover * cover, pref);
        if (aquatic) habitat = 1f; // water identity/depth supply habitat; woodland must not erase lily colonies.
        float keep = habitat * colonyKeep * VegetationHabitat.AgeKeep(treeAge, cover);
        return math.lerp(1f, keep, math.saturate(clumpiness));
    }

    // Stable across processes (String.GetHashCode is randomised per run, which would move every grove between
    // sessions). Same FNV-1a the tree variant seeds use.
    public static uint GroupSeedFor(string key)
    {
        unchecked
        {
            uint h = 2166136261u;
            if (key != null) foreach (char c in key) { h ^= c; h *= 16777619u; }
            return h;
        }
    }
}

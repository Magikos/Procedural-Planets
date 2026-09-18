using Unity.Mathematics;

// Potential woodland, not measured crown occupancy. Shared by all species and mirrored in
// VegetationHabitat.hlsl for grass. World-local directions keep the field continuous across cube faces.
public static class VegetationHabitat
{
    public static float WoodlandPotential(BiomeType biome) => biome switch
    {
        BiomeType.Forest or BiomeType.Tropical or BiomeType.Taiga => 1f,
        BiomeType.Swamp => 0.85f,
        BiomeType.LakeShore => 0.75f,
        BiomeType.Savanna or BiomeType.Scrub => 0.4f,
        BiomeType.Grassland => 0.25f,
        BiomeType.Steppe or BiomeType.Tundra or BiomeType.IceBog => 0.1f,
        BiomeType.Mountain => 0.3f,
        BiomeType.Beach => 0.4f,
        BiomeType.Desert or BiomeType.Snow => 0.1f,
        _ => 0f,
    };

    public static float BlendPotential(int primary, int secondary, float blend) =>
        math.lerp(WoodlandPotential((BiomeType)primary), WoodlandPotential((BiomeType)secondary), math.saturate(blend));

    public static float Cover(float3 direction, float worldRadius, uint seed, float slopeCos, float potential)
    {
        float field = ValueNoise(direction * (worldRadius / 700f), seed);
        // Flat terrain favours openings. Sparse biomes retain broad plains instead of small forest gaps.
        field += (0.85f - slopeCos) * 0.22f;
        float threshold = math.lerp(0.82f, 0.36f, math.saturate(potential));
        return math.saturate(potential) * math.smoothstep(threshold, threshold + 0.24f, field);
    }

    public static float AgeKeep(float age, float cover)
    {
        if (age < 0f) return 1f;
        float target = math.lerp(0.25f, 1f, math.saturate(cover));
        // Overlapping age distributions retain old edge trees and young interior trees.
        return math.lerp(0.12f, 1f, math.saturate(1f - math.abs(age - target) / 0.65f));
    }

    public static float GrassKeep(float cover) => math.lerp(1f, 0.15f, math.saturate(cover));

    public static float ValueNoise(float3 p, uint seed)
    {
        int3 cell = (int3)math.floor(p);
        float3 t = math.frac(p);
        t = t * t * (3f - 2f * t);
        float a = math.lerp(Corner(cell, seed), Corner(cell + new int3(1, 0, 0), seed), t.x);
        float b = math.lerp(Corner(cell + new int3(0, 1, 0), seed), Corner(cell + new int3(1, 1, 0), seed), t.x);
        float c = math.lerp(Corner(cell + new int3(0, 0, 1), seed), Corner(cell + new int3(1, 0, 1), seed), t.x);
        float d = math.lerp(Corner(cell + new int3(0, 1, 1), seed), Corner(cell + new int3(1, 1, 1), seed), t.x);
        return math.lerp(math.lerp(a, b, t.y), math.lerp(c, d, t.y), t.z);
    }

    static float Corner(int3 cell, uint seed)
    {
        unchecked
        {
            uint h = seed ^ (uint)cell.x * 0x8da6b343u ^ (uint)cell.y * 0xd8163841u ^ (uint)cell.z * 0xcb1ab31fu;
            h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
            return (h & 0x00ffffffu) / 16777216f;
        }
    }
}

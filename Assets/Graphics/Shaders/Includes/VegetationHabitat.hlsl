#ifndef VEGETATION_HABITAT_INCLUDED
#define VEGETATION_HABITAT_INCLUDED
// Mirrors VegetationHabitat.cs. Potential woodland is independent of sun position and camera movement.
float HabitatCorner(int3 cell, uint seed)
{
    uint h = seed ^ (uint)cell.x * 0x8da6b343u ^ (uint)cell.y * 0xd8163841u ^ (uint)cell.z * 0xcb1ab31fu;
    h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
    return (h & 0x00ffffffu) / 16777216.0;
}
float HabitatValueNoise(float3 p, uint seed)
{
    int3 cell = (int3)floor(p);
    float3 t = frac(p);
    t = t * t * (3.0 - 2.0 * t);
    float a = lerp(HabitatCorner(cell, seed), HabitatCorner(cell + int3(1,0,0), seed), t.x);
    float b = lerp(HabitatCorner(cell + int3(0,1,0), seed), HabitatCorner(cell + int3(1,1,0), seed), t.x);
    float c = lerp(HabitatCorner(cell + int3(0,0,1), seed), HabitatCorner(cell + int3(1,0,1), seed), t.x);
    float d = lerp(HabitatCorner(cell + int3(0,1,1), seed), HabitatCorner(cell + int3(1,1,1), seed), t.x);
    return lerp(lerp(a,b,t.y), lerp(c,d,t.y), t.z);
}
float HabitatCover(float3 direction, float worldRadius, uint seed, float slopeCos, float potential)
{
    float field = HabitatValueNoise(direction * (worldRadius / 700.0), seed);
    field += (0.85 - slopeCos) * 0.22;
    float threshold = lerp(0.82, 0.36, saturate(potential));
    return saturate(potential) * smoothstep(threshold, threshold + 0.24, field);
}
float HabitatGrassKeep(float cover) { return lerp(1.0, 0.15, saturate(cover)); }
#endif

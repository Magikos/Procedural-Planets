#ifndef PROCEDURAL_PLANETS_GRASS_PLACEMENT_COMMON_INCLUDED
#define PROCEDURAL_PLANETS_GRASS_PLACEMENT_COMMON_INCLUDED

struct BiomeGrassParams
{
    float4 Shape;     // x density, y height, z width, w clump strength
    float4 Placement; // x maxSlopeDeg, y slopeFadeDeg, z waterClearance, w blendPower
    float4 Tint;
    float4 TintDry;   // multiplier on Tint at moisture == 0; default (1,1,1,1) = no shift
    float4 TintLush;  // multiplier on Tint at moisture == 1; default (1,1,1,1) = no shift
};

struct GrassBladeInstance
{
    float4 RootHeight;
    float4 UpWidth;
    float4 Color;
};

uint HashUint(uint x)
{
    x ^= x >> 16;
    x *= 0x7feb352du;
    x ^= x >> 15;
    x *= 0x846ca68bu;
    x ^= x >> 16;
    return x;
}

float Hash01(uint seed)
{
    return (HashUint(seed) & 0x00ffffffu) / 16777216.0;
}

float3 CubeFaceToUnitSphere(int face, float2 uv)
{
    float u = uv.x * 2.0 - 1.0;
    float v = uv.y * 2.0 - 1.0;
    float3 p = float3(0.0, 1.0, 0.0);

    if (face == 0) p = float3(u, 1.0, -v);
    else if (face == 1) p = float3(-u, -1.0, -v);
    else if (face == 2) p = float3(-1.0, -v, -u);
    else if (face == 3) p = float3(1.0, -v, u);
    else if (face == 4) p = float3(-v, u, 1.0);
    else if (face == 5) p = float3(-v, -u, -1.0);

    return normalize(p);
}

float SurfaceStateReject(float4 state)
{
    float scorchReject = smoothstep(0.08, 0.72, state.g);
    return saturate(max(state.r, scorchReject));
}

void GrassPlacementBilinearTexels(float2 uv, int atlasResolution, out int2 t00, out int2 t10, out int2 t01, out int2 t11, out float2 f)
{
    int resolution = max(atlasResolution, 1);
    int maxTexel = max(resolution - 1, 0);
    int2 maxTexel2 = int2(maxTexel, maxTexel);

    float2 texelCoord = saturate(uv) * (float)resolution - 0.5;
    int2 baseTexel = (int2)floor(texelCoord);
    f = saturate(texelCoord - baseTexel);

    t00 = clamp(baseTexel, int2(0, 0), maxTexel2);
    t10 = clamp(baseTexel + int2(1, 0), int2(0, 0), maxTexel2);
    t01 = clamp(baseTexel + int2(0, 1), int2(0, 0), maxTexel2);
    t11 = clamp(baseTexel + int2(1, 1), int2(0, 0), maxTexel2);
}

#endif

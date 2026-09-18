#ifndef PROCEDURAL_PLANETS_GRASS_PLACEMENT_PARAM_BLEND_INCLUDED
#define PROCEDURAL_PLANETS_GRASS_PLACEMENT_PARAM_BLEND_INCLUDED

float SampleClimateMoisture(int face, float2 faceUv)
{
    if (_ClimateMapResolution <= 0)
        return 0.5;
    int res = max(_ClimateMapResolution - 1, 0);
    int2 ip = clamp(int2(round(saturate(faceUv) * (float)res)), int2(0, 0), int2(res, res));
    return saturate(_ClimateMap.Load(int4(ip, face, 0)).g);
}

BiomeGrassParams GetGrassParams(uint id)
{
    BiomeGrassParams p;
    p.Habitat = 0.0;
    p.Shape = 0.0;
    p.Placement = 0.0;
    p.Tint = 0.0;
    p.TintDry = 1.0;
    p.TintLush = 1.0;
    if (id < (uint)GRASS_PLACEMENT_PARAM_COUNT)
        p = GRASS_PLACEMENT_PARAM_BUFFER[id];
    return p;
}

BiomeGrassParams AccumulateGrassParams(float4 idsPacked, float4 weightsPacked, out float totalParamWeight)
{
    BiomeGrassParams blended;
    blended.Habitat = 0.0;
    blended.Shape = 0.0;
    blended.Placement = 0.0;
    blended.Tint = 0.0;
    blended.TintDry = 0.0;
    blended.TintLush = 0.0;

    float density = 0.0;
    totalParamWeight = 0.0;

    [unroll]
    for (uint i = 0u; i < 4u; i++)
    {
        uint biomeId = (uint)round(idsPacked[i] * 255.0);
        float weight = saturate(weightsPacked[i]);
        if (weight <= 0.0001)
            continue;

        BiomeGrassParams p = GetGrassParams(biomeId);
        float grassDensity = saturate(p.Shape.x);
        float blendPower = max(p.Placement.w, 0.001);
        density += grassDensity * pow(weight, blendPower);

        blended.Habitat.x += p.Habitat.x * weight;
        blended.Habitat.y += weight;
        float paramWeight = weight * grassDensity;
        if (paramWeight <= 0.0001)
            continue;

        blended.Shape.y += p.Shape.y * paramWeight;
        blended.Shape.z += p.Shape.z * paramWeight;
        blended.Shape.w += p.Shape.w * paramWeight;
        blended.Placement += p.Placement * paramWeight;
        blended.Tint.rgb += p.Tint.rgb * paramWeight;
        blended.TintDry.rgb += p.TintDry.rgb * paramWeight;
        blended.TintLush.rgb += p.TintLush.rgb * paramWeight;
        totalParamWeight += paramWeight;
    }

    density = saturate(density);
    blended.Shape.x = density;
    blended.Tint.a = 1.0;
    blended.TintDry.a = 1.0;
    blended.TintLush.a = 1.0;

    return blended;
}

BiomeGrassParams NormalizeGrassParams(BiomeGrassParams blended, float totalParamWeight)
{
    if (totalParamWeight > 0.0)
    {
        float inv = rcp(totalParamWeight);
        blended.Shape.y *= inv;
        blended.Shape.z *= inv;
        blended.Shape.w *= inv;
        blended.Placement *= inv;
        blended.Tint.rgb *= inv;
        blended.TintDry.rgb *= inv;
        blended.TintLush.rgb *= inv;
    }

    blended.Habitat.x /= max(blended.Habitat.y, 0.0001);
    return blended;
}

BiomeGrassParams LerpGrassParams(BiomeGrassParams a, BiomeGrassParams b, float t)
{
    BiomeGrassParams result;
    result.Habitat = lerp(a.Habitat, b.Habitat, t);
    result.Shape = lerp(a.Shape, b.Shape, t);
    result.Placement = lerp(a.Placement, b.Placement, t);
    result.Tint = lerp(a.Tint, b.Tint, t);
    result.TintDry = lerp(a.TintDry, b.TintDry, t);
    result.TintLush = lerp(a.TintLush, b.TintLush, t);
    return result;
}

BiomeGrassParams BlendGrassParamCorners(
    float4 ids00, float4 weights00,
    float4 ids10, float4 weights10,
    float4 ids01, float4 weights01,
    float4 ids11, float4 weights11,
    float2 f,
    out float density)
{
    float w00, w10, w01, w11;
    BiomeGrassParams p00 = AccumulateGrassParams(ids00, weights00, w00);
    BiomeGrassParams p10 = AccumulateGrassParams(ids10, weights10, w10);
    BiomeGrassParams p01 = AccumulateGrassParams(ids01, weights01, w01);
    BiomeGrassParams p11 = AccumulateGrassParams(ids11, weights11, w11);

    BiomeGrassParams px0 = LerpGrassParams(p00, p10, f.x);
    BiomeGrassParams px1 = LerpGrassParams(p01, p11, f.x);
    BiomeGrassParams result = LerpGrassParams(px0, px1, f.y);
    density = saturate(result.Shape.x);
    result.Shape.x = density;
    // Empty corners reduce coverage, not the properties of the grass that remains.
    float totalParamWeight = lerp(lerp(w00, w10, f.x), lerp(w01, w11, f.x), f.y);
    return NormalizeGrassParams(result, totalParamWeight);
}

#endif

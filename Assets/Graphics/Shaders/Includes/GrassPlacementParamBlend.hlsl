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
    p.Shape = 0.0;
    p.Placement = 0.0;
    p.Tint = 0.0;
    p.TintDry = 1.0;
    p.TintLush = 1.0;
    if (id < (uint)GRASS_PLACEMENT_PARAM_COUNT)
        p = GRASS_PLACEMENT_PARAM_BUFFER[id];
    return p;
}

BiomeGrassParams BlendGrassParams(float4 idsPacked, float4 weightsPacked, out float density)
{
    BiomeGrassParams blended;
    blended.Shape = 0.0;
    blended.Placement = 0.0;
    blended.Tint = 0.0;
    blended.TintDry = 0.0;
    blended.TintLush = 0.0;

    density = 0.0;
    float totalParamWeight = 0.0;

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

    if (totalParamWeight > 0.0001)
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

    return blended;
}

BiomeGrassParams LerpGrassParams(BiomeGrassParams a, BiomeGrassParams b, float t)
{
    BiomeGrassParams result;
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
    float cornerDensity;
    BiomeGrassParams p00 = BlendGrassParams(ids00, weights00, cornerDensity);
    BiomeGrassParams p10 = BlendGrassParams(ids10, weights10, cornerDensity);
    BiomeGrassParams p01 = BlendGrassParams(ids01, weights01, cornerDensity);
    BiomeGrassParams p11 = BlendGrassParams(ids11, weights11, cornerDensity);

    BiomeGrassParams px0 = LerpGrassParams(p00, p10, f.x);
    BiomeGrassParams px1 = LerpGrassParams(p01, p11, f.x);
    BiomeGrassParams result = LerpGrassParams(px0, px1, f.y);
    density = saturate(result.Shape.x);
    result.Shape.x = density;
    return result;
}

#endif

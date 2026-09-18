#ifndef RIVER_FIELD_INCLUDED
#define RIVER_FIELD_INCLUDED
#include "WaterLevelProjection.hlsl"
Texture2D<float4> _RiverSegments;
Texture2D<float4> _RiverRanges;
Texture2D<float4> _RiverIndices;
float _RiverPlanetRadius;
int _RiverActive;

float4 RiverSegmentData(int index)
{
    return _RiverSegments.Load(int3(index & 1023, index >> 10, 0));
}

// Projection and width match RiverSegment.Distance/Width in the native field.
int2 RiverCellRange(float3 direction)
{
    int face; float2 uv;
    WaterLevelFaceUvRaw(direction, face, uv);
    int2 cell = clamp((int2)(uv * 64), 0, 63);
    return (int2)_RiverRanges.Load(int3(cell.x, face * 64 + cell.y, 0)).xy;
}

bool RiverDistance(float3 direction, float4 a, float4 b, float4 profile,
    out float t, out float distance)
{
    float3 edge = b.xyz - a.xyz;
    float raw = dot(direction - a.xyz, edge) / max(dot(edge, edge), 1e-12);
    t = saturate(raw);
    float beyond = (raw - t) * length(edge) * _RiverPlanetRadius;
    distance = length(direction - normalize(lerp(a.xyz, b.xyz, t))) * _RiverPlanetRadius;
    return !(((((int)profile.w & 1) != 0) && beyond < -.005)
        || ((((int)profile.w & 2) != 0) && beyond > .005));
}

float RiverWidth(float4 shape, float4 profile, float t)
{
    float wave = sin(3.14159265359 * t);
    return lerp(shape.x, profile.x > 0 ? profile.x : shape.x, t) + profile.y * wave * wave;
}

// Standing-water cover must not flood the lower channel or its carved banks.
// Use the same local segment buckets as carving, without the surface-height blending pass.
bool RiverBankBelow(float3 direction, float standingRadius, bool preserveStandingBanks)
{
    if (_RiverActive == 0) return false;
    int2 range = RiverCellRange(direction);
    [loop] for (int i = range.x; i < range.y; i++)
    {
        int index = (int)_RiverIndices.Load(int3(i & 1023, i >> 10, 0)).r;
        float4 a = RiverSegmentData(index * 5), b = RiverSegmentData(index * 5 + 1);
        if (min(a.w, b.w) >= standingRadius - .005) continue;
        float4 shape = RiverSegmentData(index * 5 + 2);
        if (shape.w > 0) continue;
        float4 profile = RiverSegmentData(index * 5 + 4);
        float t, distance;
        if (!RiverDistance(direction, a, b, profile, t, distance)) continue;
        if (lerp(a.w, b.w, t) >= standingRadius - .005) continue;
        float4 flow = RiverSegmentData(index * 5 + 3);
        float bankWidth = RiverWidth(shape, profile, t) * flow.w / shape.x;
        if (distance < bankWidth && (!preserveStandingBanks || distance <= RiverWidth(shape, profile, t))) return true;
    }
    return false;
}

// Returns radius, speed, sheet flag and signed bank clearance. C# uses the same chord projection.
float4 SampleRiver(float3 direction)
{
    float4 result = float4(0, 0, 0, -1e9);
    if (_RiverActive == 0) return result;
    int2 range = RiverCellRange(direction);
    float score = 1e9;
    float reach = -1;
    float heightBlend = 1;
    float radiusSum = 0, weightSum = 0;
    [loop] for (int i = range.x; i < range.y; i++)
    {
        int index = (int)_RiverIndices.Load(int3(i & 1023, i >> 10, 0)).r;
        float4 a = RiverSegmentData(index * 5), b = RiverSegmentData(index * 5 + 1);
        float4 shape = RiverSegmentData(index * 5 + 2);
        float4 profile = RiverSegmentData(index * 5 + 4);
        if (shape.w > 0) continue;
        float t, distance;
        if (!RiverDistance(direction, a, b, profile, t, distance)) continue;
        float width = RiverWidth(shape, profile, t);
        float relative = distance / width;
        if (relative >= score) continue;
        score = relative;
        reach = profile.z;
        heightBlend = (((int)profile.w & 1) != 0 ? smoothstep(0, .2, t) : 1) *
            (((int)profile.w & 2) != 0 ? smoothstep(0, .2, 1-t) : 1);
        result = float4(lerp(a.w, b.w, t), shape.z, shape.w, width - distance);
    }
    [loop] for (int blendIndex = range.x; blendIndex < range.y; blendIndex++)
    {
        int index = (int)_RiverIndices.Load(int3(blendIndex & 1023, blendIndex >> 10, 0)).r;
        float4 shape = RiverSegmentData(index * 5 + 2), profile = RiverSegmentData(index * 5 + 4);
        if (shape.w > 0 || profile.z != reach) continue;
        float4 a = RiverSegmentData(index * 5), b = RiverSegmentData(index * 5 + 1);
        float t, distance;
        if (!RiverDistance(direction, a, b, profile, t, distance)) continue;
        float width = RiverWidth(shape, profile, t);
        float4 flow = RiverSegmentData(index * 5 + 3);
        float weight = pow(saturate(1 - distance / (width * flow.w / shape.x)), 4);
        radiusSum += lerp(a.w, b.w, t) * weight;
        weightSum += weight;
    }
    if (weightSum > 1e-6) result.x = lerp(result.x, radiusSum / weightSum, heightBlend);
    return result;
}
#endif


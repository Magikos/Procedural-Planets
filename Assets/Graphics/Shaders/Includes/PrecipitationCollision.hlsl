#ifndef PRECIPITATION_COLLISION_INCLUDED
#define PRECIPITATION_COLLISION_INCLUDED
#include "GrassPlacementCommon.hlsl"
#include "WeatherCubeFace.hlsl"

Texture2D<float> _RainSurfaceRadius0;
Texture2D<float> _RainSurfaceRadius1;
Texture2D<float> _RainSurfaceRadius2;
Texture2D<float> _RainSurfaceRadius3;
Texture2D<float> _RainSurfaceRadius4;
Texture2D<float> _RainSurfaceRadius5;
SamplerState sampler_linear_clamp;
int _RainSurfaceResolution;

struct RainCollider
{
    float4x4 WorldToLocal;
    float4 CenterType;
    float4 Size;
};
StructuredBuffer<RainCollider> _RainColliders;
StructuredBuffer<float3> _RainTriangles;
int _RainColliderCount;

float RainTerrainRadius(float3 n, float fallback)
{
    if (_RainSurfaceResolution <= 0) return fallback;
    float2 uv = 0;
    int face = 0;
    CubeFaceUv(n, face, uv);
    uv = (uv * (_RainSurfaceResolution - 1) + 0.5) / _RainSurfaceResolution;
    float radius;
    if (face == 0) radius = _RainSurfaceRadius0.SampleLevel(sampler_linear_clamp, uv, 0);
    else if (face == 1) radius = _RainSurfaceRadius1.SampleLevel(sampler_linear_clamp, uv, 0);
    else if (face == 2) radius = _RainSurfaceRadius2.SampleLevel(sampler_linear_clamp, uv, 0);
    else if (face == 3) radius = _RainSurfaceRadius3.SampleLevel(sampler_linear_clamp, uv, 0);
    else if (face == 4) radius = _RainSurfaceRadius4.SampleLevel(sampler_linear_clamp, uv, 0);
    else radius = _RainSurfaceRadius5.SampleLevel(sampler_linear_clamp, uv, 0);
    return radius > 0 ? radius : fallback;
}

float RainSurfaceRadius(float3 n, float fallback, out bool water)
{
    float ground = RainTerrainRadius(n, fallback);
    float level = GrassWaterSurfaceRadiusAt(n, -1.0);
    water = level > ground;
    return max(ground, level);
}

float ShapeDistance(float3 p, RainCollider shape)
{
    if (shape.CenterType.w < 0.5)
    {
        float3 q = abs(p) - shape.Size.xyz;
        return length(max(q, 0)) + min(max(q.x, max(q.y, q.z)), 0);
    }
    p.y -= clamp(p.y, -shape.Size.y, shape.Size.y);
    return length(p) - shape.Size.x;
}

// Sphere tracing an exact primitive SDF cannot step past its first surface.
bool RainColliderHit(float3 start, float3 end, RainCollider shape, out float hit, out float3 normal)
{
    float3 origin = mul(shape.WorldToLocal, float4(start, 1)).xyz - shape.CenterType.xyz;
    float3 delta = mul((float3x3)shape.WorldToLocal, end - start);
    float lengthLocal = length(delta);
    hit = 0; normal = 0;
    if (lengthLocal < 1e-6) return false;
    float3 extent = shape.CenterType.w != 1 ? shape.Size.xyz : float3(shape.Size.x, shape.Size.x + shape.Size.y, shape.Size.x);
    float3 inverse = 1.0 / (sign(delta + 1e-12) * max(abs(delta), 1e-8));
    float3 nearT = (-extent - origin) * inverse;
    float3 farT = (extent - origin) * inverse;
    float3 lo = min(nearT, farT), hi = max(nearT, farT);
    float enter = max(0, max(lo.x, max(lo.y, lo.z)));
    float leave = min(1, min(hi.x, min(hi.y, hi.z)));
    if (enter > leave) return false;
    if (shape.CenterType.w > 1.5)
    {
        float closest = 2;
        int count = (int)shape.CenterType.w - 2;
        int offset = (int)shape.Size.w;
        [loop] for (int i = 0; i < count; i++)
        {
            float3 v = _RainTriangles[offset + i * 3];
            float3 e1 = _RainTriangles[offset + i * 3 + 1] - v;
            float3 e2 = _RainTriangles[offset + i * 3 + 2] - v;
            float3 h = cross(delta, e2);
            float determinant = dot(e1,h);
            if (abs(determinant) < 1e-8) continue;
            float3 s = origin-v;
            float u = dot(s,h)/determinant;
            float3 q = cross(s,e1);
            float b = dot(delta,q)/determinant;
            float t = dot(e2,q)/determinant;
            if (u >= 0 && b >= 0 && u+b <= 1 && t >= 0 && t <= 1 && t < closest)
            {
                closest = t;
                float3 n = cross(e1,e2);
                if (dot(n,delta)>0) n=-n;
                normal = normalize(mul(n,(float3x3)shape.WorldToLocal));
            }
        }
        hit = closest;
        return closest <= 1;
    }
    float3 direction = delta / lengthLocal;
    float travel = enter * lengthLocal;
    [loop] for (int step = 0; step < 64; step++)
    {
        float3 p = origin + direction * travel;
        float d = ShapeDistance(p, shape);
        if (d <= 0.001)
        {
            hit = travel / lengthLocal;
            float e = 0.001;
            float3 gradient = float3(
                ShapeDistance(p + float3(e,0,0), shape) - ShapeDistance(p - float3(e,0,0), shape),
                ShapeDistance(p + float3(0,e,0), shape) - ShapeDistance(p - float3(0,e,0), shape),
                ShapeDistance(p + float3(0,0,e), shape) - ShapeDistance(p - float3(0,0,e), shape));
            normal = normalize(mul(gradient, (float3x3)shape.WorldToLocal) + 1e-8);
            return true;
        }
        travel += d;
        if (travel > lengthLocal) break;
    }
    return false;
}

bool RainContact(float3 start, float3 end, float3 center, float fallback,
    out float3 contactPosition, out float3 normal, out bool water)
{
    float first = 2;
    normal = normalize(end - center);
    water = false;
    // Sample horizontal travel at sub-texel intervals so a fast drop cannot skip a ridge.
    float3 startNormal = normalize(start - center);
    float horizontalTravel = length(normal - startNormal) * max(fallback, 1);
    float cellSize = max(fallback, 1) / max(_RainSurfaceResolution, 1);
    int steps = max(1, (int)ceil(horizontalTravel / max(cellSize * 0.25, 0.1)));
    float lo = 0;
    [loop] for (int sampleIndex = 0; sampleIndex <= steps; sampleIndex++)
    {
        float hi = (float)sampleIndex / steps;
        float3 p = lerp(start, end, hi) - center;
        bool w;
        if (length(p) > RainSurfaceRadius(normalize(p), fallback, w)) { lo = hi; continue; }
        [unroll] for (int i = 0; i < 10; i++)
        {
            float mid = (lo + hi) * 0.5;
            p = lerp(start, end, mid) - center;
            if (length(p) > RainSurfaceRadius(normalize(p), fallback, w)) lo = mid;
            else hi = mid;
        }
        first = hi;
        normal = normalize(lerp(start, end, first) - center);
        RainSurfaceRadius(normal, fallback, water);
        if (!water && _RainSurfaceResolution > 0)
        {
            float3 reference = abs(normal.y) < 0.9 ? float3(0,1,0) : float3(1,0,0);
            float3 tangent = normalize(cross(normal, reference));
            float3 bitangent = cross(normal, tangent);
            float e = 0.25 / _RainSurfaceResolution;
            float3 a = normalize(normal + tangent * e), b = normalize(normal - tangent * e);
            float3 c = normalize(normal + bitangent * e), d = normalize(normal - bitangent * e);
            float3 dx = a * RainTerrainRadius(a, fallback) - b * RainTerrainRadius(b, fallback);
            float3 dy = c * RainTerrainRadius(c, fallback) - d * RainTerrainRadius(d, fallback);
            normal = normalize(cross(dx,dy));
        }
        break;
    }
    [loop] for (int c = 0; c < _RainColliderCount; c++)
    {
        float t; float3 n;
        if (RainColliderHit(start, end, _RainColliders[c], t, n) && t < first)
        { first = t; normal = n; water = false; }
    }
    contactPosition = lerp(start, end, min(first, 1));
    return first <= 1;
}
#endif

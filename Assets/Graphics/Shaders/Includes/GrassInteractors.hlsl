#ifndef GRASS_INTERACTORS_INCLUDED
#define GRASS_INTERACTORS_INCLUDED

// Per-frame buffer of dynamic objects pushing grass aside (debug sphere today;
// player character, projectiles, animals, magic AOEs later). Packed by C# side
// GrassInteractorRegistry; bound globally before grass passes. Cap matches
// GrassInteractorRegistry.MaxInteractors (8).

struct GrassInteractor
{
    float4 PositionRadius;  // xyz = world position, w = effect radius (meters)
    float4 StrengthType;    // x = strength [0,2], y = type (0 = transient, 1 = persistent), z/w reserved
};

StructuredBuffer<GrassInteractor> _GrassInteractors;
int _GrassInteractorCount;
StructuredBuffer<GrassInteractor> _GrassInteractorsPrevious;
int _GrassInteractorPreviousCount;

// Returns a tangent-plane bend offset to add to blade tip / card lean. Magnitude
// is capped by maxBend so overlapping recovery samples cannot displace geometry
// outside a sensible blade-relative range. Caller still applies tip weighting.
//
// upWS = local up direction at rootWS (on a planet, the radial direction). Used
// to project the bend onto the tangent plane so blades sway horizontally rather
// than push vertically (which looks like blades growing taller, not bending).
//
// Implementation: for each active or fading history sample within radius, compute bend
// direction (rootWS - interactorPos) projected to tangent plane, falloff by
// smoothstep over distance, and sum the contributions.
float3 SampleGrassInteractorBend(float3 rootWS, float3 upWS, float maxBend, bool previous)
{
    float3 bend = float3(0.0, 0.0, 0.0);
    // Hard-clamp count to a sane range. C# side initializes _GrassInteractorCount to 0
    // before any frame renders, but a defensive clamp here guarantees we never iterate
    // beyond the buffer regardless of bind ordering or hot-reload edge cases.
    int count = clamp(previous ? _GrassInteractorPreviousCount : _GrassInteractorCount, 0, 8);
    for (int i = 0; i < count; i++)
    {
        GrassInteractor interactor;
        if (previous) interactor = _GrassInteractorsPrevious[i];
        else interactor = _GrassInteractors[i];
        float4 posRadius = interactor.PositionRadius;
        float3 toRoot = rootWS - posRadius.xyz;
        float dist = length(toRoot);
        float radius = posRadius.w;
        if (dist >= radius || dist < 0.0001) continue;

        float falloff = 1.0 - smoothstep(0.0, radius, dist);
        float3 dir = toRoot / dist;
        // Project to tangent plane: remove vertical component so the bend sways
        // sideways, not radially-outward.
        dir = dir - upWS * dot(dir, upWS);
        float dirLen = length(dir);
        if (dirLen < 0.0001) continue;

        float strength = interactor.StrengthType.x;
        float displacement = min(radius * 0.35, max(maxBend, 0.0));
        bend += (dir / dirLen) * falloff * strength * displacement;
    }

    float bendLength = length(bend);
    float bendLimit = max(maxBend, 0.0);
    return bendLength > bendLimit && bendLength > 0.0001
        ? bend * (bendLimit / bendLength)
        : bend;
}

float3 SampleGrassInteractorBend(float3 rootWS, float3 upWS, float maxBend)
{
    return SampleGrassInteractorBend(rootWS, upWS, maxBend, false);
}

#endif

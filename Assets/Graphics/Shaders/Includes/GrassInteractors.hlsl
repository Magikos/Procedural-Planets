#ifndef GRASS_INTERACTORS_INCLUDED
#define GRASS_INTERACTORS_INCLUDED

// Forward-compat hooks for slice 6 (character/entity grass bend) and slice 7 (path
// cutting / persistent state). The buffer + count are reserved here so all three grass
// shaders (Grass.shader, GrassMidField.shader future, future variants) can call
// SampleGrassInteractorBend at the same vertex point without a shader rewrite when the
// runtime side ships.
//
// Slice 4a state: STUB. Buffer declared, count defaults to 0 (no allocations needed on
// C# side), SampleGrassInteractorBend returns float3(0,0,0). Zero cost per vertex when
// count is 0. Real implementation lands in slice 6.

struct GrassInteractor
{
    float4 PositionRadius;  // xyz = world position, w = effect radius (meters)
    float4 StrengthType;    // x = strength [0,1], y = type (0=transient bend, 1=persistent), z/w reserved
};

StructuredBuffer<GrassInteractor> _GrassInteractors;
int _GrassInteractorCount;

// Returns a world-space bend offset to add to blade tip / card lean. Magnitude is in
// world units (meters). For near-field blades, scale by blade height (taller blades bend
// further). For mid-field cards, apply to the top-vertex offset.
//
// SLICE 4a STUB: returns zero. Slice 6 fills in:
//   for (int i = 0; i < _GrassInteractorCount; i++) {
//       float3 toRoot = rootWS - _GrassInteractors[i].PositionRadius.xyz;
//       float dist = length(toRoot);
//       float radius = _GrassInteractors[i].PositionRadius.w;
//       if (dist < radius) {
//           float falloff = 1.0 - smoothstep(0, radius, dist);
//           bend += normalize(toRoot) * falloff * _GrassInteractors[i].StrengthType.x;
//       }
//   }
float3 SampleGrassInteractorBend(float3 rootWS)
{
    // Stub: count defaults to 0 so the loop body never runs. Cheaper than an early-out
    // branch (HLSL compiler hoists the zero-trip loop entirely).
    return float3(0.0, 0.0, 0.0);
}

#endif

#ifndef WATER_LEVEL_PROJECTION_INCLUDED
#define WATER_LEVEL_PROJECTION_INCLUDED

// How a direction maps onto the water level field's cube-face grid, and nothing else - no texture
// declarations, no URP macros, no globals.
//
// Split out of WaterLevelField.hlsl so the grass placement COMPUTES can share it. They read texture arrays
// with .Load and cannot pull in URP's TEXTURE2D_ARRAY macros, so including that file wholesale is not an
// option - and this projection has to stay identical to WaterLevelGrid.Index in C#. It was already one
// hand-kept copy; a third one living in two compute shaders is exactly what the note over there warns off.

#define WATER_LEVEL_NO_WATER_MAX (-0.5)

void WaterLevelFaceUvRaw(float3 direction, out int face, out float2 uv)
{
    float3 a = abs(direction);
    float uSigned, vSigned;

    if (a.y >= a.x && a.y >= a.z)
    {
        float inv = 1.0 / max(a.y, 1e-8);
        if (direction.y >= 0.0) { face = 0; uSigned = direction.x * inv; vSigned = -direction.z * inv; }
        else                    { face = 1; uSigned = -direction.x * inv; vSigned = -direction.z * inv; }
    }
    else if (a.x >= a.y && a.x >= a.z)
    {
        float inv = 1.0 / max(a.x, 1e-8);
        if (direction.x >= 0.0) { face = 3; uSigned = direction.z * inv; vSigned = -direction.y * inv; }
        else                    { face = 2; uSigned = -direction.z * inv; vSigned = -direction.y * inv; }
    }
    else
    {
        float inv = 1.0 / max(a.z, 1e-8);
        if (direction.z >= 0.0) { face = 4; uSigned = direction.y * inv; vSigned = -direction.x * inv; }
        else                    { face = 5; uSigned = -direction.y * inv; vSigned = -direction.x * inv; }
    }

    uv = saturate(float2(uSigned * 0.5 + 0.5, vSigned * 0.5 + 0.5));
}

// Snapped to the cell centre the C# side would land on, so both agree on which cell a direction owns.
void WaterLevelFaceUv(float3 direction, float resolution, out int face, out float2 uv)
{
    WaterLevelFaceUvRaw(direction, face, uv);
    uv = (floor(uv * resolution) + 0.5) / resolution;
}

#endif

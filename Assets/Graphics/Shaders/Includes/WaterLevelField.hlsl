#ifndef WATER_LEVEL_FIELD_INCLUDED
#define WATER_LEVEL_FIELD_INCLUDED

// How high water stands above a direction, as opposed to above the planet.
//
// Every shader that asked "where is the water surface" used to answer with _SeaLevelRadius, a single
// sphere. That is wrong the moment a lake sits above sea level: grass faded against a surface 40 m below
// the one it was growing into, and the volume never treated a raised lake as water at all.
//
// The grid is published by WaterLevelTexture, one array slice per cube face, point-sampled. The projection
// below is a mirror of WaterLevelGrid.Index in C# - a texture fetch cannot share that code, so it can only
// be kept identical. Change one, change both.
TEXTURE2D_ARRAY(_WaterLevelTex);
SAMPLER(sampler_WaterLevelTex);
float _WaterLevelRes;
TEXTURE2D_ARRAY(_ShoreLevelTex);
SAMPLER(sampler_ShoreLevelTex);
float _ShoreLevelRes;
// Scale the level is expressed against, published with the field so no caller has to supply it.
float _WaterLevelBaseRadius;

#define WATER_LEVEL_NO_WATER_MAX (-0.5)

// Cube-face projection, shared by both fields. Extracted rather than copied because it has to stay identical
// to WaterLevelGrid.Index in C# and one copy is already one more than can be kept in step by hand.
void WaterLevelFaceUv(float3 direction, float resolution, out int face, out float2 uv)
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

    uv = float2(uSigned * 0.5 + 0.5, vSigned * 0.5 + 0.5);
    // Sample at the cell centre the C# side would land on, so both agree on which cell a direction owns.
    uv = (floor(saturate(uv) * resolution) + 0.5) / resolution;
}

// Water surface height in planet-radius units, or WATER_LEVEL_NO_WATER_MAX and below where none stands.
float SampleWaterLevel(float3 direction)
{
    if (_WaterLevelRes <= 0.0)
        return WATER_LEVEL_NO_WATER_MAX - 1.0;

    int face;
    float2 uv;
    WaterLevelFaceUv(direction, _WaterLevelRes, face, uv);
    return SAMPLE_TEXTURE2D_ARRAY_LOD(_WaterLevelTex, sampler_WaterLevelTex, uv, face, 0).r;
}

// The same solve carried further onto dry land. Answers "how high does water stand near here" for callers
// measuring a height ABOVE the water; never use it as a wetness test, because it deliberately covers ground
// that is dry.
float SampleShoreLevel(float3 direction)
{
    if (_ShoreLevelRes <= 0.0)
        return WATER_LEVEL_NO_WATER_MAX - 1.0;

    int face;
    float2 uv;
    WaterLevelFaceUv(direction, _ShoreLevelRes, face, uv);
    return SAMPLE_TEXTURE2D_ARRAY_LOD(_ShoreLevelTex, sampler_ShoreLevelTex, uv, face, 0).r;
}

// Local-space radius of the water surface above a direction, falling back to the global sea radius
// wherever no water stands. That fallback is what keeps ocean shorelines behaving exactly as before.
float WaterSurfaceRadiusAt(float3 direction, float fallbackSeaRadius)
{
    float level = SampleWaterLevel(direction);
    return level > WATER_LEVEL_NO_WATER_MAX ? _WaterLevelBaseRadius * (1.0 + level) : fallbackSeaRadius;
}

// Same, against the wider field. For "how far above the water is this ground" - the tight field ends one cell
// from the water, so a fade measured against it snaps from suppressed to clear across one cell boundary.
float ShoreSurfaceRadiusAt(float3 direction, float fallbackSeaRadius)
{
    float level = SampleShoreLevel(direction);
    return level > WATER_LEVEL_NO_WATER_MAX ? _WaterLevelBaseRadius * (1.0 + level) : fallbackSeaRadius;
}

#endif

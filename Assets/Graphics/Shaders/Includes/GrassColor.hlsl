#ifndef PROCEDURAL_PLANETS_GRASS_COLOR_INCLUDED
#define PROCEDURAL_PLANETS_GRASS_COLOR_INCLUDED

float GrassColorLuminance(float3 color)
{
    return dot(color, float3(0.299, 0.587, 0.114));
}

float3 GradeGrassTint(float3 tint, float saturation, float brightness)
{
    tint = saturate(tint);
    float luminance = GrassColorLuminance(tint);
    float3 neutral = float3(luminance, luminance, luminance);
    float3 graded = lerp(neutral, tint, saturate(saturation));
    return saturate(graded * max(brightness, 0.0));
}

float3 MatchGrassTintLuminance(float3 tint, float targetLuminance)
{
    float sourceLuminance = max(GrassColorLuminance(tint), 0.001);
    return saturate(tint * (max(targetLuminance, 0.0) / sourceLuminance));
}

// Single source for the distant-canopy albedo. Grass.shader's blades converge to this
// color at the far end of the fade band, and the terrain grass overlay paints with the
// same color, so the 3D canopy and the painted surface meet at one brightness.
#define GRASS_CANOPY_ALBEDO_SCALE 0.76

float3 GrassCanopyAlbedo(float3 bladeTint)
{
    return GradeGrassTint(bladeTint, 0.82, 0.98) * GRASS_CANOPY_ALBEDO_SCALE;
}

#endif

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

#endif

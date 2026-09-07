#ifndef SCATTER_DITHER_INCLUDED
#define SCATTER_DITHER_INCLUDED

TEXTURE2D(_ScatterDitherNoise);

// The shader importer binds URP's linear blue-noise texture. Both representations
// sample the same texel so their coverage remains complementary, without diagonal stripes.

float ScatterDitherThreshold(float4 screenPos)
{
    float2 pixel = floor(screenPos.xy / max(screenPos.w, 1e-4) * _ScreenParams.xy);
    uint width, height;
    _ScatterDitherNoise.GetDimensions(width, height);
    uint2 texel = uint2(pixel) % uint2(max(width, 1u), max(height, 1u));
    return clamp(LOAD_TEXTURE2D(_ScatterDitherNoise, texel).r, 0.00001, 0.99999);
}

float ScatterCardCoverage(float distance, float2 fadeIn, float2 fadeOut, float appear)
{
    float incoming = saturate((distance - fadeIn.x) / max(0.001, fadeIn.y - fadeIn.x));
    float outgoing = 1.0 - saturate((distance - fadeOut.x) / max(0.001, fadeOut.y - fadeOut.x));
    return incoming * outgoing * appear;
}

#endif

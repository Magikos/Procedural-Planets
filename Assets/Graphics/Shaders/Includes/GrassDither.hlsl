#ifndef GRASS_DITHER_INCLUDED
#define GRASS_DITHER_INCLUDED

// Shared Bayer 3x3 dither for grass fade clip. Used by Grass.shader (near-field +
// chunk-path tuft shader) so fade transitions use a stable stipple pattern - prevents
// visible seam.
//
// Stable across frames (no time / camera coordinate input) so dithered fade does not
// shimmer. Pattern varies by 2D screen-space pixel coords.
static const float _GrassBayer3x3[9] =
{
    0.0/9.0, 7.0/9.0, 3.0/9.0,
    6.0/9.0, 5.0/9.0, 2.0/9.0,
    4.0/9.0, 1.0/9.0, 8.0/9.0,
};

// Returns a stable dither value in [0,1) for the given clip-space pixel coordinates.
// Caller usage: `clip(fadeAlpha - SampleGrassDither(input.positionCS.xy));`
float SampleGrassDither(float2 positionCS)
{
    uint2 pix = (uint2)positionCS;
    return _GrassBayer3x3[(pix.x % 3u) + (pix.y % 3u) * 3u];
}

#endif

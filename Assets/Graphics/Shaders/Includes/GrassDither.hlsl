#ifndef GRASS_DITHER_INCLUDED
#define GRASS_DITHER_INCLUDED

// Shared dither for grass fade clip. Used by Grass.shader (near-field + chunk-path
// tuft shader) so fade transitions use a stable stipple pattern - prevents visible seam.
//
// Interleaved gradient noise (Jimenez 2014): effectively continuous threshold
// distribution, unlike the previous 3x3 Bayer whose 9 discrete levels quantized the
// fade band into visible step arcs. Stable across frames (no time / camera input)
// so the dithered fade does not shimmer.
float SampleGrassDither(float2 positionCS)
{
    return frac(52.9829189 * frac(dot(positionCS, float2(0.06711056, 0.00583715))));
}

#endif

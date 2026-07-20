#ifndef CLOUD_DENSITY_INCLUDED
#define CLOUD_DENSITY_INCLUDED

// Weather-driven vertical density profile shared by the sky march (Cloud.shader) and the
// ground-shadow proxy (CloudShadows.hlsl) so cloud height and shadow darkness never drift
// (audit finding D2 - both must evaluate the SAME shape). Pure function: callers pass their
// own uniforms so neither file redeclares globals.
//
// height01: 0 at cloud base, 1 at cloud top. Cloud type is chosen by:
//   storm        -> cumulonimbus (fills the whole shell, top-heavy, towering)
//   convectivity -> cumulus (rounded billow) vs stratus (thin flat sheet). This is an axis
//                   INDEPENDENT of density (driven by climate temperature): warm/convective air
//                   builds cumulus, cold/stable air layers into stratus - so a dense cloud is not
//                   automatically cumulus. Keying this on the moisture channel instead fails,
//                   because moisture tracks condensation, so every visible cloud reads the same.
// The three type weights sum to 1, so total coverage is preserved as a cell changes type.
float CloudVerticalProfile(float height01, float convectivity, float storm,
    float bottomFeather, float topFeather, float topBias)
{
    float h = saturate(height01);
    float bottomFade = smoothstep(0.0, max(bottomFeather, 0.0001), h);
    float topFadeFull = 1.0 - smoothstep(1.0 - saturate(topFeather), 1.0, h);

    float stratus = bottomFade * (1.0 - smoothstep(0.20, 0.45, h));
    float cumulus = bottomFade * (1.0 - smoothstep(0.55, 0.90, h));
    float cumulonimbus = bottomFade * topFadeFull * lerp(0.75, max(topBias, 0.5), h);

    float cb = saturate(storm);
    float cu = saturate(convectivity) * (1.0 - cb);
    float st = (1.0 - cb) * (1.0 - saturate(convectivity));

    return stratus * st + cumulus * cu + cumulonimbus * cb;
}

#endif

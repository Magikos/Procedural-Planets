# Atmosphere v3 — Implementation Reference

## Architecture Overview

Brute-force ray-marched Rayleigh + Mie scattering as a URP fullscreen post-process.
No baked LUT — both view and sun rays are marched in the fragment shader.

## Three Radii (Critical)

The atmosphere uses three concentric spheres, all set by AtmosphereController from PlanetGeneratedEvent data:

| Uniform | Value | Purpose |
|---------|-------|---------|
| `_PlanetRadius` | Sea level (ocean sphere radius) | Ray-planet intersection floor. Camera on beach is always above this. |
| `_DensityOriginRadius` | Same as `_PlanetRadius` | Height=0 for density calculations. `exp(-height/scaleHeight)` where height = `length(pos) - _DensityOriginRadius`. |
| `_AtmosphereRadius` | Max terrain radius × AtmosphereScale | Outer edge of atmosphere shell. |

**Why sea level?** Previous attempts used max elevation radius (~5257) which meant the camera on the beach (~5060) was "inside the planet" and got no atmosphere. Using the ocean sphere (~4994) ensures the camera is always inside the atmosphere shell. The depth buffer handles actual terrain occlusion — the ray-planet intersection sphere is just a safety floor.

**Why not lower?** Using 85% of max radius put the dense atmosphere 600 units underground — all orange, no blue sky. Sea level puts density=1.0 right at the ocean surface where the player stands.

## Scattering Coefficients (Critical)

Earth reference values (5.8e-3, 13.5e-3, 33.1e-3) are tuned for ~100km atmosphere on a 6371km planet.
Our planet has ~789 unit atmosphere on a ~4994 unit radius — proportionally 6× thicker.

**Scaled coefficients**: Divided by ~5 to produce similar total optical depth:
- Rayleigh: (1.2e-3, 2.8e-3, 6.9e-3)
- Blue optical depth through full atmosphere: ~3.5 (similar to Earth's ~3.3)
- Red optical depth: ~0.6 (red passes through — correct)

**If coefficients are too high**: Blue gets completely attenuated → everything orange/red.
**If coefficients are too low**: No color differentiation → gray/white sky.

## Tone Mapping (Critical)

Reinhard `x/(1+x)` is applied ONLY to in-scattered light, NOT to terrain color:
```hlsl
float3 toneMappedScatter = inScattered / (1.0 + inScattered);
float3 result = sceneColor * viewTransmittance + toneMappedScatter;
```

**Why?** Applying Reinhard to the full result compressed terrain colors toward gray (green grass → grayish-green). Separating them preserves terrain fidelity while preventing sky blowout.

## Current Parameters (Atmosphere Settings.asset)

| Parameter | Value | Notes |
|-----------|-------|-------|
| AtmosphereScale | 1.1 | Outer radius = maxRadius × 1.1 |
| ViewSteps | 16 | View ray march steps |
| SunSteps | 8 | Sun ray steps per view sample |
| SunIntensity | 17 | Multiplier on final scattered light |
| RayleighScattering | (1.2e-3, 2.8e-3, 6.9e-3) | Scaled for our planet size |
| RayleighScaleHeight | 0.08 | Fraction of atmosphere thickness |
| MieScattering | 0.002 | Scalar (renamed to _MieScatteringCoeff to avoid type conflict) |
| MieScaleHeight | 0.02 | Fraction of atmosphere thickness |
| MieAnisotropy | 0.76 | HG phase function forward scattering |
| SunDiscSize | 0.9995 | Smoothstep threshold for sun disc |
| SunDiscBlend | 0.002 | Smoothstep blend width |

Scale heights are stored as fractions (0.08 = 8% of atmosphere thickness). The controller converts to world units: `fraction * (atmosphereRadius - seaLevelRadius)`.

## File Roles

| File | Role |
|------|------|
| `Atmosphere.hlsl` | All scattering math, density, phase functions, ray marching |
| `Atmosphere.shader` | Fullscreen pass structure, vertex shader, depth read |
| `AtmosphereController.cs` | Sets shader globals, receives planet event, converts settings to world units |
| `AtmosphereSettings.cs` | ScriptableObject with all tunable parameters |
| `AtmosphereRenderFeature.cs` | URP renderer feature, creates material, enqueues pass |
| `AtmosphereRenderPass.cs` | RenderGraph API, DrawProcedural, texture management |
| `AtmosphereDiagnostics.cs` | F12 screen capture, reads all shader globals, writes analysis |

## Shader Uniforms

All set via `Shader.SetGlobal*` from AtmosphereController (no material properties):

| Uniform | Type | Source |
|---------|------|--------|
| `_SunParams` | float3 | CelestialManager.SunDirection (normalized) |
| `_PlanetCenter` | float3 | Planet transform position |
| `_PlanetRadius` | float | Sea level radius |
| `_DensityOriginRadius` | float | Same as _PlanetRadius |
| `_AtmosphereRadius` | float | maxRadius × AtmosphereScale |
| `_ViewSteps` | int | 16 |
| `_SunSteps` | int | 8 |
| `_RayleighScattering` | float3 | Coefficients per channel |
| `_RayleighScaleHeight` | float | In world units (fraction × thickness) |
| `_MieScatteringCoeff` | float | Scalar (NOT float3 — renamed to avoid Unity type conflict) |
| `_MieScaleHeight` | float | In world units |
| `_MieAnisotropy` | float | HG phase g parameter |
| `_SunIntensity` | float | Light multiplier |
| `_SunDiscSize` | float | Smoothstep threshold |
| `_SunDiscBlend` | float | Smoothstep width |
| `_DebugMode` | int | 0-5 |

**Important**: `_MieScattering` was renamed to `_MieScatteringCoeff` because the old v1 shader registered `_MieScattering` as a float3 in Unity's global property sheet. Changing type within a session causes errors. The rename avoids the conflict.

## Known Limitations

- **Performance**: 128 ray marches per pixel (16 view × 8 sun). No LUT optimization yet.
- **No stars**: Night sky is black. Stars planned as next feature.
- **No night ambient**: Night side has no ambient light — pitch black.
- **No ozone/absorption**: Upper atmosphere color layer not implemented.
- **No aerial perspective**: Distant terrain doesn't fade into atmosphere.
- **No clouds**: Will need to integrate with atmosphere when added.

## Lessons Learned (for future changes)

1. **Don't copy reference coefficients** — they're tuned for specific planet scales. Calculate optical depth and verify it's ~3-4 for blue channel through full atmosphere.
2. **Sea level as density origin** — any lower and the dense atmosphere is underground (all orange). Any higher and the camera on the beach has no atmosphere.
3. **Tone map scatter only** — applying to full result kills terrain color.
4. **Unity global property types are sticky** — renaming a uniform is safer than changing its type.
5. **Scene serialization overrides code defaults** — use ScriptableObject assets (YAML) for settings.
6. **PlanetVertexColor.shader needs DepthOnly pass** — without it, atmosphere can't read depth buffer.
7. **RaySphere returns (dstToNear, dstThrough)** — not (near, far). `hitAtmo.y` is distance through, not far point.
8. **Test with version tags in logs** — `[AtmosphereController v3.5]` confirms Unity compiled the latest code.

## Git References

- Tag: `atmosphere-v2-checkpoint` — last commit before v3 rewrite
- Tag: `atmosphere-v3-good` — best visual result, committed working state
- Branch: `phase4-biomes`

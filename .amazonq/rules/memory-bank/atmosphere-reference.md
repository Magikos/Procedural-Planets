# Atmosphere Implementation Reference — All Projects Compared

## The Four Reference Implementations

### 1. URP-Atmosphere (Kai Angulo / ShaderToy port)
**Our original basis. Scale-dependent, works at specific radius.**

**Key characteristics:**
- Separate Rayleigh/Mie/Ozone density channels (3-channel LUT: `float3`)
- LUT baked at **world scale** (passes actual `planetRadius` and `atmosphereRadius` to compute)
- **Phase functions**: Rayleigh `3/(16π)(1+cos²θ)` and Mie HG phase
- **Separate accumulation**: `totalRay` and `totalMie` accumulated separately, combined with phase at the end
- **Opacity**: `exp(-coefficients * opticalDepth)` computed from accumulated density (NOT from LUT)
- **Final**: `(rayleigh * phaseRay + mie * phaseMie + ambient) * intensity + sceneColor * opacity`
- **No `/planetRadius`** — coefficients are raw physical values tuned for specific scale
- LUT format: `ARGBHalf` (4-channel, stores Rayleigh/Mie/Ozone densities)
- Compute `RWTexture2D<float4>` matches `ARGBHalf`

**Critical difference from our code:**
- Accumulates `density * stepSize` into `opticalDepth` INSIDE the loop (running sum)
- Uses running `opticalDepth` + baked `lightOpticalDepth` for attenuation
- Does NOT use `OpticalDepthBaked2` for view ray — computes it incrementally
- Opacity for scene color uses the accumulated `opticalDepth`, not a baked lookup

### 2. Solar System (Sebastian Lague — early project)
**Wavelength-based, `/planetRadius` normalization. Simple but scale-dependent in practice.**

**Key characteristics:**
- Single-channel density (no separate Rayleigh/Mie)
- LUT baked at **normalized scale** (planetRadius=1)
- **No phase functions** — uniform scattering in all directions
- Single `scatteringCoefficients` from wavelengths: `(400/λ)^4 * strength`
- `opticalDepthBaked2` for bidirectional view ray sampling
- **Final**: `inScatteredLight *= coefficients * intensity * stepSize / planetRadius`
- **Scene attenuation**: Hacky `exp(-viewRayOD * intensity * 3)` with brightness adaptation
- Planet radius: **100-1000 units** (small)
- Tuned values (Humble Abode asset): `densityFalloff: 4.3`, `scatteringStrength: 21.23`, `atmosphereScale: 0.322`

**Critical difference:**
- The `/planetRadius` compensates for `stepSize` being in world units
- The hacky attenuation works at small scale but breaks at large scale
- `opticalDepthBaked2` uses bidirectional blending to handle rays passing through dense regions

### 3. Fluid Planet (Sebastian Lague — newer project)
**Same as Solar System but with cleaner scene attenuation.**

**Key characteristics:**
- Identical loop to Solar System
- **Scene attenuation**: `originalCol * transmittance` — uses the transmittance from the LAST loop iteration directly
- No hacky `exp(-OD * intensity * 3)` — just raw transmittance
- Same `/planetRadius` normalization
- Same single-channel LUT at normalized scale

**Critical difference from Solar System:**
- The `transmittance` variable at end of loop = `exp(-(sunOD + viewOD) * coefficients)`
- This naturally handles scene dimming — more atmosphere = more dimming
- Much cleaner and more physically correct

### 4. Geographical Adventures (Sebastian Lague — most mature, full game)
**Multi-LUT approach, physically-based, tone-mapped.**

**Key characteristics:**
- **3-channel transmittance LUT** (RGB, stores actual transmittance not optical depth)
- **Aerial perspective LUT** (3D texture, precomputed per-pixel scattering)
- **Sky rendered to separate texture** (compute shader, high step count ~100)
- Separate Rayleigh/Mie/Ozone with proper extinction
- **Phase functions**: Rayleigh + Mie HG
- **Normalization**: `scaledStepSize = stepSize / atmosphereThickness`
- **Better integration**: `(inScattering - inScattering * sampleTransmittance) / extinction` (converges faster)
- **Tone mapping**: Reinhard extended with white point, contrast, intensity
- **Transmittance LUT**: Stores `getSunTransmittance()` result (RGB transmittance, not scalar OD)
- Planet center at origin (0,0,0)

**Critical differences:**
- The transmittance LUT stores **RGB transmittance** (3 channels), not scalar optical depth
- Uses `/ atmosphereThickness` not `/ planetRadius`
- Per-step transmittance accumulated multiplicatively: `transmittance *= exp(-extinction * scaledStepSize)`
- Tone mapping prevents white blowout
- Aerial perspective (surface view) is a SEPARATE pass from sky rendering

---

## Key Architectural Differences

### LUT Contents
| Project | LUT Channels | LUT Stores | LUT Scale |
|---------|-------------|------------|-----------|
| URP-Atmosphere | 3 (RGB) | Rayleigh/Mie/Ozone density integrals | World scale |
| Solar System | 1 (R) | Scalar optical depth | Normalized (planetRadius=1) |
| Fluid Planet | 1 (R) | Scalar optical depth | Normalized (planetRadius=1) |
| Geo Adventures | 3 (RGB) | RGB transmittance values | World scale |

### View Ray Optical Depth
| Project | Method |
|---------|--------|
| URP-Atmosphere | Accumulated incrementally in loop (`opticalDepth += density * stepSize`) |
| Solar System | `opticalDepthBaked2()` — bidirectional LUT lookup |
| Fluid Planet | `opticalDepthBaked2()` — bidirectional LUT lookup |
| Geo Adventures | Accumulated incrementally (`transmittance *= exp(-extinction * scaledStepSize)`) |

### Scene Color Attenuation
| Project | Method |
|---------|--------|
| URP-Atmosphere | `sceneColor * exp(-coefficients * accumulatedOpticalDepth)` |
| Solar System | `sceneColor * exp(-viewRayOD * intensity * 3)` (hacky) |
| Fluid Planet | `sceneColor * transmittance` (from last loop iteration) |
| Geo Adventures | `sceneColor * transmittanceLUT3D` (precomputed 3D texture) |

### Normalization
| Project | Step Size Normalization |
|---------|----------------------|
| URP-Atmosphere | None — raw world-scale coefficients |
| Solar System | `* stepSize / planetRadius` on final result |
| Fluid Planet | `* stepSize / planetRadius` on final result |
| Geo Adventures | `scaledStepSize = stepSize / atmosphereThickness` used throughout loop |

### Phase Functions
| Project | Rayleigh Phase | Mie Phase |
|---------|---------------|-----------|
| URP-Atmosphere | `3/(16π)(1+cos²θ)` | HG phase with anisotropy `g` |
| Solar System | None | None |
| Fluid Planet | None | None |
| Geo Adventures | `3/(16π)(1+cos²θ)` (commented out, set to 1) | HG phase with g=0.8 |

---

## Our Current Implementation

**Hybrid of Solar System + Fluid Planet:**
- Single-channel LUT at normalized scale (Solar System)
- `opticalDepthBaked2` for view ray (Solar System)
- `sceneColor * transmittance` attenuation (Fluid Planet)
- `* stepSize / planetRadius` normalization (Solar System)
- No phase functions
- No tone mapping
- Planet radius: ~5257 (much larger than any reference)

**Known issues:**
- White blowout on planet surface during daytime
- In-scattered light exceeds 1.0 on all channels → appears white
- Transmittance from loop may not correctly attenuate scene color

---

## Root Cause Analysis: Why White?

The white comes from `inScatteredLight` having all RGB channels > 1.0.

With our values (radius 5257, strength 20, falloff 4, atmosphereScale 0.15):
- Blue coefficient: `(400/460)^4 * 20 = 11.44`
- stepSize / planetRadius ≈ 0.0167
- If `sum(density * transmittance)` over 10 steps ≈ 5-8
- Blue channel: `8 * 11.44 * 0.0167 ≈ 1.53` → exceeds 1.0
- Red channel: `8 * 2.14 * 0.0167 ≈ 0.29` → fine
- Green channel: `8 * 6.46 * 0.0167 ≈ 0.86` → close to 1.0

So blue clips at 1.0, green is close, red is low → should appear blue-white, not pure white.

**But if transmittance ≈ 1 (weak attenuation), the density sum is higher:**
- With LUT optical depth ~0.04 (surface-up) and coefficients ~11:
- `transmittance = exp(-0.04 * 11.44) = exp(-0.46) = 0.63` for blue
- So blue transmittance reduces accumulation... but red transmittance = `exp(-0.04 * 2.14) = 0.92`
- Red accumulates at nearly full strength

The issue may be that **red and green accumulate too much** relative to blue because their transmittance is higher (less attenuation). This makes the color shift toward white.

---

## Possible Solutions

### Option A: URP-Atmosphere approach (proven at any scale)
- Use 3-channel LUT (Rayleigh/Mie/Ozone)
- Bake at world scale
- Accumulate optical depth incrementally in loop (no `opticalDepthBaked2`)
- Use phase functions
- Use raw coefficients (no wavelength calculation)
- Scene attenuation via accumulated optical depth

**Pros:** Scale-independent by design, phase functions add realism
**Cons:** Need to tune raw Rayleigh/Mie coefficients per planet scale, 3-channel LUT

### Option B: Geographical Adventures approach (best quality)
- Multi-LUT system (transmittance + aerial perspective + sky)
- Tone mapping to prevent blowout
- Phase functions
- `/ atmosphereThickness` normalization

**Pros:** Best visual quality, handles all edge cases
**Cons:** Most complex, multiple compute passes, 3D textures

### Option C: Fix current approach (Solar System + Fluid Planet hybrid)
- Keep single-channel LUT
- Keep `/ planetRadius` normalization
- **Add tone mapping** to prevent white blowout (Reinhard extended)
- **Add Rayleigh phase function** to reduce uniform scattering
- Tune `scatteringStrength` down until values stay in displayable range

**Pros:** Minimal changes
**Cons:** Tone mapping is a band-aid, doesn't fix the underlying scale issue

### Option D: Revert to URP-Atmosphere with scale fix
- Go back to the original URP-Atmosphere code (3-channel, phase functions, world-scale LUT)
- Add the `/planetRadius` normalization only where needed
- Keep our RenderGraph infrastructure

**Pros:** Was working before (just scale-dependent), known good code
**Cons:** Need to figure out where exactly to add scale normalization

---

## Recommended Approach

**Step-by-step from first principles** (from `local-only/atmospheric_scattering_shader_unity_guide.md`).

Previous attempts to copy reference implementations failed because:
1. Baked LUT adds scale-dependent complexity — remove it initially
2. Reference coefficients are tuned for specific radii — don't copy them
3. The `* (1 - height01)` density term from URP-Atmosphere changes the profile — use standard `exp(-height/scaleHeight)`
4. Trying to do all steps at once makes debugging impossible — build incrementally
5. The diagnostics showed `_DirToSun` was reading wrong uniform name (`_SunParams` vs `_DirToSun`) — always verify globals match between C# and shader
6. Scene serialization kept overriding code defaults — use ScriptableObject assets
7. The DepthOnly pass was missing from PlanetVertexColor.shader — atmosphere couldn't see terrain
8. The `_CutoffRadius` killed atmosphere when camera was on the surface (below max elevation)

The new approach: brute-force ray march both view and sun rays, verify each step with debug modes, then optimize with LUTs once it's visually correct.

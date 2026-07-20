# Audit 2026-07-01 — Grass near→mid→far transitions + general findings

- **Commit audited:** `ec0b1cd` (branch `code-refactor`)
- **Status:** IMPLEMENTED 2026-07-01 (Bryan approved; Codex feedback amendments applied). G1–G5, G7, PERF-1/2 landed: distances live in `IGrassQualitySettings` (no separate profile — Codex amendment), fades are alpha/dither-only via IGN, shared `GrassCanopyAlbedo`, overlay window = blade fade band, G3 fully reverted after in-game testing (2026-07-02): the widened remap left a bright wash along biome-blend borders and the linear orbit attenuation weakened the blanket from altitude — Codex's G3 caution confirmed; the original coverage formula stands. G2's overlay brightness likewise reverted to the hand-tuned absolute color (0.46 through the shared 0.76 canopy scale) because the brighter paint glowed as a halo at partial coverage — the remaining 200m brightness match is a lighting-model difference, tune live via `grass.surface-brightness`, altitude fade uses `_GrassChunkFade` *after* its semantics changed to pure alpha (Codex G7 caution). **G6 (chunk layer delete/promote) deliberately not executed — Bryan's call.** ARCH-1 execution-order migrations not executed. Needs in-Unity visual verification (shaders can't compile outside the editor).
- **Priority focus:** sharp visible lines in the grass near→mid→far handoff.
- **Scope not audited:** third-party packages (QFSW, GrassFlow, Shapes, sc.stylizedgrass — all unused by the planet grass path), caustics (don't-touch rule), editor tooling, water/cloud subsystems beyond spot checks.

---

## Part 0 — The grass LOD system as it actually ships

Three layers, but only two are on by default:

| Layer | Owner | On by default | Distance window (defaults) |
|---|---|---|---|
| **Near field** (dense camera-centered blades) | `GrassNearFieldController` + `GrassNearFieldPlace.compute` | yes | full density 0–144 m, thin+fade 144–200 m, hard cull 200 m |
| **Chunk / mid** (per-chunk blade buffers) | `GrassPlacementController` + `BiomeGrassPlace.compute` | **no** (`_chunkGrassEnabled = false`, [PlanetGrassCoordinator.cs:18](../../Assets/Scripts/Planet/PlanetGrassCoordinator.cs#L18)) | fade in 128–220 m, fade out 200–240 m, peak coverage 0.42 |
| **Far overlay** (grass painted into terrain albedo) | `PlanetGrassCoordinator` → `PlanetVertexColor.shader` | yes | fades in over view distance 24–120 m, then constant |

So with default flags the visible handoff is: **dense 3-D blades → (nothing) → flat painted terrain**, and the entire 3-D→2-D transition must happen inside the 144–200 m band. Every finding below either sharpens that band or leaves a mismatch across it.

### Where the constants live (this is itself finding G5)

| Value | Meaning | Defined at |
|---|---|---|
| 144 / 200 / 56 | near full-density / draw distance / fade band | [GrassNearFieldController.cs:40-42](../../Assets/Scripts/Planet/Grass/GrassNearFieldController.cs#L40-L42) |
| 144 / 200, 200–600 | overlay `nearWeight` / `midWeight` (debug-only) | [PlanetVertexColor.shader:731-733](../../Assets/Graphics/Shaders/PlanetVertexColor.shader#L731-L733) (hardcoded copies) |
| 24 / 120 | overlay fade-in start / end | [PlanetGrassCoordinator.cs:43-44](../../Assets/Scripts/Planet/PlanetGrassCoordinator.cs#L43-L44) (overrides shader defaults 35/260) |
| 128 / 220 | chunk-layer inner fade | [GrassPlacementController.cs:9-10](../../Assets/Scripts/Planet/Grass/GrassPlacementController.cs#L9-L10) |
| 200 / 240 | chunk-layer LowLod / MaxRenderDistance | [QualityController.cs:25-26](../../Assets/Scripts/Core/QualityController.cs#L25-L26) |
| 120→240 | blade→canopy color handoff | [Grass.shader:291](../../Assets/Graphics/Shaders/Grass.shader#L291) |
| 150→420 | billboard turn | [Grass.shader:227](../../Assets/Graphics/Shaders/Grass.shader#L227) |
| 160→500 | width inflation (coverage preservation) | [Grass.shader:244](../../Assets/Graphics/Shaders/Grass.shader#L244) |
| 0.42 | chunk peak coverage | [GrassChunkRuntime.cs:16](../../Assets/Scripts/Planet/Grass/GrassChunkRuntime.cs#L16) |
| 0.35 | overlay surface brightness | [PlanetGrassCoordinator.cs:41](../../Assets/Scripts/Planet/PlanetGrassCoordinator.cs#L41) |

---

## Part 1 — Grass transition findings (the sharp lines)

Ordered by contribution to the visible artifact. G1–G4 together explain the lines; G5 is the structural cause that lets them drift apart.

---

### [G1] The 144–200 m band fades twice and dithers at 9 levels — concentric banding + a hard horizon

- **Evidence:**
  - Placement compute stochastically deletes blades over 144–200 m: [GrassNearFieldPlace.compute:504-514](../../Assets/Resources/GrassNearFieldPlace.compute#L504-L514) (`distanceKeep` smoothstep, hard cull at `_NearFieldDrawDistance` line 499).
  - The shader *also* fades the survivors over the exact same 144–200 m window: [GrassNearFieldController.cs:196-197](../../Assets/Scripts/Planet/Grass/GrassNearFieldController.cs#L196-L197) sets `_GrassVisualFadeStart/End = 144/200`, consumed at [Grass.shader:215-216](../../Assets/Graphics/Shaders/Grass.shader#L215-L216).
  - That shader fade does three things at once: shrinks height *and* width to zero ([Grass.shader:245-247](../../Assets/Graphics/Shaders/Grass.shader#L245-L247)), darkens albedo via `edgeShade = lerp(0.55, 1.0, fadeAlpha)` ([Grass.shader:335](../../Assets/Graphics/Shaders/Grass.shader#L335)), and dither-clips with a **3×3 Bayer matrix = 9 discrete alpha levels** ([GrassDither.hlsl:10-23](../../Assets/Graphics/Shaders/Includes/GrassDither.hlsl#L10-L23), used at [Grass.shader:309](../../Assets/Graphics/Shaders/Grass.shader#L309)).
- **Impact:** perceived coverage falls as the *product* of density-thinning × dither-alpha × width-shrink — a roughly cubic falloff crammed into 56 m. Most of the grass is gone by ~170 m, then a sparse, darkened, shrinking residue survives to exactly 200 m where it hard-stops. The 9-level dither quantizes `fadeAlpha` into visible concentric step-arcs around the camera (each Bayer threshold crossing is a "sharp line"), and the triple-darkening makes the last band read as a dark ring against the brighter overlay behind it.
- **Effort:** M. **Risk:** LOW (visual tuning, all knobs already exist). **Confidence:** HIGH.
- **Fix sketch:** give each mechanism its own job instead of stacking them on one band:
  1. Let **placement thinning** own the density ramp (it already fades to zero smoothly).
  2. Let the **shader fade** own only the *individual blade* exit — shrink height near each blade's own stochastic death distance, not globally.
  3. Replace the 3×3 Bayer with interleaved gradient noise (effectively continuous, still stable per-pixel).
  4. Stop darkening fading blades (`edgeShade`) — darkening + thinning double-signals the boundary.

**Code example — dither upgrade (drop-in for GrassDither.hlsl):**

```hlsl
// Interleaved gradient noise (Jimenez 2014). Stable per pixel, no frame/time input,
// ~continuous threshold distribution instead of 9 Bayer levels.
float SampleGrassDither(float2 positionCS)
{
    return frac(52.9829189 * frac(dot(positionCS, float2(0.06711056, 0.00583715))));
}
```

**Code example — decouple the two fades.** In `GrassNearFieldController` constructor, move the visual fade window to cover only the *tail* of the placement fade so the shader isn't re-fading blades the compute already thinned:

```csharp
// Placement thinning (compute) owns 144→200 density. The visual fade only softens
// each surviving blade's exit in the last stretch so there is no hard 200 m wall.
_material.SetFloat(VisualFadeStartId, Mathf.Max(0f, _drawDistance - 20f)); // 180
_material.SetFloat(VisualFadeEndId, _drawDistance);                        // 200
```

And in `Grass.shader`, remove the fade-driven darkening (keep the dither clip):

```hlsl
// was: float edgeShade = lerp(0.55, 1.0, saturate(input.fadeAlpha));
float edgeShade = 1.0;
```

Verify with: `grass.overlay-status`, F10 capture at 100/150/180/200 m eye distance, and the near-field stats (`DistanceFadeRejectedCells` should still climb across the band — placement thinning unchanged).

---

### [G2] Blade color and painted-overlay color are built from different constants and lit by different models — the 200 m horizon is a brightness step that moves with the sun

- **Evidence:**
  - Distant blades converge to `canopyAlbedo = GradeGrassTint(tint, 0.82, 0.98) * 0.76` ([Grass.shader:290](../../Assets/Graphics/Shaders/Grass.shader#L290)), lit by the grass wrap-diffuse model (`0.12 + wrapDiffuse * surfaceDirect * 0.82`, [Grass.shader:357](../../Assets/Graphics/Shaders/Grass.shader#L357)).
  - The overlay builds `grassSurface = GradeGrassTint(tint, 0.82, 0.98) * surfaceVariation * _GrassSurfaceBrightness` with `_GrassSurfaceBrightness = 0.35` hand-tuned ([PlanetVertexColor.shader:801-807](../../Assets/Graphics/Shaders/PlanetVertexColor.shader#L801-L807), [PlanetGrassCoordinator.cs:41](../../Assets/Scripts/Planet/PlanetGrassCoordinator.cs#L41)), then lit by the **terrain** lighting path.
  - The blade→canopy color handoff `smoothstep(120, 240, viewDistance)` ([Grass.shader:291](../../Assets/Graphics/Shaders/Grass.shader#L291)) never completes: blades die at 200 m, where the mix is only 74 % canopy.
- **Impact:** at the blade horizon the ground behind is ~half the brightness of the canopy in front (0.76 vs ≈0.35×variation), *and* the two sides respond differently to sun angle (wrap diffuse vs terrain N·L), so no single `surface-brightness` value can hide the seam — it reappears at different times of day. This is the sharpest single line in the default configuration.
- **Effort:** M. **Risk:** MED (touches the tuned overlay look; the `grass.surface-brightness` console knob keeps live recovery possible). **Confidence:** HIGH.
- **Fix sketch:** make the overlay color a *function of the canopy color pipeline*, not an independently tuned constant, and match the diffuse response for grass-covered texels.

**Code example — shared canopy constant.** Add to `GrassColor.hlsl` so both shaders compile the same number:

```hlsl
// Single source for the distant-canopy albedo scale. Grass.shader multiplies
// canopyAlbedo by this; the terrain overlay multiplies grassSurface by the same
// value so the 3D canopy and the painted surface converge to one brightness.
#define GRASS_CANOPY_ALBEDO_SCALE 0.76

float3 GrassCanopyAlbedo(float3 bladeTint)
{
    return GradeGrassTint(bladeTint, 0.82, 0.98) * GRASS_CANOPY_ALBEDO_SCALE;
}
```

In `Grass.shader:290`: `float3 canopyAlbedo = GrassCanopyAlbedo(blade.Color.rgb);`
In `PlanetVertexColor.shader` `ApplyGrassSurfaceAlbedo`: `float3 grassSurface = GrassCanopyAlbedo(eval.tint);` then keep `surfaceVariation` as a *zero-mean* modulation (re-center its lerps around 1.0) and let `_GrassSurfaceBrightness` default to **1.0** as a pure trim knob rather than carrying the entire match. Also finish the canopy handoff before the cull: change `smoothstep(120.0, 240.0, viewDistance)` to `smoothstep(120.0, 200.0, viewDistance)` so blades are 100 % canopy-colored when they hand off.

Lighting match is the follow-up: where `grassCoverage > 0`, lerp the terrain diffuse term toward the same wrap-diffuse used by blades (`saturate(NdotL * 0.72 + 0.28)`). Do this only after the albedo unification — it may already be close enough.

---

### [G3] `grassCoverage = smoothstep(0.05, 0.55, farWeight)` re-sharpens every soft edge in the overlay

- **Evidence:** [PlanetVertexColor.shader:770](../../Assets/Graphics/Shaders/PlanetVertexColor.shader#L770). Input `farWeight = envCoverage * farMask * approachWeight * strength` is already smooth ([:735](../../Assets/Graphics/Shaders/PlanetVertexColor.shader#L735)), and `envCoverage` already applies `pow(density·slope·water, 0.62)` ([:730](../../Assets/Graphics/Shaders/PlanetVertexColor.shader#L730)).
- **Impact:** everything feeding `farWeight` gets its gradient compressed ~2×: the distance fade-in ring lands entirely inside ~27–77 m instead of 24–120, biome-density edges become crisp painted borders, and the altitude/orbit attenuation (`approachWeight`, max 0.42 in orbit) sits right on the steep part of the curve so small altitude changes visibly swing coverage. These are the sharp *ground* lines, both radial (distance ring) and irregular (biome borders).
- **Effort:** S. **Risk:** LOW. **Confidence:** HIGH.
- **Fix sketch:** widen or delete the remap; the 0.05 floor (ignore trace coverage) is worth keeping, the 0.55 ceiling is not:

```hlsl
// was: float grassCoverage = smoothstep(0.05, 0.55, eval.farWeight);
float grassCoverage = smoothstep(0.05, 0.95, eval.farWeight);
```

If the intent of the steep remap was "reach full paint quickly with distance," express that in `farMask`'s own start/end instead, where it doesn't also sharpen biome edges.

---

### [G4] The overlay's distance window (24–120 m) is not tied to the blade fade band (144–200 m) — two independent rings

- **Evidence:** overlay fade-in constants at [PlanetGrassCoordinator.cs:43-44](../../Assets/Scripts/Planet/PlanetGrassCoordinator.cs#L43-L44) (`GrassFarOverlayStart = 24`, `End = 120`); blade fade band 144–200 at [GrassNearFieldController.cs:40-42](../../Assets/Scripts/Planet/Grass/GrassNearFieldController.cs#L40-L42). Neither reads the other; the shader's own defaults (35/260) are a third pair that applies if `ApplyTerrainOverlay` is never called.
- **Impact:** two visible transitions where one should exist: the ground turns painted-grass-dark at ~30–77 m (under *full-density* blades, so it reads as a ground-color ring through the blade gaps), then the blades themselves fade at 144–200 m against an overlay that stopped changing 80 m earlier. Tuning either side never fixes the other because they're separate constants.
- **Effort:** S (once G5's shared profile exists, this is wiring). **Risk:** LOW-MED (the early overlay start may be intentional under-canopy darkening — see fix). **Confidence:** HIGH on mechanism; MED on how much of the visible artifact is this vs G2.
- **Fix sketch:** split the overlay's two jobs. Job 1: darken ground *under* the blade canopy (can start near, but should be a subtle multiply toward the canopy shadow color, not full grass paint). Job 2: *replace* blades beyond the fade band (must ramp exactly over `fullDensityDistance → drawDistance`). Concretely, drive the constants from the shared profile:

```csharp
// PlanetGrassCoordinator.ApplyTerrainOverlay — replace the hardcoded consts:
SetMaterialFloatIfPresent(mat, _grassFarOverlayStartId, GrassLodProfile.FullDensityDistance); // 144
SetMaterialFloatIfPresent(mat, _grassFarOverlayEndId, GrassLodProfile.DrawDistance);          // 200
```

plus a separate cheap under-canopy tint in the terrain shader if the near ground reads too bright once the early paint is gone (start with none; add only if a capture shows the need).

---

### [G5] Root cause: nine transition constants in six files, several stale — the coverage-preservation mechanisms never actually engage

- **Evidence:** table in Part 0. The stale ones, measured against the 200 m draw distance:
  - width inflation `smoothstep(160, 500, d)` ([Grass.shader:244](../../Assets/Graphics/Shaders/Grass.shader#L244)) reaches only **×1.016** at 200 m — the mechanism whose comment says "preserve projected coverage as physical density thins" contributes ~2 % before every blade is gone;
  - billboard turn `smoothstep(150, 420, d) * 0.78` ([Grass.shader:227](../../Assets/Graphics/Shaders/Grass.shader#L227)) reaches **0.07** of its 0.78 maximum;
  - canopy color handoff `smoothstep(120, 240, d)` reaches 74 % (G2);
  - the terrain shader hardcodes its own copies of 144/200 ([PlanetVertexColor.shader:731-733](../../Assets/Graphics/Shaders/PlanetVertexColor.shader#L731-L733)).
  These ranges are consistent with an earlier ~400–500 m draw distance that was later reduced to 200 without re-tuning.
- **Impact:** the blades thin across 144–200 m with *no* compensating width growth or billboard flattening — pure density loss, which is exactly what produces a "thinning cliff" instead of a soft aggregate. And because every layer owns private copies, any future tuning re-introduces seams (this already happened at least once).
- **Effort:** M. **Risk:** LOW (mechanical consolidation + retune of three smoothstep ranges). **Confidence:** HIGH.
- **Fix sketch:** one static profile in C#, pushed to every grass material and the terrain material as uniforms; shaders stop hardcoding distances.

**Code example — the profile and its binding:**

```csharp
// GrassLodProfile.cs — single source of truth for every grass LOD distance.
// All layers and shaders receive these as uniforms; no shader hardcodes a distance.
public static class GrassLodProfile
{
    public const float FullDensityDistance = 144f; // near blades at authored density
    public const float DrawDistance = 200f;        // last near blade
    public const float FadeBand = DrawDistance - FullDensityDistance;

    // Perceptual compensation ramps all end AT DrawDistance so they finish
    // before the geometry disappears.
    public const float WidthInflateStart = FullDensityDistance * 0.7f; // ~100
    public const float WidthInflateEnd = DrawDistance;                 // 200
    public const float BillboardStart = FullDensityDistance * 0.7f;
    public const float BillboardEnd = DrawDistance;
    public const float CanopyColorStart = FullDensityDistance * 0.8f;  // ~115
    public const float CanopyColorEnd = DrawDistance;
}
```

In `Grass.shader`, replace the literals with uniforms (`_GrassWidthInflateStart/End`, `_GrassBillboardStart/End`, `_GrassCanopyColorStart/End`) set once per material from the profile; e.g.:

```hlsl
// was: width *= lerp(1.0, 1.42, smoothstep(160.0, 500.0, viewDistance));
width *= lerp(1.0, 1.42, smoothstep(_GrassWidthInflateStart, _GrassWidthInflateEnd, viewDistance));
// was: float billboardWeight = smoothstep(150.0, 420.0, viewDistance) * 0.78;
float billboardWeight = smoothstep(_GrassBillboardStart, _GrassBillboardEnd, viewDistance) * 0.78;
// was: float canopyHandoff = smoothstep(120.0, 240.0, viewDistance);
float canopyHandoff = smoothstep(_GrassCanopyColorStart, _GrassCanopyColorEnd, viewDistance);
```

`GrassNearFieldController`, `GrassPlacementController` (via `IGrassQualitySettings`), and `PlanetGrassCoordinator.ApplyTerrainOverlay` all read the profile instead of local consts. Remove the hardcoded 144/200/600 from `EvaluateGrassOverlay` (they feed only the debug output — pass the uniforms there too).

This is the highest-leverage grass change: G1's re-split, G2's handoff completion, and G4's alignment all become one-line edits against the profile. **Do G5 first.**

---

### [G6] The mid (chunk) layer: currently dead weight — decide promote or delete

- **Evidence:** `_chunkGrassEnabled = false` default ([PlanetGrassCoordinator.cs:18](../../Assets/Scripts/Planet/PlanetGrassCoordinator.cs#L18)). If enabled, chunk blades render with `_GrassChunkFade = 0.42` ([GrassChunkRuntime.cs:16,130](../../Assets/Scripts/Planet/Grass/GrassChunkRuntime.cs#L16)), which in `Grass.shader` multiplies into `visualEdgeFade` (line 217): height/width shrink to ~38 %, dither passes ~42 %, and albedo drops to 68 % via `lerp(0.45, 1.0, _GrassChunkFade)` (line 337). The near field renders the same shader with `ChunkFade = 1`.
- **Impact:** ~1,050 lines of maintained controller/dispatcher/compute (plus a pooled buffer per resident chunk) ship disabled. If turned on as-is, the mid tier is one-third-height, darker grass — the near→mid boundary at ~200 m would itself be a sharp height-and-brightness line.
- **Effort:** delete = S; promote = M-L. **Risk:** delete LOW (git preserves it); promote MED. **Confidence:** HIGH on the mismatch math; the promote/delete call is Bryan's.
- **Fix sketch:** two honest options.
  - **Delete** (consistent with the dead-code rule and with the G1–G5 plan, which makes near+overlay self-sufficient): remove `GrassPlacementController`, `GrassChunkDispatcher`, `GrassChunkRuntime`, `GrassBladeBufferPool`, `GrassChunkResidencyResolver`, `BiomeGrassPlace.compute`, the `GrassRenderLayer.Chunk` enum member and coordinator wiring, and the chunk-stat fields in `GrassDebugModule`.
  - **Promote**: make `_GrassChunkFade` drive *placement density only* (it already exists as `innerKeep` in [BiomeGrassPlace.compute:438-446](../../Assets/Resources/BiomeGrassPlace.compute#L438-L446)) and stop it from scaling height/albedo in the shader, so mid blades look identical to near blades and only their *count* differs; align windows to the profile (mid fades in exactly where near thins: 144→200 in, out at 400+ with the overlay ramping under it).
  - Recommendation: **delete** unless the 200 m blade horizon still bothers after G1–G5 land; the fixed near+overlay pair is the simpler system, and a longer near-field `DrawDistance` (paid for by the freed chunk budget) is a cheaper way to push blades out than a third layer.

---

### [G7] Near-field layer pops in/out whole at the 350/500 m altitude gate, reallocating 48 MB each time

- **Evidence:** activation check per tick at [PlanetGrassCoordinator.cs:112-124](../../Assets/Scripts/Planet/PlanetGrassCoordinator.cs#L112-L124); thresholds `NearFieldActivationAltitude = 350` / `Deactivation = 500` ([QualityController.cs:35-36](../../Assets/Scripts/Core/QualityController.cs#L35-L36)). Deactivation calls `Dispose()`, activation constructs a new controller whose constructor allocates the 1 M-instance buffer (`~48 MB`, [GrassNearFieldController.cs:47,230](../../Assets/Scripts/Planet/Grass/GrassNearFieldController.cs#L47)).
- **Impact:** flying up through ~500 m altitude, every blade disappears in one frame (and reappears in one frame on descent) — a different "sharp line," vertical this time. Each crossing also frees and reallocates the GPU buffer inside the hysteresis band.
- **Effort:** S-M. **Risk:** LOW. **Confidence:** HIGH (mechanism read from code; pop not visually confirmed this session).
- **Fix sketch:** keep the controller alive across the gate and fade instead of destroy: coordinator computes `altitudeFade = 1 - smoothstep(350, 500, altitude)` per tick and writes it to the near-field material's `_GrassChunkFade` (already multiplied into `visualEdgeFade`); only dispose the controller well past the band (e.g. > 800 m) to reclaim memory. With G1's dither upgrade the fade is smooth; without it, it will band (another reason G1 goes first).

---

### [G8] Minor grass notes (flag-only)

- **Cube-corner straddle** is detected but unhandled ([FaceSpaceCellRangeBuilder.cs:12-15](../../Assets/Scripts/Planet/Grass/FaceSpaceCellRangeBuilder.cs#L12-L15)) — a missing-grass wedge within ~1.3° of a cube corner. Documented, rare, surfaced via `SeamRisk`. Leave.
- **Overlay `midWeight`/`nearWeight`** are computed and consumed only by a debug output ([PlanetVertexColor.shader:731-737](../../Assets/Graphics/Shaders/PlanetVertexColor.shader#L731-L737), [:1000](../../Assets/Graphics/Shaders/PlanetVertexColor.shader#L1000)) — fold into G5 (either wire them to real behavior or delete).
- **Suppression path is dead**: `SuppressionRadiusFraction = 0` ([GrassNearFieldController.cs:46](../../Assets/Scripts/Planet/Grass/GrassNearFieldController.cs#L46)) makes `SuppressionRadius` always 0, so the chunk-suppression block in [GrassPlacementController.cs:139-171](../../Assets/Scripts/Planet/Grass/GrassPlacementController.cs#L139-L171) never runs. Delete with G6, whichever way G6 goes.
- **`ResolveChunkInnerFade`** ([GrassPlacementController.cs:338-342](../../Assets/Scripts/Planet/Grass/GrassPlacementController.cs#L338-L342)) returns two constants through `out` params — vestigial; also folds into G6.

---

### Recommended execution order (grass)

1. **G5** — `GrassLodProfile` + shader uniforms (everything else edits against it).
2. **G1** — IGN dither, fade re-split, drop `edgeShade`.
3. **G2** — shared `GrassCanopyAlbedo`, finish handoff at `DrawDistance`, re-center `surfaceVariation`, brightness knob → 1.0 trim.
4. **G3** — widen the coverage remap.
5. **G4** — overlay window from profile; evaluate whether an under-canopy tint is still needed.
6. Re-capture (F10) at 50 / 120 / 160 / 190 / 210 m and at 300 / 450 / 600 m altitude. Only then decide **G6** (delete vs promote) and do **G7**.

Each step is independently verifiable in-game via the existing console: `grass.overlay-status`, `grass.surface-brightness`, layer debug tints (`_GrassDebugLayerColors`), and the near-field stats in `GrassDebugModule`.

---

## Part 2 — General findings (not grass)

### [ARCH-1] Four `[DefaultExecutionOrder]` attributes vs the boot-path rule

- **Evidence:** `SceneBootstrap` (−10000), `GameBootstrap` (−9000), `SurfacePathMousePainter` (−100) at [SurfacePathMousePainter.cs:5](../../Assets/Scripts/Core/Services/SurfacePathMousePainter.cs#L5), `RainParticleController` (−50) at [RainParticleController.cs:28](../../Assets/Scripts/Planet/Precipitation/RainParticleController.cs#L28).
- **Impact:** CLAUDE.md says ordering belongs in the init-phase system, no exceptions listed for these. The two bootstraps arguably *are* the phase system's entry point and may deserve an explicit sanction in CLAUDE.md (like `LoadingManager.CreateInstance` has); the painter and rain controller are ordinary consumers that should declare init-phase dependencies instead.
- **Effort:** S (rule doc) + S-M (migrating the two consumers). **Risk:** MED — execution-order changes can surface latent ordering assumptions; test scene load + rain + path painting. **Confidence:** HIGH that the rule is violated; MED on migration ease.
- **Fix sketch:** add a CLAUDE.md sanction line for the two bootstraps (or migrate them too if the init graph design lands); move `SurfacePathMousePainter` and `RainParticleController` ordering needs into `IEarlyInitialize`/`ILateInitialize` phases.

### [PERF-1] `PlanetGrassCoordinator.Tick` resolves services/settings every frame

- **Evidence:** `SettingsProvider.GetSettings<PlanetDto>()` per tick at [PlanetGrassCoordinator.cs:98](../../Assets/Scripts/Planet/PlanetGrassCoordinator.cs#L98); `ServiceLocator.Get<IGrassQualitySettings>()` per tick via `ShouldActivateNearFieldGrass` at [:136](../../Assets/Scripts/Planet/PlanetGrassCoordinator.cs#L136).
- **Impact:** violates the "resolve once at init, never per frame" rule on a hot path. Cost is a dictionary lookup + cast per frame — small, but it's the exact pattern the rule exists to keep out of `Tick`.
- **Effort:** S. **Risk:** LOW — cache in `Configure`, refresh `PlanetDto` on `SettingsChangedEvent` per the settings rule. **Confidence:** HIGH.
- **Fix sketch:** cache `_quality` in the constructor and `_planetDto` in `Configure`; subscribe to `EventBus<SettingsChangedEvent>` to re-fetch.

### [PERF-2] `GrassPlacementController.Tick` writes two material floats every frame regardless of change

- **Evidence:** [GrassPlacementController.cs:117-118](../../Assets/Scripts/Planet/Grass/GrassPlacementController.cs#L117-L118) — `SetFloat` runs before the `transitionChanged` check, and `ResolveChunkInnerFade` returns constants anyway.
- **Impact:** violates the dirty-flag discipline (minor in isolation; the values literally never change at runtime today).
- **Effort:** S. **Risk:** LOW. **Confidence:** HIGH.
- **Fix sketch:** set the two floats once in the constructor; delete `ResolveChunkInnerFade` and the per-tick compare. (Subsumed by G6 if the chunk layer is deleted.)

### [DEBT-1] `ChunkedSurfaceProvider` at 1,764 lines

- **Evidence:** `Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs`, largest file in the codebase by 2×; a restructure design already exists at [docs/design/2026-06-12-chunked-surface-provider-restructure.md](../design/2026-06-12-chunked-surface-provider-restructure.md).
- **Impact:** the file-size rule's "split before adding responsibility" trigger — surface edits, path wear, scorch, grass atlases, and residency queries all live here, and grass work keeps touching it.
- **Effort:** L. **Risk:** MED. **Confidence:** HIGH.
- **Fix sketch:** execute the existing design doc; don't redesign here. Defer until after the grass arc unless a grass fix has to add responsibility to it (then split first per the rule).

### [DEBT-2] Empty catch-all in `Planet.TryGetSettings`

- **Evidence:** [Planet.cs:601-617](../../Assets/Scripts/Planet/Planet.cs#L601-L617) — `catch (System.Exception) { }` around a settings probe.
- **Impact:** any real `SettingsProvider` failure (mis-registered world, disposed registry) is silently converted to "settings absent," which can mask boot-order bugs. LOW severity: it already checks `IsRegistered` first, so the catch should be nearly unreachable — which is also the argument for removing it.
- **Effort:** S. **Risk:** LOW. **Confidence:** HIGH.
- **Fix sketch:** drop the try/catch (keep the `IsRegistered` guard), or log at `Debug` level in the catch if there's a known race it papers over.

### [DOC-1] CLAUDE.md points at a deleted audit directory

- **Evidence:** CLAUDE.md references `docs/audit/2026-06-code-refactor/`; the directory is empty (contents removed at commit `5e33fca`).
- **Impact:** every agent session loads a stale pointer as "the current source of refactor findings."
- **Effort:** S. **Risk:** LOW. **Confidence:** HIGH.
- **Fix sketch:** update CLAUDE.md to point here (or note the arc closed at `7048c2c`).

### Direction (options, not defects)

- **Grass quality tiers are stubbed but inert.** `IGrassQualitySettings` exists with exactly one implementation ([QualityController.cs:19-37](../../Assets/Scripts/Core/QualityController.cs#L19-L37)), registered unconditionally in `GameBootstrap`; `QualityController`'s tier switching only affects clouds. Once `GrassLodProfile` (G5) exists, per-tier grass profiles (draw distance, capacity, density multiplier) become a small, natural extension — and the interface finally earns its keep. Until then it's a one-implementation abstraction the ponytail rule would question.
- **Near-field draw distance as the mid-tier replacement.** If G6 lands as "delete," the freed per-chunk buffer budget (up to ~29 MB per resident chunk set) could fund raising `DrawDistance` from 200 m toward 280–320 m with the same 1 M-instance capacity (spacing thins with distance anyway). Cheaper than maintaining a third layer; measurable via the existing `FrameTimingSection.NearGrass` counter.

---

## Considered and rejected

- **Near-field multi-face quota starvation** ([GrassNearFieldController.cs:421-457](../../Assets/Scripts/Planet/Grass/GrassNearFieldController.cs#L421-L457)) — budgets sum exactly to capacity, remainder goes to the last range; read carefully, no defect.
- **`GrassChunkResidencyResolver.Chunks.Contains` in the release loop** — it's a `HashSet`, O(1); not the O(n²) it looks like from the call site.
- **Raw string property names in grass controllers** — these are per-material/compute-scoped names, which the shader-globals rule explicitly leaves module-local. Compliant.
- **Coroutines / `async void` / `Task.Run` sweep** — zero hits in `Assets/Scripts`. Clean.
- **Raw `Shader.SetGlobal*("_literal")` sweep** — zero hits; `ShaderGlobalIds` discipline is holding.
- **`Debug.Log` migration debt** — only `UnityLogger` (the sink) and one console file; effectively migrated.

---

## Codex feedback

Reviewed against current `HEAD` (`ec0b1cd`). I agree with the core diagnosis: G1-G5 are supported by the code, especially compute thinning plus shader geometry/dither fade over the same 144-200 m band, and the terrain overlay using its own 24-120 m window.

Implementation cautions before anyone starts fixing:

- **G5 should not create a second distance authority.** `IGrassQualitySettings` already owns chunk render distances, overlay altitude gates, density, and the future quality-tier surface ([QualityController.cs](../../Assets/Scripts/Core/QualityController.cs#L1-L37)); the locked grass design also says quality settings drive grass LOD thresholds ([2026-05-30-grass-and-chunks.md](../research/2026-05-30-grass-and-chunks.md#L994-L995)). If `GrassLodProfile` is added, make `DefaultGrassQualitySettings` consume it, or move the missing near/overlay distances into `IGrassQualitySettings`. Do not leave a static profile beside `IGrassQualitySettings` with overlapping values.
- **G1 can be smaller than the fix sketch.** `GrassNearFieldPlace.compute` already stochastically thins to zero before `_NearFieldDrawDistance` ([GrassNearFieldPlace.compute](../../Assets/Resources/GrassNearFieldPlace.compute#L499-L510)). Moving `_GrassVisualFadeStart` to 180 only narrows the double fade; it does not remove it. First patch should try disabling the near-field far visual fade, or making it alpha/dither-only, while removing fade-driven height/width scaling and `edgeShade`. Add per-blade death-distance logic only if captures prove survivors still form a 200 m wall.
- **G7 should not reuse `_GrassChunkFade` as-is.** In current `Grass.shader`, `_GrassChunkFade` multiplies `visualEdgeFade` and also darkens albedo ([Grass.shader](../../Assets/Graphics/Shaders/Grass.shader#L212-L217), [Grass.shader](../../Assets/Graphics/Shaders/Grass.shader#L335-L338)). Using it for altitude fade will shrink and darken near grass during climb, recreating a dark transition ring. Use a separate altitude uniform, or first change `_GrassChunkFade` semantics as part of G1/G6.
- **G6 delete/promote is Bryan's call, not just dead-code cleanup.** The locked grass/chunk plan explicitly expected a chunked compute grass renderer and `IGrassQualitySettings`-driven LOD ([2026-05-30-grass-and-chunks.md](../research/2026-05-30-grass-and-chunks.md#L851-L865), [2026-05-30-grass-and-chunks.md](../research/2026-05-30-grass-and-chunks.md#L991-L995)). I agree the default-disabled chunk path is risky to promote as-is, but delete it only after Bryan accepts near+overlay as the final architecture after G1-G5 captures.
- **G3 should be measured before baking.** Widening `smoothstep(0.05, 0.55)` to `0.95` is the right direction for edge softness, but it will also reduce peak blanket coverage anywhere `farWeight` rarely reaches 1. Keep this as a live console/capture sweep first, not a blind one-line final.

Net: I agree with the findings, but would implement through the existing quality-settings path and avoid reusing shader knobs that currently carry geometry/albedo side effects.
